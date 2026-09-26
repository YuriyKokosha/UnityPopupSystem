using System;
using System.Collections.Generic;

namespace PopupSystem.Game.Domain.Inventory
{
    /// <summary>The whole inventory state: what is stored locally and what is checked against the server.
    /// Stacks are ordered by <see cref="ItemStack.StackId"/>; the constructor enforces it so the hash is
    /// canonical whichever way the list was built.</summary>
    public sealed class InventorySnapshot
    {
        public static readonly InventorySnapshot Empty = new(Array.Empty<ItemStack>(), revision: 0, nextStackId: 1);

        public IReadOnlyList<ItemStack> Stacks { get; }
        public long Revision { get; }
        public long NextStackId { get; }
        public string Hash { get; }

        public InventorySnapshot(IReadOnlyList<ItemStack> stacks, long revision, long nextStackId)
        {
            if (stacks == null)
            {
                throw new ArgumentNullException(nameof(stacks));
            }

            var ordered = new List<ItemStack>(stacks);
            ordered.Sort((a, b) => a.StackId.CompareTo(b.StackId));

            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].StackId == ordered[i - 1].StackId)
                {
                    throw new ArgumentException($"Stack id {ordered[i].StackId} appears twice.", nameof(stacks));
                }
            }

            if (ordered.Count > 0 && ordered[ordered.Count - 1].StackId >= nextStackId)
            {
                throw new ArgumentException(
                    $"NextStackId {nextStackId} must be greater than every existing stack id.", nameof(nextStackId));
            }

            Stacks = ordered;
            Revision = revision;
            NextStackId = nextStackId;
            Hash = InventoryHasher.Compute(this);
        }

        public int UsedSlots => Stacks.Count;
    }
}
