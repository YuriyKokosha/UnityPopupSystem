using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Core;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.Wallet;
using PopupSystem.Game.Services.Rpc.WindowQueue;

namespace PopupSystem.Tests.EditMode.Fakes
{
    public sealed class FakeClockRpcManager : IRpcManager, ICoreRpcApi
    {
        public FakeClockRpcManager(DateTime serverUtcNow)
        {
            ServerUtcNow = serverUtcNow;
        }

        public DateTime ServerUtcNow { get; set; }

        public int ServerTimeCallCount { get; private set; }

        public ICoreRpcApi Core => this;
        public IWalletRpcApi Wallet => null;
        public IInventoryRpcApi Inventory => null;
        public IWindowQueueRpcApi WindowQueue => null;
        public IRemoteConfigApi RemoteConfig => null;

        public UniTask<PlayerProfile> GetPlayerProfileAsync(CancellationToken cancellationToken)
        {
            throw new NotSupportedException(
                $"{nameof(FakeClockRpcManager)} exists for the time provider only - it has no profile.");
        }

        public UniTask<DateTime> GetServerTimeUtcAsync(CancellationToken cancellationToken)
        {
            ServerTimeCallCount++;
            return UniTask.FromResult(ServerUtcNow);
        }
    }
}
