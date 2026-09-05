using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime;

namespace PopupSystem.UI.Transitions
{
    public interface IWindowTransition
    {
        /// <summary>Gets the window's lifetime token - let cancellation propagate rather than swallow it.</summary>
        UniTask PlayOpenAsync(WindowView view, CancellationToken cancellationToken);

        /// <summary>Always called with a non-cancellable token: closing must run to completion.</summary>
        UniTask PlayCloseAsync(WindowView view, CancellationToken cancellationToken);
    }
}
