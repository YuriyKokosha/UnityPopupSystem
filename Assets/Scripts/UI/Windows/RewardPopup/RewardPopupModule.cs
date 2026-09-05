using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.RewardPopup
{
    public sealed class RewardPopupModule : IWindowModule
    {
        private readonly IFactory<RewardPopupController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.RewardPopup,
            UIEntryKind.Popup,
            UILayerType.Popups,
            isModal: true,
            viewType: typeof(RewardPopupView),
            prefabAddress: "UI/Windows/RewardPopupWindow",
            transition: SharedWindowTransitions.PopupDefault,
            closeOnBackdropClick: true);

        public RewardPopupModule(IFactory<RewardPopupController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
