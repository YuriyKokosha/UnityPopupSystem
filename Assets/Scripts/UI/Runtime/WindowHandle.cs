using System;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Enum;

namespace PopupSystem.UI.Runtime
{
    public sealed class WindowHandle
    {
        private readonly Func<UniTask> _closeAsync;
        private readonly UniTaskCompletionSource _closedSource = new();

        public WindowType Type { get; }
        public bool IsClosed { get; private set; }

        /// <summary>
        /// Current stage of the window's lifecycle. Always progresses in order:
        /// Initializing -> Opening -> Active -> Closing -> Disposed.
        /// </summary>
        public WindowLifecycleState State { get; private set; } = WindowLifecycleState.None;

        /// <summary>
        /// Raised every time <see cref="State"/> changes. Lets callers (e.g. queue logic or
        /// diagnostics) observe the lifecycle without polling.
        /// </summary>
        public event Action<WindowLifecycleState> StateChanged;

        public WindowHandle(WindowType type, Func<UniTask> closeAsync)
        {
            Type = type;
            _closeAsync = closeAsync;
        }

        public UniTask CloseAsync()
        {
            if (IsClosed)
            {
                return UniTask.CompletedTask;
            }

            return _closeAsync.Invoke();
        }

        public UniTask WaitForCloseAsync()
        {
            return _closedSource.Task;
        }

        internal void SetState(WindowLifecycleState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateChanged?.Invoke(state);
        }

        internal void MarkClosed()
        {
            if (IsClosed)
            {
                return;
            }

            IsClosed = true;
            SetState(WindowLifecycleState.Disposed);
            _closedSource.TrySetResult();
        }
    }
}
