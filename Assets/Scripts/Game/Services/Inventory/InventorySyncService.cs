using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Rpc;
using UnityEngine;

namespace PopupSystem.Game.Services.Inventory
{
    /// <summary>The connect-time handshake: config from remote config, the cached snapshot from storage, then the
    /// server's verdict on the cached hash. Lives in Game rather than in the app state so the EditMode suite can
    /// drive it against fakes. See Docs/feature-maps/inventory.md for the sequence.</summary>
    public sealed class InventorySyncService
    {
        private readonly IRpcManager _rpcManager;
        private readonly InventoryManager _inventoryManager;
        private readonly IInventoryStorage _storage;

        public InventorySyncService(IRpcManager rpcManager, InventoryManager inventoryManager, IInventoryStorage storage)
        {
            _rpcManager = rpcManager;
            _inventoryManager = inventoryManager;
            _storage = storage;
        }

        // Config, then the local cache, then the server: the cached snapshot is loaded before the network call so
        // a slow or failed sync still leaves the last known state behind the retry prompt.
        public async UniTask SyncAsync(string playerId, CancellationToken cancellationToken)
        {
            var config = await _rpcManager.RemoteConfig.GetInventoryConfigAsync(cancellationToken);
            _inventoryManager.Initialize(config);

            var local = await LoadLocalSnapshotSafeAsync(playerId, cancellationToken);
            _inventoryManager.Load(playerId, local ?? InventorySnapshot.Empty);

            var sync = await _rpcManager.Inventory.SyncAsync(local?.Hash, local?.Revision ?? 0, cancellationToken);

            if (!sync.IsValid)
            {
                _inventoryManager.ReplaceFromServer(sync.Snapshot);
            }
        }

        // A damaged file is not a reason to block the connect: log it, start empty, and let the server's answer
        // (which a missing hash always forces) put the real state back.
        private async UniTask<InventorySnapshot> LoadLocalSnapshotSafeAsync(string playerId, CancellationToken cancellationToken)
        {
            try
            {
                return await _storage.LoadAsync(playerId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
        }
    }
}
