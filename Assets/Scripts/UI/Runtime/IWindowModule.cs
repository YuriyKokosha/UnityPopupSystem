using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime
{
    /// <summary>
    /// Self-contained registration for one window type: its WindowDefinition (how/where it is
    /// created and how it looks) plus how to build its controller. Adding a new window type means
    /// writing one new XxxWindowModule next to that window's View/Controller and binding it as an
    /// IWindowModule in the installer - WindowRegistry and WindowControllerResolver are entirely
    /// generic and never need to change for it (mirrors the IWindowQueueAggregator multi-binding
    /// already used by WindowQueueRunner).
    /// </summary>
    public interface IWindowModule
    {
        WindowDefinition Definition { get; }

        IWindowController CreateController();
    }
}
