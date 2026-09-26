using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class InventoryHasherTests
    {
        [Test]
        public void EmptySnapshot_HasTheDocumentedHash()
        {
            Assert.That(InventoryHasher.Canonicalize(InventorySnapshot.Empty), Is.EqualTo("0"));
            Assert.That(
                InventorySnapshot.Empty.Hash,
                Is.EqualTo("5feceb66ffc86f38d952786c6d696c79c2dbc239dd4e91b46729d73a27fb57e9"));
        }

        [Test]
        public void TestVector_FromTheFeatureMap_StillMatches()
        {
            // Docs/feature-maps/inventory.md fixes this string and hash as the contract a backend has to reproduce.
            var snapshot = new InventorySnapshot(
                new[] { new ItemStack(1, TestItems.Sword, 1), new ItemStack(2, TestItems.Potion, 7) },
                revision: 3,
                nextStackId: 3);

            Assert.That(InventoryHasher.Canonicalize(snapshot), Is.EqualTo("3;sword:1;potion:7"));
            Assert.That(snapshot.Hash, Is.EqualTo("661f4d03000ebd300783237b3de8ac30c00b214620c11d548d15c0139d6fc813"));
        }

        [Test]
        public void SameState_SameHash_WhicheverOrderTheStacksWereListedIn()
        {
            var a = new InventorySnapshot(
                new[] { new ItemStack(1, TestItems.Sword, 1), new ItemStack(2, TestItems.Potion, 7) }, 1, 3);
            var b = new InventorySnapshot(
                new[] { new ItemStack(2, TestItems.Potion, 7), new ItemStack(1, TestItems.Sword, 1) }, 1, 3);

            Assert.That(a.Hash, Is.EqualTo(b.Hash));
        }

        [Test]
        public void StackOrder_IsPartOfTheState()
        {
            var a = new InventorySnapshot(
                new[] { new ItemStack(1, TestItems.Sword, 1), new ItemStack(2, TestItems.Potion, 7) }, 1, 3);
            var b = new InventorySnapshot(
                new[] { new ItemStack(1, TestItems.Potion, 7), new ItemStack(2, TestItems.Sword, 1) }, 1, 3);

            Assert.That(a.Hash, Is.Not.EqualTo(b.Hash));
        }

        [Test]
        public void Revision_IsPartOfTheHash()
        {
            var a = new InventorySnapshot(new[] { new ItemStack(1, TestItems.Sword, 1) }, 1, 2);
            var b = new InventorySnapshot(new[] { new ItemStack(1, TestItems.Sword, 1) }, 2, 2);

            Assert.That(a.Hash, Is.Not.EqualTo(b.Hash));
        }

        [Test]
        public void Snapshot_RejectsDuplicateStackIds_AndAStaleNextStackId()
        {
            Assert.That(
                () => new InventorySnapshot(new[] { new ItemStack(1, TestItems.Sword, 1), new ItemStack(1, TestItems.Potion, 1) }, 0, 2),
                Throws.ArgumentException);
            Assert.That(
                () => new InventorySnapshot(new[] { new ItemStack(5, TestItems.Sword, 1) }, 0, 5),
                Throws.ArgumentException);
        }
    }
}
