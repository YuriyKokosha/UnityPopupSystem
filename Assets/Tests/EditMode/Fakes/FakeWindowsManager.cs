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

        public UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null)
        {
            if (OpenExceptionFor == type)
            {
                throw new InvalidOperationException($"{type} could not open (simulated failure).");
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

            IsQueueIdle = false;
            OpenCalls.Add(new OpenCall(type, payload, handle));
            return UniTask.FromResult(handle);
        }

        public UniTask CloseCurrentWindowAsync() => UniTask.CompletedTask;

        public UniTask CloseTopPopupAsync() => UniTask.CompletedTask;
    }
}
