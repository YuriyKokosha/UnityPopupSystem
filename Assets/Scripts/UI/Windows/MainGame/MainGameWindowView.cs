using System;
using System.Collections.Generic;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.MainGame
{
    public sealed class MainGameWindowView : WindowView
    {
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _panelImage;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _playerText;
        [SerializeField] private IconAmountStripView _balances;
        [SerializeField] private TMP_Text _inventoryText;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _inventoryButton;

        public event Action SettingsClicked;
        public event Action InventoryClicked;

        internal Button SettingsButton => _settingsButton;
        internal Button InventoryButton => _inventoryButton;

        private void Awake()
        {
            _settingsButton.onClick.AddListener(OnSettingsClicked);

            if (_inventoryButton != null)
            {
                _inventoryButton.onClick.AddListener(OnInventoryClicked);
            }
        }

        private void OnDestroy()
        {
            if (_settingsButton != null)
            {
                _settingsButton.onClick.RemoveListener(OnSettingsClicked);
            }

            if (_inventoryButton != null)
            {
                _inventoryButton.onClick.RemoveListener(OnInventoryClicked);
            }
        }

        public void SetPlayerData(string displayName, int level, string playerId)
        {
            if (_playerText != null)
            {
                _playerText.text = $"Player: {displayName}\nLevel: {level}\nId: {playerId}";
            }
        }

        public void SetBalances(IReadOnlyList<IconAmountModel> balances)
        {
            if (_balances != null)
            {
                _balances.Render(balances);
            }
        }

        public void SetInventorySlots(int usedSlots, int slotLimit)
        {
            if (_inventoryText != null)
            {
                // Sits under the chest button, so the icon carries the word "inventory".
                _inventoryText.text = slotLimit > 0 ? $"{usedSlots} / {slotLimit}" : usedSlots.ToString();
            }
        }

        private void OnSettingsClicked()
        {
            SettingsClicked?.Invoke();
        }

        private void OnInventoryClicked()
        {
            InventoryClicked?.Invoke();
        }
    }
}
