using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Manager;
using UnityEngine;

namespace PopupSystem.Game.Services.WindowQueue
{
    /// <summary>
    /// Drives the priority queue: while the screen is idle, opens the highest-priority available
    /// window. A window may either be waited out (default) or, if it opts in via
    /// <see cref="WindowQueueInfo.AllowInterrupt"/>, force-closed and re-queued the moment a
    /// strictly higher-priority window becomes available.
    /// </summary>
    public sealed class WindowQueueRunner
    {
        private const int IdleMonitorIntervalMs = 500;
        private const int InterruptPollIntervalMs = 250;

        private readonly WindowQueueManager _windowQueueManager;
        private readonly IWindowsManager _windowsManager;
        private readonly Dictionary<WindowType, IWindowQueueAggregator> _aggregators;

        // Windows that already completed a full (non-interrupted) turn during the current burst.
        // Without this, a window with CooldownSeconds == 0 that is still "available" (e.g. an
        // unclaimed daily reward the player closed without claiming) would immediately win the
        // next iteration of the loop below and starve every lower-priority window forever.
        private readonly HashSet<WindowType> _shownThisBurst = new();

        private CancellationTokenSource _monitorCts;
        private bool _isProcessing;

        public WindowQueueRunner(
            WindowQueueManager windowQueueManager,
            IWindowsManager windowsManager,
            List<IWindowQueueAggregator> aggregators)
        {
            _windowQueueManager = windowQueueManager;
            _windowsManager = windowsManager;
            _aggregators = new Dictionary<WindowType, IWindowQueueAggregator>();

            for (var i = 0; i < aggregators.Count; i++)
            {
                _aggregators[aggregators[i].WindowType] = aggregators[i];
            }
        }

        public UniTask ShowAvailableWindowsAsync()
        {
            return ShowAvailableWindowsInternalAsync(default);
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
        }

        private async UniTaskVoid MonitorIdleAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_windowsManager.IsQueueIdle)
                    {
                        await ShowAvailableWindowsInternalAsync(cancellationToken);
                    }

                    await UniTask.Delay(IdleMonitorIntervalMs, cancellationToken: cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // The try/catch lives INSIDE the loop on purpose: an exception escaping a
                    // single tick must not end idle monitoring for the rest of the session - that
                    // would silently stop every future popup (daily reward, offer, ...) from ever
                    // appearing again, with no visible symptom beyond a console log.
                    Debug.LogException(ex);
                }
            }
        }

        private async UniTask ShowAvailableWindowsInternalAsync(CancellationToken cancellationToken)
        {
            if (_isProcessing)
            {
                return;
            }

            _isProcessing = true;
            _shownThisBurst.Clear();

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
                        // A single misbehaving window (bad config, a bug in its own Init) must
                        // not take the rest of the queue down with it. Log it, count this item as
                        // shown so it doesn't retry-loop for the rest of THIS burst, and move on -
                        // it gets another chance on the next idle check in case the failure was
                        // transient. Deliberately not calling MarkShown: that drives cooldown
                        // timing, which should only advance for windows actually shown.
                        Debug.LogException(ex);
                        _shownThisBurst.Add(item.WindowType);
                        continue;
                    }

                    if (wasInterrupted)
                    {
                        // Not marked shown and not added to _shownThisBurst: it stays eligible and
                        // will be reconsidered right after the interrupting window closes.
                        continue;
                    }

                    _windowQueueManager.MarkShown(item);
                    _shownThisBurst.Add(item.WindowType);
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Waits for the window to close on its own. If it opts into interruption, this races
        /// that wait against a watcher for a strictly higher-priority window becoming available;
        /// if the watcher wins, the window is force-closed and <c>true</c> is returned so the
        /// caller knows not to treat this as a completed turn.
        /// </summary>
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

            var (winArgIndex, _, _) = await UniTask.WhenAny(closeTask, interruptTask);
            watchCts.Cancel();

            if (winArgIndex == 1 && !handle.IsClosed)
            {
                await handle.CloseAsync();
                return true;
            }

            return false;
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

                    await UniTask.Delay(InterruptPollIntervalMs, cancellationToken: cancellationToken);
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

            for (var i = 0; i < items.Count; i++)
            {
                var candidate = items[i];

                // Items are sorted by descending priority, so once we drop to currentPriority or
                // below nothing further in the list can interrupt it either.
                if (candidate.Priority <= currentPriority)
                {
                    break;
                }

                if (_shownThisBurst.Contains(candidate.WindowType))
                {
                    continue;
                }

                if (!_aggregators.TryGetValue(candidate.WindowType, out var aggregator))
                {
                    continue;
                }

                if (aggregator.IsAvailable() && _windowQueueManager.IsCooldownReady(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetNextWindow(out WindowQueueInfo item, out IWindowData payload)
        {
            var items = _windowQueueManager.GetItemsSortedByPriority();

            for (var i = 0; i < items.Count; i++)
            {
                var currentItem = items[i];

                if (_shownThisBurst.Contains(currentItem.WindowType))
                {
                    continue;
                }

                if (!_aggregators.TryGetValue(currentItem.WindowType, out var aggregator))
                {
                    continue;
                }

                if (!aggregator.IsAvailable() || !_windowQueueManager.IsCooldownReady(currentItem))
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
    }
}
