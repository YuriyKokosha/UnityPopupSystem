using System;
using Cysharp.Threading.Tasks;

namespace PopupSystem.Contracts
{
    public interface IWindowsManager
    {
        /// <summary>True when nothing the queue should wait for is on screen. A window still playing its close
        /// transition counts as busy.</summary>
        bool IsQueueIdle { get; }

        /// <summary>Raised the moment <see cref="IsQueueIdle"/> turns true.</summary>
        event Action QueueBecameIdle;

        /// <summary>True while any popup is on screen.</summary>
        bool HasOpenPopups { get; }

        UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null);
        UniTask CloseCurrentWindowAsync();
        UniTask CloseTopPopupAsync();
    }
}
