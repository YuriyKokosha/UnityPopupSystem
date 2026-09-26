using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class StackPackerTests
    {
        private static Dictionary<string, int> Totals(string itemId, int count)
        {
            return StackPacker.Aggregate(TestItems.Of(itemId, count));
        }

        [Test]
        public void Aggregate_SumsDuplicateItemIds()
        {
            var totals = StackPacker.Aggregate(new[]
            {
                new ItemAmount(TestItems.Potion, 3),
                new ItemAmount(TestItems.Sword, 1),
                new ItemAmount(TestItems.Potion, 4),
            });

            Assert.That(totals[TestItems.Potion], Is.EqualTo(7));
            Assert.That(totals[TestItems.Sword], Is.EqualTo(1));
        }

        [Test]
        public void CountNewStacksRequired_NonStackable_NeedsOneSlotPerUnit()
        {
            var required = StackPacker.CountNewStacksRequired(
                new List<ItemStack>(), TestItems.Catalog, Totals(TestItems.Sword, 3));

            Assert.That(required, Is.EqualTo(3));
        }

        [Test]
        public void CountNewStacksRequired_RoundsUpToWholeStacks()
        {
            var required = StackPacker.CountNewStacksRequired(
                new List<ItemStack>(), TestItems.Catalog, Totals(TestItems.Potion, 21));

            Assert.That(required, Is.EqualTo(3), "21 potions at 10 per slot need 3 slots.");
        }

        [Test]
        public void CountNewStacksRequired_TopsUpPartialStacksFirst()
        {
            var stacks = new List<ItemStack> { new(1, TestItems.Potion, 7) };

            var required = StackPacker.CountNewStacksRequired(stacks, TestItems.Catalog, Totals(TestItems.Potion, 3));

            Assert.That(required, Is.Zero, "3 more potions fit into the 7/10 stack.");
        }

        [Test]
        public void Add_FillsExistingPartialStack_ThenOpensNewOnes()
        {
            var stacks = new List<ItemStack> { new(1, TestItems.Potion, 7) };
            long nextId = 2;

            var result = StackPacker.Add(stacks, TestItems.Catalog, Totals(TestItems.Potion, 15), ref nextId);

            Assert.That(result.Select(s => s.Count), Is.EqualTo(new[] { 10, 10, 2 }));
            Assert.That(result.Select(s => s.StackId), Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(nextId, Is.EqualTo(4));
        }

        [Test]
        public void Add_NonStackable_MakesOneStackPerUnit()
        {
            long nextId = 1;

            var result = StackPacker.Add(new List<ItemStack>(), TestItems.Catalog, Totals(TestItems.Sword, 2), ref nextId);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.All(s => s.Count == 1), Is.True);
        }

        [Test]
        public void Remove_TakesFromTheSmallestStackFirst()
        {
            var stacks = new List<ItemStack>
            {
                new(1, TestItems.Potion, 10),
                new(2, TestItems.Potion, 3),
            };

            var result = StackPacker.Remove(stacks, Totals(TestItems.Potion, 2));

            Assert.That(result.Single(s => s.StackId == 2).Count, Is.EqualTo(1));
            Assert.That(result.Single(s => s.StackId == 1).Count, Is.EqualTo(10));
        }

        [Test]
        public void Remove_EmptiedStacksDisappear_AndOthersAreUntouched()
        {
            var stacks = new List<ItemStack>
            {
                new(1, TestItems.Potion, 10),
                new(2, TestItems.Sword, 1),
                new(3, TestItems.Potion, 3),
            };

            var result = StackPacker.Remove(stacks, Totals(TestItems.Potion, 5));

            Assert.That(result.Select(s => s.StackId), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(result[0].Count, Is.EqualTo(8), "3 came off the small stack, 2 off the full one.");
            Assert.That(result[1].ItemId, Is.EqualTo(TestItems.Sword));
        }

        [Test]
        public void CountOf_SumsAcrossStacks()
        {
            var stacks = new List<ItemStack>
            {
                new(1, TestItems.Potion, 10),
                new(2, TestItems.Sword, 1),
                new(3, TestItems.Potion, 3),
            };

            Assert.That(StackPacker.CountOf(stacks, TestItems.Potion), Is.EqualTo(13));
            Assert.That(StackPacker.CountOf(stacks, TestItems.Arrows), Is.Zero);
        }

        // (value + divisor - 1) / divisor wrapped to -214748364 here.
        [Test]
        public void CountNewStacksRequired_DoesNotWrap_NearIntMaxValue()
        {
            var required = StackPacker.CountNewStacksRequired(
                new List<ItemStack>(), TestItems.Catalog, Totals(TestItems.Potion, int.MaxValue));

            Assert.That(required, Is.EqualTo(214748365));
        }

        [Test]
        public void Aggregate_Saturates_InsteadOfWrapping()
        {
            var totals = StackPacker.Aggregate(new[]
            {
                new ItemAmount(TestItems.Potion, int.MaxValue),
                new ItemAmount(TestItems.Potion, 1),
            });

            Assert.That(totals[TestItems.Potion], Is.EqualTo(int.MaxValue));
        }
    }
}
