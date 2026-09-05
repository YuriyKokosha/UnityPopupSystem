using System;
using System.Collections.Generic;
using System.Linq;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.Time;

namespace PopupSystem.Game.Services.WindowQueue
{
    public sealed class WindowQueueManager
    {
        private readonly ITimeProvider _timeProvider;
        private readonly Dictionary<WindowType, WindowQueueInfo> _items = new();
        private readonly Dictionary<WindowType, DateTime> _lastShownAt = new();

        private readonly List<WindowType> _staleTypes = new();

        private WindowQueueInfo[] _sortedItems = Array.Empty<WindowQueueInfo>();

        public WindowQueueManager(ITimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public event Action ItemsChanged;

        public void SetItems(IReadOnlyList<WindowQueueInfo> items)
        {
            _items.Clear();

            if (items != null)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    _items[items[i].WindowType] = items[i];
                }
            }

            PruneLastShownToCurrentItems();
            RebuildSortedItems();

            ItemsChanged?.Invoke();
        }

        public bool Contains(WindowType type)
        {
            return _items.ContainsKey(type);
        }

        public IReadOnlyList<WindowQueueInfo> GetItemsSortedByPriority()
        {
            return _sortedItems;
        }

        public bool IsCooldownReady(WindowQueueInfo item)
        {
            if (!_lastShownAt.TryGetValue(item.WindowType, out var lastShownAt))
            {
                return true;
            }

            return _timeProvider.UtcNow >= lastShownAt.AddSeconds(item.CooldownSeconds);
        }

        public TimeSpan? TimeUntilCooldownReady(WindowQueueInfo item)
        {
            if (item.CooldownSeconds <= 0f)
            {
                return null;
            }

            if (!_lastShownAt.TryGetValue(item.WindowType, out var lastShownAt))
            {
                return null;
            }

            var remaining = lastShownAt.AddSeconds(item.CooldownSeconds) - _timeProvider.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : null;
        }

        public void MarkShown(WindowQueueInfo item)
        {
            _lastShownAt[item.WindowType] = _timeProvider.UtcNow;
        }

        private void PruneLastShownToCurrentItems()
        {
            if (_lastShownAt.Count == 0)
            {
                return;
            }

            _staleTypes.Clear();

            foreach (var windowType in _lastShownAt.Keys)
            {
                if (!_items.ContainsKey(windowType))
                {
                    _staleTypes.Add(windowType);
                }
            }

            for (var i = 0; i < _staleTypes.Count; i++)
            {
                _lastShownAt.Remove(_staleTypes[i]);
            }

            _staleTypes.Clear();
        }

        private void RebuildSortedItems()
        {
            _sortedItems = _items.Values
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.WindowType)
                .ToArray();
        }
    }
}
