using System;

namespace PopupSystem.Game.Services.Wallet
{
    /// <summary>A credit was refused because it would take a balance past <see cref="WalletManager.MaxBalance"/>.
    /// Thrown before anything is applied, so neither the wallet nor the inventory has moved.</summary>
    public sealed class BalanceLimitExceededException : InvalidOperationException
    {
        public string CurrencyId { get; }
        public int Balance { get; }
        public long Delta { get; }

        public BalanceLimitExceededException(string currencyId, int balance, long delta)
            : base($"Crediting {delta} {currencyId} to a balance of {balance} would exceed {WalletManager.MaxBalance}.")
        {
            CurrencyId = currencyId;
            Balance = balance;
            Delta = delta;
        }
    }
}
