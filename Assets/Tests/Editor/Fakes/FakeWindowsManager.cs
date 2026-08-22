using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Manager;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>
    /// In-memory stand-in for the real WindowsManager. Records every OpenAsync call and hands
    /// back a real <see cref="WindowHandle"/> wired to a close callback that flips
    /// <see cref="IsQueueIdle"/> back to true and marks the handle closed - this lets tests drive
    /// "the player closed the window" (or WindowQueueRunner force-closing it on interrupt) just
    /// by calling handle.CloseAsync(), without needing a real WindowsManager, a View, or a
    /// Zenject container.
    ///
    /// Only the members WindowQueueRunner actually depends on (<see cref="IsQueueIdle"/>,
    /// <see cref="OpenAsync"/>) are exercised meaningfully here. CloseCurrentWindowAsync and
    /// CloseTopPopupAsync are no-ops: the runner always drives closing through the WindowHandle
    /// it received from OpenAsync, never through these.
    /// </summary>
    public sealed class FakeWindowsManager : IWindowsManager
    {
        public sealed class OpenCall
        {
            public OpenCall(WindowType type, IWindowData payload, WindowHandle handle)
            {
                Type = type;
                Payload = payload;
                Handle = handle;
            }

            public WindowType Type { get; }
            public IWindowData Payload { get; }
            public WindowHandle Handle { get; }
        }

        public List<OpenCall> OpenCalls { get; } = new();
        public bool IsQueueIdle { get; set; } = true;

        public UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null)
        {
            WindowHandle handle = null;
            handle = new WindowHandle(type, () =>
            {
                // Internal access granted via [InternalsVisibleTo] on Assembly-CSharp-Editor -
                // see Assets/Scripts/AssemblyInfo.cs.
                //
                // Order matters here and must mirror the real WindowsManager: it always removes
                // an instance from its stack (which is what IsQueueIdle is computed from) BEFORE
                // calling Handle.MarkClosed() - see WindowsManager.CloseInstanceAsync, called only
                // after the caller already popped the instance. MarkClosed() completes
                // WindowHandle's WaitForCloseAsync() source, which WindowQueueRunner is awaiting;
                // that resumption can run synchronously, re-entering this fake and reading
                // IsQueueIdle before this closure would otherwise get a chance to flip it. Setting
                // it first (as done here) closes that race - flipping it after would leave the
                // runner's own while-loop reading a stale "not idle" and silently drop the item it
                // was about to reconsider.
                IsQueueIdle = true;
                handle.MarkClosed();
                return UniTask.CompletedTask;
            });

            IsQueueIdle = false;
            OpenCalls.Add(new OpenCall(type, payload, handle));
            return UniTask.FromResult(handle);
        }

        public UniTask CloseCurrentWindowAsync() => UniTask.CompletedTask;

        public UniTask CloseTopPopupAsync() => UniTask.CompletedTask;
    }
}
