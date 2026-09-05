using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime
{
    /// <summary>Registration for one window type: its definition plus how to build its controller.
    /// The engine's extension seam - see CLAUDE.md §6.</summary>
    public interface IWindowModule
    {
        WindowDefinition Definition { get; }

        IWindowController CreateController();
    }
}
