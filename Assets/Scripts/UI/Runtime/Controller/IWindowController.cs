using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Core;

namespace PopupSystem.UI.Runtime.Controller
{
    public interface IWindowController
    {
        UniTask InitializeAsync(WindowView view, WindowHandle handle, IWindowData payload, CancellationToken cancellationToken);
        void Dispose();
    }
}
