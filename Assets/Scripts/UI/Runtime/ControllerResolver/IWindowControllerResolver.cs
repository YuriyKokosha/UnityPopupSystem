using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime.ControllerResolver
{
    public interface IWindowControllerResolver
    {
        IWindowController Create(WindowType type);
    }
}
