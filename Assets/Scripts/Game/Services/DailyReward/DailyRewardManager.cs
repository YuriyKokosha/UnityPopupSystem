using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Services.Time;

namespace PopupSystem.Game.Services.DailyReward
{
    public sealed class DailyRewardManager
    {
        private static readonly TimeSpan RewardInterval = TimeSpan.FromMinutes(1);

        private readonly ITimeProvider _timeProvider;

        private DateTime _nextAvailableAtUtc = DateTime.MinValue;

        public DailyRewardManager(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public event Action AvailabilityChanged;

        public DateTime NextAvailableAtUtc => _nextAvailableAtUtc;

        public bool IsRewardAvailable()
        {
            return _timeProvider.UtcNow >= _nextAvailableAtUtc;
        }

        public async UniTask<RewardPopupData> ClaimRewardAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(500, cancellationToken: cancellationToken);
            _nextAvailableAtUtc = _timeProvider.UtcNow + RewardInterval;
            AvailabilityChanged?.Invoke();

            return new RewardPopupData(
                "Daily Reward Claimed",
                new[]
                {
                    new InventoryResource("gold", 500),
                    new InventoryResource("gems", 10),
                });
        }
    }
}
