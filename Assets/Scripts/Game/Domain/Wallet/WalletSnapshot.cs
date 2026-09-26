using System.Collections.Generic;

namespace PopupSystem.Game.Domain.Wallet
{
    public sealed class WalletSnapshot
    {
        public IReadOnlyList<CurrencyAmount> Balances { get; }

        public WalletSnapshot(IReadOnlyList<CurrencyAmount> balances)
        {
            Balances = balances;
        }
    }
}
