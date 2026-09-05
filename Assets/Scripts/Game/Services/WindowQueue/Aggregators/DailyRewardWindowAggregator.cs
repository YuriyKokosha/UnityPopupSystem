using System;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.DailyReward;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public sealed class DailyRewardWindowAggregator : IWindowQueueAggregator, IDisposable
    {
        private readonly DailyRewardManager _dailyRewardManager;

        public DailyRewardWindowAggregator(DailyRewardManager dailyRewardManager)
        {
            _dailyRewardManager = dailyRewardManager;
            _dailyRewardManager.AvailabilityChanged += OnAvailabilityChanged;
        }

        public event Action AvailabilityChanged;

        public WindowType WindowType => WindowType.DailyReward;

        public DateTime? NextAvailabilityChangeUtc =>
            IsAvailable() ? null : _dailyRewardManager.NextAvailableAtUtc;

        public bool IsAvailable()
        {
            return _dailyRewardManager.IsRewardAvailable();
        }

        public IWindowData CreatePayload()
        {
            return null;
        }

        public void Dispose()
        {
            _dailyRewardManager.AvailabilityChanged -= OnAvailabilityChanged;
        }

        private void OnAvailabilityChanged()
        {
            AvailabilityChanged?.Invoke();
        }
    }
}
