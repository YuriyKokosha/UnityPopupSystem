using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using Zenject;

namespace PopupSystem.UI.Windows.MainGame
{
    public sealed class MainGameWindowModule : IWindowModule
    {
        private readonly IFactory<MainGameWindowController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.MainGame,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: false,
            viewType: typeof(MainGameWindowView),
            prefabAddress: "UI/Windows/MainGameWindow",
            isBaseScreen: true);

        public MainGameWindowModule(IFactory<MainGameWindowController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
