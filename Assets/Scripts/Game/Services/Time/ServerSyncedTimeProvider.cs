using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Services.Rpc;

namespace PopupSystem.Game.Services.Time
{
    public sealed class ServerSyncedTimeProvider : ITimeProvider
    {
        private static readonly TimeSpan StaleDriftThreshold = TimeSpan.FromSeconds(5);

        private readonly IRpcManager _rpcManager;
        private readonly Stopwatch _sinceSync = new();

        private DateTime _serverUtcAtSync;
        private DateTime _deviceUtcAtSync;

        public ServerSyncedTimeProvider(IRpcManager rpcManager)
        {
            _rpcManager = rpcManager;
        }

        public bool IsSynced => _sinceSync.IsRunning;

        public DateTime UtcNow => IsSynced ? _serverUtcAtSync + _sinceSync.Elapsed : DateTime.UtcNow;

        public TimeSpan TimeSinceSync => IsSynced ? _sinceSync.Elapsed : TimeSpan.Zero;

        public bool IsSuspectedStale
        {
            get
            {
                if (!IsSynced)
                {
                    return false;
                }

                var drift = DateTime.UtcNow - _deviceUtcAtSync - _sinceSync.Elapsed;
                return drift > StaleDriftThreshold || drift < -StaleDriftThreshold;
            }
        }

        public async UniTask SyncAsync(CancellationToken cancellationToken)
        {
            var serverUtcNow = await _rpcManager.Core.GetServerTimeUtcAsync(cancellationToken);

            _serverUtcAtSync = serverUtcNow;

            // Recorded only for IsSuspectedStale, never to tell the time.
            _deviceUtcAtSync = DateTime.UtcNow;
            _sinceSync.Restart();
        }
    }
}
