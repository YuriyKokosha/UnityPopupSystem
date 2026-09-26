using System;
using System.Collections.Generic;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.DailyReward
{
    public sealed class DailyRewardWindowView : WindowView
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private Button _claimButton;
        [SerializeField] private TMP_Text _claimButtonText;
        [SerializeField] private Button _closeButton;
        [SerializeField] private IconAmountStripView _reward;
        [SerializeField] private GameObject _cooldownRoot;
        [SerializeField] private TMP_Text _cooldownText;

        public event Action ClaimClicked;

        internal Button ClaimButton => _claimButton;
        internal GameObject CooldownRoot => _cooldownRoot;
        internal TMP_Text CooldownText => _cooldownText;

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

        public void SetReward(IReadOnlyList<IconAmountModel> reward)
        {
            if (_reward != null)
            {
                _reward.Render(reward);
            }
        }

        public void SetActionInteractable(bool isInteractable)
        {
            _claimButton.interactable = isInteractable;
        }

        /// <summary>Puts the countdown in the claim button's slot: one slot, two states, never both.</summary>
        public void ShowCooldown(string remaining)
        {
            _claimButton.gameObject.SetActive(false);

            if (_cooldownRoot != null)
            {
                _cooldownRoot.SetActive(true);
                _cooldownText.text = remaining;
            }
        }

        public void ShowClaim()
        {
            _claimButton.gameObject.SetActive(true);

            if (_cooldownRoot != null)
            {
                _cooldownRoot.SetActive(false);
            }
        }

        internal override void ResetForPool()
        {
            ShowClaim();
        }

        private void OnClaimClicked()
        {
            ClaimClicked?.Invoke();
        }
    }
}
