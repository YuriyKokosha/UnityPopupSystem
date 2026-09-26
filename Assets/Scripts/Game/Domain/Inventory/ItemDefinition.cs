using System;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>The config of one item type. <see cref="MaxStack"/> is how many units share a slot:
    /// 1 means the item never stacks (a sword), 10 means ten per slot (a potion).</summary>
    public sealed class ItemDefinition
    {
        public string ItemId { get; }
        public string DisplayName { get; }
        public string IconAddress { get; }
        public int MaxStack { get; }
        public ItemCategory Category { get; }

        public ItemDefinition(string itemId, string displayName, string iconAddress, int maxStack, ItemCategory category)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("An item needs an id.", nameof(itemId));
            }

            if (maxStack < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxStack), maxStack, "MaxStack must be at least 1.");
            }

            ItemId = itemId;
            DisplayName = displayName ?? itemId;
            IconAddress = iconAddress;
            MaxStack = maxStack;
            Category = category;
        }

        public bool IsStackable => MaxStack > 1;
    }
}
