using System;
using Cysharp.Threading.Tasks;

namespace PopupSystem.Contracts
{
    public sealed class WindowHandle
    {
        private readonly Func<UniTask> _closeAsync;
        private readonly UniTaskCompletionSource _closedSource = new();

        public WindowType Type { get; }
        public bool IsClosed { get; private set; }

        /// <summary>Never moves backwards, but stages can be skipped - a cancelled open goes straight to
        /// Closing. Test for the stage you need; never infer that its predecessor ran.</summary>
        public WindowLifecycleState State { get; private set; } = WindowLifecycleState.None;

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
