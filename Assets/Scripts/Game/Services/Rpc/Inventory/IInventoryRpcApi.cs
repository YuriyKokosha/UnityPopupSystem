using System.Threading;
using Cysharp.Threading.Tasks;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
    public interface IInventoryRpcApi
    {
        /// <summary>Checks the client's inventory against the server's. <paramref name="localHash"/> is null on a
        /// first launch with nothing stored; the server then always answers with a snapshot.</summary>
        UniTask<InventorySyncResult> SyncAsync(string localHash, long localRevision, CancellationToken cancellationToken);
    }
}
