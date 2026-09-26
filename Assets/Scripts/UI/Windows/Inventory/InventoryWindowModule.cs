using PopupSystem.Contracts;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Transitions;
using Zenject;

namespace PopupSystem.UI.Windows.Inventory
{
    public sealed class InventoryWindowModule : IWindowModule
    {
        public const string PrefabAddress = "UI/Windows/InventoryWindow";

        private readonly IFactory<InventoryWindowController> _controllerFactory;

        public WindowDefinition Definition { get; } = new WindowDefinition(
            WindowType.Inventory,
            UIEntryKind.Window,
            UILayerType.Windows,
            isModal: true,
            viewType: typeof(InventoryWindowView),
            prefabAddress: PrefabAddress,
            transition: SharedWindowTransitions.PopupDefault,
            closeOnBackdropClick: true);

        public InventoryWindowModule(IFactory<InventoryWindowController> controllerFactory)
        {
            _controllerFactory = controllerFactory;
        }

        public IWindowController CreateController() => _controllerFactory.Create();
    }
}
