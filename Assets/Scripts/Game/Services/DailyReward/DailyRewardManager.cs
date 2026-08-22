using System;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;

namespace PopupSystem.Game.Services.DailyReward
{
    public sealed class DailyRewardManager
    {
        private DateTime _nextAvailableAtUtc = DateTime.UtcNow;
        
        public bool IsRewardAvailable()
        {
            return DateTime.UtcNow >= _nextAvailableAtUtc;
        }

        public async UniTask<RewardPopupData> ClaimRewardAsync()
        {
            await UniTask.Delay(500);
            _nextAvailableAtUtc = DateTime.UtcNow.AddMinutes(1);

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
