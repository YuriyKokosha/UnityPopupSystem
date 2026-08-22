using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Domain.WindowQueue
{
    public sealed class WindowQueueInfo
    {
        public WindowType WindowType { get; }
        public int Priority { get; }
        public float CooldownSeconds { get; }

        /// <summary>
        /// Whether a higher-priority queued window is allowed to interrupt (force-close) this
        /// one while it is being shown. When false, this window can only ever be waited for.
        /// </summary>
        public bool AllowInterrupt { get; }

        public WindowQueueInfo(WindowType windowType, int priority, float cooldownSeconds, bool allowInterrupt = true)
        {
            WindowType = windowType;
            Priority = priority;
            CooldownSeconds = cooldownSeconds;
            AllowInterrupt = allowInterrupt;
        }
    }
}
