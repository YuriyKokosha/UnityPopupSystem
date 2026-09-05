using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.DailyReward
{
    public sealed class DailyRewardWindowModule : IWindowModule
    {
        private readonly IFactory<DailyRewardWindowController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.DailyReward,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: true,
            viewType: typeof(DailyRewardWindowView),
            prefabAddress: "UI/Windows/DailyRewardWindow",
            transition: SharedWindowTransitions.PopupDefault);

        public DailyRewardWindowModule(IFactory<DailyRewardWindowController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
