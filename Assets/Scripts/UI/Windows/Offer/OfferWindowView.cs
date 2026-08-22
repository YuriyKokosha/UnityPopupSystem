using System;
using PopupSystem.UI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.Offer
{
    public sealed class OfferWindowView : WindowView
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Button _buyButton;
        [SerializeField] private Text _buyButtonText;
        [SerializeField] private Button _closeButton;

        // Remote-sourced promo banner: exactly one of "showing the downloaded image",
        // "loading", or "fallback" is active at any time.
        [SerializeField] private RawImage _bannerImage;
        [SerializeField] private GameObject _bannerLoadingIndicator;
        [SerializeField] private GameObject _bannerFallback;

        public event Action BuyClicked;

        private void Awake()
        {
            _buyButton.onClick.AddListener(OnBuyClicked);
            _closeButton.onClick.AddListener(RequestClose);

            SetBannerLoading(false);
        }

        private void OnDestroy()
        {
            if (_buyButton != null)
            {
                _buyButton.onClick.RemoveListener(OnBuyClicked);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }

            ReleaseBannerTexture();
        }

        /// <summary>
        /// This view is pooled rather than destroyed on close (see WindowFactory) - the
        /// downloaded banner texture is the one piece of state a fresh OfferWindowController
        /// wouldn't otherwise clear before its own load replaces it, so without this the pooled
        /// instance would briefly show the *previous* offer's banner underneath the "Loading
        /// banner..." indicator the next time it's reused.
        /// </summary>
        internal override void ResetForPool()
        {
            ReleaseBannerTexture();

            if (_bannerImage != null)
            {
                _bannerImage.gameObject.SetActive(false);
            }

            if (_bannerFallback != null)
            {
                _bannerFallback.SetActive(false);
            }

            SetBannerLoading(false);
        }

        private void ReleaseBannerTexture()
        {
            if (_bannerImage != null && _bannerImage.texture != null)
            {
                // This texture was downloaded for this instance alone - nothing else references
                // it, so it must be destroyed here or it leaks for the life of the process.
                Destroy(_bannerImage.texture);
                _bannerImage.texture = null;
            }
        }

        public void SetContent(string title, string description, string actionText)
        {
            _titleText.text = title;
            _descriptionText.text = description;
            _buyButtonText.text = actionText;
        }

        public void SetBannerLoading(bool isLoading)
        {
            if (_bannerLoadingIndicator != null)
            {
                _bannerLoadingIndicator.SetActive(isLoading);
            }
        }

        public void SetBannerTexture(Texture2D texture)
        {
            if (_bannerImage == null)
            {
                return;
            }

            _bannerImage.texture = texture;
            _bannerImage.gameObject.SetActive(true);

            if (_bannerFallback != null)
            {
                _bannerFallback.SetActive(false);
            }
        }

        public void SetBannerFallback()
        {
            if (_bannerImage != null)
            {
                _bannerImage.gameObject.SetActive(false);
            }

            if (_bannerFallback != null)
            {
                _bannerFallback.SetActive(true);
            }
        }

        private void OnBuyClicked()
        {
            BuyClicked?.Invoke();
        }
    }
}
