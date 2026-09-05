using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;

namespace PopupSystem.UI.Runtime.Controller
{
    public interface IWindowController : IDisposable
    {
        UniTask InitializeAsync(WindowView view, WindowHandle handle, IWindowData payload, CancellationToken cancellationToken);
    }
}
