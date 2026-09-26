using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class DailyRewardManagerTests
    {
        private FakeTimeProvider _time;
        private TestGrants _world;
        private DailyRewardManager _manager;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTimeProvider();
            _world = new TestGrants(slotLimit: 12);
            _manager = new DailyRewardManager(_time, _world.Grants);
        }

        [Test]
        public void IsRewardAvailable_IsTrue_FromTheVeryFirstFrame()
        {
            Assert.That(_manager.IsRewardAvailable(), Is.True);
        }

        [Test]
        public async Task ClaimRewardAsync_MakesTheRewardUnavailable_AndSaysSo()
        {
            var availabilityChanged = 0;
            _manager.AvailabilityChanged += () => availabilityChanged++;

            var reward = await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(reward, Is.Not.Null);
            Assert.That(_manager.IsRewardAvailable(), Is.False);

            Assert.That(availabilityChanged, Is.EqualTo(1));
        }

        [Test]
        public async Task ClaimRewardAsync_PutsTheCurrenciesInTheWallet_AndTheItemsInTheInventory()
        {
            await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(500));
            Assert.That(_world.Wallet.GetBalance("gems"), Is.EqualTo(10));
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.HealthPotionItemId), Is.EqualTo(2));
        }

        [Test]
        public async Task IsRewardAvailable_ComesBack_OnceTheIntervalHasElapsed()
        {
            await _manager.ClaimRewardAsync(CancellationToken.None);
            var nextAvailableAt = _manager.NextAvailableAtUtc;

            Assert.That(nextAvailableAt, Is.GreaterThan(_time.UtcNow));

            _time.UtcNow = nextAvailableAt;

            Assert.That(_manager.IsRewardAvailable(), Is.True);
        }

        [Test]
        public async Task TimeUntilAvailable_IsZero_WhileAvailable_AndCountsDownTheCooldown()
        {
            Assert.That(_manager.TimeUntilAvailable, Is.EqualTo(TimeSpan.Zero));

            await _manager.ClaimRewardAsync(CancellationToken.None);
            var full = _manager.TimeUntilAvailable;

            Assert.That(full, Is.EqualTo(_manager.NextAvailableAtUtc - _time.UtcNow));
            Assert.That(full, Is.GreaterThan(TimeSpan.Zero));

            _time.AdvanceSeconds(10);
            Assert.That(_manager.TimeUntilAvailable, Is.EqualTo(full - TimeSpan.FromSeconds(10)));

            _time.UtcNow = _manager.NextAvailableAtUtc + TimeSpan.FromSeconds(5);
            Assert.That(_manager.TimeUntilAvailable, Is.EqualTo(TimeSpan.Zero), "Never negative.");
        }

        [Test]
        public async Task ClaimRewardAsync_Cancelled_LeavesAvailabilityUntouched()
        {
            var availabilityChanged = 0;
            _manager.AvailabilityChanged += () => availabilityChanged++;

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Exception caught = null;

            try
            {
                await _manager.ClaimRewardAsync(cancelled.Token);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());

            Assert.That(_manager.IsRewardAvailable(), Is.True);
            Assert.That(availabilityChanged, Is.Zero);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.Zero, "All-or-nothing: nothing applied before the call went through.");
        }

        [Test]
        public void CanClaimIntoInventory_IsFalse_WhenTheInventoryIsFull()
        {
            _world.FillInventory();

            Assert.That(_manager.CanClaimIntoInventory().Success, Is.False);
            Assert.That(_manager.IsRewardAvailable(), Is.True, "A full inventory is not a cooldown.");
        }

        [Test]
        public async Task ClaimRewardAsync_WithAFullInventory_Fails_WithoutStartingTheCooldown()
        {
            _world.FillInventory();
            var availabilityChanged = 0;
            _manager.AvailabilityChanged += () => availabilityChanged++;

            Exception caught = null;

            try
            {
                await _manager.ClaimRewardAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<InventoryFullException>());
            Assert.That(_manager.IsRewardAvailable(), Is.True, "The reward is still claimable once space is freed.");
            Assert.That(availabilityChanged, Is.Zero);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.Zero);
        }

        [Test]
        public async Task ClaimRewardAsync_OnCooldown_IsRefused_AndMovesNothing()
        {
            await _manager.ClaimRewardAsync(CancellationToken.None);
            var nextAvailableAt = _manager.NextAvailableAtUtc;

            var caught = await CaptureAsync(() => _manager.ClaimRewardAsync(CancellationToken.None));

            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(500), "Granted once, not twice.");
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.HealthPotionItemId), Is.EqualTo(2));
            Assert.That(_manager.NextAvailableAtUtc, Is.EqualTo(nextAvailableAt), "A refused claim does not restart the cooldown.");
        }

        [Test]
        public async Task ClaimRewardAsync_TwoAtOnce_GrantsOnce()
        {
            var first = _manager.ClaimRewardAsync(CancellationToken.None);

            var caught = await CaptureAsync(() => _manager.ClaimRewardAsync(CancellationToken.None));
            await first;

            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(500));
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.HealthPotionItemId), Is.EqualTo(2));
        }

        [Test]
        public async Task ClaimRewardAsync_CancelledMidFlight_LeavesTheRewardClaimable()
        {
            using var cancellation = new CancellationTokenSource();
            var abandoned = _manager.ClaimRewardAsync(cancellation.Token);
            cancellation.Cancel();

            var caught = await CaptureAsync(() => abandoned);

            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_manager.IsRewardAvailable(), Is.True);

            await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(500), "The in-flight guard was released.");
        }

        private static async Task<Exception> CaptureAsync<T>(Func<Cysharp.Threading.Tasks.UniTask<T>> call)
        {
            try
            {
                await call();
            }
            catch (Exception ex)
            {
                return ex;
            }

            return null;
        }

        // A throwing inventory observer must not abort the claim after the items land but
        // before the currencies and the cooldown - that would leave a claim that can be repeated.
        [Test]
        public async Task AThrowingInventoryObserver_CannotAbortTheClaim_OrLeaveItRepeatable()
        {
            _world.Inventory.Changed += () => throw new InvalidOperationException("[Expected] reward badge failure");
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] reward badge failure"));

            var reward = await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(reward, Is.Not.Null);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(500));
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.HealthPotionItemId), Is.EqualTo(2));
            Assert.That(_manager.IsRewardAvailable(), Is.False, "The cooldown started with the grant.");
        }

        [Test]
        public async Task InventoryObservers_AlreadySeeTheRewardAsClaimed()
        {
            bool? availableWhenObserved = null;
            _world.Inventory.Changed += () => availableWhenObserved = _manager.IsRewardAvailable();

            await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(availableWhenObserved, Is.False);
        }

        [Test]
        public async Task AThrowingAvailabilityObserver_DoesNotFailTheClaim()
        {
            _manager.AvailabilityChanged += () => throw new InvalidOperationException("[Expected] timer label failure");
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] timer label failure"));

            var reward = await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(reward, Is.Not.Null);
        }
    }
}
