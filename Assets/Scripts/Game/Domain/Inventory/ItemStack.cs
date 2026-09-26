using System;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>One occupied slot. <see cref="StackId"/> is a monotonic counter inside the snapshot: it gives
    /// stacks a stable identity and a deterministic order for both the UI and the hash.</summary>
    public sealed class ItemStack
    {
        public long StackId { get; }
        public string ItemId { get; }
        public int Count { get; }

        public ItemStack(long stackId, string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                throw new ArgumentException("A stack needs an item id.", nameof(itemId));
            }

            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "A stack holds at least 1 unit; an empty stack does not exist.");
            }

            StackId = stackId;
            ItemId = itemId;
            Count = count;
        }

        public ItemStack WithCount(int count)
        {
            return new ItemStack(StackId, ItemId, count);
        }
    }
}
