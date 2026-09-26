using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Inventory
{
    /// <summary>Local persistence of the inventory snapshot — a cache of the server's truth, so the player sees
    /// the last known state instantly and offline. Only bytes in, bytes out: no rules live here.</summary>
    public interface IInventoryStorage
    {
        /// <summary>Null when nothing has been saved for this player. A corrupt file throws rather than returning
        /// null, so the caller can tell "first launch" from "damaged".</summary>
        UniTask<InventorySnapshot> LoadAsync(string playerId, CancellationToken cancellationToken);

        UniTask SaveAsync(string playerId, InventorySnapshot snapshot, CancellationToken cancellationToken);

        UniTask ClearAsync(string playerId, CancellationToken cancellationToken);
    }
}
