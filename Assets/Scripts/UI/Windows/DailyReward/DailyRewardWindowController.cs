using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Extensions;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.UI.Runtime.Controller;
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
            var view = View;
            var handle = Handle;

            view.SetActionInteractable(false);

            // CancellationToken.None deliberately: a claim is a transaction, and the player closing the window
            // must not abandon it half-applied. Share() lets the popup await the same claim concurrently.
            var claimTask = _dailyRewardManager.ClaimRewardAsync(CancellationToken.None).Share();
            var rewardPopup = await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(claimTask));

            try
            {
                await claimTask;
            }
            catch (OperationCanceledException)
            {
                _isClaimInProgress = false;
                return;
            }
            catch
            {
                if (!handle.IsClosed)
                {
                    view.SetActionInteractable(true);
                }

                _isClaimInProgress = false;
                throw;
            }

            await rewardPopup.WaitForCloseAsync();

            if (!handle.IsClosed)
            {
                await handle.CloseAsync();
            }
        }
    }
}
