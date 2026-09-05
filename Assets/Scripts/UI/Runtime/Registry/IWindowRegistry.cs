using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;

namespace PopupSystem.UI.Runtime.Registry
{
    public interface IWindowRegistry
    {
        WindowDefinition Get(WindowType type);
    }
}
