using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.Rewards;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupRequest : IWindowData
    {
        public UniTask<RewardPopupData> RewardTask { get; }

        public RewardPopupRequest(UniTask<RewardPopupData> rewardTask)
        {
            RewardTask = rewardTask;
        }
    }
}
