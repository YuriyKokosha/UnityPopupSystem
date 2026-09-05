using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Core;

namespace PopupSystem.Game.Services.Rpc.Core
{
    public interface ICoreRpcApi
    {
        UniTask<PlayerProfile> GetPlayerProfileAsync(CancellationToken cancellationToken);

        UniTask<DateTime> GetServerTimeUtcAsync(CancellationToken cancellationToken);
    }
}
