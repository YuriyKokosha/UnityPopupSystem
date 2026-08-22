using System.Collections.Generic;

namespace PopupSystem.Game.Domain.Inventory
{
    public sealed class InventorySnapshot
    {
        public IReadOnlyList<InventoryResource> Resources { get; }

        public InventorySnapshot(IReadOnlyList<InventoryResource> resources)
        {
            Resources = resources;
        }
    }
}
