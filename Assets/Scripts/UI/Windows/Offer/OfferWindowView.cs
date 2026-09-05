using System;
using PopupSystem.UI.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.Offer
{
    public sealed class OfferWindowView : WindowView
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private Button _buyButton;
        [SerializeField] private TMP_Text _buyButtonText;
        [SerializeField] private Button _closeButton;

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

            DetachBannerTexture();
        }

        internal override void ResetForPool()
        {
            DetachBannerTexture();

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

        // Detach, never destroy: RemoteImageLoader owns the texture and hands the same one to the
        // next window that asks for that URL. Clearing is still needed - this view is pooled.
        private void DetachBannerTexture()
        {
            if (_bannerImage != null)
            {
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
