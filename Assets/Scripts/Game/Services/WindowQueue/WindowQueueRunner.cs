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

        // After a pass of the idle monitor fails unexpectedly, it waits this long before the next pass whatever
        // wakes it: a failure that repeats synchronously would otherwise spin the main thread.
        private const int MonitorFailureBackoffMs = 1_000;

        // A window whose open failed, or whose aggregator threw, is left out until its retry time: 2 s after the
        // first failure, doubling per consecutive failure up to a minute. Measured on the injected clock and
        // independent of wake-ups, so neither QueueBecameIdle (raised when a load fails) nor any other wake can
        // turn a permanently broken window into a tight retry loop.
        private static readonly TimeSpan FailureRetryBase = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FailureRetryMax = TimeSpan.FromSeconds(60);

        private readonly WindowQueueManager _windowQueueManager;
        private readonly IWindowsManager _windowsManager;
        private readonly ITimeProvider _timeProvider;
        private readonly Dictionary<WindowType, IWindowQueueAggregator> _aggregators;
        private readonly List<IWindowQueueAggregator> _subscribedAggregators = new();

        private readonly HashSet<WindowType> _presentedSinceAvailable = new();

        private readonly Dictionary<WindowType, FailureRecord> _failures = new();

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
                var failed = false;

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
                    failed = true;
                }

                if (!failed)
                {
                    continue;
                }

                // Never straight back into a pass that has just failed: that would be a loop with no yield.
                try
                {
                    await UniTask.Delay(MonitorFailureBackoffMs, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
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

                if (_failures.TryGetValue(item.WindowType, out var failure))
                {
                    var untilRetry = failure.RetryAtUtc - now;
                    if (untilRetry > TimeSpan.Zero && untilRetry < soonest)
                    {
                        soonest = untilRetry;
                    }
                }

                if (IsWaitingToRetry(item.WindowType)
                    || !_aggregators.TryGetValue(item.WindowType, out var aggregator))
                {
                    continue;
                }

                DateTime? scheduled;
                try
                {
                    scheduled = aggregator.NextAvailabilityChangeUtc;
                }
                catch (Exception ex)
                {
                    // The retry time was set just now, after this item's pass over _failures above: count it
                    // here, or the wait would fall back to the heartbeat instead of ending at the retry.
                    var untilRetry = RecordFailure(item.WindowType, ex);
                    if (untilRetry < soonest)
                    {
                        soonest = untilRetry;
                    }

                    continue;
                }

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
                        _failures.Remove(item.WindowType);
                        wasInterrupted = await WaitForCloseOrInterruptAsync(handle, item, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(item.WindowType, ex);
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

                // A throwing payload factory takes only its own window out (until its retry time); the rest of the
                // queue - lower priorities included - carries on.
                try
                {
                    payload = aggregator.CreatePayload();
                }
                catch (Exception ex)
                {
                    RecordFailure(currentItem.WindowType, ex);
                    continue;
                }

                item = currentItem;
                return true;
            }

            item = null;
            payload = null;
            return false;
        }

        private bool IsEligible(WindowQueueInfo item, out IWindowQueueAggregator aggregator)
        {
            aggregator = null;

            if (IsWaitingToRetry(item.WindowType))
            {
                return false;
            }

            if (!_aggregators.TryGetValue(item.WindowType, out aggregator))
            {
                return false;
            }

            if (!TryIsAvailable(item.WindowType, aggregator, out var isAvailable)
                || !isAvailable
                || !_windowQueueManager.IsCooldownReady(item))
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

                if (!TryIsAvailable(item.WindowType, aggregator, out var isAvailable))
                {
                    continue;
                }

                var becameUnavailable = !isAvailable;
                var cooldownElapsed = item.CooldownSeconds > 0f && _windowQueueManager.IsCooldownReady(item);

                if (becameUnavailable || cooldownElapsed)
                {
                    _presentedSinceAvailable.Remove(item.WindowType);
                }
            }
        }

        // Aggregators are extension points: whatever one throws is contained to its own window. The only way the
        // runner asks an aggregator for availability, so the retry guard here covers every path (the scan, the
        // re-arm pass and the interrupt watcher): a window inside its backoff is not asked, however often the
        // runner wakes.
        private bool TryIsAvailable(WindowType type, IWindowQueueAggregator aggregator, out bool isAvailable)
        {
            if (IsWaitingToRetry(type))
            {
                isAvailable = false;
                return false;
            }

            try
            {
                isAvailable = aggregator.IsAvailable();
                return true;
            }
            catch (Exception ex)
            {
                RecordFailure(type, ex);
                isAvailable = false;
                return false;
            }
        }

        private bool IsWaitingToRetry(WindowType type)
        {
            if (!_failures.TryGetValue(type, out var failure))
            {
                return false;
            }

            // A retry time further away than the longest backoff means the clock jumped backwards (a server time
            // resync): the wait is over rather than stretched by the size of the jump.
            var untilRetry = failure.RetryAtUtc - _timeProvider.UtcNow;
            return untilRetry > TimeSpan.Zero && untilRetry <= FailureRetryMax;
        }

        /// <returns>How long until the window's retry time.</returns>
        private TimeSpan RecordFailure(WindowType type, Exception exception)
        {
            Debug.LogException(exception);

            _failures.TryGetValue(type, out var previous);
            var count = previous.ConsecutiveFailures + 1;

            var delay = FailureRetryMax;
            if (count <= 6)
            {
                var scaled = TimeSpan.FromTicks(FailureRetryBase.Ticks << (count - 1));
                if (scaled < delay)
                {
                    delay = scaled;
                }
            }

            _failures[type] = new FailureRecord(count, _timeProvider.UtcNow + delay);
            return delay;
        }

        private readonly struct FailureRecord
        {
            public FailureRecord(int consecutiveFailures, DateTime retryAtUtc)
            {
                ConsecutiveFailures = consecutiveFailures;
                RetryAtUtc = retryAtUtc;
            }

            public int ConsecutiveFailures { get; }
            public DateTime RetryAtUtc { get; }
        }
    }
}
