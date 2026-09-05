using PopupSystem.UI.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.Settings
{
    public sealed class SettingsWindowView : WindowView
    {
        [SerializeField] private Button _closeButton;
        [SerializeField] private TMP_Text _statusLabel;
        [SerializeField] private GameObject _loadingState;

        public void SetStatus(string text)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = text;
            }
        }

        public void SetLoading(bool isVisible)
        {
            if (_loadingState != null)
            {
                _loadingState.SetActive(isVisible);
            }
        }

        private void Awake()
        {
            SetLoading(false);

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(RequestClose);
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }
        }
    }
}
