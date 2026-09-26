using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class RewardGrantServiceTests
    {
        private static readonly RewardBundle Mixed = new(
            new[] { new CurrencyAmount("gold", 100) },
            new[] { new ItemAmount(FakeRemoteConfigApi.SwordItemId, 1) });

        [Test]
        public void Grant_CreditsBothHalves()
        {
            var world = new TestGrants(slotLimit: 5);

            world.Grants.Grant(Mixed);

            Assert.That(world.Wallet.GetBalance("gold"), Is.EqualTo(100));
            Assert.That(world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
        }

        [Test]
        public void CanGrant_IsFalse_WhenTheInventoryIsFull()
        {
            var world = new TestGrants(slotLimit: 2);
            world.FillInventory();

            var result = world.Grants.CanGrant(Mixed);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.NotEnoughSlots));
        }

        [Test]
        public void Grant_LeavesTheWalletUntouched_WhenTheItemsDoNotFit()
        {
            var world = new TestGrants(slotLimit: 2);
            world.FillInventory();

            Assert.That(() => world.Grants.Grant(Mixed), Throws.InstanceOf<InventoryFullException>());

            Assert.That(world.Wallet.GetBalance("gold"), Is.Zero, "All or nothing: no currency without the items.");
        }

        [Test]
        public void ACurrencyOnlyBundle_Grants_EvenWithAFullInventory()
        {
            var world = new TestGrants(slotLimit: 1);
            world.FillInventory();
            var currenciesOnly = RewardBundle.OfCurrencies(new CurrencyAmount("gems", 5));

            Assert.That(world.Grants.CanGrant(currenciesOnly).Success, Is.True);

            world.Grants.Grant(currenciesOnly);

            Assert.That(world.Wallet.GetBalance("gems"), Is.EqualTo(5));
        }

        [Test]
        public void AnEmptyBundle_IsANoOp()
        {
            var world = new TestGrants(slotLimit: 1);
            var changed = 0;
            world.Inventory.Changed += () => changed++;
            world.Wallet.BalancesChanged += () => changed++;

            world.Grants.Grant(RewardBundle.Empty);

            Assert.That(changed, Is.Zero);
        }

        // A throwing inventory observer must not abort Grant after the items have landed -
        // that means no save, no currencies, and an exception inviting the player to claim again.
        [Test]
        public void AThrowingInventoryObserver_CannotAbortTheGrant_OrSkipTheSave()
        {
            var world = new TestGrants(slotLimit: 5);
            var savesBefore = world.Storage.SaveCount;
            world.Inventory.Changed += () => throw new InvalidOperationException("[Expected] UI subscriber failure");
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] UI subscriber failure"));
            var reward = new RewardBundle(
                new[] { new CurrencyAmount("gold", 500) },
                new[] { new ItemAmount(FakeRemoteConfigApi.HealthPotionItemId, 2) });

            world.Grants.Grant(reward);

            Assert.That(world.Inventory.GetCount(FakeRemoteConfigApi.HealthPotionItemId), Is.EqualTo(2));
            Assert.That(world.Wallet.GetBalance("gold"), Is.EqualTo(500), "The currency half still lands.");
            Assert.That(world.Storage.SaveCount, Is.EqualTo(savesBefore + 1), "The committed items are still saved.");
            Assert.That(world.Storage.LastSaved, Is.SameAs(world.Inventory.Snapshot));
        }

        [Test]
        public void AThrowingObserver_DoesNotStopTheOtherObservers()
        {
            var world = new TestGrants(slotLimit: 5);
            var laterObserverRan = false;
            world.Inventory.Changed += () => throw new InvalidOperationException("[Expected] first observer failure");
            world.Inventory.Changed += () => laterObserverRan = true;
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] first observer failure"));

            world.Grants.Grant(Mixed);

            Assert.That(laterObserverRan, Is.True);
        }

        [Test]
        public void Observers_RunOnlyOnceEveryHalfIsApplied()
        {
            var world = new TestGrants(slotLimit: 5);
            var goldSeenByInventoryObserver = -1;
            var swordsSeenByWalletObserver = -1;
            world.Inventory.Changed += () => goldSeenByInventoryObserver = world.Wallet.GetBalance("gold");
            world.Wallet.BalancesChanged += () =>
                swordsSeenByWalletObserver = world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId);

            world.Grants.Grant(Mixed);

            Assert.That(goldSeenByInventoryObserver, Is.EqualTo(100));
            Assert.That(swordsSeenByWalletObserver, Is.EqualTo(1));
        }

        [Test]
        public void GrantWithAPrice_ChargesAndGrants_WithOneNotificationPerService()
        {
            var world = new TestGrants(slotLimit: 5);
            world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1000) }));
            var walletEvents = 0;
            var inventoryEvents = 0;
            world.Wallet.BalancesChanged += () => walletEvents++;
            world.Inventory.Changed += () => inventoryEvents++;
            var reward = new RewardBundle(
                new[] { new CurrencyAmount("gems", 5) },
                new[] { new ItemAmount(FakeRemoteConfigApi.SwordItemId, 1) });

            world.Grants.Grant(reward, new CurrencyAmount("gold", 1000));

            Assert.That(world.Wallet.GetBalance("gold"), Is.Zero);
            Assert.That(world.Wallet.GetBalance("gems"), Is.EqualTo(5));
            Assert.That(world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.EqualTo(1));
            Assert.That(walletEvents, Is.EqualTo(1), "Credit and debit are one change.");
            Assert.That(inventoryEvents, Is.EqualTo(1));
        }

        // Re-entrancy: a subscriber runs user code, so it must run only after both the balance
        // check and the charge - then whatever it spends is checked against the real
        // balance and the wallet can never be charged twice for one check.
        [Test]
        public void AReentrantObserver_CannotSpendBetweenTheCheckAndTheCharge()
        {
            var world = new TestGrants(slotLimit: 5);
            world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1000) }));
            var goldWhenObserverRan = -1;
            Exception observerSpendError = null;
            world.Inventory.Changed += () =>
            {
                goldWhenObserverRan = world.Wallet.GetBalance("gold");
                try
                {
                    world.Wallet.Spend(new CurrencyAmount("gold", 1000));
                }
                catch (Exception ex)
                {
                    observerSpendError = ex;
                }
            };

            world.Grants.Grant(Mixed, new CurrencyAmount("gold", 1000));

            Assert.That(goldWhenObserverRan, Is.EqualTo(100), "The price was already charged, the credit already made.");
            Assert.That(observerSpendError, Is.InstanceOf<InsufficientFundsException>(), "The observer's own spend was refused.");
            Assert.That(world.Wallet.GetBalance("gold"), Is.EqualTo(100));
        }

        [Test]
        public void AnUnaffordablePrice_IsRefused_BeforeAnythingMoves()
        {
            var world = new TestGrants(slotLimit: 5);
            world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));
            var savesBefore = world.Storage.SaveCount;

            Exception caught = null;
            try
            {
                world.Grants.Grant(Mixed, new CurrencyAmount("gold", 1000));
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<InsufficientFundsException>());
            Assert.That(world.Wallet.GetBalance("gold"), Is.EqualTo(10));
            Assert.That(world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.Zero);
            Assert.That(world.Storage.SaveCount, Is.EqualTo(savesBefore));
        }

        // int.MaxValue + a positive credit must not wrap into a negative balance.
        [Test]
        public void ACreditPastTheBalanceLimit_IsRefused_BeforeAnythingMoves()
        {
            var world = new TestGrants(slotLimit: 5);
            world.Wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", WalletManager.MaxBalance) }));

            Exception caught = null;
            try
            {
                world.Grants.Grant(Mixed);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<BalanceLimitExceededException>());
            Assert.That(world.Wallet.GetBalance("gold"), Is.EqualTo(WalletManager.MaxBalance));
            Assert.That(world.Inventory.GetCount(FakeRemoteConfigApi.SwordItemId), Is.Zero, "No items without the currency.");
        }
    }
}
