using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.RewardPopup
{
    /// <summary>
    /// Self-registering description of the RewardPopup window - see IWindowModule for why this
    /// exists instead of a case in a shared switch/dictionary.
    /// </summary>
    public sealed class RewardPopupModule : IWindowModule
    {
        private readonly DiContainer _container;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.RewardPopup,
            UIEntryKind.Popup,
            UILayerType.Popups,
            isModal: true,
            viewType: typeof(RewardPopupView),
            prefabResourcePath: "UI/Windows/RewardPopupWindow",
            transition: SharedWindowTransitions.PopupDefault,
            // Purely informational ("here's your reward") - dismissing by tapping outside is
            // expected and low-risk.
            closeOnBackdropClick: true);

        public RewardPopupModule(DiContainer container)
        {
            _container = container;
        }

        public IWindowController CreateController() => _container.Instantiate<RewardPopupController>();
    }
}
