using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupController : WindowController<RewardPopupRequest, RewardPopupView>
    {
        protected override UniTask OnInitializeAsync(RewardPopupRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                View.SetLoading(false);
                View.SetTitle("Reward");
                View.SetRewardText(string.Empty);
                return UniTask.CompletedTask;
            }

            View.SetTitle("Reward");
            View.SetRewardText(string.Empty);
            View.SetLoading(true);

            ObserveRewardAsync(request.RewardTask, cancellationToken).Forget();
            return UniTask.CompletedTask;
        }

        private async UniTaskVoid ObserveRewardAsync(UniTask<RewardPopupData> rewardTask, CancellationToken cancellationToken)
        {
            RewardPopupData data = null;
            Exception failure = null;

            try
            {
                data = await rewardTask;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (failure != null)
            {
                View.SetLoading(false);
                View.SetRewardText("Couldn't claim your reward. Please try again.");
                return;
            }

            View.SetTitle(data?.Title ?? "Reward");
            var summary = data?.Rewards == null
                ? string.Empty
                : string.Join(", ", data.Rewards.Select(x => $"{x.ResourceId}: {x.Amount}"));
            View.SetRewardText(summary);
            View.SetLoading(false);
        }
    }
}
