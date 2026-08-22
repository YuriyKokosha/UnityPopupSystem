using System.Threading;
using Cysharp.Threading.Tasks;
using System.Linq;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Profile;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Runtime.Manager;

namespace PopupSystem.UI.Windows.MainGame
{
    public sealed class MainGameWindowController : WindowController<EmptyWindowData, MainGameWindowView>
    {
        private readonly PlayerInventoryManager _playerInventoryManager;
        private readonly PlayerProfileManager _playerProfileManager;
        private readonly IWindowsManager _windowsManager;
        private bool _isSettingsOpenRequested;

        public MainGameWindowController(
            PlayerInventoryManager playerInventoryManager,
            PlayerProfileManager playerProfileManager,
            IWindowsManager windowsManager)
        {
            _playerInventoryManager = playerInventoryManager;
            _playerProfileManager = playerProfileManager;
            _windowsManager = windowsManager;
        }

        protected override UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            var profile = _playerProfileManager.CurrentProfile;

            if (profile != null)
            {
                View.SetPlayerData(profile.DisplayName, profile.Level, profile.PlayerId);
            }

            RefreshBalances();
            _playerInventoryManager.BalancesChanged += RefreshBalances;
            View.SettingsClicked += OnSettingsClicked;
            return UniTask.CompletedTask;
        }

        public override void Dispose()
        {
            _playerInventoryManager.BalancesChanged -= RefreshBalances;
            if (View != null)
            {
                View.SettingsClicked -= OnSettingsClicked;
            }
            base.Dispose();
        }

        private void RefreshBalances()
        {
            var text = string.Join(", ", _playerInventoryManager.GetSnapshot().Select(x => $"{x.ResourceId}: {x.Amount}"));
            View.SetBalances(text);
        }

        private void OnSettingsClicked()
        {
            if (_isSettingsOpenRequested)
            {
                return;
            }

            _isSettingsOpenRequested = true;
            OpenSettingsAsync().Forget();
        }

        private async UniTaskVoid OpenSettingsAsync()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);
            await handle.WaitForCloseAsync();
            _isSettingsOpenRequested = false;
        }
    }
}
