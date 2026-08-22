using System;
using PopupSystem.UI.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.DailyReward
{
    public sealed class DailyRewardWindowView : WindowView
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _descriptionText;
        [SerializeField] private Button _claimButton;
        [SerializeField] private Text _claimButtonText;
        [SerializeField] private Button _closeButton;

        public event Action ClaimClicked;

        private void Awake()
        {
            _claimButton.onClick.AddListener(OnClaimClicked);
            _closeButton.onClick.AddListener(RequestClose);
        }

        private void OnDestroy()
        {
            if (_claimButton != null)
            {
                _claimButton.onClick.RemoveListener(OnClaimClicked);
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }
        }

        public void SetContent(string title, string description, string actionText)
        {
            _titleText.text = title;
            _descriptionText.text = description;
            _claimButtonText.text = actionText;
        }

        public void SetActionInteractable(bool isInteractable)
        {
            _claimButton.interactable = isInteractable;
        }

        private void OnClaimClicked()
        {
            ClaimClicked?.Invoke();
        }
    }
}
