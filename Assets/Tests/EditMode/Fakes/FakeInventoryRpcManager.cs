using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.Wallet;
using PopupSystem.Game.Services.Rpc.WindowQueue;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>An IRpcManager whose inventory sync answers whatever the test scripted, synchronously. The
    /// production FakeInventoryRpcApi has real latency and a fixed answer, which is the wrong tool here.</summary>
    public sealed class FakeInventoryRpcManager : IRpcManager, IInventoryRpcApi, IRemoteConfigApi
    {
        public InventoryConfig Config { get; set; } = FakeRemoteConfigApi.CreateDemoConfig();

        public Func<string, long, InventorySyncResult> SyncAnswer { get; set; } = (_, _) => InventorySyncResult.Valid;

        public List<(string Hash, long Revision)> SyncCalls { get; } = new();

        public ICoreRpcApi Core => throw new NotSupportedException("Sync tests never call Core.");
        public IWalletRpcApi Wallet => throw new NotSupportedException("Sync tests never call Wallet.");
        public IInventoryRpcApi Inventory => this;
        public IWindowQueueRpcApi WindowQueue => throw new NotSupportedException("Sync tests never call WindowQueue.");
        public IRemoteConfigApi RemoteConfig => this;

        public UniTask<InventorySyncResult> SyncAsync(string localHash, long localRevision, CancellationToken cancellationToken)
        {
            SyncCalls.Add((localHash, localRevision));
            return UniTask.FromResult(SyncAnswer(localHash, localRevision));
        }

        public UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("Sync tests never fetch offer content.");
        }

        public UniTask<InventoryConfig> GetInventoryConfigAsync(CancellationToken cancellationToken)
        {
            return UniTask.FromResult(Config);
        }
    }
}
