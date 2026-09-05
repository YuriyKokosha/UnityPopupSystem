using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    public interface IRemoteConfigApi
    {
        UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken);
    }
}
