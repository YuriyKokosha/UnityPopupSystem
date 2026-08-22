using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    /// <summary>
    /// Local stand-in for the remote config/CMS endpoint - see FakeRpcManager for why the whole
    /// backend is faked out this way.
    /// </summary>
    public sealed class FakeRemoteConfigApi : IRemoteConfigApi
    {
        public async UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken)
        {
            // Simulated CDN/CMS latency - real enough to exercise the loading state in the UI.
            await UniTask.Delay(700, cancellationToken: cancellationToken);

            return new OfferRemoteContent(
                "Starter Offer",
                "Limited offer: 500 gems and 50 energy for a discounted price.",
                "Buy",
                "offers/starter-offer-banner.png");
        }
    }
}
