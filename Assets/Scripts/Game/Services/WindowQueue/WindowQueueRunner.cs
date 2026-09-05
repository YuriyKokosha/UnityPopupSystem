using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using UnityEngine;

namespace PopupSystem.Game.Services.WindowQueue
{
    public sealed class WindowQueueRunner : IDisposable
    {
        private const int FallbackHeartbeatMs = 60_000;

        private const int MinimumWaitMs = 16;

        private readonly WindowQueueManager _windowQueueManager;
        private readonly IWindowsManager _windowsManager;
        private readonly ITimeProvider _timeProvider;
        private readonly Dictionary<WindowType, IWindowQueueAggregator> _aggregators;
        private readonly List<IWindowQueueAggregator> _subscribedAggregators = new();

        private readonly HashSet<WindowType> _presentedSinceAvailable = new();

        private readonly HashSet<WindowType> _failedThisBurst = new();

        private CancellationTokenSource _monitorCts;
        private bool _isProcessing;

        private UniTaskCompletionSource _wakeSource;
        private bool _wakeRequested;

        public WindowQueueRunner(
            WindowQueueManager windowQueueManager,
            IWindowsManager windowsManager,
            ITimeProvider timeProvider,
            List<IWindowQueueAggregator> aggregators)
        {
            _windowQueueManager = windowQueueManager;
            _windowsManager = windowsManager;
            _timeProvider = timeProvider;
            _aggregators = new Dictionary<WindowType, IWindowQueueAggregator>();

            for (var i = 0; i < aggregators.Count; i++)
            {
                _aggregators[aggregators[i].WindowType] = aggregators[i];

                aggregators[i].AvailabilityChanged += Wake;
                _subscribedAggregators.Add(aggregators[i]);
            }

            _windowsManager.QueueBecameIdle += Wake;
            _windowQueueManager.ItemsChanged += OnQueueItemsChanged;
        }

        public void Dispose()
        {
            StopIdleMonitoring();

            for (var i = 0; i < _subscribedAggregators.Count; i++)
            {
                _subscribedAggregators[i].AvailabilityChanged -= Wake;
            }

            _subscribedAggregators.Clear();
            _windowsManager.QueueBecameIdle -= Wake;
            _windowQueueManager.ItemsChanged -= OnQueueItemsChanged;
        }

        public UniTask ShowAvailableWindowsAsync()
        {
            return ShowAvailableWindowsInternalAsync(default);
        }

        private void OnQueueItemsChanged()
        {
            _presentedSinceAvailable.RemoveWhere(IsNoLongerQueueable);
            Wake();
        }

        private bool IsNoLongerQueueable(WindowType type)
        {
            return !_windowQueueManager.Contains(type);
        }

        public void StartIdleMonitoring()
        {
            if (_monitorCts != null)
            {
                return;
            }

            _monitorCts = new CancellationTokenSource();
            MonitorIdleAsync(_monitorCts.Token).Forget();
        }

        public void StopIdleMonitoring()
        {
            if (_monitorCts == null)
            {
                return;
            }

            _monitorCts.Cancel();
            _monitorCts.Dispose();
            _monitorCts = null;

            Wake();
        }

        private void Wake()
        {
            _wakeRequested = true;
            _wakeSource?.TrySetResult();
        }

        private async UniTaskVoid MonitorIdleAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // try/catch inside the loop on purpose: an exception escaping one pass must not end
                // idle monitoring, which would silently stop every future popup for the session.
                try
                {
                    if (_windowsManager.IsQueueIdle)
                    {
                        await ShowAvailableWindowsInternalAsync(cancellationToken);
                    }

                    await WaitForNextScanAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private async UniTask WaitForNextScanAsync(CancellationToken cancellationToken)
        {
            if (_wakeRequested)
            {
                _wakeRequested = false;
                return;
            }

            var delayMs = Mathf.Max(MinimumWaitMs, NextScheduledWakeMs());
            var source = new UniTaskCompletionSource();
            _wakeSource = source;

            try
            {
                await UniTask.WhenAny(
                    source.Task,
                    UniTask.Delay(delayMs, cancellationToken: cancellationToken));
            }
            finally
            {
                // Only if nobody has taken over: a stale waiter clearing the live one's source makes the next
                // wake sleep out the full heartbeat.
                if (ReferenceEquals(_wakeSource, source))
                {
                    _wakeSource = null;
                    _wakeRequested = false;
                }
            }
        }

        private int NextScheduledWakeMs()
        {
            var soonest = TimeSpan.FromMilliseconds(FallbackHeartbeatMs);
            var now = _timeProvider.UtcNow;
            var items = _windowQueueManager.GetItemsSortedByPriority();

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                var cooldown = _windowQueueManager.TimeUntilCooldownReady(item);
                if (cooldown.HasValue && cooldown.Value < soonest)
                {
                    soonest = cooldown.Value;
                }

                if (!_aggregators.TryGetValue(item.WindowType, out var aggregator))
                {
                    continue;
                }

                var scheduled = aggregator.NextAvailabilityChangeUtc;
                if (!scheduled.HasValue)
                {
                    continue;
                }

                var untilScheduled = scheduled.Value - now;
                if (untilScheduled > TimeSpan.Zero && untilScheduled < soonest)
                {
                    soonest = untilScheduled;
                }
            }

            return (int)soonest.TotalMilliseconds;
        }

        private async UniTask ShowAvailableWindowsInternalAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            _failedThisBurst.Clear();

            try
            {
                while (!cancellationToken.IsCancellationRequested && _windowsManager.IsQueueIdle)
                {
                    if (!TryGetNextWindow(out var item, out var payload))
                    {
                        return;
                    }

                    bool wasInterrupted;
                    try
                    {
                        var handle = await _windowsManager.OpenAsync(item.WindowType, payload);
                        wasInterrupted = await WaitForCloseOrInterruptAsync(handle, item, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        _failedThisBurst.Add(item.WindowType);
                        continue;
                    }

                    if (wasInterrupted)
                    {
                        continue;
                    }

                    _windowQueueManager.MarkShown(item);
                    _presentedSinceAvailable.Add(item.WindowType);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        private async UniTask<bool> WaitForCloseOrInterruptAsync(
            WindowHandle handle,
            WindowQueueInfo currentItem,
            CancellationToken cancellationToken)
        {
            if (!currentItem.AllowInterrupt)
            {
                await handle.WaitForCloseAsync();
                return false;
            }

            using var watchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var closeTask = WaitForNaturalCloseAsync(handle);
            var interruptTask = WatchForHigherPriorityAsync(currentItem.Priority, watchCts.Token);

            var (winArgIndex, _, interruptWon) = await UniTask.WhenAny(closeTask, interruptTask);
            watchCts.Cancel();

            Wake();

            if (winArgIndex != 1 || !interruptWon || handle.IsClosed)
            {
                return false;
            }

            if (_windowsManager.HasOpenPopups)
            {
                await handle.WaitForCloseAsync();
                return false;
            }

            await handle.CloseAsync();
            return true;
        }

        private static async UniTask<bool> WaitForNaturalCloseAsync(WindowHandle handle)
        {
            await handle.WaitForCloseAsync();
            return true;
        }

        private async UniTask<bool> WatchForHigherPriorityAsync(int currentPriority, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (HasHigherPriorityWindowReady(currentPriority))
                    {
                        return true;
                    }

                    await WaitForNextScanAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }

            return false;
        }

        private bool HasHigherPriorityWindowReady(int currentPriority)
        {
            var items = _windowQueueManager.GetItemsSortedByPriority();
            ReArmPresentedWindows(items);

            for (var i = 0; i < items.Count; i++)
            {
                var candidate = items[i];

                if (candidate.Priority <= currentPriority)
                {
                    break;
                }

                if (IsEligible(candidate, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetNextWindow(out WindowQueueInfo item, out IWindowData payload)
        {
            var items = _windowQueueManager.GetItemsSortedByPriority();
            ReArmPresentedWindows(items);

            for (var i = 0; i < items.Count; i++)
            {
                var currentItem = items[i];

                if (!IsEligible(currentItem, out var aggregator))
                {
                    continue;
                }

                item = currentItem;
                payload = aggregator.CreatePayload();
                return true;
            }

            item = null;
            payload = null;
            return false;
        }

        private bool IsEligible(WindowQueueInfo item, out IWindowQueueAggregator aggregator)
        {
            aggregator = null;

            if (_failedThisBurst.Contains(item.WindowType))
            {
                return false;
            }

            if (!_aggregators.TryGetValue(item.WindowType, out aggregator))
            {
                return false;
            }

            if (!aggregator.IsAvailable() || !_windowQueueManager.IsCooldownReady(item))
            {
                return false;
            }

            // Already had its turn and nothing re-armed it. CooldownSeconds == 0 means "no cooldown-driven
            // repeat", not "repeat immediately".
            return !_presentedSinceAvailable.Contains(item.WindowType);
        }

        private void ReArmPresentedWindows(IReadOnlyList<WindowQueueInfo> items)
        {
            if (_presentedSinceAvailable.Count == 0)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (!_presentedSinceAvailable.Contains(item.WindowType))
                {
                    continue;
                }

                if (!_aggregators.TryGetValue(item.WindowType, out var aggregator))
                {
                    continue;
                }

                var becameUnavailable = !aggregator.IsAvailable();
                var cooldownElapsed = item.CooldownSeconds > 0f && _windowQueueManager.IsCooldownReady(item);

                if (becameUnavailable || cooldownElapsed)
                {
                    _presentedSinceAvailable.Remove(item.WindowType);
                }
            }
        }
    }
}
