using System;
using System.Linq;
using NUnit.Framework;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Tests.EditMode.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class InventoryManagerTests
    {
        private FakeInventoryStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _storage = new FakeInventoryStorage();
        }

        [Test]
        public void SlotLimitZero_MeansNoLimit()
        {
            var manager = TestItems.Manager(InventoryConfig.NoSlotLimit, _storage);

            var result = manager.TryAdd(TestItems.Of(TestItems.Sword, 500));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.UsedSlots, Is.EqualTo(500));
            Assert.That(manager.FreeSlots, Is.Null);
        }

        [Test]
        public void TryAdd_ExactlyUpToTheLimit_Succeeds()
        {
            var manager = TestItems.Manager(3, _storage);

            var result = manager.TryAdd(TestItems.Of(TestItems.Sword, 3));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.FreeSlots, Is.Zero);
        }

        [Test]
        public void TryAdd_OneOverTheLimit_IsRejected_WithTheNumbers()
        {
            var manager = TestItems.Manager(3, _storage);

            var result = manager.TryAdd(TestItems.Of(TestItems.Sword, 4));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.NotEnoughSlots));
            Assert.That(result.SlotsRequired, Is.EqualTo(4));
            Assert.That(result.SlotsFree, Is.EqualTo(3));
            Assert.That(manager.UsedSlots, Is.Zero, "A rejected add changes nothing.");
        }

        [Test]
        public void TryAdd_IsAtomicAcrossTheWholeBundle()
        {
            var manager = TestItems.Manager(2, _storage);

            var result = manager.TryAdd(new[]
            {
                new ItemAmount(TestItems.Sword, 1),
                new ItemAmount(TestItems.Potion, 15),
            });

            Assert.That(result.Success, Is.False, "1 sword + 15 potions need 3 slots, only 2 exist.");
            Assert.That(manager.UsedSlots, Is.Zero, "The sword must not land while the potions are refused.");
            Assert.That(manager.GetCount(TestItems.Sword), Is.Zero);
        }

        [Test]
        public void TryAdd_StacksIntoExistingPartialStack_WithoutUsingANewSlot()
        {
            var manager = TestItems.Manager(1, _storage);
            manager.TryAdd(TestItems.Of(TestItems.Potion, 4));

            var result = manager.TryAdd(TestItems.Of(TestItems.Potion, 6));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.UsedSlots, Is.EqualTo(1));
            Assert.That(manager.GetCount(TestItems.Potion), Is.EqualTo(10));
        }

        [Test]
        public void CanAdd_DoesNotMutate_AndDoesNotSave()
        {
            var manager = TestItems.Manager(5, _storage);
            var savesBefore = _storage.SaveCount;

            var result = manager.CanAdd(TestItems.Of(TestItems.Potion, 25));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.UsedSlots, Is.Zero);
            Assert.That(_storage.SaveCount, Is.EqualTo(savesBefore));
        }

        [Test]
        public void TryAdd_UnknownItem_IsRejected_NotThrown()
        {
            var manager = TestItems.Manager(5, _storage);

            var result = manager.TryAdd(TestItems.Of("dragon-egg", 1));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.UnknownItem));
            Assert.That(result.ItemId, Is.EqualTo("dragon-egg"));
        }

        [Test]
        public void TryRemove_MoreThanOwned_IsRejected_AndChangesNothing()
        {
            var manager = TestItems.Manager(5, _storage);
            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));

            var result = manager.TryRemove(TestItems.Potion, 4);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.NotEnoughItems));
            Assert.That(manager.GetCount(TestItems.Potion), Is.EqualTo(3));
        }

        [Test]
        public void TryRemove_FreesTheSlot_WhenTheStackEmpties()
        {
            var manager = TestItems.Manager(5, _storage);
            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));

            var result = manager.TryRemove(TestItems.Potion, 3);

            Assert.That(result.Success, Is.True);
            Assert.That(manager.UsedSlots, Is.Zero);
        }

        [Test]
        public void TryRemoveStack_DropsThatStackOnly()
        {
            var manager = TestItems.Manager(5, _storage);
            manager.TryAdd(new[] { new ItemAmount(TestItems.Sword, 2), new ItemAmount(TestItems.Potion, 1) });
            var swordStack = manager.Stacks.First(s => s.ItemId == TestItems.Sword);

            var removed = manager.TryRemoveStack(swordStack.StackId);

            Assert.That(removed, Is.True);
            Assert.That(manager.UsedSlots, Is.EqualTo(2));
            Assert.That(manager.GetCount(TestItems.Sword), Is.EqualTo(1));
            Assert.That(manager.TryRemoveStack(swordStack.StackId), Is.False, "Already gone.");
        }

        [Test]
        public void Changed_FiresOncePerSuccessfulMutation_AndNeverOnARefusal()
        {
            var manager = TestItems.Manager(1, _storage);
            var changed = 0;
            manager.Changed += () => changed++;

            manager.TryAdd(TestItems.Of(TestItems.Sword, 1));
            manager.TryAdd(TestItems.Of(TestItems.Sword, 1));
            manager.TryRemove(TestItems.Sword, 1);

            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void EverySuccessfulMutation_IsSaved_WithTheLatestSnapshot()
        {
            var manager = TestItems.Manager(5, _storage);

            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));
            manager.TryAdd(TestItems.Of(TestItems.Sword, 1));
            manager.TryAdd(TestItems.Of(TestItems.Sword, 99));

            Assert.That(_storage.SaveCount, Is.EqualTo(2), "The refused add does not write.");
            Assert.That(_storage.LastSaved.Hash, Is.EqualTo(manager.Snapshot.Hash));
        }

        [Test]
        public void Revision_GrowsByOne_PerMutation()
        {
            var manager = TestItems.Manager(5, _storage);

            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));
            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));
            manager.TryRemove(TestItems.Potion, 1);

            Assert.That(manager.Snapshot.Revision, Is.EqualTo(3));
        }

        [Test]
        public void Load_DoesNotSave_ButReplaceFromServerDoes()
        {
            var manager = TestItems.Manager(5, _storage);
            var savesAfterSetup = _storage.SaveCount;
            var serverSnapshot = new InventorySnapshot(new[] { new ItemStack(1, TestItems.Sword, 1) }, revision: 7, nextStackId: 2);

            manager.Load("p1", serverSnapshot);
            Assert.That(_storage.SaveCount, Is.EqualTo(savesAfterSetup));

            manager.ReplaceFromServer(serverSnapshot);
            Assert.That(_storage.SaveCount, Is.EqualTo(savesAfterSetup + 1));
            Assert.That(manager.UsedSlots, Is.EqualTo(1));
        }

        [Test]
        public void ASaveFailure_IsSwallowed_AndTheStateStaysCorrect()
        {
            _storage.ThrowOnSave = true;
            var manager = TestItems.Manager(5, _storage);
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(@"\[Expected\] Disk full"));

            var result = manager.TryAdd(TestItems.Of(TestItems.Sword, 1));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.UsedSlots, Is.EqualTo(1));
        }

        [Test]
        public void LoweringTheLimitBelowUsage_KeepsItems_ButBlocksAdding()
        {
            var manager = TestItems.Manager(5, _storage);
            manager.TryAdd(TestItems.Of(TestItems.Sword, 4));

            manager.Initialize(TestItems.Config(2));

            Assert.That(manager.UsedSlots, Is.EqualTo(4), "Nothing is thrown away.");
            Assert.That(manager.FreeSlots, Is.Zero, "Never negative.");
            Assert.That(manager.TryAdd(TestItems.Of(TestItems.Sword, 1)).Success, Is.False);
            Assert.That(manager.TryRemove(TestItems.Sword, 1).Success, Is.True, "Removing still works.");
        }

        [Test]
        public void Operations_BeforeInitialize_AreAProgrammingError()
        {
            var manager = new InventoryManager(_storage);

            Assert.That(() => manager.CanAdd(TestItems.Of(TestItems.Sword, 1)), Throws.InvalidOperationException);
            Assert.That(() => manager.TryAdd(TestItems.Of(TestItems.Sword, 1)), Throws.InvalidOperationException);
        }

        [Test]
        public void EmptyBundle_IsAlwaysOk_AndChangesNothing()
        {
            var manager = TestItems.Manager(0, _storage);
            var changed = 0;
            manager.Changed += () => changed++;

            var result = manager.TryAdd(Array.Empty<ItemAmount>());

            Assert.That(result.Success, Is.True);
            Assert.That(changed, Is.Zero);
        }

        // If the save were scheduled after Changed, a throwing observer would skip it.
        [Test]
        public void AThrowingChangedObserver_CannotFailTheAdd_OrSkipTheSave()
        {
            var manager = TestItems.Manager(5, _storage);
            var savesBefore = _storage.SaveCount;
            manager.Changed += () => throw new InvalidOperationException("[Expected] slot view failure");
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex(@"\[Expected\] slot view failure"));

            var result = manager.TryAdd(TestItems.Of(TestItems.Sword, 1));

            Assert.That(result.Success, Is.True);
            Assert.That(manager.GetCount(TestItems.Sword), Is.EqualTo(1));
            Assert.That(_storage.SaveCount, Is.EqualTo(savesBefore + 1));
        }

        // ItemAmount accepts int.MaxValue, and unchecked slot arithmetic wraps on it.
        [Test]
        public void AnAddOfIntMaxValue_IsRefused_EvenWithNoSlotLimit()
        {
            var manager = TestItems.Manager(InventoryConfig.NoSlotLimit, _storage);

            var result = manager.TryAdd(TestItems.Of(TestItems.Potion, int.MaxValue));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.QuantityLimitExceeded));
            Assert.That(manager.UsedSlots, Is.Zero);
        }

        [Test]
        public void TheQuantityLimit_CountsWhatIsAlreadyHeld()
        {
            var manager = TestItems.Manager(InventoryConfig.NoSlotLimit, _storage);
            Assert.That(manager.TryAdd(TestItems.Of(TestItems.Arrows, InventoryConfig.MaxUnitsPerItem - 1)).Success, Is.True);

            var overLimit = manager.TryAdd(TestItems.Of(TestItems.Arrows, 2));
            var upToLimit = manager.TryAdd(TestItems.Of(TestItems.Arrows, 1));

            Assert.That(overLimit.Reason, Is.EqualTo(InventoryFailureReason.QuantityLimitExceeded));
            Assert.That(upToLimit.Success, Is.True);
            Assert.That(manager.GetCount(TestItems.Arrows), Is.EqualTo(InventoryConfig.MaxUnitsPerItem));
        }

        [Test]
        public void TwoLinesOfOneItem_ThatOnlyOverflowTogether_AreRefused()
        {
            var manager = TestItems.Manager(3, _storage);

            var result = manager.CanAdd(new[]
            {
                new ItemAmount(TestItems.Potion, int.MaxValue),
                new ItemAmount(TestItems.Potion, int.MaxValue),
            });

            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void ARemoveOfIntMaxValue_IsRefused_AsNotEnoughItems()
        {
            var manager = TestItems.Manager(3, _storage);
            manager.TryAdd(TestItems.Of(TestItems.Potion, 5));

            var result = manager.TryRemove(new[]
            {
                new ItemAmount(TestItems.Potion, int.MaxValue),
                new ItemAmount(TestItems.Potion, int.MaxValue),
            });

            Assert.That(result.Reason, Is.EqualTo(InventoryFailureReason.NotEnoughItems));
            Assert.That(manager.GetCount(TestItems.Potion), Is.EqualTo(5));
        }

        // Synchronous fake storage never has a write in flight, so only a held save exercises
        // the save loop's "one writer, newest snapshot last" promise.
        [Test]
        public void MutationsDuringAnInFlightSave_AreWrittenOnce_AsTheNewestSnapshot()
        {
            _storage.HoldSaves = true;
            var manager = TestItems.Manager(5, _storage);
            var savesBefore = _storage.SaveCount;

            manager.TryAdd(TestItems.Of(TestItems.Sword, 1));
            manager.TryAdd(TestItems.Of(TestItems.Potion, 3));
            manager.TryAdd(TestItems.Of(TestItems.Arrows, 7));

            Assert.That(_storage.SaveCount, Is.EqualTo(savesBefore + 1), "Only one write in flight at a time.");
            Assert.That(_storage.HeldSaveCount, Is.EqualTo(1));

            _storage.ReleaseNextSave();

            Assert.That(_storage.SaveCount, Is.EqualTo(savesBefore + 2), "The two queued mutations coalesce into one write.");
            Assert.That(_storage.SaveRequests[_storage.SaveRequests.Count - 1], Is.SameAs(manager.Snapshot));

            _storage.ReleaseNextSave();

            Assert.That(_storage.SaveCount, Is.EqualTo(savesBefore + 2), "Nothing left to write.");
            Assert.That(_storage.HeldSaveCount, Is.Zero);
        }
    }
}
