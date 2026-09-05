using System;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.Offer;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public sealed class OfferWindowAggregator : IWindowQueueAggregator
    {
        private readonly OfferManager _offerManager;

        public OfferWindowAggregator(OfferManager offerManager)
        {
            _offerManager = offerManager;
        }

        public event Action AvailabilityChanged;

        public WindowType WindowType => WindowType.Offer;

        public DateTime? NextAvailabilityChangeUtc => _offerManager.NextActivityChangeUtc;

        public bool IsAvailable()
        {
            return _offerManager.GetActiveOfferData() != null;
        }

        public IWindowData CreatePayload()
        {
            return null;
        }
    }
}
