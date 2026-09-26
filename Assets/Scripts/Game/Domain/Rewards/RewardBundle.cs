using System;
using System.Collections.Generic;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Domain.Rewards
{
    /// <summary>Everything a single grant hands the player: currencies go to the wallet, items go to inventory
    /// slots. Either half may be empty, never null.</summary>
    public sealed class RewardBundle
    {
        public static readonly RewardBundle Empty = new(Array.Empty<CurrencyAmount>(), Array.Empty<ItemAmount>());

        public IReadOnlyList<CurrencyAmount> Currencies { get; }
        public IReadOnlyList<ItemAmount> Items { get; }

        public RewardBundle(IReadOnlyList<CurrencyAmount> currencies, IReadOnlyList<ItemAmount> items)
        {
            Currencies = currencies ?? Array.Empty<CurrencyAmount>();
            Items = items ?? Array.Empty<ItemAmount>();
        }

        public static RewardBundle OfCurrencies(params CurrencyAmount[] currencies)
        {
            return new RewardBundle(currencies, Array.Empty<ItemAmount>());
        }

        public static RewardBundle OfItems(params ItemAmount[] items)
        {
            return new RewardBundle(Array.Empty<CurrencyAmount>(), items);
        }

        public bool IsEmpty => Currencies.Count == 0 && Items.Count == 0;
    }
}
