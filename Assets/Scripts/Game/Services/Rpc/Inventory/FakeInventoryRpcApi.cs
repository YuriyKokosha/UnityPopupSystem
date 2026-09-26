using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
    /// <summary>The stand-in server trusts the client: any stored hash is "valid", and only a first launch (no
    /// hash) gets the starter kit. A real backend recomputes the hash from its own copy and answers Replace when
    /// they differ.</summary>
    public sealed class FakeInventoryRpcApi : IInventoryRpcApi
    {
        public async UniTask<InventorySyncResult> SyncAsync(string localHash, long localRevision, CancellationToken cancellationToken)
        {
            await UniTask.Delay(700, cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(localHash))
            {
                return InventorySyncResult.Replace(StarterInventory());
            }

            return InventorySyncResult.Valid;
        }

        private static InventorySnapshot StarterInventory()
        {
            return new InventorySnapshot(
                new[]
                {
                    new ItemStack(1, FakeRemoteConfigApi.SwordItemId, 1),
                    new ItemStack(2, FakeRemoteConfigApi.HealthPotionItemId, 3),
                },
                revision: 0,
                nextStackId: 3);
        }
    }
}
