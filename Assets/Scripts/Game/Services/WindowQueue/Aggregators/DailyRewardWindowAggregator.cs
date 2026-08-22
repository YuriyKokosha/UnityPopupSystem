using PopupSystem.Game.Services.DailyReward;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public sealed class DailyRewardWindowAggregator : IWindowQueueAggregator
    {
        private readonly DailyRewardManager _dailyRewardManager;

        public DailyRewardWindowAggregator(DailyRewardManager dailyRewardManager)
        {
            _dailyRewardManager = dailyRewardManager;
        }

        public WindowType WindowType => WindowType.DailyReward;

        public bool IsAvailable()
        {
            return _dailyRewardManager.IsRewardAvailable();
        }

        public IWindowData CreatePayload()
        {
            return null;
        }
    }
}
