using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class InventorySyncTests
    {
        private const string PlayerId = "p1";

        private static readonly InventorySnapshot Local = new(
            new[] { new ItemStack(1, FakeRemoteConfigApi.SwordItemId, 1) }, revision: 4, nextStackId: 2);

        private static readonly InventorySnapshot Server = new(
            new[] { new ItemStack(1, FakeRemoteConfigApi.HealthPotionItemId, 9), new ItemStack(2, FakeRemoteConfigApi.ChestItemId, 1) },
            revision: 11,
            nextStackId: 3);

        private FakeInventoryStorage _storage;
        private FakeInventoryRpcManager _rpc;
        private InventoryManager _manager;
        private InventorySyncService _sync;

        [SetUp]
        public void SetUp()
        {
            _storage = new FakeInventoryStorage();
            _rpc = new FakeInventoryRpcManager();
            _manager = new InventoryManager(_storage);
            _sync = new InventorySyncService(_rpc, _manager, _storage);
        }

        [Test]
        public async Task ServerSaysValid_LocalSnapshotStays_AndNothingIsWritten()
        {
            _storage.Seed(PlayerId, Local);

            await _sync.SyncAsync(PlayerId, CancellationToken.None);

            Assert.That(_manager.Snapshot.Hash, Is.EqualTo(Local.Hash));
            Assert.That(_storage.SaveCount, Is.Zero);
            Assert.That(_rpc.SyncCalls[0], Is.EqualTo((Local.Hash, 4L)), "The stored hash and revision go to the server.");
        }

        [Test]
        public async Task ServerAnswersWithASnapshot_ItReplacesTheLocalOne_AndIsSaved()
        {
            _storage.Seed(PlayerId, Local);
            _rpc.SyncAnswer = (_, _) => InventorySyncResult.Replace(Server);

            await _sync.SyncAsync(PlayerId, CancellationToken.None);

            Assert.That(_manager.Snapshot.Hash, Is.EqualTo(Server.Hash));
            Assert.That(_manager.UsedSlots, Is.EqualTo(2));
            Assert.That(_storage.SaveCount, Is.EqualTo(1));
            Assert.That(_storage.LastSaved.Hash, Is.EqualTo(Server.Hash));
        }

        [Test]
        public async Task FirstLaunch_SendsNoHash_AndStartsEmptyUntilTheServerAnswers()
        {
            _rpc.SyncAnswer = (hash, _) => hash == null ? InventorySyncResult.Replace(Server) : InventorySyncResult.Valid;

            await _sync.SyncAsync(PlayerId, CancellationToken.None);

            Assert.That(_rpc.SyncCalls[0], Is.EqualTo(((string)null, 0L)));
            Assert.That(_manager.Snapshot.Hash, Is.EqualTo(Server.Hash));
        }

        [Test]
        public async Task ACorruptFile_IsLogged_TreatedAsEmpty_AndDoesNotBlockTheConnect()
        {
            _storage.ThrowOnLoad = true;
            _rpc.SyncAnswer = (hash, _) => hash == null ? InventorySyncResult.Replace(Server) : InventorySyncResult.Valid;
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(@"\[Expected\] Corrupt inventory file"));

            await _sync.SyncAsync(PlayerId, CancellationToken.None);

            Assert.That(_rpc.SyncCalls[0].Hash, Is.Null, "A file we could not read has no hash to offer.");
            Assert.That(_manager.Snapshot.Hash, Is.EqualTo(Server.Hash));
        }

        [Test]
        public async Task TheConfig_IsAppliedBeforeAnythingElse()
        {
            _rpc.Config = new InventoryConfig(3, FakeRemoteConfigApi.CreateDemoConfig().Catalog);

            await _sync.SyncAsync(PlayerId, CancellationToken.None);

            Assert.That(_manager.SlotLimit, Is.EqualTo(3));
            Assert.That(_manager.IsInitialized, Is.True);
        }

        [Test]
        public async Task ACancelledSync_LeavesNoHalfAppliedState()
        {
            _storage.Seed(PlayerId, Local);
            using var cts = new CancellationTokenSource();
            _rpc.SyncAnswer = (_, _) =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return null;
            };

            // Awaited in the body, not asserted as an async delegate: see testing-in-unity.md.
            System.Exception caught = null;
            try
            {
                await _sync.SyncAsync(PlayerId, cts.Token);
            }
            catch (System.Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<System.OperationCanceledException>());

            Assert.That(_manager.Snapshot.Hash, Is.EqualTo(Local.Hash), "The cached state is what the player sees.");
            Assert.That(_storage.SaveCount, Is.Zero);
        }
    }
}
