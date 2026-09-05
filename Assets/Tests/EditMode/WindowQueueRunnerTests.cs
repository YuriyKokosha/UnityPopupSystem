using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class WindowQueueRunnerTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollStepMs = 50;

        private const int WrongBehaviourWindowMs = 200;

        private FakeTimeProvider _time;
        private WindowQueueManager _queueManager;
        private FakeWindowsManager _windowsManager;
        private FakeWindowQueueAggregator _dailyRewardAggregator;
        private FakeWindowQueueAggregator _offerAggregator;
        private WindowQueueRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTimeProvider();
            _queueManager = new WindowQueueManager(_time);
            _windowsManager = new FakeWindowsManager();
            _dailyRewardAggregator = new FakeWindowQueueAggregator(WindowType.DailyReward, _time, available: false);
            _offerAggregator = new FakeWindowQueueAggregator(WindowType.Offer, _time, available: false);

            _runner = new WindowQueueRunner(
                _queueManager,
                _windowsManager,
                _time,
                new List<IWindowQueueAggregator> { _dailyRewardAggregator, _offerAggregator });
        }

        [TearDown]
        public void TearDown()
        {
            _runner.Dispose();
        }

        private static async UniTask<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = PollTimeoutMs)
        {
            var elapsed = Stopwatch.StartNew();

            while (!condition())
            {
                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                {
                    return false;
                }

                await UniTask.Delay(PollStepMs);
            }

            return true;
        }

        [Test]
        public async Task OpensHighestPriorityAvailableWindow_AndCompletes_AfterItCloses()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));
            Assert.That(_windowsManager.OpenCalls[0].Type, Is.EqualTo(WindowType.DailyReward));
            Assert.That(_windowsManager.IsQueueIdle, Is.False);

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await runTask;

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(1),
                "Nothing else should reopen once nothing is left eligible this burst.");
        }

        [Test]
        public async Task DoesNothing_WhenQueueIsNotIdle()
        {
            _dailyRewardAggregator.Available = true;
            _windowsManager.IsQueueIdle = false;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Is.Empty);
        }

        [Test]
        public async Task SkipsAggregator_WhenNotAvailable()
        {
            _dailyRewardAggregator.Available = false;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Is.Empty);
        }

        [Test]
        public async Task SkipsItem_WhenNoAggregatorIsRegisteredForIt()
        {
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.RewardPopup, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Is.Empty);
        }

        [Test]
        public async Task ReentrancyGuard_ConcurrentCall_ReturnsImmediately_WhileFirstIsStillShowing()
        {
            _dailyRewardAggregator.Available = true;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;
        }

        [Test]
        public async Task NonInterruptibleWindow_StaysOpen_WhenHigherPriorityBecomesAvailable()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0, allowInterrupt: false),
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();
            var handle = _windowsManager.OpenCalls[0].Handle;

            _offerAggregator.Available = true;

            await UniTask.Delay(WrongBehaviourWindowMs);

            Assert.That(handle.IsClosed, Is.False, "A non-interruptible window must never be force-closed.");
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            _offerAggregator.Available = false;

            await handle.CloseAsync();
            await runTask;
        }

        [Test]
        public async Task InterruptibleWindow_IsForceClosed_ThenReconsidered_WhenHigherPriorityBecomesAvailable()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));
            Assert.That(_windowsManager.OpenCalls[0].Type, Is.EqualTo(WindowType.DailyReward));
            var dailyRewardHandle = _windowsManager.OpenCalls[0].Handle;

            _offerAggregator.Available = true;

            var wasClosed = await WaitUntilAsync(() => dailyRewardHandle.IsClosed);
            Assert.That(wasClosed, Is.True, "Runner should have force-closed the interruptible low-priority window.");
            Assert.That(dailyRewardHandle.State, Is.EqualTo(WindowLifecycleState.Disposed));

            Assert.That(
                _queueManager.IsCooldownReady(new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0)),
                Is.True);

            var offerOpened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 2);
            Assert.That(offerOpened, Is.True, "Offer should be opened right after DailyReward is interrupted.");
            Assert.That(_windowsManager.OpenCalls[1].Type, Is.EqualTo(WindowType.Offer));

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();

            var reopened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 3);
            Assert.That(reopened, Is.True, "DailyReward should be reconsidered once the interrupting window closes.");
            Assert.That(_windowsManager.OpenCalls[2].Type, Is.EqualTo(WindowType.DailyReward));
            Assert.That(_windowsManager.OpenCalls[2].Handle, Is.Not.SameAs(dailyRewardHandle));

            await _windowsManager.OpenCalls[2].Handle.CloseAsync();
            await runTask;
        }

        [Test]
        public async Task DoesNotReopenWindow_OnALaterBurst_WhileItsAvailabilityHasNotChanged()
        {
            _dailyRewardAggregator.Available = true;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
            });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(1),
                "A dismissed window must not reopen on a later burst while its availability has not changed.");
        }

        [Test]
        public async Task ReopensWindow_OnALaterBurst_AfterItsAvailabilityDroppedAndReturned()
        {
            _dailyRewardAggregator.Available = true;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
            });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;

            _dailyRewardAggregator.Available = false;
            await _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1), "Nothing to show while it is unavailable.");

            _dailyRewardAggregator.Available = true;
            var thirdRun = _runner.ShowAvailableWindowsAsync();

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(2),
                "Availability returning re-arms the window, so it is shown again.");

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            await thirdRun;
        }

        [Test]
        public async Task ReopensWindow_OnALaterBurst_AfterAPositiveCooldownElapses()
        {
            _offerAggregator.Available = true;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 20),
            });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;

            _time.AdvanceSeconds(19);
            await _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1), "Still on cooldown.");

            _time.AdvanceSeconds(1);
            var thirdRun = _runner.ShowAvailableWindowsAsync();

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(2),
                "The cooldown elapsing re-arms the window even though its availability never changed.");

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            await thirdRun;
        }

        [Test]
        public async Task InterruptibleWindow_IsNotForceClosed_WhileItHasAPopupOfItsOwnOpen()
        {
            _offerAggregator.Available = true;
            _dailyRewardAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 0),
                new WindowQueueInfo(WindowType.DailyReward, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls[0].Type, Is.EqualTo(WindowType.Offer));
            var offerHandle = _windowsManager.OpenCalls[0].Handle;

            _windowsManager.HasOpenPopups = true;
            _dailyRewardAggregator.Available = true;

            await UniTask.Delay(WrongBehaviourWindowMs);

            Assert.That(
                offerHandle.IsClosed,
                Is.False,
                "A window with a popup of its own open must not be force-closed - its flow would resume on a torn-down View.");
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            _windowsManager.HasOpenPopups = false;
            await offerHandle.CloseAsync();

            var dailyOpened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 2);
            Assert.That(dailyOpened, Is.True, "The deferred higher-priority window should open once the popup-owning window closes.");
            Assert.That(_windowsManager.OpenCalls[1].Type, Is.EqualTo(WindowType.DailyReward));

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            await runTask;
        }

        [Test]
        public async Task WakesAtTheScheduledMoment_WhenAvailabilityChangesWithNothingToRaiseAnEvent()
        {
            _offerAggregator.AvailableFromUtc = _time.UtcNow.AddMilliseconds(150);
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 0),
            });

            _runner.StartIdleMonitoring();

            await UniTask.Delay(WrongBehaviourWindowMs);
            Assert.That(_windowsManager.OpenCalls, Is.Empty, "The offer window has not opened yet.");

            _time.AdvanceSeconds(1);

            var opened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count == 1);
            Assert.That(
                opened,
                Is.True,
                "The runner must wake on its own at the scheduled moment, with no event behind it.");
            Assert.That(_windowsManager.OpenCalls[0].Type, Is.EqualTo(WindowType.Offer));

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
        }

        [Test]
        public async Task StillWakesPromptly_AfterAnInterruptCycleHasOverlappedTwoWaiters()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();
            var dailyRewardHandle = _windowsManager.OpenCalls[0].Handle;

            _offerAggregator.Available = true;
            Assert.That(await WaitUntilAsync(() => dailyRewardHandle.IsClosed), Is.True);
            Assert.That(await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 2), Is.True);

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            Assert.That(await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 3), Is.True);
            await _windowsManager.OpenCalls[2].Handle.CloseAsync();
            await runTask;

            _runner.StartIdleMonitoring();

            _dailyRewardAggregator.Available = false;
            await UniTask.Delay(WrongBehaviourWindowMs);
            _dailyRewardAggregator.Available = true;

            var reopened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 4);
            Assert.That(
                reopened,
                Is.True,
                "The wake signal must still reach the idle loop after an interrupt cycle - a stale " +
                "waiter must not be able to clear the live one's state.");
            Assert.That(_windowsManager.OpenCalls[3].Type, Is.EqualTo(WindowType.DailyReward));

            await _windowsManager.OpenCalls[3].Handle.CloseAsync();
        }

        [Test]
        public async Task WindowThatThrowsWhileOpening_IsSkippedForTheBurst_WithoutAdvancingItsBookkeeping()
        {
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Exception,
                new Regex("DailyReward could not open"));

            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = true;
            _windowsManager.OpenExceptionFor = WindowType.DailyReward;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 10, cooldownSeconds: 60),
                new WindowQueueInfo(WindowType.Offer, priority: 5, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));
            Assert.That(_windowsManager.OpenCalls[0].Type, Is.EqualTo(WindowType.Offer));

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await runTask;

            Assert.That(
                _queueManager.IsCooldownReady(new WindowQueueInfo(WindowType.DailyReward, priority: 10, cooldownSeconds: 60)),
                Is.True,
                "A window that threw was never shown, so its cooldown must not have started.");

            _windowsManager.OpenExceptionFor = null;
            var secondRun = _runner.ShowAvailableWindowsAsync();

            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(2));
            Assert.That(_windowsManager.OpenCalls[1].Type, Is.EqualTo(WindowType.DailyReward));

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            await secondRun;
        }

        [Test]
        public async Task WindowDroppedFromTheConfigAndBroughtBack_LosesItsAlreadyShownState()
        {
            _dailyRewardAggregator.Available = true;
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0);
            _queueManager.SetItems(new[] { item });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;

            await _runner.ShowAvailableWindowsAsync();
            Assert.That(_windowsManager.OpenCalls, Has.Count.EqualTo(1));

            _queueManager.SetItems(Array.Empty<WindowQueueInfo>());
            _queueManager.SetItems(new[] { item });

            var thirdRun = _runner.ShowAvailableWindowsAsync();

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(2),
                "A window the backend removed and re-added is a new decision, not a continuation.");

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();
            await thirdRun;
        }

        [Test]
        public async Task ConfigRefresh_KeepsSuppressing_AWindowThePlayerAlreadyDismissed()
        {
            _dailyRewardAggregator.Available = true;
            var item = new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0);
            _queueManager.SetItems(new[] { item });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;

            _queueManager.SetItems(new[] { item });

            await _runner.ShowAvailableWindowsAsync();

            Assert.That(
                _windowsManager.OpenCalls,
                Has.Count.EqualTo(1),
                "A config refresh must not undo a dismissal.");
        }

        [Test]
        public async Task ConfigArrivingWhileIdle_WakesTheRunner()
        {
            _dailyRewardAggregator.Available = true;
            _runner.StartIdleMonitoring();

            await UniTask.Delay(WrongBehaviourWindowMs);
            Assert.That(_windowsManager.OpenCalls, Is.Empty, "Nothing is in the queue config yet.");

            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
            });

            var opened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count == 1);
            Assert.That(opened, Is.True, "The runner must look again when the queue config changes.");

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
        }
    }
}
