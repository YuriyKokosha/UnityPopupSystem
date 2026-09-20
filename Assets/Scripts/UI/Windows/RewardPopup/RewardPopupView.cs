using PopupSystem.UI.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupView : WindowView
    {
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _okButton;
        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _rewardLabel;
        [SerializeField] private GameObject _loadingState;

        public void SetTitle(string text)
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = text;
            }
        }

        public void SetRewardText(string text)
        {
            if (_rewardLabel != null)
            {
                _rewardLabel.text = text;
            }
        }

        public void SetLoading(bool isVisible)
        {
            if (_loadingState != null)
            {
                _loadingState.SetActive(isVisible);
            }

            // OK dismisses the popup, so it stays disabled until the reward has actually arrived.
            // The close cross is left alone: the player must always be able to walk away.
            if (_okButton != null)
            {
                _okButton.interactable = !isVisible;
            }
        }

        private void Awake()
        {
            SetLoading(false);
            _closeButton.onClick.AddListener(RequestClose);

            if (_okButton != null)
            {
                _okButton.onClick.AddListener(RequestClose);
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }

            if (_okButton != null)
            {
                _okButton.onClick.RemoveListener(RequestClose);
            }
        }
    }
}
