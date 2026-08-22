using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Core;

namespace PopupSystem.Game.Services.Rpc.Core
{
    /// <summary>
    /// Local stand-in for the core player-profile endpoint - see FakeRpcManager for why the whole
    /// backend is faked out this way. Simulates network latency via UniTask.Delay so callers that
    /// depend on this taking real time (e.g. a loading screen's progress bar) are exercised for
    /// real even without a live server.
    /// </summary>
    public sealed class FakeCoreRpcApi : ICoreRpcApi
    {
        public async UniTask<PlayerProfile> GetPlayerProfileAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);
            return new PlayerProfile("player-001", "Test Player", 27);
        }
    }
}
