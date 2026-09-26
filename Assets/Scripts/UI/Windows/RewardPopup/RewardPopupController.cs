using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Services;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupController : WindowController<RewardPopupRequest, RewardPopupView>
    {
        private const string GenericFailureText = "Couldn't claim your reward. Please try again.";
        private const string InventoryFullText = "Your inventory is full. Free some slots and try again.";
        private const string InsufficientFundsText = "Not enough currency for this purchase.";

        private readonly RewardIcons _icons;

        public RewardPopupController(RewardIcons icons)
        {
            _icons = icons;
        }

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

        private static string DescribeFailure(Exception failure)
        {
            return failure switch
            {
                InventoryFullException => InventoryFullText,
                InsufficientFundsException => InsufficientFundsText,
                _ => GenericFailureText,
            };
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
                View.SetRewardText(DescribeFailure(failure));
                return;
            }

            try
            {
                await _icons.PreloadAsync(data?.Reward, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            View.SetTitle(data?.Title ?? "Reward");
            View.SetRewards(_icons.Describe(data?.Reward));
            View.SetLoading(false);
        }
    }
}
