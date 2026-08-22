using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.WindowQueue;

namespace PopupSystem.Game.Services.Rpc
{
    public interface IRpcManager
    {
        ICoreRpcApi Core { get; }
        IInventoryRpcApi Inventory { get; }
        IWindowQueueRpcApi WindowQueue { get; }
        IRemoteConfigApi RemoteConfig { get; }
    }
}
