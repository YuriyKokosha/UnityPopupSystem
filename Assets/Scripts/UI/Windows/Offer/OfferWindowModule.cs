using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.Offer
{
    public sealed class OfferWindowModule : IWindowModule
    {
        private readonly IFactory<OfferWindowController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.Offer,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: true,
            viewType: typeof(OfferWindowView),
            prefabAddress: "UI/Windows/OfferWindow",
            transition: SharedWindowTransitions.PopupDefault);

        public OfferWindowModule(IFactory<OfferWindowController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
