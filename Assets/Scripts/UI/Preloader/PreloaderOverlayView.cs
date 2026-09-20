using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Preloader
{
    public sealed class PreloaderOverlayView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private GameObject _progressBar;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Button _retryButton;

        public event Action RetryClicked;

        private void Awake()
        {
            if (_retryButton != null)
            {
                _retryButton.onClick.AddListener(OnRetryClicked);
            }

            Hide();
        }

        private void OnDestroy()
        {
            if (_retryButton != null)
            {
                _retryButton.onClick.RemoveListener(OnRetryClicked);
            }
        }

        public void Show(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message;
            }

            SetErrorVisible(false);
            gameObject.SetActive(true);
        }

        public void ShowError(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message;
            }

            SetErrorVisible(true);
            gameObject.SetActive(true);
        }

        public void HideError()
        {
            SetErrorVisible(false);
        }

        private void OnRetryClicked()
        {
            RetryClicked?.Invoke();
        }

        public void SetProgress(float normalizedValue)
        {
            if (_progressFill == null)
            {
                return;
            }

            var fillRect = _progressFill.rectTransform;
            var clampedValue = Mathf.Clamp01(normalizedValue);
            fillRect.anchorMax = new Vector2(clampedValue, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        // The bar and the Retry button share one slot in the layout, so exactly one of
        // them may be on at a time - otherwise they draw on top of each other and the
        // column's height changes with the state.
        private void SetErrorVisible(bool isVisible)
        {
            if (_retryButton != null)
            {
                _retryButton.gameObject.SetActive(isVisible);
            }

            if (_progressBar != null)
            {
                _progressBar.SetActive(!isVisible);
            }
        }
    }
}
