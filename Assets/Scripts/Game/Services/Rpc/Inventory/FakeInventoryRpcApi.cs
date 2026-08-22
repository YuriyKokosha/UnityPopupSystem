using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
    /// <summary>
    /// Local stand-in for the inventory-snapshot endpoint - see FakeRpcManager for why the whole
    /// backend is faked out this way.
    /// </summary>
    public sealed class FakeInventoryRpcApi : IInventoryRpcApi
    {
        public async UniTask<InventorySnapshot> GetInventorySnapshotAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);

            return new InventorySnapshot(new[]
            {
                new InventoryResource("gold", 1500),
                new InventoryResource("gems", 42),
            });
        }
    }
}
