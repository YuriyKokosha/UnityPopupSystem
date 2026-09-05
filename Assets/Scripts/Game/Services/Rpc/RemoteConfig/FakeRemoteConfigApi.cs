using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    public sealed class FakeRemoteConfigApi : IRemoteConfigApi
    {
        public async UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken)
        {
            await UniTask.Delay(700, cancellationToken: cancellationToken);

            return new OfferRemoteContent(
                "Starter Offer",
                "Limited offer: 500 gems and 50 energy for a discounted price.",
                "Buy",
                "offers/starter-offer-banner.png");
        }
    }
}
