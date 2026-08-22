using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Transitions;
using UnityEngine;

namespace PopupSystem.UI.Runtime
{
    public abstract class WindowView : MonoBehaviour
    {
        public event Action CloseRequested;

        private IWindowTransition _transition;

        /// <summary>
        /// Assigned by WindowFactory right after creation, from the window's own
        /// <see cref="PopupSystem.UI.Definitions.WindowDefinition.Transition"/> - a specific view
        /// never needs to know or care which transition it got. May be null (instant show/hide).
        /// </summary>
        internal void SetTransition(IWindowTransition transition)
        {
            _transition = transition;
        }

        public virtual void Show()
        {
            gameObject.SetActive(true);
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Plays the "Opening" stage of the window lifecycle: activates the GameObject, then
        /// defers to the assigned transition (if any). Override only if a specific window needs
        /// behaviour beyond what a transition strategy can express.
        /// </summary>
        public virtual async UniTask PlayOpenAsync(CancellationToken cancellationToken)
        {
            Show();

            if (_transition != null)
            {
                await _transition.PlayOpenAsync(this, cancellationToken);
            }
        }

        /// <summary>
        /// Plays the "Closing" stage of the window lifecycle: defers to the assigned transition
        /// (if any), then hides the GameObject. Always awaited with a non-cancellable token by
        /// WindowsManager, so disposal is guaranteed to complete.
        /// </summary>
        public virtual async UniTask PlayCloseAsync(CancellationToken cancellationToken)
        {
            if (_transition != null)
            {
                await _transition.PlayCloseAsync(this, cancellationToken);
            }

            Hide();
        }

        protected void RequestClose()
        {
            CloseRequested?.Invoke();
        }

        /// <summary>
        /// Called by WindowFactory right before a closed view is stashed in the pool for reuse by
        /// a later Open of the same window type (see WindowFactory.Release) - never called on an
        /// ordinary Hide()/PlayCloseAsync. The base implementation does nothing, since most views
        /// have no per-open state beyond what their own controller already overwrites on the next
        /// Init. Override when a view holds something a fresh controller won't reset by itself -
        /// e.g. a previously downloaded texture - so a pooled instance doesn't flash stale content
        /// the next time it's shown.
        /// </summary>
        internal virtual void ResetForPool()
        {
        }
    }
}
