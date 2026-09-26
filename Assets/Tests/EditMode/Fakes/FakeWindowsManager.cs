using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;

namespace PopupSystem.Tests.EditMode.Fakes
{
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

        public event Action QueueBecameIdle;

        public bool HasOpenPopups { get; set; }

        public WindowType? OpenExceptionFor { get; set; }

        /// <summary>Every OpenAsync call, including the ones that failed.</summary>
        public int OpenAttempts { get; private set; }

        public UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null)
        {
            OpenAttempts++;

            if (OpenExceptionFor == type)
            {
                // Mirrors the real WindowsManager: a failed open stops counting as pending and raises
                // QueueBecameIdle before the exception reaches the caller - a wake the runner must not turn into
                // an immediate retry of the same broken window.
                IsQueueIdle = true;
                QueueBecameIdle?.Invoke();
                throw new InvalidOperationException($"[Expected] {type} could not open (simulated failure).");
            }

            WindowHandle handle = null;
            handle = new WindowHandle(type, () =>
            {
                // Order mirrors the real WindowsManager: stop counting the window as busy before MarkClosed,
                // which can resume the runner synchronously.
                IsQueueIdle = true;
                handle.MarkClosed();
                QueueBecameIdle?.Invoke();
                return UniTask.CompletedTask;
            });

            // Busy synchronously, as production is: WindowsManager counts a pending open before it awaits the prefab
            // load. A PlayMode test pins the production side (see the "drift" rule in testing-in-unity.md).
            IsQueueIdle = false;
            OpenCalls.Add(new OpenCall(type, payload, handle));
            return UniTask.FromResult(handle);
        }

        public UniTask CloseCurrentWindowAsync() => UniTask.CompletedTask;

        public UniTask CloseTopPopupAsync() => UniTask.CompletedTask;
    }
}
