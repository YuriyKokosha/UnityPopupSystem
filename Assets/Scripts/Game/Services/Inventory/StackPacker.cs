using System;
using System.Collections.Generic;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Inventory
{
    /// <summary>Stateless stacking rules: how a quantity is laid out over slots. Nothing here touches the
    /// manager's state, so every rule is testable on plain lists. See Docs/feature-maps/inventory.md.</summary>
    public static class StackPacker
    {
        // Every sum below is accumulated in long and saturated at int.MaxValue instead of wrapping. Saturation
        // is safe because each caller compares the result with a bound below int.MaxValue
        // (InventoryConfig.MaxUnitsPerItem, a free-slot count, a held count): a saturated value still fails that
        // comparison, whereas a wrapped one reads as a small or negative number and passes it.

        /// <summary>Sums duplicates so a bundle listing the same item twice is treated as one quantity. A total
        /// past <see cref="int.MaxValue"/> saturates rather than wrapping.</summary>
        public static Dictionary<string, int> Aggregate(IReadOnlyList<ItemAmount> items)
        {
            var totals = new Dictionary<string, int>();

            if (items == null)
            {
                return totals;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                totals.TryGetValue(item.ItemId, out var current);
                totals[item.ItemId] = Saturate((long)current + item.Count);
            }

            return totals;
        }

        public static int CountOf(IReadOnlyList<ItemStack> stacks, string itemId)
        {
            long total = 0;

            for (var i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                {
                    total += stacks[i].Count;
                }
            }

            return Saturate(total);
        }

        /// <summary>How many new slots adding <paramref name="totals"/> would open, after topping up the
        /// existing partial stacks of each item. Never mutates.</summary>
        public static int CountNewStacksRequired(
            IReadOnlyList<ItemStack> stacks,
            ItemCatalog catalog,
            IReadOnlyDictionary<string, int> totals)
        {
            long required = 0;

            foreach (var pair in totals)
            {
                var definition = catalog.Get(pair.Key);
                var remaining = (long)pair.Value - FreeCapacityIn(stacks, pair.Key, definition.MaxStack);

                if (remaining > 0)
                {
                    required += CeilDiv(remaining, definition.MaxStack);
                }
            }

            return Saturate(required);
        }

        /// <summary>Lays the quantities out: partial stacks first, then new stacks of MaxStack each. The caller
        /// has already checked the slot limit. Returns a new list; <paramref name="nextStackId"/> advances.</summary>
        public static List<ItemStack> Add(
            IReadOnlyList<ItemStack> stacks,
            ItemCatalog catalog,
            IReadOnlyDictionary<string, int> totals,
            ref long nextStackId)
        {
            var result = new List<ItemStack>(stacks);

            foreach (var pair in totals)
            {
                var itemId = pair.Key;
                var remaining = pair.Value;
                var maxStack = catalog.Get(itemId).MaxStack;

                for (var i = 0; i < result.Count && remaining > 0; i++)
                {
                    var stack = result[i];

                    if (stack.ItemId != itemId || stack.Count >= maxStack)
                    {
                        continue;
                    }

                    var toAdd = Math.Min(maxStack - stack.Count, remaining);
                    result[i] = stack.WithCount(stack.Count + toAdd);
                    remaining -= toAdd;
                }

                while (remaining > 0)
                {
                    var count = Math.Min(maxStack, remaining);
                    result.Add(new ItemStack(nextStackId++, itemId, count));
                    remaining -= count;
                }
            }

            return result;
        }

        /// <summary>Takes units from the smallest stacks of an item first so partial stacks do not pile up. The
        /// caller has already checked there is enough. A stack that reaches 0 disappears.</summary>
        public static List<ItemStack> Remove(
            IReadOnlyList<ItemStack> stacks,
            IReadOnlyDictionary<string, int> totals)
        {
            var result = new List<ItemStack>(stacks);

            foreach (var pair in totals)
            {
                var itemId = pair.Key;
                var remaining = pair.Value;

                var candidates = new List<int>();
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].ItemId == itemId)
                    {
                        candidates.Add(i);
                    }
                }

                candidates.Sort((a, b) =>
                {
                    var byCount = result[a].Count.CompareTo(result[b].Count);
                    return byCount != 0 ? byCount : result[a].StackId.CompareTo(result[b].StackId);
                });

                var emptied = new List<int>();

                for (var c = 0; c < candidates.Count && remaining > 0; c++)
                {
                    var index = candidates[c];
                    var stack = result[index];
                    var toTake = Math.Min(stack.Count, remaining);
                    remaining -= toTake;

                    if (toTake == stack.Count)
                    {
                        emptied.Add(index);
                    }
                    else
                    {
                        result[index] = stack.WithCount(stack.Count - toTake);
                    }
                }

                emptied.Sort();
                for (var e = emptied.Count - 1; e >= 0; e--)
                {
                    result.RemoveAt(emptied[e]);
                }
            }

            return result;
        }

        public static int FreeCapacityIn(IReadOnlyList<ItemStack> stacks, string itemId, int maxStack)
        {
            long free = 0;

            for (var i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].ItemId == itemId)
                {
                    // A stack over MaxStack (config lowered after it was filled) simply has no room; it is not negative room.
                    free += Math.Max(0, maxStack - stacks[i].Count);
                }
            }

            return Saturate(free);
        }

        // Division first: (value + divisor - 1) overflows for a value near the type's maximum.
        private static long CeilDiv(long value, int divisor)
        {
            return value / divisor + (value % divisor == 0 ? 0 : 1);
        }

        private static int Saturate(long value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
