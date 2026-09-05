using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Time;

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

        private readonly PlayerInventoryManager _playerInventoryManager;
        private readonly IRpcManager _rpcManager;
        private readonly ITimeProvider _timeProvider;

        private OfferData _offer;

        public OfferManager(
            PlayerInventoryManager playerInventoryManager,
            IRpcManager rpcManager,
            ITimeProvider timeProvider)
        {
            _playerInventoryManager = playerInventoryManager;
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

        public async UniTask<RewardPopupData> PurchaseOfferAsync(OfferData offer, CancellationToken cancellationToken)
        {
            if (offer == null)
            {
                throw new InvalidOperationException("Offer data is required for purchase.");
            }

            if (!offer.IsActiveAt(_timeProvider.UtcNow))
            {
                throw new InvalidOperationException("Offer is no longer active.");
            }

            await UniTask.Delay(500, cancellationToken: cancellationToken);
            _playerInventoryManager.AddResources(offer.Rewards);

            return new RewardPopupData(
                "Offer Purchased",
                offer.Rewards);
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
                new[]
                {
                    new InventoryResource("gems", 500),
                    new InventoryResource("energy", 50),
                },
                startUtc,
                startUtc + OfferDuration);

            return _offer;
        }
    }
}
