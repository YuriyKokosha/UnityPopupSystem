using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.UI.Preloader;
using PopupSystem.UI.Windows.MainGame;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Zenject;

namespace PopupSystem.Tests.PlayMode
{
    /// <summary>Boot smoke of the real <c>MainScene</c>: its <c>SceneContext</c>, <c>AppInstaller</c>, serialized
    /// references, <c>UIRoot</c>, <c>EventSystem</c> and the startup state machine, none of which the other
    /// fixtures touch (they build their own container and hierarchy - see testing-in-unity.md). It proves the
    /// composition root still wires up and reaches the main screen with nothing logged as an error or exception
    /// (the test framework fails the test on any such log). It does not drive input.
    ///
    /// Exactly one binding is replaced: <c>IInventoryStorage</c> is re-bound (Zenject's
    /// <see cref="SceneContext.AfterInstallHooks"/>, which runs after <c>AppInstaller</c>) to the same
    /// production <see cref="FileInventoryStorage"/>, rooted in a scratch directory instead of
    /// <c>Application.persistentDataPath</c>. The file storage path is still exercised, and a developer's real
    /// save is never read or written - asserted below, not just intended.</summary>
    [TestFixture]
    public sealed class MainSceneBootPlayModeTests
    {
        private const string SceneName = "MainScene";

        // Boot passes the fake backend's simulated latencies (time sync, profile, wallet, inventory, config).
        private const int BootTimeoutMs = 20_000;
        private const int PollStepMs = 50;

        // Lets the startup queue burst reach a steady state before the scene is torn down, so nothing the boot
        // started is still mid-flight against destroyed objects.
        private const int SettleMs = 2_000;

        [Test]
        public async Task MainScene_BootsThroughTheRealCompositionRoot_ToTheMainScreen()
        {
            var scratchRoot = Path.Combine(Application.temporaryCachePath, "MainSceneBoot-" + Guid.NewGuid().ToString("N"));
            var realSaveBefore = SnapshotSaveFiles(Application.persistentDataPath);

            // Consumed and reset by the SceneContext of the scene loaded next. FromInstance, not
            // To<FileInventoryStorage>().AsSingle().WithArguments(...): AppInstaller already declares that concrete
            // type as a singleton with other arguments, and Zenject rejects a second singleton of the same type
            // with different arguments ("Ambiguous set of creation properties").
            SceneContext.AfterInstallHooks = container => container
                .Rebind<IInventoryStorage>()
                .FromInstance(new FileInventoryStorage(scratchRoot));

            var scene = default(Scene);

            try
            {
                await SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
                scene = SceneManager.GetSceneByName(SceneName);

                Assert.That(scene.isLoaded, Is.True, "MainScene must be in the build settings.");

                var mainScreenUp = await WaitUntilAsync(
                    () => FindInScene<MainGameWindowView>(scene) is { } view && view.gameObject.activeInHierarchy);

                Assert.That(mainScreenUp, Is.True, "The startup state machine never opened the main screen.");

                var preloader = FindInScene<PreloaderOverlayView>(scene);
                Assert.That(
                    preloader == null || !preloader.gameObject.activeInHierarchy,
                    Is.True,
                    "The preloader is hidden once the main screen is up.");

                var eventSystem = FindInScene<EventSystem>(scene);
                Assert.That(eventSystem, Is.Not.Null, "The scene carries its own EventSystem.");
                Assert.That(
                    eventSystem.GetComponent<BaseInputModule>(),
                    Is.Not.Null,
                    "An EventSystem without an input module takes no clicks.");

                // An empty scratch storage has no hash, so the fake server answers with the starter kit and the
                // inventory writes it: proof that the boot used the scratch storage and that the write works.
                var scratchSaveWritten = await WaitUntilAsync(() => SnapshotSaveFiles(scratchRoot).Count > 0);
                Assert.That(scratchSaveWritten, Is.True, "The boot saved its inventory into the scratch storage.");

                await UniTask.Delay(SettleMs);
            }
            finally
            {
                SceneContext.AfterInstallHooks = null;

                if (scene.isLoaded)
                {
                    await SceneManager.UnloadSceneAsync(scene);
                }

                DeleteScratch(scratchRoot);
            }

            Assert.That(
                SnapshotSaveFiles(Application.persistentDataPath),
                Is.EqualTo(realSaveBefore),
                "The developer's real inventory save was neither created, changed nor removed.");
        }

        // Path -> last write time of every inventory file under a storage root; empty when there is none.
        private static Dictionary<string, DateTime> SnapshotSaveFiles(string storageRoot)
        {
            var directory = Path.Combine(storageRoot, "inventory");
            if (!Directory.Exists(directory))
            {
                return new Dictionary<string, DateTime>();
            }

            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.GetLastWriteTimeUtc);
        }

        private static void DeleteScratch(string scratchRoot)
        {
            try
            {
                if (Directory.Exists(scratchRoot))
                {
                    Directory.Delete(scratchRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // A save still finishing on the thread pool can hold the file for a moment; the directory is
                // under temporaryCachePath and the OS clears it, so a leftover is harmless.
            }
        }

        private static T FindInScene<T>(Scene scene)
            where T : Component
        {
            var roots = scene.GetRootGameObjects();

            for (var i = 0; i < roots.Length; i++)
            {
                var found = roots[i].GetComponentInChildren<T>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static async UniTask<bool> WaitUntilAsync(Func<bool> condition)
        {
            var elapsed = Stopwatch.StartNew();

            while (!condition())
            {
                if (elapsed.ElapsedMilliseconds >= BootTimeoutMs)
                {
                    return false;
                }

                await UniTask.Delay(PollStepMs);
            }

            return true;
        }
    }
}
