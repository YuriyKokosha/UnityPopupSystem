using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
    public interface IInventoryRpcApi
    {
        UniTask<InventorySnapshot> GetInventorySnapshotAsync(CancellationToken cancellationToken);
    }
}
