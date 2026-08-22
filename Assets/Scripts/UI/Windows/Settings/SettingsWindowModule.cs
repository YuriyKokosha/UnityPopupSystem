using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.Settings
{
    /// <summary>
    /// Self-registering description of the Settings window - see IWindowModule for why this
    /// exists instead of a case in a shared switch/dictionary.
    /// </summary>
    public sealed class SettingsWindowModule : IWindowModule
    {
        private readonly DiContainer _container;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.Settings,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: false,
            viewType: typeof(SettingsWindowView),
            prefabResourcePath: "UI/Windows/SettingsWindow",
            transition: SharedWindowTransitions.PopupDefault);

        public SettingsWindowModule(DiContainer container)
        {
            _container = container;
        }

        public IWindowController CreateController() => _container.Instantiate<SettingsWindowController>();
    }
}
