using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    public interface IRemoteConfigApi
    {
        UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken);

        /// <summary>The item catalog and the slot limit. Fetched once at connect; there is no offline fallback,
        /// because without a catalog no item can be stacked or drawn.</summary>
        UniTask<InventoryConfig> GetInventoryConfigAsync(CancellationToken cancellationToken);
    }
}
