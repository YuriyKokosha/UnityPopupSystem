using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Core;

namespace PopupSystem.Game.Services.Rpc.Core
{
    public sealed class FakeCoreRpcApi : ICoreRpcApi
    {
        public async UniTask<PlayerProfile> GetPlayerProfileAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);
            return new PlayerProfile("player-001", "Test Player", 27);
        }

        public async UniTask<DateTime> GetServerTimeUtcAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(100, cancellationToken: cancellationToken);
            return DateTime.UtcNow;
        }
    }
}
