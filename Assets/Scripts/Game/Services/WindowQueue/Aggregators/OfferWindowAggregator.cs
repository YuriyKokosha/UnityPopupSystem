using PopupSystem.Game.Services.Offer;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public sealed class OfferWindowAggregator : IWindowQueueAggregator
    {
        private readonly OfferManager _offerManager;

        public OfferWindowAggregator(OfferManager offerManager)
        {
            _offerManager = offerManager;
        }

        public WindowType WindowType => WindowType.Offer;

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
