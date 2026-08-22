using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Services.Rpc.WindowQueue
{
    /// <summary>
    /// Local stand-in for the window-queue config endpoint - see FakeRpcManager for why the whole
    /// backend is faked out this way.
    /// </summary>
    public sealed class FakeWindowQueueRpcApi : IWindowQueueRpcApi
    {
        public async UniTask<IReadOnlyList<WindowQueueInfo>> GetWindowQueueAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);

            return new List<WindowQueueInfo>
            {
                // Highest priority and nothing above it can pre-empt it anyway, so it makes
                // no practical difference here - but it is not meant to be interruptible.
                new(WindowType.DailyReward, 100, 0f, allowInterrupt: false),
                // Lower priority: if DailyReward becomes available while this is showing
                // (e.g. daily rollover happens mid-session), it will be interrupted and
                // re-queued instead of finishing its turn first.
                new(WindowType.Offer, 50, 20f, allowInterrupt: true),
            };
        }
    }
}
