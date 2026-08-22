using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.WindowQueue;

namespace PopupSystem.Game.Services.Rpc.WindowQueue
{
    public interface IWindowQueueRpcApi
    {
        UniTask<IReadOnlyList<WindowQueueInfo>> GetWindowQueueAsync(CancellationToken cancellationToken);
    }
}
