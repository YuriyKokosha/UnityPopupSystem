using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Services.Time;
using UnityEngine;
using Zenject;

namespace PopupSystem.App.Runtime
{
    public sealed class TimeResyncTicker : ITickable
    {
        private static readonly TimeSpan MaxAnchorAge = TimeSpan.FromMinutes(5);

        private const float CheckIntervalSeconds = 1f;

        private readonly ServerSyncedTimeProvider _timeProvider;

        private float _secondsUntilNextCheck;
        private bool _isResyncing;

        public TimeResyncTicker(ServerSyncedTimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public void Tick()
        {
            _secondsUntilNextCheck -= Time.unscaledDeltaTime;
            if (_secondsUntilNextCheck > 0f)
            {
                return;
            }

            _secondsUntilNextCheck = CheckIntervalSeconds;

            if (_isResyncing || !_timeProvider.IsSynced)
            {
                return;
            }

            if (!_timeProvider.IsSuspectedStale && _timeProvider.TimeSinceSync < MaxAnchorAge)
            {
                return;
            }

            ResyncAsync().Forget();
        }

        private async UniTaskVoid ResyncAsync()
        {
            _isResyncing = true;

            try
            {
                await _timeProvider.SyncAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                _isResyncing = false;
            }
        }
    }
}
