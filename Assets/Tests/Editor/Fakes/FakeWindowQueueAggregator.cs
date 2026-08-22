using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>
    /// Deterministic stand-in for a real aggregator (see DailyRewardWindowAggregator /
    /// OfferWindowAggregator). Tests flip <see cref="Available"/>/<see cref="Payload"/> directly
    /// at whatever point in the test body they need to simulate "the reward just became
    /// claimable" or "the offer just expired", instead of driving a real game-state manager.
    /// </summary>
    public sealed class FakeWindowQueueAggregator : IWindowQueueAggregator
    {
        public FakeWindowQueueAggregator(WindowType windowType, bool available = true, IWindowData payload = null)
        {
            WindowType = windowType;
            Available = available;
            Payload = payload;
        }

        public WindowType WindowType { get; }
        public bool Available { get; set; }
        public IWindowData Payload { get; set; }

        public bool IsAvailable() => Available;

        public IWindowData CreatePayload() => Payload;
    }
}
