using System;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Services.Wallet
{
    /// <summary>A spend was refused because the balance does not cover it. Thrown before anything is applied, so
    /// neither the wallet nor the inventory has moved.</summary>
    public sealed class InsufficientFundsException : InvalidOperationException
    {
        public CurrencyAmount Price { get; }
        public int Balance { get; }

        public InsufficientFundsException(CurrencyAmount price, int balance)
            : base($"Not enough {price.CurrencyId}: {price.Amount} needed, {balance} available.")
        {
            Price = price;
            Balance = balance;
        }
    }
}
