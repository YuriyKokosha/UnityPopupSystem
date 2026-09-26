using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Wallet;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class WalletManagerTests
    {
        private WalletManager _wallet;

        [SetUp]
        public void SetUp()
        {
            _wallet = new WalletManager();
        }

        private static Exception Capture(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        // Two valid positive credits must not sum (and wrap) into a negative balance.
        [Test]
        public void ACreditPastMaxBalance_IsRefused_AndChangesNothing()
        {
            _wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", WalletManager.MaxBalance) }));
            var changed = 0;
            _wallet.BalancesChanged += () => changed++;

            var caught = Capture(() => _wallet.AddCurrencies(new[] { new CurrencyAmount("gold", 1) }));

            Assert.That(caught, Is.InstanceOf<BalanceLimitExceededException>());
            Assert.That(_wallet.GetBalance("gold"), Is.EqualTo(WalletManager.MaxBalance));
            Assert.That(changed, Is.Zero);
        }

        [Test]
        public void ARefusedCurrency_KeepsTheOtherCreditsOfTheSameCallOut_Too()
        {
            _wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", WalletManager.MaxBalance) }));

            var caught = Capture(() => _wallet.AddCurrencies(new[]
            {
                new CurrencyAmount("gems", 5),
                new CurrencyAmount("gold", 1),
            }));

            Assert.That(caught, Is.InstanceOf<BalanceLimitExceededException>());
            Assert.That(_wallet.GetBalance("gems"), Is.Zero, "All or nothing across the whole call.");
        }

        [Test]
        public void TwoCreditsOfOneCurrency_ThatOnlyOverflowTogether_AreRefused()
        {
            var caught = Capture(() => _wallet.AddCurrencies(new[]
            {
                new CurrencyAmount("gold", int.MaxValue),
                new CurrencyAmount("gold", 1),
            }));

            Assert.That(caught, Is.InstanceOf<BalanceLimitExceededException>());
            Assert.That(_wallet.GetBalance("gold"), Is.Zero);
        }

        [Test]
        public void ACreditUpToMaxBalance_IsAccepted()
        {
            _wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", WalletManager.MaxBalance - 1) }));

            _wallet.AddCurrencies(new[] { new CurrencyAmount("gold", 1) });

            Assert.That(_wallet.GetBalance("gold"), Is.EqualTo(WalletManager.MaxBalance));
        }

        [Test]
        public void ANegativeCredit_IsAProgrammingError()
        {
            var caught = Capture(() => _wallet.AddCurrencies(new[] { new CurrencyAmount("gold", -5) }));

            Assert.That(caught, Is.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(_wallet.GetBalance("gold"), Is.Zero);
        }

        [Test]
        public void Spend_MoreThanTheBalance_IsRefused_AndChangesNothing()
        {
            _wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));

            var caught = Capture(() => _wallet.Spend(new CurrencyAmount("gold", 11)));

            Assert.That(caught, Is.InstanceOf<InsufficientFundsException>());
            Assert.That(_wallet.GetBalance("gold"), Is.EqualTo(10));
        }

        [Test]
        public void AThrowingObserver_CannotFailASpend_ThatHasAlreadyHappened()
        {
            _wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));
            _wallet.BalancesChanged += () => throw new InvalidOperationException("[Expected] balance label failure");
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] balance label failure"));

            var caught = Capture(() => _wallet.Spend(new CurrencyAmount("gold", 4)));

            Assert.That(caught, Is.Null);
            Assert.That(_wallet.GetBalance("gold"), Is.EqualTo(6));
        }
    }
}
