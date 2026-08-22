using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rpc;

namespace PopupSystem.Game.Services.Offer
{
    public sealed class OfferManager
    {
        // Served when the remote config endpoint is unreachable, so a network hiccup degrades to
        // generic-but-correct copy instead of a broken/empty offer popup.
        private static readonly OfferRemoteContent FallbackContent = new(
            "Special Offer",
            "A limited-time offer is available.",
            "Buy",
            bannerImageUrl: null);

        private readonly PlayerInventoryManager _playerInventoryManager;
        private readonly IRpcManager _rpcManager;

        public OfferManager(PlayerInventoryManager playerInventoryManager, IRpcManager rpcManager)
        {
            _playerInventoryManager = playerInventoryManager;
            _rpcManager = rpcManager;
        }

        public OfferData GetActiveOfferData()
        {
            var utcNow = DateTime.UtcNow;
            var offer = new OfferData(
                "starter-offer",
                new[]
                {
                    new PopupSystem.Game.Domain.Inventory.InventoryResource("gems", 500),
                    new PopupSystem.Game.Domain.Inventory.InventoryResource("energy", 50),
                },
                utcNow.AddDays(-1),
                utcNow.AddDays(7));

            return offer.IsActiveAt(utcNow) ? offer : null;
        }

        /// <summary>
        /// Fetches this offer's presentation (title/description/banner) from the remote config
        /// endpoint. Falls back to <see cref="FallbackContent"/> on any failure other than
        /// cancellation, so a flaky/unreachable backend never leaves the popup blank.
        /// </summary>
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

        public async UniTask<RewardPopupData> PurchaseOfferAsync(OfferData offer)
        {
            if (offer == null)
            {
                throw new InvalidOperationException("Offer data is required for purchase.");
            }

            if (!offer.IsActiveAt(DateTime.UtcNow))
            {
                throw new InvalidOperationException("Offer is no longer active.");
            }

            await UniTask.Delay(500);
            _playerInventoryManager.AddResources(offer.Rewards);

            return new RewardPopupData(
                "Offer Purchased",
                offer.Rewards);
        }
    }
}
