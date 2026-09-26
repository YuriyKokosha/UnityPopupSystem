using System;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>Everything remote config says about the inventory. <see cref="SlotLimit"/> of 0 means no
    /// limit at all.</summary>
    public sealed class InventoryConfig
    {
        public const int NoSlotLimit = 0;

        /// <summary>The most units of one item the inventory holds in total, across all its stacks. A domain
        /// limit rather than a config value: it keeps every count, slot calculation and hash input far inside
        /// <see cref="int"/> range whatever a config or a reward says, and it bounds how many stacks one add can
        /// create even with no slot limit. An add that would cross it is refused as a whole
        /// (<see cref="InventoryFailureReason.QuantityLimitExceeded"/>).</summary>
        public const int MaxUnitsPerItem = 1_000_000;

        public int SlotLimit { get; }
        public ItemCatalog Catalog { get; }

        public InventoryConfig(int slotLimit, ItemCatalog catalog)
        {
            if (slotLimit < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotLimit), slotLimit, "SlotLimit is 0 (unlimited) or positive.");
            }

            SlotLimit = slotLimit;
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public bool HasSlotLimit => SlotLimit != NoSlotLimit;
    }
}
