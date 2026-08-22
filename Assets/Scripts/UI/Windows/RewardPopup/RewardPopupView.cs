using PopupSystem.UI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupView : WindowView
    {
        [SerializeField] private Button _closeButton;
        [SerializeField] private Text _titleLabel;
        [SerializeField] private Text _rewardLabel;
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
        }

        private void Awake()
        {
            SetLoading(false);
            _closeButton.onClick.AddListener(RequestClose);
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
