using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime;

namespace PopupSystem.UI.Transitions
{
    /// <summary>
    /// A pluggable strategy for how a window appears/disappears. Assigned per window type via
    /// <see cref="PopupSystem.UI.Definitions.WindowDefinition.Transition"/> (see WindowRegistry) -
    /// adding a new visual style, or picking a different one for a specific popup, never
    /// requires touching WindowsManager, WindowFactory or any other window's code.
    /// </summary>
    public interface IWindowTransition
    {
        /// <summary>
        /// Called by WindowView.PlayOpenAsync right after the window's GameObject is activated.
        /// The provided token is the window's lifetime token - if the window is closed before
        /// this finishes, the transition must let the cancellation propagate rather than swallow it.
        /// </summary>
        UniTask PlayOpenAsync(WindowView view, CancellationToken cancellationToken);

        /// <summary>
        /// Called by WindowView.PlayCloseAsync before the window's GameObject is deactivated.
        /// Always invoked with a non-cancellable token by WindowsManager, so implementations do
        /// not need to handle cancellation here - closing must always run to completion.
        /// </summary>
        UniTask PlayCloseAsync(WindowView view, CancellationToken cancellationToken);
    }
}
