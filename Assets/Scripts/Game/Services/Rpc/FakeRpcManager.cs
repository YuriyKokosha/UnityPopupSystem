using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.WindowQueue;

namespace PopupSystem.Game.Services.Rpc
{
    public sealed class FakeRpcManager : IRpcManager
    {
        public ICoreRpcApi Core { get; } = new FakeCoreRpcApi();
        public IInventoryRpcApi Inventory { get; } = new FakeInventoryRpcApi();
        public IWindowQueueRpcApi WindowQueue { get; } = new FakeWindowQueueRpcApi();
        public IRemoteConfigApi RemoteConfig { get; } = new FakeRemoteConfigApi();
    }
}
