using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.Inventory
{
    /// <summary>One cell of the inventory grid. Pooled by <see cref="InventoryWindowView"/>: <see cref="Bind"/>
    /// fully overwrites every visual, so a cell reused from a previous open carries nothing over. The icon is the
    /// item; the name is only drawn when there is no icon to draw.</summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image _iconImage;
        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_Text _countLabel;
        [SerializeField] private Button _useButton;
        [SerializeField] private Button _discardButton;
        [SerializeField] private GameObject _emptyState;
        [SerializeField] private GameObject _filledState;

        public event Action<long> UseClicked;
        public event Action<long> DiscardClicked;

        public long StackId { get; private set; }

        private void Awake()
        {
            if (_useButton != null)
            {
                _useButton.onClick.AddListener(OnUseClicked);
            }

            if (_discardButton != null)
            {
                _discardButton.onClick.AddListener(OnDiscardClicked);
            }
        }

        private void OnDestroy()
        {
            if (_useButton != null)
            {
                _useButton.onClick.RemoveListener(OnUseClicked);
            }

            if (_discardButton != null)
            {
                _discardButton.onClick.RemoveListener(OnDiscardClicked);
            }
        }

        public void Bind(long stackId, string displayName, int count, bool isStackable, bool canUse, Sprite icon)
        {
            StackId = stackId;

            SetActive(_emptyState, false);
            SetActive(_filledState, true);

            if (_nameLabel != null)
            {
                _nameLabel.text = icon != null ? string.Empty : displayName;
                _nameLabel.gameObject.SetActive(icon == null);
            }

            if (_countLabel != null)
            {
                _countLabel.text = isStackable ? $"x{count}" : string.Empty;
            }

            if (_iconImage != null)
            {
                _iconImage.sprite = icon;
                _iconImage.enabled = icon != null;
            }

            if (_useButton != null)
            {
                _useButton.gameObject.SetActive(canUse);
            }

            if (_discardButton != null)
            {
                _discardButton.gameObject.SetActive(true);
            }
        }

        public void BindEmpty()
        {
            StackId = 0;

            SetActive(_emptyState, true);
            SetActive(_filledState, false);

            if (_iconImage != null)
            {
                _iconImage.sprite = null;
                _iconImage.enabled = false;
            }

            if (_useButton != null)
            {
                _useButton.gameObject.SetActive(false);
            }

            if (_discardButton != null)
            {
                _discardButton.gameObject.SetActive(false);
            }
        }

        private void OnUseClicked()
        {
            if (StackId != 0)
            {
                UseClicked?.Invoke(StackId);
            }
        }

        private void OnDiscardClicked()
        {
            if (StackId != 0)
            {
                DiscardClicked?.Invoke(StackId);
            }
        }

        private static void SetActive(GameObject target, bool isActive)
        {
            if (target != null)
            {
                target.SetActive(isActive);
            }
        }
    }
}
