using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Time;

namespace PopupSystem.Game.Services.DailyReward
{
    public sealed class DailyRewardManager
    {
        private static readonly TimeSpan RewardInterval = TimeSpan.FromMinutes(1);

        private static readonly RewardBundle DailyReward = new(
            new[]
            {
                new CurrencyAmount("gold", 500),
                new CurrencyAmount("gems", 10),
            },
            new[]
            {
                new ItemAmount(DemoItemIds.HealthPotion, 2),
            });

        private readonly ITimeProvider _timeProvider;
        private readonly RewardGrantService _grants;

        private DateTime _nextAvailableAtUtc = DateTime.MinValue;
        private bool _isClaimInFlight;

        public DailyRewardManager(ITimeProvider timeProvider, RewardGrantService grants)
        {
            _timeProvider = timeProvider;
            _grants = grants;
        }

        public event Action AvailabilityChanged;

        public DateTime NextAvailableAtUtc => _nextAvailableAtUtc;

        public RewardBundle Reward => DailyReward;

        public bool IsRewardAvailable()
        {
            return _timeProvider.UtcNow >= _nextAvailableAtUtc;
        }

        /// <summary>How long until the reward can be claimed again; zero while it is available.</summary>
        public TimeSpan TimeUntilAvailable
        {
            get
            {
                var remaining = _nextAvailableAtUtc - _timeProvider.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        /// <summary>False when the reward's items would not fit. Availability (the cooldown) is a separate
        /// question: a full inventory does not hide the window, it disables its button.</summary>
        public InventoryOperationResult CanClaimIntoInventory()
        {
            return _grants.CanGrant(DailyReward);
        }

        /// <summary>Grants the reward and starts the cooldown. Refuses (<see cref="InvalidOperationException"/>)
        /// while the reward is on cooldown or another claim is still in flight, so the service itself - not the
        /// window that happened to call it - guarantees one grant per interval.</summary>
        public async UniTask<RewardPopupData> ClaimRewardAsync(CancellationToken cancellationToken)
        {
            if (_isClaimInFlight)
            {
                throw new InvalidOperationException("A daily reward claim is already in progress.");
            }

            if (!IsRewardAvailable())
            {
                throw new InvalidOperationException("The daily reward is not available yet.");
            }

            var space = _grants.CanGrant(DailyReward);
            if (!space.Success)
            {
                throw new InventoryFullException(space);
            }

            _isClaimInFlight = true;
            try
            {
                await UniTask.Delay(500, cancellationToken: cancellationToken);

                // The cooldown starts together with the grant: Grant either refuses before anything moves (the
                // items stopped fitting during the delay), in which case the cooldown is rolled back and the
                // reward stays claimable, or it completes — observer failures cannot make it throw — and the
                // cooldown stands. Setting it first means inventory and wallet observers already see the reward
                // as claimed.
                var previousNextAvailableAtUtc = _nextAvailableAtUtc;
                _nextAvailableAtUtc = _timeProvider.UtcNow + RewardInterval;

                try
                {
                    _grants.Grant(DailyReward);
                }
                catch
                {
                    _nextAvailableAtUtc = previousNextAvailableAtUtc;
                    throw;
                }
            }
            finally
            {
                _isClaimInFlight = false;
            }

            StateChangeNotifier.InvokeIsolated(AvailabilityChanged);

            return new RewardPopupData("Daily Reward Claimed", DailyReward);
        }
    }
}
