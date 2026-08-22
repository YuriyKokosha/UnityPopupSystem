using System;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Preloader
{
    public sealed class PreloaderOverlayView : MonoBehaviour
    {
        [SerializeField] private Text _messageText;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Button _retryButton;

        /// <summary>
        /// Raised when the player taps Retry after <see cref="ShowError"/>. A state (e.g.
        /// AppConnectServerState) that can legitimately fail to connect listens for this instead
        /// of leaving the player stuck on a frozen loading screen with no way to recover.
        /// </summary>
        public event Action RetryClicked;

        private void Awake()
        {
            _retryButton.onClick.AddListener(OnRetryClicked);
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

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Replaces the message with an error and reveals the Retry button. The caller is
        /// responsible for waiting on <see cref="RetryClicked"/> and re-attempting whatever
        /// failed - this view only presents the failure and lets the player ask to try again.
        /// </summary>
        public void ShowError(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message;
            }

            if (_retryButton != null)
            {
                _retryButton.gameObject.SetActive(true);
            }

            gameObject.SetActive(true);
        }

        public void HideError()
        {
            if (_retryButton != null)
            {
                _retryButton.gameObject.SetActive(false);
            }
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
    }
}
