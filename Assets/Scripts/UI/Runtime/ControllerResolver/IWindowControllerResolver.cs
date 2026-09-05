using PopupSystem.Contracts;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime.ControllerResolver
{
    public interface IWindowControllerResolver
    {
        IWindowController Create(WindowType type);
    }
}
