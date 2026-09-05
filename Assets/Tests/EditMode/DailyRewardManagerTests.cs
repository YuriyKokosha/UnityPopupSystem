using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Tests.EditMode.Fakes;

namespace PopupSystem.Tests.EditMode
{
    [TestFixture]
    public sealed class DailyRewardManagerTests
    {
        private FakeTimeProvider _time;
        private DailyRewardManager _manager;

        [SetUp]
        public void SetUp()
        {
            _time = new FakeTimeProvider();
            _manager = new DailyRewardManager(_time);
        }

        [Test]
        public void IsRewardAvailable_IsTrue_FromTheVeryFirstFrame()
        {
            Assert.That(_manager.IsRewardAvailable(), Is.True);
        }

        [Test]
        public async Task ClaimRewardAsync_MakesTheRewardUnavailable_AndSaysSo()
        {
            var availabilityChanged = 0;
            _manager.AvailabilityChanged += () => availabilityChanged++;

            var reward = await _manager.ClaimRewardAsync(CancellationToken.None);

            Assert.That(reward, Is.Not.Null);
            Assert.That(_manager.IsRewardAvailable(), Is.False);

            Assert.That(availabilityChanged, Is.EqualTo(1));
        }

        [Test]
        public async Task IsRewardAvailable_ComesBack_OnceTheIntervalHasElapsed()
        {
            await _manager.ClaimRewardAsync(CancellationToken.None);
            var nextAvailableAt = _manager.NextAvailableAtUtc;

            Assert.That(nextAvailableAt, Is.GreaterThan(_time.UtcNow));

            _time.UtcNow = nextAvailableAt;

            Assert.That(_manager.IsRewardAvailable(), Is.True);
        }

        [Test]
        public async Task ClaimRewardAsync_Cancelled_LeavesAvailabilityUntouched()
        {
            var availabilityChanged = 0;
            _manager.AvailabilityChanged += () => availabilityChanged++;

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            Exception caught = null;

            try
            {
                await _manager.ClaimRewardAsync(cancelled.Token);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());

            Assert.That(_manager.IsRewardAvailable(), Is.True);
            Assert.That(availabilityChanged, Is.Zero);
        }
    }
}
