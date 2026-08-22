using Cysharp.Threading.Tasks;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;

namespace PopupSystem.UI.Runtime.Manager
{
    public interface IWindowsManager
    {
        bool IsQueueIdle { get; }
        UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null);
        UniTask CloseCurrentWindowAsync();
        UniTask CloseTopPopupAsync();
    }
}
