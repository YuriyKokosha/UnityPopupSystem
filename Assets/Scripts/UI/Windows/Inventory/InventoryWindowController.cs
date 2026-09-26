using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Services;

namespace PopupSystem.UI.Windows.Inventory
{
    public sealed class InventoryWindowController : WindowController<EmptyWindowData, InventoryWindowView>
    {
        // With no slot limit the grid still needs a shape: show at least this many cells.
        private const int MinimumCellsWithoutLimit = 12;

        private readonly InventoryManager _inventoryManager;
        private readonly RewardIcons _icons;

        public InventoryWindowController(InventoryManager inventoryManager, RewardIcons icons)
        {
            _inventoryManager = inventoryManager;
            _icons = icons;
        }

        protected override async UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            // Every icon the catalog can put in a cell, once: Render is synchronous and runs on every Changed.
            await _icons.PreloadCatalogAsync(cancellationToken);

            View.SetTitle("Inventory");
            Render();

            _inventoryManager.Changed += Render;
            View.UseClicked += OnUseClicked;
            View.DiscardClicked += OnDiscardClicked;
        }

        public override void Dispose()
        {
            _inventoryManager.Changed -= Render;

            if (View != null)
            {
                View.UseClicked -= OnUseClicked;
                View.DiscardClicked -= OnDiscardClicked;
            }

            base.Dispose();
        }

        private void Render()
        {
            if (View == null)
            {
                return;
            }

            var stacks = _inventoryManager.Stacks;
            var catalog = _inventoryManager.Catalog;
            var slots = new List<InventoryWindowView.SlotModel>(stacks.Count);

            for (var i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];

                // An item that left the catalog still belongs to the player: draw it by id rather than crash.
                if (catalog.TryGet(stack.ItemId, out var definition))
                {
                    slots.Add(new InventoryWindowView.SlotModel(
                        stack.StackId,
                        definition.DisplayName,
                        stack.Count,
                        definition.IsStackable,
                        canUse: definition.Category == ItemCategory.Consumable,
                        _icons.GetItemIcon(stack.ItemId)));
                }
                else
                {
                    slots.Add(new InventoryWindowView.SlotModel(stack.StackId, stack.ItemId, stack.Count, true, false, icon: null));
                }
            }

            var slotLimit = _inventoryManager.SlotLimit;
            var totalCells = slotLimit > 0 ? slotLimit : Math.Max(MinimumCellsWithoutLimit, stacks.Count);

            View.SetSlotsSummary(_inventoryManager.UsedSlots, slotLimit);
            View.Render(slots, totalCells);
        }

        // "Use" spends one unit from the tapped cell; the effect of the item is not modelled in this build.
        private void OnUseClicked(long stackId)
        {
            _inventoryManager.TryRemoveFromStack(stackId, 1);
        }

        private void OnDiscardClicked(long stackId)
        {
            _inventoryManager.TryRemoveStack(stackId);
        }
    }
}
