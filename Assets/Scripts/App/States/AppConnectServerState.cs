using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.UI.Preloader;
using UnityEngine;

namespace PopupSystem.App.States
{
    public sealed class AppConnectServerState : IAppState
    {
        private readonly IRpcManager _rpcManager;
        private readonly ServerSyncedTimeProvider _timeProvider;
        private readonly WalletManager _walletManager;
        private readonly InventorySyncService _inventorySync;
        private readonly PlayerProfileManager _playerProfileManager;
        private readonly WindowQueueManager _windowQueueManager;
        private readonly PreloaderOverlayView _preloaderOverlay;

        public AppConnectServerState(
            IRpcManager rpcManager,
            ServerSyncedTimeProvider timeProvider,
            WalletManager walletManager,
            InventorySyncService inventorySync,
            PlayerProfileManager playerProfileManager,
            WindowQueueManager windowQueueManager,
            PreloaderOverlayView preloaderOverlay)
        {
            _rpcManager = rpcManager;
            _timeProvider = timeProvider;
            _walletManager = walletManager;
            _inventorySync = inventorySync;
            _playerProfileManager = playerProfileManager;
            _windowQueueManager = windowQueueManager;
            _preloaderOverlay = preloaderOverlay;
        }

        private CancellationTokenSource _cts;

        public async UniTask EnterAsync()
        {
            _cts = new CancellationTokenSource();
            var cancellationToken = _cts.Token;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await ConnectAsync(cancellationToken);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    await WaitForRetryAsync(cancellationToken);
                }
            }
        }

        public UniTask ExitAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            return UniTask.CompletedTask;
        }

        private async UniTask ConnectAsync(CancellationToken cancellationToken)
        {
            _preloaderOverlay.HideError();
            _preloaderOverlay.Show("Connecting to server...");
            _preloaderOverlay.SetProgress(0.15f);

            await _timeProvider.SyncAsync(cancellationToken);

            _preloaderOverlay.SetProgress(0.25f);
            var profile = await _rpcManager.Core.GetPlayerProfileAsync(cancellationToken);
            _playerProfileManager.SetProfile(profile);

            _preloaderOverlay.SetProgress(0.4f);
            var wallet = await _rpcManager.Wallet.GetWalletSnapshotAsync(cancellationToken);
            _walletManager.SetWallet(wallet);

            _preloaderOverlay.SetProgress(0.55f);
            // The inventory sync is not caught here on purpose: the inventory is where rewards land, and playing
            // on an unconfirmed state is worse than one more retry prompt. See Docs/knowledge-base/resilience.md.
            await _inventorySync.SyncAsync(profile.PlayerId, cancellationToken);

            _preloaderOverlay.SetProgress(0.8f);
            var windowQueue = await _rpcManager.WindowQueue.GetWindowQueueAsync(cancellationToken);
            _windowQueueManager.SetItems(windowQueue);
            _preloaderOverlay.SetProgress(0.9f);
        }

        private UniTask WaitForRetryAsync(CancellationToken cancellationToken)
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
                    await retrySource.Task.AttachExternalCancellation(cancellationToken);
                }
                finally
                {
                    _preloaderOverlay.RetryClicked -= OnRetryClicked;
                }
            }
        }
    }
}
