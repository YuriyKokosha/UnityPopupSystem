using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Services.Rpc.Wallet
{
    public interface IWalletRpcApi
    {
        UniTask<WalletSnapshot> GetWalletSnapshotAsync(CancellationToken cancellationToken);
    }
}
