using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Domain.WindowQueue;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.Tests.EditMode.Fakes;
using PopupSystem.UI.Enum;
using UnityEngine.TestTools;

namespace PopupSystem.Tests.EditMode
{
    /// <summary>
    /// Behavioral coverage for WindowQueueRunner against fakes (<see cref="FakeWindowsManager"/> /
    /// <see cref="FakeWindowQueueAggregator"/>) - no scene, no Zenject container, no real window
    /// prefabs.
    ///
    /// Every test body is an `async UniTask` bridged into the Unity test runner via
    /// UniTask.ToCoroutine() (see UniTaskExtensions in the UniTask package): this drives the
    /// UniTask-based production code frame-by-frame under the (Edit Mode) player loop instead of
    /// blocking the test thread on the task, which would deadlock given WindowQueueRunner itself
    /// awaits things like UniTask.Delay internally. ToCoroutine() with no exceptionHandler
    /// re-throws any exception (including a failed NUnit Assert) out of MoveNext(), so assertion
    /// failures inside the async body correctly fail the test rather than being swallowed.
    ///
    /// A key property exploited throughout: awaiting an already-completed UniTask (as
    /// FakeWindowsManager.OpenAsync returns) continues synchronously, the same way any C# async
    /// method runs synchronously up to its first real suspension point. So immediately after
    /// calling (not awaiting) runner.ShowAvailableWindowsAsync(), any OpenAsync call it makes
    /// before hitting a real await (e.g. handle.WaitForCloseAsync() on a window nobody has closed
    /// yet) has already happened - letting the "happy path" tests assert on
    /// FakeWindowsManager.OpenCalls with no polling at all. Only genuinely time-driven behavior
    /// (the runner's 250ms interrupt poll) needs a bounded wait loop, via WaitUntilAsync below.
    /// </summary>
    [TestFixture]
    public sealed class WindowQueueRunnerTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollStepMs = 50;

        private WindowQueueManager _queueManager;
        private FakeWindowsManager _windowsManager;
        private FakeWindowQueueAggregator _dailyRewardAggregator;
        private FakeWindowQueueAggregator _offerAggregator;
        private WindowQueueRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _queueManager = new WindowQueueManager();
            _windowsManager = new FakeWindowsManager();
            _dailyRewardAggregator = new FakeWindowQueueAggregator(WindowType.DailyReward, available: false);
            _offerAggregator = new FakeWindowQueueAggregator(WindowType.Offer, available: false);

