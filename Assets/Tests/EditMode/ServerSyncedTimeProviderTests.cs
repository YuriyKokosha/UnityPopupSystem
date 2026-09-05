using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Services.Time;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class ServerSyncedTimeProviderTests
    {
        private static readonly DateTime ServerUtcNow = new(2099, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void UtcNow_FallsBackToTheDeviceClock_BeforeTheFirstSync()
        {
            var provider = new ServerSyncedTimeProvider(new FakeClockRpcManager(ServerUtcNow));

            Assert.That(provider.IsSynced, Is.False);

            Assert.That(provider.UtcNow, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public async Task UtcNow_ReportsServerTime_AfterSync()
        {
            var rpc = new FakeClockRpcManager(ServerUtcNow);
            var provider = new ServerSyncedTimeProvider(rpc);

            await provider.SyncAsync(default);

            Assert.That(provider.IsSynced, Is.True);
            Assert.That(rpc.ServerTimeCallCount, Is.EqualTo(1));
            Assert.That(provider.UtcNow, Is.EqualTo(ServerUtcNow).Within(TimeSpan.FromMinutes(1)));

            Assert.That(provider.UtcNow.Year, Is.EqualTo(ServerUtcNow.Year));
        }

        [Test]
        public async Task UtcNow_AdvancesFromTheServerAnchor_RatherThanBeingFrozenAtIt()
        {
            var provider = new ServerSyncedTimeProvider(new FakeClockRpcManager(ServerUtcNow));
            await provider.SyncAsync(default);

            var firstReading = provider.UtcNow;
            await UniTask.Delay(50);
            var secondReading = provider.UtcNow;

            Assert.That(secondReading, Is.GreaterThan(firstReading));
            Assert.That(secondReading - firstReading, Is.LessThan(TimeSpan.FromMinutes(1)));
        }

        [Test]
        public async Task SyncAsync_CalledAgain_ReanchorsToTheNewServerTime()
        {
            var rpc = new FakeClockRpcManager(ServerUtcNow);
            var provider = new ServerSyncedTimeProvider(rpc);
            await provider.SyncAsync(default);

            rpc.ServerUtcNow = ServerUtcNow.AddDays(10);
            await provider.SyncAsync(default);

            Assert.That(provider.UtcNow, Is.EqualTo(ServerUtcNow.AddDays(10)).Within(TimeSpan.FromMinutes(1)));
            Assert.That(rpc.ServerTimeCallCount, Is.EqualTo(2));
        }
    }
}
