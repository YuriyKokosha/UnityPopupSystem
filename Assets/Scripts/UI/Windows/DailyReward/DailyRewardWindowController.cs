using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Extensions;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.RewardPopup;

namespace PopupSystem.UI.Windows.DailyReward
{
    public sealed class DailyRewardWindowController : WindowController<EmptyWindowData, DailyRewardWindowView>
    {
        private const string ClaimText = "Claim";
        private const string InventoryFullText = "Inventory full";
        private const string ReadyText = "Your daily reward is ready.";
        private const string NoSpaceText = "Free up inventory slots to claim today's reward.";
        private const string CooldownText = "Your next reward is on its way.";

        private readonly DailyRewardManager _dailyRewardManager;
        private readonly InventoryManager _inventoryManager;
        private readonly IWindowsManager _windowsManager;
        private readonly RewardIcons _icons;
        private bool _isClaimInProgress;
        private bool _isCooldownTicking;
        private CancellationToken _lifetime;

        public DailyRewardWindowController(
            DailyRewardManager dailyRewardManager,
            InventoryManager inventoryManager,
            IWindowsManager windowsManager,
            RewardIcons icons)
        {
            _icons = icons;
            _dailyRewardManager = dailyRewardManager;
            _inventoryManager = inventoryManager;
            _windowsManager = windowsManager;
        }

        protected override async UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            _lifetime = cancellationToken;
            await _icons.PreloadAsync(_dailyRewardManager.Reward, cancellationToken);

            View.SetContent("Daily Reward", ReadyText, ClaimText);
            View.SetReward(_icons.Describe(_dailyRewardManager.Reward));

            RefreshClaimButton();
            _inventoryManager.Changed += RefreshClaimButton;
            View.ClaimClicked += OnClaimClicked;
        }

        public override void Dispose()
        {
            _inventoryManager.Changed -= RefreshClaimButton;

            if (View != null)
            {
                View.ClaimClicked -= OnClaimClicked;
            }

            base.Dispose();
        }

        // A full inventory disables the button rather than hiding the window: the player has to see why the
        // reward is not coming, and the button comes back the moment a slot frees up (InventoryManager.Changed).
        private void RefreshClaimButton()
        {
            if (_isClaimInProgress || View == null)
            {
                return;
            }

            if (!_dailyRewardManager.IsRewardAvailable())
            {
                View.SetContent("Daily Reward", CooldownText, ClaimText);
                View.ShowCooldown(FormatRemaining(_dailyRewardManager.TimeUntilAvailable));
                StartCooldownTicker();
                return;
            }

            View.ShowClaim();
            var hasSpace = _dailyRewardManager.CanClaimIntoInventory().Success;
            View.SetActionInteractable(hasSpace);
            View.SetContent(
                "Daily Reward",
                hasSpace ? ReadyText : NoSpaceText,
                hasSpace ? ClaimText : InventoryFullText);
        }

        private void StartCooldownTicker()
        {
            if (_isCooldownTicking || _lifetime.IsCancellationRequested)
            {
                return;
            }

            _isCooldownTicking = true;
            TickCooldownAsync(View, _lifetime).Forget();
        }

        // Wakes on each whole-second boundary of the remaining time (the label rounds up), and hands the slot back
        // to the button the moment the reward is claimable again. Real-time delay, server-anchored remaining time.
        private async UniTaskVoid TickCooldownAsync(DailyRewardWindowView view, CancellationToken cancellationToken)
        {
            while (!_dailyRewardManager.IsRewardAvailable())
            {
                var remaining = _dailyRewardManager.TimeUntilAvailable;
                view.ShowCooldown(FormatRemaining(remaining));

                var wholeSeconds = Math.Ceiling(remaining.TotalSeconds);
                var untilNextTick = (int)Math.Ceiling(remaining.TotalMilliseconds - (wholeSeconds - 1) * 1000d);

                var cancelled = await UniTask
                    .Delay(Math.Clamp(untilNextTick, 1, 1000), ignoreTimeScale: true, cancellationToken: cancellationToken)
                    .SuppressCancellationThrow();

                if (cancelled)
                {
                    return;
                }
            }

            _isCooldownTicking = false;
            RefreshClaimButton();
        }

        internal static string FormatRemaining(TimeSpan remaining)
        {
            var seconds = (long)Math.Ceiling(remaining.TotalSeconds);
            var rounded = TimeSpan.FromSeconds(seconds);

            return rounded.TotalHours >= 1
                ? $"{(int)rounded.TotalHours:00}:{rounded.Minutes:00}:{rounded.Seconds:00}"
                : $"{rounded.Minutes:00}:{rounded.Seconds:00}";
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
            // A failed popup returns null instead of throwing: the claim is already running and its outcome, not the
            // popup's, decides whether the button comes back.
            var rewardPopup = await RewardPopupLauncher.TryOpenAsync(_windowsManager, claimTask);

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
                _isClaimInProgress = false;

                if (!handle.IsClosed)
                {
                    RefreshClaimButton();
                }

                throw;
            }

            if (rewardPopup != null)
            {
                await rewardPopup.WaitForCloseAsync();
            }

            if (!handle.IsClosed)
            {
                await handle.CloseAsync();
            }
        }
    }
}
