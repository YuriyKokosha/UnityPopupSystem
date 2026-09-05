using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Extensions;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Services.Offer;
using PopupSystem.UI.Runtime.Controller;
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
            var view = View;

            view.SetBannerLoading(true);

            OfferRemoteContent content;
            try
            {
                content = await _offerManager.GetOfferContentAsync(_offerData, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            view.SetContent(content.Title, content.Description, content.ActionText);

            if (string.IsNullOrEmpty(content.BannerImageUrl))
            {
                view.SetBannerLoading(false);
                view.SetBannerFallback();
                return;
            }

            try
            {
                var texture = await _imageLoader.LoadAsync(content.BannerImageUrl, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                view.SetBannerTexture(texture);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    view.SetBannerFallback();
                }
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    view.SetBannerLoading(false);
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
            var handle = Handle;

            // CancellationToken.None deliberately: this window is the one the queue may force-close, and the
            // lifetime token would cancel a payment the backend may already have taken.
            var purchaseTask = _offerManager.PurchaseOfferAsync(_offerData, CancellationToken.None).Share();
            var rewardPopup = await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(purchaseTask));

            try
            {
                await purchaseTask;
            }
            catch (OperationCanceledException)
            {
                _isPurchaseInProgress = false;
                return;
            }
            catch
            {
                _isPurchaseInProgress = false;
                throw;
            }

            if (!handle.IsClosed)
            {
                await handle.CloseAsync();
            }

            await rewardPopup.WaitForCloseAsync();
        }
    }
}
