using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Services.Rpc.Wallet
{
    public sealed class FakeWalletRpcApi : IWalletRpcApi
    {
        public async UniTask<WalletSnapshot> GetWalletSnapshotAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);

            return new WalletSnapshot(new[]
            {
                new CurrencyAmount("gold", 1500),
                new CurrencyAmount("gems", 42),
            });
        }
    }
}
