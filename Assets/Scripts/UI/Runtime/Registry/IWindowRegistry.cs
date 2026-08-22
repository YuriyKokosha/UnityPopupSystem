using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;

namespace PopupSystem.UI.Runtime.Registry
{
    public interface IWindowRegistry
    {
        WindowDefinition Get(WindowType type);
    }
}
