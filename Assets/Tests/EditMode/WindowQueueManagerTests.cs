using System;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class WindowQueueManagerTests
    {
        private FakeTimeProvider _time;
        private WindowQueueManager _manager;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTimeProvider();
            _manager = new WindowQueueManager(_time);
        }

        [Test]
        public void GetItemsSortedByPriority_ReturnsEmpty_WhenNoItemsSet()
        {
            Assert.That(_manager.GetItemsSortedByPriority(), Is.Empty);
        }

        [Test]
        public void SetItems_Null_ClearsItems()
        {
            _manager.SetItems(new[] { new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0) });
            Assert.That(_manager.GetItemsSortedByPriority(), Is.Not.Empty);

            _manager.SetItems(null);

            Assert.That(_manager.GetItemsSortedByPriority(), Is.Empty);
        }

        [Test]
        public void GetItemsSortedByPriority_OrdersByPriorityDescending_ThenByWindowTypeAscending()
        {
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 0);
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0);
            var rewardPopup = new WindowQueueInfo(WindowType.RewardPopup, priority: 10, cooldownSeconds: 0);

            _manager.SetItems(new[] { offer, dailyReward, rewardPopup });

            var sorted = _manager.GetItemsSortedByPriority();

            Assert.That(sorted.Count, Is.EqualTo(3));
            Assert.That(sorted[0].WindowType, Is.EqualTo(WindowType.RewardPopup));
            Assert.That(sorted[1].WindowType, Is.EqualTo(WindowType.DailyReward));
            Assert.That(sorted[2].WindowType, Is.EqualTo(WindowType.Offer));
        }

        [Test]
        public void IsCooldownReady_ReturnsTrue_ForItemNeverShown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 9999);

            Assert.That(_manager.IsCooldownReady(item), Is.True);
        }

        [Test]
        public void IsCooldownReady_ReturnsFalse_ImmediatelyAfterMarkShown_WithPositiveCooldown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);

            _manager.MarkShown(item);

            Assert.That(_manager.IsCooldownReady(item), Is.False);
        }

        [Test]
        public void IsCooldownReady_ReturnsTrue_ImmediatelyAfterMarkShown_WithZeroCooldown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0);

            _manager.MarkShown(item);

            Assert.That(_manager.IsCooldownReady(item), Is.True);
        }

        [Test]
        public void IsCooldownReady_StaysFalse_UntilTheFullCooldownHasElapsed()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.MarkShown(item);

            _time.AdvanceSeconds(59);

            Assert.That(_manager.IsCooldownReady(item), Is.False);
        }

        [Test]
        public void IsCooldownReady_ReturnsTrue_AfterTheCooldownElapses()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.MarkShown(item);
            Assert.That(_manager.IsCooldownReady(item), Is.False);

            _time.AdvanceSeconds(60);

            Assert.That(_manager.IsCooldownReady(item), Is.True);
        }

        [Test]
        public void IsCooldownReady_RestartsTheCooldown_OnEveryMarkShown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.MarkShown(item);

            _time.AdvanceSeconds(59);
            _manager.MarkShown(item);
            _time.AdvanceSeconds(59);

            Assert.That(_manager.IsCooldownReady(item), Is.False);
        }

        [Test]
        public void MarkShown_OnlyAffectsMatchingWindowType()
        {
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 60);

            _manager.MarkShown(dailyReward);

            Assert.That(_manager.IsCooldownReady(dailyReward), Is.False);
            Assert.That(_manager.IsCooldownReady(offer), Is.True);
        }

        [Test]
        public void SetItems_CalledAgain_KeepsCooldownState_ForTypesStillInTheConfig()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.SetItems(new[] { item });
            _manager.MarkShown(item);
            Assert.That(_manager.IsCooldownReady(item), Is.False);

            _manager.SetItems(new[] { item });

            Assert.That(_manager.IsCooldownReady(item), Is.False);
        }

        [Test]
        public void SetItems_DropsCooldownState_ForTypesTheNewConfigNoLongerContains()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.SetItems(new[] { item });
            _manager.MarkShown(item);

            _manager.SetItems(Array.Empty<WindowQueueInfo>());
            _manager.SetItems(new[] { item });

            Assert.That(_manager.IsCooldownReady(item), Is.True);
        }

        [Test]
        public void SetItems_RaisesItemsChanged()
        {
            var raised = 0;
            _manager.ItemsChanged += () => raised++;

            _manager.SetItems(new[] { new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0) });
            _manager.SetItems(null);

            Assert.That(raised, Is.EqualTo(2));
        }

        [Test]
        public void GetItemsSortedByPriority_ReturnsTheSameInstance_UntilTheItemSetChanges()
        {
            _manager.SetItems(new[] { new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0) });

            var first = _manager.GetItemsSortedByPriority();
            var second = _manager.GetItemsSortedByPriority();

            Assert.That(second, Is.SameAs(first));

            _manager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            Assert.That(_manager.GetItemsSortedByPriority(), Is.Not.SameAs(first));
        }

        [Test]
        public void TimeUntilCooldownReady_IsNull_ForItemNeverShown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);

            Assert.That(_manager.TimeUntilCooldownReady(item), Is.Null);
        }

        [Test]
        public void TimeUntilCooldownReady_IsNull_ForZeroCooldown_EvenAfterMarkShown()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0);
            _manager.MarkShown(item);

            Assert.That(_manager.TimeUntilCooldownReady(item), Is.Null);
        }

        [Test]
        public void TimeUntilCooldownReady_ReportsTheRemainder_WhileTheCooldownIsRunning()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.MarkShown(item);

            _time.AdvanceSeconds(20);

            Assert.That(_manager.TimeUntilCooldownReady(item), Is.EqualTo(TimeSpan.FromSeconds(40)));
        }

        [Test]
        public void TimeUntilCooldownReady_IsNull_OnceTheCooldownHasElapsed()
        {
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 60);
            _manager.MarkShown(item);

            _time.AdvanceSeconds(60);

            Assert.That(_manager.TimeUntilCooldownReady(item), Is.Null);
            Assert.That(_manager.IsCooldownReady(item), Is.True);
        }

        [Test]
        public void GetItemsSortedByPriority_ExcludesTypesNotInLatestSetItemsCall()
        {
            var offer = new WindowQueueInfo(WindowType.Offer, priority: 1, cooldownSeconds: 0);
            var dailyReward = new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0);
            _manager.SetItems(new[] { offer, dailyReward });

            _manager.SetItems(new[] { offer });

            var sorted = _manager.GetItemsSortedByPriority();

            Assert.That(sorted.Count, Is.EqualTo(1));
            Assert.That(sorted[0].WindowType, Is.EqualTo(WindowType.Offer));
        }
    }
}
