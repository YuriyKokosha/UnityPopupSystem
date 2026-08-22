using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.DailyReward
{
    /// <summary>
    /// Self-registering description of the DailyReward window - see IWindowModule for why this
    /// exists instead of a case in a shared switch/dictionary.
    /// </summary>
    public sealed class DailyRewardWindowModule : IWindowModule
    {
        private readonly DiContainer _container;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.DailyReward,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: true,
            viewType: typeof(DailyRewardWindowView),
            prefabResourcePath: "UI/Windows/DailyRewardWindow",
            transition: SharedWindowTransitions.PopupDefault);
            // No closeOnBackdropClick: this is an engagement popup with a primary action - it
            // should only close via an explicit button, never an accidental outside tap.

        public DailyRewardWindowModule(DiContainer container)
        {
            _container = container;
        }

        public IWindowController CreateController() => _container.Instantiate<DailyRewardWindowController>();
    }
}
