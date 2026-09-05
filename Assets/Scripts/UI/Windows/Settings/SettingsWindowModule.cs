using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.Settings
{
    public sealed class SettingsWindowModule : IWindowModule
    {
        private readonly IFactory<SettingsWindowController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.Settings,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: false,
            viewType: typeof(SettingsWindowView),
            prefabAddress: "UI/Windows/SettingsWindow",
            transition: SharedWindowTransitions.PopupDefault);

        public SettingsWindowModule(IFactory<SettingsWindowController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
