using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Extensions;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Windows.RewardPopup;

namespace PopupSystem.UI.Windows.DailyReward
{
    public sealed class DailyRewardWindowController : WindowController<EmptyWindowData, DailyRewardWindowView>
    {
        private readonly DailyRewardManager _dailyRewardManager;
        private readonly IWindowsManager _windowsManager;
        private bool _isClaimInProgress;

        public DailyRewardWindowController(
            DailyRewardManager dailyRewardManager,
            IWindowsManager windowsManager)
        {
            _dailyRewardManager = dailyRewardManager;
            _windowsManager = windowsManager;
        }

        protected override UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            View.SetContent(
                "Daily Reward",
                "Today you can claim a daily reward chest.",
                "Claim");

            // The view can be a pooled instance left over from a previous claim, where the
            // button was disabled for the rest of that window's life (see OpenRewardPopupFlowAsync)
            // and never re-enabled since the window closed right after. Restore a clean baseline
            // on every Init rather than assuming a fresh view.
            View.SetActionInteractable(true);
            View.ClaimClicked += OnClaimClicked;
            return UniTask.CompletedTask;
        }

        public override void Dispose()
        {
            if (View != null)
            {
                View.ClaimClicked -= OnClaimClicked;
            }

            base.Dispose();
        }

        private void OnClaimClicked()
        {
            if (_isClaimInProgress)
            {
                return;
            }

            _isClaimInProgress = true;
            OpenRewardPopupFlowAsync().Forget();
        }

        private async UniTaskVoid OpenRewardPopupFlowAsync()
        {
            View.SetActionInteractable(false);

            // Share() (not Preserve() - see UniTaskShareExtensions) lets both this method and
            // RewardPopupController await the same claim concurrently: the popup opens right
            // away and drives its own loading state from it, while this method still needs the
            // outcome to restore the Claim button on failure.
            var claimTask = _dailyRewardManager.ClaimRewardAsync().Share();
            var rewardPopup = await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(claimTask));

            try
            {
                await claimTask;
            }
            catch
            {
                View.SetActionInteractable(true);
                _isClaimInProgress = false;
                throw;
            }

            await rewardPopup.WaitForCloseAsync();
            await Handle.CloseAsync();
        }
    }
}
