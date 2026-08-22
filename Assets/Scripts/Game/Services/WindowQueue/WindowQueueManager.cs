using System;
using System.Collections.Generic;
using System.Linq;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Services.WindowQueue
{
    public sealed class WindowQueueManager
    {
        private readonly Dictionary<WindowType, WindowQueueInfo> _items = new();
        private readonly Dictionary<WindowType, DateTime> _lastShownAt = new();

        public void SetItems(IReadOnlyList<WindowQueueInfo> items)
        {
            _items.Clear();
            _lastShownAt.Clear();

            if (items == null)
            {
                return;
            }

            for (var i = 0; i < items.Count; i++)
            {
                _items[items[i].WindowType] = items[i];
            }
        }

        public IReadOnlyList<WindowQueueInfo> GetItemsSortedByPriority()
        {
            return _items.Values
                .OrderByDescending(item => item.Priority)
                .ThenBy(item => item.WindowType)
                .ToArray();
        }

        public bool IsCooldownReady(WindowQueueInfo item)
        {
            if (!_lastShownAt.TryGetValue(item.WindowType, out var lastShownAt))
            {
                return true;
            }

            return DateTime.UtcNow >= lastShownAt.AddSeconds(item.CooldownSeconds);
        }

        public void MarkShown(WindowQueueInfo item)
        {
            _lastShownAt[item.WindowType] = DateTime.UtcNow;
        }
    }
}
