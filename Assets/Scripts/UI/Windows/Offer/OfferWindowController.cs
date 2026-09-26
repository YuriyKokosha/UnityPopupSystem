using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Extensions;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.RewardPopup;

namespace PopupSystem.UI.Windows.Offer
{
    public sealed class OfferWindowController : WindowController<EmptyWindowData, OfferWindowView>
    {
        private const string InventoryFullText = "Inventory full";
        private const string LoadingText = "Loading...";

        private readonly OfferManager _offerManager;
        private readonly InventoryManager _inventoryManager;
        private readonly WalletManager _walletManager;
        private readonly IWindowsManager _windowsManager;
        private readonly IRemoteImageLoader _imageLoader;
        private readonly RewardIcons _icons;
        private bool _isPurchaseInProgress;
        private OfferData _offerData;
        private OfferRemoteContent _content;

        public OfferWindowController(
            OfferManager offerManager,
            InventoryManager inventoryManager,
            WalletManager walletManager,
            IWindowsManager windowsManager,
            IRemoteImageLoader imageLoader,
            RewardIcons icons)
        {
            _icons = icons;
            _offerManager = offerManager;
            _inventoryManager = inventoryManager;
            _walletManager = walletManager;
            _windowsManager = windowsManager;
            _imageLoader = imageLoader;
        }

        protected override async UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            _offerData = _offerManager.GetActiveOfferData();
            View.BuyClicked += OnBuyClicked;
            View.SetActionInteractable(true);

            if (_offerData == null)
            {
                View.SetContent("Offer", "No active offer.", "Close");
                View.SetReward(null);
                View.SetBannerLoading(false);
                View.SetBannerFallback();
                return;
            }

            // The reward is local (it is the offer's bundle), only the copy and the banner are remote: the icons
            // are on screen from the first frame, next to the "Loading offer..." placeholder.
            await _icons.PreloadAsync(_offerData.Reward, cancellationToken);
            View.SetReward(_icons.Describe(_offerData.Reward));

            _inventoryManager.Changed += RefreshBuyButton;
            _walletManager.BalancesChanged += RefreshBuyButton;
            // Disabled until the copy arrives: an amber, clickable button with no label would sell the offer
            // before the player has read what it is (the "Offer - loading" mockup still shows that state; see Docs/mockups/README.md).
            View.SetActionInteractable(false);
            View.SetContent("Offer", "Loading offer...", LoadingText);
            LoadRemoteContentAsync(cancellationToken).Forget();
        }

        public override void Dispose()
        {
            _inventoryManager.Changed -= RefreshBuyButton;
            _walletManager.BalancesChanged -= RefreshBuyButton;

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

            _content = content;
            RefreshBuyButton();

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

        // Same rule as the daily reward: a full inventory or an empty wallet disables Buy with a reason, and the
        // button returns when a slot frees up or the balance changes. Copy is only written once remote content has
        // arrived, so the loading text is not lost.
        private void RefreshBuyButton()
        {
            if (_isPurchaseInProgress || View == null || _content == null || _offerData == null)
            {
                return;
            }

            var hasSpace = _offerManager.CanPurchaseIntoInventory(_offerData).Success;
            var canAfford = _offerManager.CanAfford(_offerData);

            View.SetActionInteractable(hasSpace && canAfford);
            View.SetContent(_content.Title, _content.Description, ActionLabel(hasSpace, canAfford));
        }

        private string ActionLabel(bool hasSpace, bool canAfford)
        {
            if (!hasSpace)
            {
                return InventoryFullText;
            }

            var price = _offerData.Price;

            if (!canAfford)
            {
                return $"Not enough {price.CurrencyId}";
            }

            return _offerData.IsFree
                ? _content.ActionText
                : $"{_content.ActionText} for {RewardIcons.FormatAmount(price.Amount)} {price.CurrencyId}";
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

            View.SetActionInteractable(false);

            // CancellationToken.None deliberately: this window is the one the queue may force-close, and the
            // lifetime token would cancel a payment the backend may already have taken.
            var purchaseTask = _offerManager.PurchaseOfferAsync(_offerData, CancellationToken.None).Share();
            // A failed popup returns null instead of throwing: the purchase is already running and its outcome, not
            // the popup's, decides whether the button comes back.
            var rewardPopup = await RewardPopupLauncher.TryOpenAsync(_windowsManager, purchaseTask);

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

                if (!handle.IsClosed)
                {
                    RefreshBuyButton();
                }

                throw;
            }

            if (!handle.IsClosed)
            {
                await handle.CloseAsync();
            }

            if (rewardPopup != null)
            {
                await rewardPopup.WaitForCloseAsync();
            }
        }
    }
}