            _runner = new WindowQueueRunner(
                _queueManager,
                _windowsManager,
                new List<IWindowQueueAggregator> { _dailyRewardAggregator, _offerAggregator });
        }

        private static async UniTask<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = PollTimeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return false;
                }

                await UniTask.Delay(PollStepMs);
            }

            return true;
        }

        [UnityTest]
        public IEnumerator OpensHighestPriorityAvailableWindow_AndCompletes_AfterItCloses() =>
            OpensHighestPriorityAvailableWindow_AndCompletes_AfterItCloses_Async().ToCoroutine();

        private async UniTask OpensHighestPriorityAvailableWindow_AndCompletes_AfterItCloses_Async()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0),
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();

            // Offer outranks DailyReward but isn't available, so DailyReward is the one that
            // should have opened - and, per the class doc, it should already have happened
            // synchronously by the time this line runs.
            Assert.AreEqual(1, _windowsManager.OpenCalls.Count);
            Assert.AreEqual(WindowType.DailyReward, _windowsManager.OpenCalls[0].Type);
            Assert.IsFalse(_windowsManager.IsQueueIdle);

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await runTask;

            Assert.AreEqual(1, _windowsManager.OpenCalls.Count, "Nothing else should reopen once nothing is left eligible this burst.");
        }

        [UnityTest]
        public IEnumerator DoesNothing_WhenQueueIsNotIdle() =>
            DoesNothing_WhenQueueIsNotIdle_Async().ToCoroutine();

        private async UniTask DoesNothing_WhenQueueIsNotIdle_Async()
        {
            _dailyRewardAggregator.Available = true;
            _windowsManager.IsQueueIdle = false;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.AreEqual(0, _windowsManager.OpenCalls.Count);
        }

        [UnityTest]
        public IEnumerator SkipsAggregator_WhenNotAvailable() =>
            SkipsAggregator_WhenNotAvailable_Async().ToCoroutine();

        private async UniTask SkipsAggregator_WhenNotAvailable_Async()
        {
            _dailyRewardAggregator.Available = false;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.AreEqual(0, _windowsManager.OpenCalls.Count);
        }

        [UnityTest]
        public IEnumerator SkipsItem_WhenNoAggregatorIsRegisteredForIt() =>
            SkipsItem_WhenNoAggregatorIsRegisteredForIt_Async().ToCoroutine();

        private async UniTask SkipsItem_WhenNoAggregatorIsRegisteredForIt_Async()
        {
            // RewardPopup has no aggregator bound in SetUp - TryGetNextWindow must skip it rather
            // than throw or get stuck.
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.RewardPopup, priority: 1, cooldownSeconds: 0) });

            await _runner.ShowAvailableWindowsAsync();

            Assert.AreEqual(0, _windowsManager.OpenCalls.Count);
        }

        [UnityTest]
        public IEnumerator ReentrancyGuard_ConcurrentCall_ReturnsImmediately_WhileFirstIsStillShowing() =>
            ReentrancyGuard_ConcurrentCall_ReturnsImmediately_WhileFirstIsStillShowing_Async().ToCoroutine();

        private async UniTask ReentrancyGuard_ConcurrentCall_ReturnsImmediately_WhileFirstIsStillShowing_Async()
        {
            _dailyRewardAggregator.Available = true;
            _queueManager.SetItems(new[] { new WindowQueueInfo(WindowType.DailyReward, priority: 1, cooldownSeconds: 0) });

            var firstRun = _runner.ShowAvailableWindowsAsync();
            Assert.AreEqual(1, _windowsManager.OpenCalls.Count);

            // The first call's _isProcessing guard is still set (it's suspended waiting on the
            // open window) - a second concurrent call must be a same-frame no-op, not a second
            // OpenAsync.
            var secondRun = _runner.ShowAvailableWindowsAsync();
            await secondRun;

            Assert.AreEqual(1, _windowsManager.OpenCalls.Count);

            await _windowsManager.OpenCalls[0].Handle.CloseAsync();
            await firstRun;
        }

        [UnityTest]
        public IEnumerator NonInterruptibleWindow_StaysOpen_WhenHigherPriorityBecomesAvailable() =>
            NonInterruptibleWindow_StaysOpen_WhenHigherPriorityBecomesAvailable_Async().ToCoroutine();

        private async UniTask NonInterruptibleWindow_StaysOpen_WhenHigherPriorityBecomesAvailable_Async()
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

            // Give the runner well more than one interrupt-poll interval (250ms) to (wrongly)
            // force-close it, if it were ever going to.
            await UniTask.Delay(1000);

            Assert.IsFalse(handle.IsClosed, "A non-interruptible window must never be force-closed.");
            Assert.AreEqual(1, _windowsManager.OpenCalls.Count);

            // This test is only about interrupt-immunity, not about what happens next - revert
            // Offer to unavailable so the burst has nothing left to pick up once DailyReward
            // closes below and runTask can complete. Leaving it available would make the runner
            // (correctly) open Offer next, same as the dedicated interrupt/reconsideration test,
            // and this test would hang forever on "await runTask" since it never closes that
            // second handle.
            _offerAggregator.Available = false;

            await handle.CloseAsync();
            await runTask;
        }

        [UnityTest]
        public IEnumerator InterruptibleWindow_IsForceClosed_ThenReconsidered_WhenHigherPriorityBecomesAvailable() =>
            InterruptibleWindow_IsForceClosed_ThenReconsidered_WhenHigherPriorityBecomesAvailable_Async().ToCoroutine();

        private async UniTask InterruptibleWindow_IsForceClosed_ThenReconsidered_WhenHigherPriorityBecomesAvailable_Async()
        {
            _dailyRewardAggregator.Available = true;
            _offerAggregator.Available = false;
            _queueManager.SetItems(new[]
            {
                new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0), // AllowInterrupt defaults to true.
                new WindowQueueInfo(WindowType.Offer, priority: 10, cooldownSeconds: 0),
            });

            var runTask = _runner.ShowAvailableWindowsAsync();

            Assert.AreEqual(1, _windowsManager.OpenCalls.Count);
            Assert.AreEqual(WindowType.DailyReward, _windowsManager.OpenCalls[0].Type);
            var dailyRewardHandle = _windowsManager.OpenCalls[0].Handle;

            // The Offer becomes available while DailyReward is on screen. DailyReward opted into
            // interruption, so the runner's watcher should force-close it in favor of the
            // strictly-higher-priority Offer.
            _offerAggregator.Available = true;

            var wasClosed = await WaitUntilAsync(() => dailyRewardHandle.IsClosed);
            Assert.IsTrue(wasClosed, "Runner should have force-closed the interruptible low-priority window.");
            Assert.AreEqual(WindowLifecycleState.Disposed, dailyRewardHandle.State);

            // Interrupted, not "shown": cooldown bookkeeping must not have advanced for it.
            Assert.IsTrue(_queueManager.IsCooldownReady(new WindowQueueInfo(WindowType.DailyReward, priority: 5, cooldownSeconds: 0)));

            var offerOpened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 2);
            Assert.IsTrue(offerOpened, "Offer should be opened right after DailyReward is interrupted.");
            Assert.AreEqual(WindowType.Offer, _windowsManager.OpenCalls[1].Type);

            await _windowsManager.OpenCalls[1].Handle.CloseAsync();

            // DailyReward was never marked shown or burst-consumed, so it must be reconsidered -
            // as a brand new WindowHandle - right after the interrupting window closes.
            var reopened = await WaitUntilAsync(() => _windowsManager.OpenCalls.Count >= 3);
            Assert.IsTrue(reopened, "DailyReward should be reconsidered once the interrupting window closes.");
            Assert.AreEqual(WindowType.DailyReward, _windowsManager.OpenCalls[2].Type);
            Assert.AreNotSame(dailyRewardHandle, _windowsManager.OpenCalls[2].Handle);

            await _windowsManager.OpenCalls[2].Handle.CloseAsync();
            await runTask;
        }
    }
}
