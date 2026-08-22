using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.WindowQueue;

namespace PopupSystem.Game.Services.Rpc
{
    /// <summary>
    /// Composes the fake implementation of each RPC module - one per subfolder (Core/ Inventory/
    /// WindowQueue/ RemoteConfig), each holding its own interface plus the fake that satisfies it
    /// for local development - behind the single IRpcManager contract a real networked
    /// implementation would also satisfy. Swapping in a real backend later means writing one real
    /// manager class here (and/or replacing individual modules), without touching any caller that
    /// only depends on IRpcManager or one of the per-module interfaces.
    /// </summary>
    public sealed class FakeRpcManager : IRpcManager
    {
        public ICoreRpcApi Core { get; } = new FakeCoreRpcApi();
        public IInventoryRpcApi Inventory { get; } = new FakeInventoryRpcApi();
        public IWindowQueueRpcApi WindowQueue { get; } = new FakeWindowQueueRpcApi();
        public IRemoteConfigApi RemoteConfig { get; } = new FakeRemoteConfigApi();
    }
}
