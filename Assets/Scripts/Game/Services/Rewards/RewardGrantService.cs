using System;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Wallet;

namespace PopupSystem.Game.Services.Rewards
{
    /// <summary>The one place a <see cref="RewardBundle"/> is applied, optionally together with the price paid
    /// for it, as one local transaction:
    /// <list type="number">
    /// <item>Check everything: the items fit, no balance would pass its limit, the price is covered. A refusal
    /// throws here, before anything has moved, so the whole grant reads as "nothing happened".</item>
    /// <item>Apply items, then currencies and price, with inventory and wallet observers held back. Nothing
    /// outside this method runs between the check and the last mutation, so no subscriber can re-enter the
    /// services and invalidate the check.</item>
    /// <item>Notify. Subscribers are isolated (see <c>StateChangeNotifier</c>): one that throws is logged and
    /// cannot turn a completed grant into a failed one, which would invite the player to claim it again.</item>
    /// </list>
    /// Feature managers call <see cref="CanGrant"/> before their (fake) server call and <see cref="Grant(RewardBundle, CurrencyAmount)"/>
    /// after it. A real backend settles this server-side; see Docs/feature-maps/game-services.md.</summary>
    public sealed class RewardGrantService
    {
        private readonly WalletManager _wallet;
        private readonly InventoryManager _inventory;

        public RewardGrantService(WalletManager wallet, InventoryManager inventory)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public InventoryOperationResult CanGrant(RewardBundle reward)
        {
            if (reward == null || reward.Items.Count == 0)
            {
                return InventoryOperationResult.Ok;
            }

            return _inventory.CanAdd(reward.Items);
        }

        public void Grant(RewardBundle reward)
        {
            Grant(reward, null);
        }

        /// <summary>Grants <paramref name="reward"/> and charges <paramref name="price"/> (may be null), all or
        /// nothing. Throws <see cref="InventoryFullException"/>, <see cref="InsufficientFundsException"/> or
        /// <see cref="BalanceLimitExceededException"/> before anything is applied.</summary>
        public void Grant(RewardBundle reward, CurrencyAmount price)
        {
            if (reward == null)
            {
                throw new ArgumentNullException(nameof(reward));
            }

            var hasItems = reward.Items.Count > 0;

            if (hasItems)
            {
                var space = _inventory.CanAdd(reward.Items);
                if (!space.Success)
                {
                    throw new InventoryFullException(space);
                }
            }

            _wallet.EnsureCanExchange(reward.Currencies, price);

            // Wallet scope disposed first, so balance observers run before inventory observers; either way every
            // mutation below is complete before the first of them runs.
            using (_inventory.DeferNotifications())
            using (_wallet.DeferNotifications())
            {
                if (hasItems)
                {
                    // Cannot be refused: CanAdd passed above and no code outside this method has run since.
                    var result = _inventory.TryAdd(reward.Items);
                    if (!result.Success)
                    {
                        throw new InvalidOperationException(
                            $"The inventory refused a grant it had just accepted ({result.Reason}).");
                    }
                }

                _wallet.Exchange(reward.Currencies, price);
            }
        }
    }
}
