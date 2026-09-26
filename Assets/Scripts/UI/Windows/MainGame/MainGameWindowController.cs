using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Services;
using UnityEngine;

namespace PopupSystem.UI.Windows.MainGame
{
    public sealed class MainGameWindowController : WindowController<EmptyWindowData, MainGameWindowView>
    {
        private readonly WalletManager _walletManager;
        private readonly InventoryManager _inventoryManager;
        private readonly PlayerProfileManager _playerProfileManager;
        private readonly IWindowsManager _windowsManager;
        private readonly RewardIcons _icons;
        private readonly HashSet<string> _requestedCurrencyIcons = new();
        private CancellationToken _lifetime;
        private bool _isSettingsOpenRequested;
        private bool _isInventoryOpenRequested;

        public MainGameWindowController(
            WalletManager walletManager,
            InventoryManager inventoryManager,
            PlayerProfileManager playerProfileManager,
            IWindowsManager windowsManager,
            RewardIcons icons)
        {
            _icons = icons;
            _walletManager = walletManager;
            _inventoryManager = inventoryManager;
            _playerProfileManager = playerProfileManager;
            _windowsManager = windowsManager;
        }

        protected override async UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            _lifetime = cancellationToken;
            _requestedCurrencyIcons.UnionWith(RewardIcons.CurrencyDisplayOrder);
            await _icons.PreloadCurrenciesAsync(RewardIcons.CurrencyDisplayOrder, cancellationToken);

            var profile = _playerProfileManager.CurrentProfile;

            if (profile != null)
            {
                View.SetPlayerData(profile.DisplayName, profile.Level, profile.PlayerId);
            }

            RefreshBalances();
            RefreshInventory();
            _walletManager.BalancesChanged += RefreshBalances;
            _inventoryManager.Changed += RefreshInventory;
            View.SettingsClicked += OnSettingsClicked;
            View.InventoryClicked += OnInventoryClicked;
        }

        public override void Dispose()
        {
            _walletManager.BalancesChanged -= RefreshBalances;
            _inventoryManager.Changed -= RefreshInventory;
            if (View != null)
            {
                View.SettingsClicked -= OnSettingsClicked;
                View.InventoryClicked -= OnInventoryClicked;
            }
            base.Dispose();
        }

        private void RefreshBalances()
        {
            if (View == null)
            {
                return;
            }

            var balances = _walletManager.GetSnapshot();
            View.SetBalances(_icons.DescribeBalances(balances));

            // A currency nobody preloaded (a new one from the server) is drawn by name now and by icon as soon as
            // its sprite arrives. Each id is asked for once, so a missing icon does not turn into a load loop.
            List<string> late = null;
            for (var i = 0; i < balances.Count; i++)
            {
                if (_requestedCurrencyIcons.Add(balances[i].CurrencyId))
                {
                    (late ??= new List<string>()).Add(balances[i].CurrencyId);
                }
            }

            if (late != null)
            {
                LoadLateIconsAsync(late).Forget();
            }
        }

        private async UniTaskVoid LoadLateIconsAsync(IReadOnlyList<string> currencyIds)
        {
            try
            {
                await _icons.PreloadCurrenciesAsync(currencyIds, _lifetime);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_lifetime.IsCancellationRequested)
            {
                RefreshBalances();
            }
        }

        private void RefreshInventory()
        {
            View.SetInventorySlots(_inventoryManager.UsedSlots, _inventoryManager.SlotLimit);
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

        private void OnInventoryClicked()
        {
            if (_isInventoryOpenRequested)
            {
                return;
            }

            _isInventoryOpenRequested = true;
            OpenInventoryAsync().Forget();
        }

        private async UniTaskVoid OpenSettingsAsync()
        {
            try
            {
                var handle = await _windowsManager.OpenAsync(WindowType.Settings);
                await handle.WaitForCloseAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                // The button must come back after a failed open; a flag left true here is a dead button for the session.
                _isSettingsOpenRequested = false;
            }
        }

        private async UniTaskVoid OpenInventoryAsync()
        {
            try
            {
                var handle = await _windowsManager.OpenAsync(WindowType.Inventory);
                await handle.WaitForCloseAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _isInventoryOpenRequested = false;
            }
        }
    }
}
