using System.Collections.Generic;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Widgets;
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
        [SerializeField] private IconAmountStripView _rewards;
        [SerializeField] private GameObject _loadingState;

        public void SetTitle(string text)
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = text;
            }
        }

        /// <summary>A sentence instead of the reward row (an error). Clears the row.</summary>
        public void SetRewardText(string text)
        {
            if (_rewardLabel != null)
            {
                _rewardLabel.text = text;
            }

            if (_rewards != null && !string.IsNullOrEmpty(text))
            {
                _rewards.Clear();
            }
        }

        /// <summary>The granted reward, one icon per line. Clears any sentence.</summary>
        public void SetRewards(IReadOnlyList<IconAmountModel> rewards)
        {
            if (_rewardLabel != null)
            {
                _rewardLabel.text = string.Empty;
            }

            if (_rewards != null)
            {
                _rewards.Render(rewards);
            }
        }

        internal override void ResetForPool()
        {
            SetRewardText(string.Empty);

            if (_rewards != null)
            {
                _rewards.Clear();
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
