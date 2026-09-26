using System;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>"How many of what" as an input to add/remove — no stack breakdown, the inventory decides that.</summary>
    public sealed class ItemAmount
    {
        public string ItemId { get; }
        public int Count { get; }

        public ItemAmount(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("An item amount needs an item id.", nameof(itemId));
            }

            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "An item amount is at least 1.");
            }

            ItemId = itemId;
            Count = count;
        }
    }
}
