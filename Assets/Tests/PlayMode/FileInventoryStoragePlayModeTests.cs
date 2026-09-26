using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using UnityEngine;

namespace PopupSystem.Tests.PlayMode
{
    /// <summary>The real storage against the real file system. Everything lands in a scratch directory under
    /// the temporary cache path, never in persistentDataPath, so a test can never overwrite a developer's save.</summary>
    [TestFixture]
    public sealed class FileInventoryStoragePlayModeTests
    {
        private const string PlayerId = "player/with:odd*chars";

        private string _root;
        private FileInventoryStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Application.temporaryCachePath, "inventory-tests", Path.GetRandomFileName());
            _storage = new FileInventoryStorage(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Test]
        public async Task Load_WithNothingSaved_ReturnsNull()
        {
            var loaded = await _storage.LoadAsync(PlayerId, CancellationToken.None);

            Assert.That(loaded, Is.Null);
        }

        [Test]
        public async Task Save_ThenLoad_RoundTripsTheSnapshot_HashIncluded()
        {
            var snapshot = new InventorySnapshot(
                new[] { new ItemStack(3, "sword", 1), new ItemStack(7, "potion", 4) }, revision: 5, nextStackId: 8);

            await _storage.SaveAsync(PlayerId, snapshot, CancellationToken.None);
            var loaded = await _storage.LoadAsync(PlayerId, CancellationToken.None);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.Hash, Is.EqualTo(snapshot.Hash));
            Assert.That(loaded.Revision, Is.EqualTo(5));
            Assert.That(loaded.NextStackId, Is.EqualTo(8));
            Assert.That(loaded.Stacks.Count, Is.EqualTo(2));
            Assert.That(loaded.Stacks[1].StackId, Is.EqualTo(7));
        }

        [Test]
        public async Task Save_LeavesNoTempFileBehind_AndOverwritesInPlace()
        {
            var first = new InventorySnapshot(new[] { new ItemStack(1, "sword", 1) }, 1, 2);
            var second = new InventorySnapshot(new[] { new ItemStack(1, "sword", 1), new ItemStack(2, "potion", 2) }, 2, 3);

            await _storage.SaveAsync(PlayerId, first, CancellationToken.None);
            await _storage.SaveAsync(PlayerId, second, CancellationToken.None);

            var path = _storage.GetPath(PlayerId);
            Assert.That(File.Exists(path), Is.True);
            Assert.That(File.Exists(path + ".tmp"), Is.False);

            var loaded = await _storage.LoadAsync(PlayerId, CancellationToken.None);
            Assert.That(loaded.Hash, Is.EqualTo(second.Hash));
        }

        [Test]
        public async Task ACorruptFile_Throws_RatherThanReadingAsEmpty()
        {
            var path = _storage.GetPath(PlayerId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ this is not json");

            // Awaited in the body, never inside a constraint: LoadAsync hops to the thread pool and back, and NUnit
            // would block the main thread it needs to come back to (see Docs/knowledge-base/testing-in-unity.md).
            var thrown = await CaptureAsync(() => _storage.LoadAsync(PlayerId, CancellationToken.None));

            Assert.That(thrown, Is.Not.Null);
        }

        [Test]
        public async Task AFileWhoseContentsDoNotMatchItsHash_IsRejected()
        {
            var snapshot = new InventorySnapshot(new[] { new ItemStack(1, "sword", 1) }, 1, 2);
            await _storage.SaveAsync(PlayerId, snapshot, CancellationToken.None);

            var path = _storage.GetPath(PlayerId);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"count\": 1", "\"count\": 99"));

            var thrown = await CaptureAsync(() => _storage.LoadAsync(PlayerId, CancellationToken.None));

            Assert.That(thrown, Is.InstanceOf<InvalidDataException>());
        }

        private static async Task<System.Exception> CaptureAsync(System.Func<Cysharp.Threading.Tasks.UniTask<InventorySnapshot>> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (System.Exception ex)
            {
                return ex;
            }
        }

        [Test]
        public async Task Clear_RemovesTheFile()
        {
            await _storage.SaveAsync(PlayerId, InventorySnapshot.Empty, CancellationToken.None);

            await _storage.ClearAsync(PlayerId, CancellationToken.None);

            Assert.That(File.Exists(_storage.GetPath(PlayerId)), Is.False);
            Assert.That(await _storage.LoadAsync(PlayerId, CancellationToken.None), Is.Null);
        }
    }
}
