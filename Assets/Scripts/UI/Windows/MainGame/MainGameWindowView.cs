using PopupSystem.UI.Runtime;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.MainGame
{
    public sealed class MainGameWindowView : WindowView
    {
        [SerializeField] private Image _backgroundImage;
        [SerializeField] private Image _panelImage;
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _playerText;
        [SerializeField] private Text _balancesText;
        [SerializeField] private Button _settingsButton;

        public event Action SettingsClicked;

        private void Awake()
        {
            _settingsButton.onClick.AddListener(OnSettingsClicked);
        }

        private void OnDestroy()
        {
            if (_settingsButton != null)
            {
                _settingsButton.onClick.RemoveListener(OnSettingsClicked);
            }
        }

        public void SetPlayerData(string displayName, int level, string playerId)
        {
            if (_playerText != null)
            {
                _playerText.text = $"Player: {displayName}\nLevel: {level}\nId: {playerId}";
            }
        }

        public void SetBalances(string balances)
        {
            if (_balancesText != null)
            {
                _balancesText.text = $"Balances: {balances}";
            }
        }

        private void OnSettingsClicked()
        {
            SettingsClicked?.Invoke();
        }
    }
}
