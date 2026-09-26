using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.Wallet;

namespace PopupSystem.Game.Services.Offer
{
    public sealed class OfferManager
    {
        private static readonly OfferRemoteContent FallbackContent = new(
            "Special Offer",
            "A limited-time offer is available.",
            "Buy",
            bannerImageUrl: null);

        private const string OfferId = "starter-offer";
        private static readonly TimeSpan OfferDuration = TimeSpan.FromDays(7);
        private static readonly CurrencyAmount OfferPrice = new("gold", 1000);

        private readonly RewardGrantService _grants;
        private readonly WalletManager _wallet;
        private readonly IRpcManager _rpcManager;
        private readonly ITimeProvider _timeProvider;

        private OfferData _offer;
        private bool _isPurchaseInFlight;

        public OfferManager(
            RewardGrantService grants,
            WalletManager wallet,
            IRpcManager rpcManager,
            ITimeProvider timeProvider)
        {
            _grants = grants;
            _wallet = wallet;
            _rpcManager = rpcManager;
            _timeProvider = timeProvider;
        }

        public DateTime? NextActivityChangeUtc
        {
            get
            {
                var offer = GetOrCreateOffer();
                var now = _timeProvider.UtcNow;

                if (now < offer.StartsAtUtc)
                {
                    return offer.StartsAtUtc;
                }

                return now <= offer.EndsAtUtc ? offer.EndsAtUtc : null;
            }
        }

        public OfferData GetActiveOfferData()
        {
            var offer = GetOrCreateOffer();
            return offer.IsActiveAt(_timeProvider.UtcNow) ? offer : null;
        }

        public InventoryOperationResult CanPurchaseIntoInventory(OfferData offer)
        {
            return _grants.CanGrant(offer?.Reward);
        }

        public bool CanAfford(OfferData offer)
        {
            return _wallet.CanAfford(offer?.Price);
        }

        public async UniTask<OfferRemoteContent> GetOfferContentAsync(OfferData offer, CancellationToken cancellationToken)
        {
            if (offer == null)
            {
                throw new ArgumentNullException(nameof(offer));
            }

            try
            {
                return await _rpcManager.RemoteConfig.GetOfferContentAsync(offer.OfferId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return FallbackContent;
            }
        }

        /// <summary>Charges the price and grants the bundle, all or nothing. Refuses
        /// (<see cref="InvalidOperationException"/>) while another purchase is still in flight, and re-checks every
        /// precondition after the simulated round-trip, right before anything moves.</summary>
        public async UniTask<RewardPopupData> PurchaseOfferAsync(OfferData offer, CancellationToken cancellationToken)
        {
            if (offer == null)
            {
                throw new InvalidOperationException("Offer data is required for purchase.");
            }

            if (_isPurchaseInFlight)
            {
                throw new InvalidOperationException("A purchase is already in progress.");
            }

            EnsureCanPurchase(offer);

            _isPurchaseInFlight = true;
            try
            {
                await UniTask.Delay(500, cancellationToken: cancellationToken);

                // The world may have moved during the round-trip (time, wallet, inventory): check again, then
                // grant and charge as one local transaction. Grant re-validates items, balances and the price
                // together before anything moves and holds every observer back until both halves are applied, so
                // no subscriber can run between the check and the charge. A real client would settle both on the
                // server in one transaction.
                EnsureCanPurchase(offer);
                _grants.Grant(offer.Reward, offer.Price);
            }
            finally
            {
                _isPurchaseInFlight = false;
            }

            return new RewardPopupData(
                "Offer Purchased",
                offer.Reward);
        }

        private void EnsureCanPurchase(OfferData offer)
        {
            if (!offer.IsActiveAt(_timeProvider.UtcNow))
            {
                throw new InvalidOperationException("Offer is no longer active.");
            }

            var space = _grants.CanGrant(offer.Reward);
            if (!space.Success)
            {
                throw new InventoryFullException(space);
            }

            if (!_wallet.CanAfford(offer.Price))
            {
                throw new InsufficientFundsException(offer.Price, _wallet.GetBalance(offer.Price.CurrencyId));
            }
        }

        private OfferData GetOrCreateOffer()
        {
            if (_offer != null)
            {
                return _offer;
            }

            var startUtc = _timeProvider.UtcNow;
            _offer = new OfferData(
                OfferId,
                OfferPrice,
                new RewardBundle(
                    new[]
                    {
                        new CurrencyAmount("gems", 500),
                        new CurrencyAmount("energy", 50),
                    },
                    new[]
                    {
                        new ItemAmount(DemoItemIds.Sword, 1),
                    }),
                startUtc,
                startUtc + OfferDuration);

            return _offer;
        }
    }
}
