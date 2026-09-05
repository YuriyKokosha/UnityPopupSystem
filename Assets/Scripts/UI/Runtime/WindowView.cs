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

        public virtual async UniTask PlayOpenAsync(CancellationToken cancellationToken)
        {
            Show();

            if (_transition != null)
            {
                await _transition.PlayOpenAsync(this, cancellationToken);
            }
        }

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

        internal virtual void ResetForPool()
        {
        }
    }
}
