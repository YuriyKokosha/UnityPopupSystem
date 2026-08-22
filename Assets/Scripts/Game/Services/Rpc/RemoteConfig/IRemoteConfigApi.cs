using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    /// <summary>
    /// Simulates a remote config/CMS endpoint: a popup's presentation copy and banner asset
    /// reference are fetched at runtime, the way a live game pulls LiveOps content, instead of
    /// being compiled into the client.
    /// </summary>
    public interface IRemoteConfigApi
    {
        UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken);
    }
}
