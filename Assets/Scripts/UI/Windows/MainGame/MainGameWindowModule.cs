using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using Zenject;

namespace PopupSystem.UI.Windows.MainGame
{
    /// <summary>
    /// Self-registering description of the MainGame window - see IWindowModule for why this
    /// exists instead of a case in a shared switch/dictionary.
    /// </summary>
    public sealed class MainGameWindowModule : IWindowModule
    {
        private readonly DiContainer _container;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.MainGame,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: false,
            viewType: typeof(MainGameWindowView),
            prefabResourcePath: "UI/Windows/MainGameWindow");
            // No transition: it's the persistent base screen, not a popup - it should just be
            // there, not animate in.

        public MainGameWindowModule(DiContainer container)
        {
            _container = container;
        }

        public IWindowController CreateController() => _container.Instantiate<MainGameWindowController>();
    }
}
