using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.WindowQueue;

namespace PopupSystem.Game.Services.Rpc.WindowQueue
{
    public sealed class FakeWindowQueueRpcApi : IWindowQueueRpcApi
    {
        public async UniTask<IReadOnlyList<WindowQueueInfo>> GetWindowQueueAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(1000, cancellationToken: cancellationToken);

            return new List<WindowQueueInfo>
            {
                new(WindowType.DailyReward, 100, 0f, allowInterrupt: false),
                new(WindowType.Offer, 50, 20f, allowInterrupt: true),
            };
        }
    }
}
