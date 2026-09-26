using System;
using UnityEngine;

namespace PopupSystem.Game.Services
{
    /// <summary>Raises a service's "state changed" event once the state is already committed.
    /// Two guarantees the services rely on (see Docs/knowledge-base/resilience.md):
    /// <list type="bullet">
    /// <item>Observer isolation: every subscriber runs on its own; one that throws is logged and the rest still
    /// run. An exception in presentation code can therefore never reach the operation that changed the state,
    /// so it can neither abort the remaining steps of that operation nor make a caller retry an operation that
    /// has in fact already happened.</item>
    /// <item>Deferral: inside <see cref="Defer"/> nothing is raised; one notification goes out when the
    /// outermost scope ends. A multi-step local transaction (items + currencies + price) holds its observers
    /// back until every step has been applied, so no subscriber can run — or re-enter the services — between
    /// the check and the last mutation.</item>
    /// </list></summary>
    internal sealed class StateChangeNotifier
    {
        private readonly Func<Action> _subscribers;

        private int _deferDepth;
        private bool _isPending;

        /// <param name="subscribers">Reads the owner's event field at raise time, so subscriptions made while a
        /// scope is open are honoured.</param>
        public StateChangeNotifier(Func<Action> subscribers)
        {
            _subscribers = subscribers ?? throw new ArgumentNullException(nameof(subscribers));
        }

        public void Raise()
        {
            if (_deferDepth > 0)
            {
                _isPending = true;
                return;
            }

            InvokeIsolated(_subscribers());
        }

        /// <summary>Holds notifications back until the returned scope is disposed. Scopes nest; dispose each
        /// exactly once (a <c>using</c> statement does that).</summary>
        public Scope Defer()
        {
            _deferDepth++;
            return new Scope(this);
        }

        /// <summary>Invokes each subscriber of <paramref name="handlers"/> separately and logs, rather than
        /// propagates, what any of them throws.</summary>
        public static void InvokeIsolated(Action handlers)
        {
            if (handlers == null)
            {
                return;
            }

            var subscribers = handlers.GetInvocationList();
            for (var i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((Action)subscribers[i]).Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }

        private void EndDefer()
        {
            if (_deferDepth == 0)
            {
                return;
            }

            _deferDepth--;

            if (_deferDepth > 0 || !_isPending)
            {
                return;
            }

            _isPending = false;
            InvokeIsolated(_subscribers());
        }

        public readonly struct Scope : IDisposable
        {
            private readonly StateChangeNotifier _owner;

            internal Scope(StateChangeNotifier owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                _owner?.EndDefer();
            }
        }
    }
}
