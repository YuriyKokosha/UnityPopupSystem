using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.Widgets;
using UnityEngine;

namespace PopupSystem.UI.Services
{
    /// <summary>Turns currencies, items and reward bundles into <see cref="IconAmountModel"/>s. Items take their
    /// icon from the catalog's <see cref="ItemDefinition.IconAddress"/>; a currency has no config of its own, so
    /// its address is the convention <c>UI/Currencies/&lt;currencyId&gt;</c>.
    /// <para>Two steps on purpose: <c>Preload*Async</c> loads what a window is about to show (and never throws
    /// for a missing icon — it logs and moves on), the <c>Describe*</c> methods are synchronous and only read what
    /// is already loaded. A window awaits the preload once and then redraws on every change without awaiting.</para></summary>
    public sealed class RewardIcons
    {
        public const string CurrencyAddressPrefix = "UI/Currencies/";

        /// <summary>Balance order on screen. Anything not listed follows, alphabetically.</summary>
        public static readonly IReadOnlyList<string> CurrencyDisplayOrder = new[] { "gold", "gems", "energy" };

        private readonly IUiIconProvider _icons;
        private readonly InventoryManager _inventory;

        public RewardIcons(IUiIconProvider icons, InventoryManager inventory)
        {
            _icons = icons;
            _inventory = inventory;
        }

        public static string CurrencyAddress(string currencyId) => CurrencyAddressPrefix + currencyId;

        public static string FormatAmount(int amount)
        {
            return amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');
        }

        public string ItemAddress(string itemId)
        {
            return _inventory.Catalog.TryGet(itemId, out var definition) ? definition.IconAddress : null;
        }

        public UniTask PreloadCurrenciesAsync(IEnumerable<string> currencyIds, CancellationToken cancellationToken)
        {
            var addresses = new List<string>();

            foreach (var id in currencyIds)
            {
                addresses.Add(CurrencyAddress(id));
            }

            return PreloadAsync(addresses, cancellationToken);
        }

        public UniTask PreloadCatalogAsync(CancellationToken cancellationToken)
        {
            var definitions = _inventory.Catalog.Definitions;
            var addresses = new List<string>(definitions.Count);

            for (var i = 0; i < definitions.Count; i++)
            {
                addresses.Add(definitions[i].IconAddress);
            }

            return PreloadAsync(addresses, cancellationToken);
        }

        public UniTask PreloadAsync(RewardBundle reward, CancellationToken cancellationToken)
        {
            if (reward == null || reward.IsEmpty)
            {
                return UniTask.CompletedTask;
            }

            var addresses = new List<string>(reward.Currencies.Count + reward.Items.Count);

            for (var i = 0; i < reward.Currencies.Count; i++)
            {
                addresses.Add(CurrencyAddress(reward.Currencies[i].CurrencyId));
            }

            for (var i = 0; i < reward.Items.Count; i++)
            {
                addresses.Add(ItemAddress(reward.Items[i].ItemId));
            }

            return PreloadAsync(addresses, cancellationToken);
        }

        public Sprite GetCurrencyIcon(string currencyId) => _icons.GetLoaded(CurrencyAddress(currencyId));

        public Sprite GetItemIcon(string itemId) => _icons.GetLoaded(ItemAddress(itemId));

        public IconAmountModel Describe(CurrencyAmount currency)
        {
            return new IconAmountModel(
                GetCurrencyIcon(currency.CurrencyId),
                FormatAmount(currency.Amount),
                currency.CurrencyId);
        }

        public IconAmountModel Describe(ItemAmount item)
        {
            var name = _inventory.Catalog.TryGet(item.ItemId, out var definition) ? definition.DisplayName : item.ItemId;
            return new IconAmountModel(GetItemIcon(item.ItemId), "x" + FormatAmount(item.Count), name);
        }

        /// <summary>Currencies first, then items, each in bundle order — the order the bundle was authored in.</summary>
        public IReadOnlyList<IconAmountModel> Describe(RewardBundle reward)
        {
            if (reward == null || reward.IsEmpty)
            {
                return Array.Empty<IconAmountModel>();
            }

            var models = new List<IconAmountModel>(reward.Currencies.Count + reward.Items.Count);

            for (var i = 0; i < reward.Currencies.Count; i++)
            {
                models.Add(Describe(reward.Currencies[i]));
            }

            for (var i = 0; i < reward.Items.Count; i++)
            {
                models.Add(Describe(reward.Items[i]));
            }

            return models;
        }

        public IReadOnlyList<IconAmountModel> DescribeBalances(IReadOnlyList<CurrencyAmount> balances)
        {
            var sorted = new List<CurrencyAmount>(balances ?? Array.Empty<CurrencyAmount>());
            sorted.Sort(CompareForDisplay);

            var models = new List<IconAmountModel>(sorted.Count);

            for (var i = 0; i < sorted.Count; i++)
            {
                models.Add(Describe(sorted[i]));
            }

            return models;
        }

        private async UniTask PreloadAsync(IReadOnlyList<string> addresses, CancellationToken cancellationToken)
        {
            var loads = new List<UniTask>(addresses.Count);

            for (var i = 0; i < addresses.Count; i++)
            {
                if (!string.IsNullOrEmpty(addresses[i]) && _icons.GetLoaded(addresses[i]) == null)
                {
                    loads.Add(LoadOneAsync(addresses[i], cancellationToken));
                }
            }

            await UniTask.WhenAll(loads);
        }

        // A missing icon is a content bug, not a reason to fail the window: the entry falls back to its label.
        private async UniTask LoadOneAsync(string address, CancellationToken cancellationToken)
        {
            try
            {
                await _icons.LoadAsync(address, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RewardIcons] Icon '{address}' did not load; showing its label instead. {exception.Message}");
            }
        }

        private static int CompareForDisplay(CurrencyAmount a, CurrencyAmount b)
        {
            var rankA = Rank(a.CurrencyId);
            var rankB = Rank(b.CurrencyId);

            return rankA != rankB ? rankA.CompareTo(rankB) : string.CompareOrdinal(a.CurrencyId, b.CurrencyId);
        }

        private static int Rank(string currencyId)
        {
            for (var i = 0; i < CurrencyDisplayOrder.Count; i++)
            {
                if (CurrencyDisplayOrder[i] == currencyId)
                {
                    return i;
                }
            }

            return CurrencyDisplayOrder.Count;
        }
    }
}
