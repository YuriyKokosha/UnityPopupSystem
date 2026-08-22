using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Extensions;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Services.Offer;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.RewardPopup;

namespace PopupSystem.UI.Windows.Offer
{
    public sealed class OfferWindowController : WindowController<EmptyWindowData, OfferWindowView>
    {
        private readonly OfferManager _offerManager;
        private readonly IWindowsManager _windowsManager;
        private readonly IRemoteImageLoader _imageLoader;
        private bool _isPurchaseInProgress;
        private OfferData _offerData;

        public OfferWindowController(
            OfferManager offerManager,
            IWindowsManager windowsManager,
            IRemoteImageLoader imageLoader)
        {
            _offerManager = offerManager;
            _windowsManager = windowsManager;
            _imageLoader = imageLoader;
        }

        protected override UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            _offerData = _offerManager.GetActiveOfferData();
            View.BuyClicked += OnBuyClicked;

            if (_offerData == null)
            {
                View.SetContent("Offer", "No active offer.", "Close");
                View.SetBannerLoading(false);
                View.SetBannerFallback();
                return UniTask.CompletedTask;
            }

            // The offer's own timing/rewards are known locally, but its copy and banner are not -
            // show a neutral placeholder immediately and keep loading in the background rather
            // than awaiting it here: WindowsManager only shows the window (Show() + transition)
            // once InitializeAsync fully completes (see OpenInstanceAsync), so awaiting the fetch
            // here would keep the window hidden for the entire wait and defeat the loading state
            // below - it would only ever appear already-loaded.
            View.SetContent("Offer", "Loading offer...", string.Empty);
            LoadRemoteContentAsync(cancellationToken).Forget();
            return UniTask.CompletedTask;
        }

        public override void Dispose()
        {
            if (View != null)
            {
                View.BuyClicked -= OnBuyClicked;
            }

            base.Dispose();
        }

        private async UniTaskVoid LoadRemoteContentAsync(CancellationToken cancellationToken)
        {
            View.SetBannerLoading(true);

            OfferRemoteContent content;
            try
            {
                // OfferManager already degrades a remote-config failure to safe fallback copy,
                // so only cancellation (window closed while we were fetching) can still throw.
                content = await _offerManager.GetOfferContentAsync(_offerData, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            // The window can be dismissed while the fetch is still in flight; WindowsManager
            // cancels this instance's token immediately when that happens, before View is torn
            // down. Bail out rather than touching a View that may already be destroyed.
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            View.SetContent(content.Title, content.Description, content.ActionText);

            if (string.IsNullOrEmpty(content.BannerImageUrl))
            {
                View.SetBannerLoading(false);
                View.SetBannerFallback();
                return;
            }

            try
            {
                var texture = await _imageLoader.LoadAsync(content.BannerImageUrl, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                View.SetBannerTexture(texture);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // Missing/unreachable banner asset must not break the offer itself - the copy
                // and the buy button are still fully usable, just without the promo image.
                if (!cancellationToken.IsCancellationRequested)
                {
                    View.SetBannerFallback();
                }
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    View.SetBannerLoading(false);
                }
            }
        }

        private void OnBuyClicked()
        {
            if (_offerData == null)
            {
                Handle.CloseAsync().Forget();
                return;
            }

            if (_isPurchaseInProgress)
            {
                return;
            }

            _isPurchaseInProgress = true;
            PurchaseOfferFlowAsync().Forget();
        }

        private async UniTaskVoid PurchaseOfferFlowAsync()
        {
            // Same reasoning as DailyRewardWindowController: open the reward popup right away
            // with the purchase still in flight (Share() - not Preserve(), see
            // UniTaskShareExtensions - lets both this method and RewardPopupController await it
            // concurrently) instead of waiting for it to resolve first - the popup's own loading
            // state is then real, not a stub.
            var purchaseTask = _offerManager.PurchaseOfferAsync(_offerData).Share();
            var rewardPopup = await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(purchaseTask));

            try
            {
                await purchaseTask;
            }
            catch
            {
                _isPurchaseInProgress = false;
                throw;
            }

            await Handle.CloseAsync();
            await rewardPopup.WaitForCloseAsync();
        }
    }
}
