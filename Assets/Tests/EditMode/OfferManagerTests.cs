using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    /// <summary>The purchase is the one flow that moves both halves of the economy - the wallet down, the
    /// inventory and wallet up - so what has to hold is that either everything moves or nothing does.</summary>
    [TestFixture]
    public sealed class OfferManagerTests
    {
        private FakeTimeProvider _time;
        private TestGrants _world;
        private OfferManager _offers;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTimeProvider();
            _world = new TestGrants(slotLimit: 12);
            _offers = new OfferManager(_world.Grants, _world.Wallet, new FakeClockRpcManager(_time.UtcNow), _time);
        }

        [Test]
        public void TheOffer_HasAPrice_AndIsNotFree()
        {
            var offer = _offers.GetActiveOfferData();

            Assert.That(offer, Is.Not.Null);
            Assert.That(offer.IsFree, Is.False, "A 'Buy' that costs nothing is a claim wearing the wrong label.");
            Assert.That(offer.Price.CurrencyId, Is.EqualTo("gold"));
        }

        [Test]
        public async Task Purchase_DebitsThePrice_AndGrantsTheBundle()
        {
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1500) }));
            var offer = _offers.GetActiveOfferData();

            var result = await _offers.PurchaseOfferAsync(offer, CancellationToken.None);

            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(1500 - offer.Price.Amount));
            Assert.That(_world.Wallet.GetBalance("gems"), Is.EqualTo(500));
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
            Assert.That(result.Reward, Is.SameAs(offer.Reward));
        }

        [Test]
        public void CanAfford_FollowsTheBalance()
        {
            var offer = _offers.GetActiveOfferData();

            Assert.That(_offers.CanAfford(offer), Is.False, "An empty wallet cannot afford it.");

            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", offer.Price.Amount) }));

            Assert.That(_offers.CanAfford(offer), Is.True, "Exactly the price is enough.");
        }

        [Test]
        public async Task Purchase_WithoutTheFunds_Throws_AndMovesNothing()
        {
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));
            var offer = _offers.GetActiveOfferData();

            Exception caught = null;
            try
            {
                await _offers.PurchaseOfferAsync(offer, CancellationToken.None);
            }
            catch (InsufficientFundsException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(10), "Nothing was charged.");
            Assert.That(_world.Wallet.GetBalance("gems"), Is.Zero, "Nothing was granted.");
            Assert.That(_world.Inventory.UsedSlots, Is.Zero);
        }

        [Test]
        public async Task Purchase_IntoAFullInventory_Throws_AndLeavesTheWalletUntouched()
        {
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1500) }));
            _world.FillInventory();
            var offer = _offers.GetActiveOfferData();

            Exception caught = null;
            try
            {
                await _offers.PurchaseOfferAsync(offer, CancellationToken.None);
            }
            catch (InventoryFullException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(1500), "Refused items must not cost anything.");
            Assert.That(_world.Wallet.GetBalance("gems"), Is.Zero);
        }

        [Test]
        public async Task Purchase_OfAnExpiredOffer_IsRefused()
        {
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1500) }));
            var offer = _offers.GetActiveOfferData();
            _time.Advance(TimeSpan.FromDays(8));

            Exception caught = null;
            try
            {
                await _offers.PurchaseOfferAsync(offer, CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(1500));
        }

        [Test]
        public async Task Purchase_WhileAnotherIsInFlight_IsRefused_AndOnlyOneBundleMoves()
        {
            var offer = _offers.GetActiveOfferData();
            // Funds and space for two purchases: only the in-flight guard can refuse the second one.
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", offer.Price.Amount * 2) }));

            var first = _offers.PurchaseOfferAsync(offer, CancellationToken.None);

            var caught = await CaptureAsync(() => _offers.PurchaseOfferAsync(offer, CancellationToken.None));
            await first;

            Assert.That(caught, Is.TypeOf<InvalidOperationException>(), "Refused by the guard, not by funds or space.");
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(offer.Price.Amount), "Charged once.");
            Assert.That(_world.Wallet.GetBalance("gems"), Is.EqualTo(500), "Granted once.");
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
        }

        [Test]
        public async Task Purchase_WhenTheFundsGoDuringTheRoundTrip_MovesNothing()
        {
            var offer = _offers.GetActiveOfferData();
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", offer.Price.Amount) }));

            var purchase = _offers.PurchaseOfferAsync(offer, CancellationToken.None);
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));

            var caught = await CaptureAsync(() => purchase);

            Assert.That(caught, Is.InstanceOf<InsufficientFundsException>());
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(10), "Nothing was charged.");
            Assert.That(_world.Wallet.GetBalance("gems"), Is.Zero, "Nothing was granted.");
            Assert.That(_world.Inventory.UsedSlots, Is.Zero);
        }

        [Test]
        public async Task Purchase_CancelledMidFlight_CanBeRetried()
        {
            var offer = _offers.GetActiveOfferData();
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", offer.Price.Amount) }));

            using var cancellation = new CancellationTokenSource();
            var abandoned = _offers.PurchaseOfferAsync(offer, cancellation.Token);
            cancellation.Cancel();

            var caught = await CaptureAsync(() => abandoned);
            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(offer.Price.Amount), "A cancelled purchase costs nothing.");

            await _offers.PurchaseOfferAsync(offer, CancellationToken.None);

            Assert.That(_world.Wallet.GetBalance("gold"), Is.Zero, "The in-flight guard was released.");
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
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

        // A throwing observer must not abort the purchase between the grant and the charge,
        // which would leave the bundle granted for free.
        [Test]
        public async Task AThrowingInventoryObserver_CannotLeaveTheBundleUnpaid()
        {
            _world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1500) }));
            var offer = _offers.GetActiveOfferData();
            _world.Inventory.Changed += () => throw new InvalidOperationException("[Expected] inventory badge failure");
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] inventory badge failure"));

            var result = await _offers.PurchaseOfferAsync(offer, CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(_world.Wallet.GetBalance("gold"), Is.EqualTo(1500 - offer.Price.Amount));
            Assert.That(_world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
        }
    }
}
