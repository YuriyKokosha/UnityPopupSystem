using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.Offer
{
    /// <summary>
    /// Self-registering description of the Offer window - see IWindowModule for why this exists
    /// instead of a case in a shared switch/dictionary.
    /// </summary>
    public sealed class OfferWindowModule : IWindowModule
    {
        private readonly DiContainer _container;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.Offer,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: true,
            viewType: typeof(OfferWindowView),
            prefabResourcePath: "UI/Windows/OfferWindow",
            transition: SharedWindowTransitions.PopupDefault);
            // Same reasoning as DailyReward: no closeOnBackdropClick for a monetization popup.

        public OfferWindowModule(DiContainer container)
        {
            _container = container;
        }

        public IWindowController CreateController() => _container.Instantiate<OfferWindowController>();
    }
}
