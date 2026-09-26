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

        /// <summary>Raised once per stage reached. Each subscriber is isolated: a throw is logged and never
        /// reaches the other subscribers or the lifecycle driving the window.</summary>
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
            // Repeats and backward moves are ignored; skipping forward is allowed (see State).
            if (state <= State)
            {
                return;
            }

            State = state;
            RaiseStateChanged(state);
        }

        internal void MarkClosed()
        {
            if (IsClosed)
            {
                return;
            }

            IsClosed = true;
            try
            {
                SetState(WindowLifecycleState.Disposed);
            }
            finally
            {
                // IsClosed is already true, so a second MarkClosed returns early: this is the only completion.
                _closedSource.TrySetResult();
            }
        }

        private void RaiseStateChanged(WindowLifecycleState state)
        {
            var handlers = StateChanged;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<WindowLifecycleState>)handler).Invoke(state);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }
    }
}
