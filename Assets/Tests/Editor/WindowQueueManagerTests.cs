using NUnit.Framework;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.UI.Enum;

namespace PopupSystem.Tests.EditMode
{
    /// <summary>
    /// Pure-logic coverage for WindowQueueManager: priority ordering, cooldown gating and the
    /// SetItems/MarkShown bookkeeping that drives them. No Unity objects or async involved, so
    /// these are plain synchronous NUnit [Test]s.
    /// </summary>
    [TestFixture]
    public sealed class WindowQueueManagerTests
    {
        private WindowQueueManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new WindowQueueManager();
        }

        [Test]
        public void GetItemsSortedByPriority_ReturnsEmpty_WhenNoItemsSet()
        {
            Assert.IsEmpty(_manager.GetItemsSortedByPriority());
        }

        [Test]
        public void SetItems_Null_ClearsItems()
        {
            _manager.SetItems(new[] { new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0) });
            Assert.IsNotEmpty(_manager.GetItemsSortedByPriority());

            _manager.SetItems(null);

            Assert.IsEmpty(_manager.GetItemsSortedByPriority());
        }

        [Test]
        public void GetItemsSortedByPriority_OrdersByPriorityDescending_ThenByWindowTypeAscending()
        {
            // DailyReward and Offer share priority 5 - WindowType (enum value) breaks the tie,
            // ascending, so DailyReward (4) must come before Offer (5).
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 0);
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0);
            var rewardPopup = new WindowQueueInfo(WindowType.RewardPopup, priority: 10, cooldownSeconds: 0);

            _manager.SetItems(new[] { offer, dailyReward, rewardPopup });

            var sorted = _manager.GetItemsSortedByPriority();

            Assert.AreEqual(3, sorted.Count);
            Assert.AreEqual(WindowType.RewardPopup, sorted[0].WindowType);
            Assert.AreEqual(WindowType.DailyReward, sorted[1].WindowType);
            Assert.AreEqual(WindowType.Offer, sorted[2].WindowType);
        }

        [Test]
        public void IsCooldownReady_ReturnsTrue_ForItemNeverShown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 9999);

            Assert.IsTrue(_manager.IsCooldownReady(item));
        }

        [Test]
        public void IsCooldownReady_ReturnsFalse_ImmediatelyAfterMarkShown_WithPositiveCooldown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);

            _manager.MarkShown(item);

            Assert.IsFalse(_manager.IsCooldownReady(item));
        }

        [Test]
        public void IsCooldownReady_ReturnsTrue_ImmediatelyAfterMarkShown_WithZeroCooldown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0);

            _manager.MarkShown(item);

            Assert.IsTrue(_manager.IsCooldownReady(item));
        }

        [Test]
        public void MarkShown_OnlyAffectsMatchingWindowType()
        {
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 60);

            _manager.MarkShown(dailyReward);

            Assert.IsFalse(_manager.IsCooldownReady(dailyReward));
            Assert.IsTrue(_manager.IsCooldownReady(offer));
        }

        [Test]
        public void SetItems_CalledAgain_ResetsCooldownState()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.SetItems(new[] { item });
            _manager.MarkShown(item);
            Assert.IsFalse(_manager.IsCooldownReady(item));

            // Re-registering the queue's item set (e.g. after a remote-config refresh) starts
            // cooldown tracking over, even for a WindowType that was already on cooldown. This
            // is the actual (if perhaps surprising) behavior of SetItems - it clears
            // _lastShownAt, not just _items - so it is worth pinning down explicitly.
            _manager.SetItems(new[] { item });

            Assert.IsTrue(_manager.IsCooldownReady(item));
        }

        [Test]
        public void GetItemsSortedByPriority_ExcludesTypesNotInLatestSetItemsCall()
        {
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0);
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0);
            _manager.SetItems(new[] { offer, dailyReward });

            _manager.SetItems(new[] { offer });

            var sorted = _manager.GetItemsSortedByPriority();

            Assert.AreEqual(1, sorted.Count);
            Assert.AreEqual(WindowType.Offer, sorted[0].WindowType);
        }
    }
}
