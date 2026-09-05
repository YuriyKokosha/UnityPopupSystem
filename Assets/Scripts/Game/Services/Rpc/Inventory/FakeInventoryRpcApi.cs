using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
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
