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

            // Deliberately not awaited here: WindowsManager only shows the window (Show() +
            // transition) once InitializeAsync fully completes (see OpenInstanceAsync), so
            // awaiting the reward here would defeat the loading state above - the popup would
            // stay hidden for the entire wait and only appear once already resolved. Returning
            // immediately lets the popup actually open with its loading state visible, then
            // ObserveRewardAsync fills it in once the reward resolves.
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
            catch (Exception ex)
            {
                failure = ex;
            }

            // The window can be dismissed (close button / backdrop tap) while the reward is
            // still resolving; WindowsManager cancels this instance's token immediately when
            // that happens, before View is torn down. Bail out here rather than touching a View
            // that may already be destroyed - there is nothing left to show anyway.
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
