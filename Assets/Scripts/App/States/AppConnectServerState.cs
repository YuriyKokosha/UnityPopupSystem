using System;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.UI.Preloader;
using UnityEngine;

namespace PopupSystem.App.States
{
    public sealed class AppConnectServerState : IAppState
    {
        private readonly IRpcManager _rpcManager;
        private readonly PlayerInventoryManager _playerInventoryManager;
        private readonly PlayerProfileManager _playerProfileManager;
        private readonly WindowQueueManager _windowQueueManager;
        private readonly PreloaderOverlayView _preloaderOverlay;

        public AppConnectServerState(
            IRpcManager rpcManager,
            PlayerInventoryManager playerInventoryManager,
            PlayerProfileManager playerProfileManager,
            WindowQueueManager windowQueueManager,
            PreloaderOverlayView preloaderOverlay)
        {
            _rpcManager = rpcManager;
            _playerInventoryManager = playerInventoryManager;
            _playerProfileManager = playerProfileManager;
            _windowQueueManager = windowQueueManager;
            _preloaderOverlay = preloaderOverlay;
        }

        public async UniTask EnterAsync()
        {
            // A real backend can legitimately fail here (timeout, 5xx, no connectivity) on the
            // very first thing the app does. Without this loop, that failure would propagate all
            // the way up through AppStateManager into AppEntryPoint's fire-and-forget
            // Initialize() and leave the player staring at a frozen "Connecting..." spinner
            // forever, with nothing but a console log to explain why.
            while (true)
            {
                try
                {
                    await ConnectAsync();
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    await WaitForRetryAsync();
                }
            }
        }

        public UniTask ExitAsync()
        {
            return UniTask.CompletedTask;
        }

        private async UniTask ConnectAsync()
        {
            _preloaderOverlay.HideError();
            _preloaderOverlay.Show("Connecting to server...");
            _preloaderOverlay.SetProgress(0.35f);
            var profile = await _rpcManager.Core.GetPlayerProfileAsync(default);
            _playerProfileManager.SetProfile(profile);
            _preloaderOverlay.SetProgress(0.6f);
            var inventory = await _rpcManager.Inventory.GetInventorySnapshotAsync(default);
            _playerInventoryManager.SetInventory(inventory);
            _preloaderOverlay.SetProgress(0.75f);
            var windowQueue = await _rpcManager.WindowQueue.GetWindowQueueAsync(default);
            _windowQueueManager.SetItems(windowQueue);
            _preloaderOverlay.SetProgress(0.85f);
        }

        private UniTask WaitForRetryAsync()
        {
            var retrySource = new UniTaskCompletionSource();

            void OnRetryClicked()
            {
                retrySource.TrySetResult();
            }

            _preloaderOverlay.RetryClicked += OnRetryClicked;
            _preloaderOverlay.ShowError("Couldn't connect to the server. Please check your connection and try again.");

            return AwaitAndUnsubscribeAsync();

            async UniTask AwaitAndUnsubscribeAsync()
            {
                try
                {
                    await retrySource.Task;
                }
                finally
                {
                    _preloaderOverlay.RetryClicked -= OnRetryClicked;
                }
            }
        }
    }
}
