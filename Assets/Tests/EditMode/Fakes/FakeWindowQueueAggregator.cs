using System;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.WindowQueue.Aggregators;

namespace PopupSystem.Tests.EditMode.Fakes
{
    public sealed class FakeWindowQueueAggregator : IWindowQueueAggregator
    {
        private readonly ITimeProvider _timeProvider;
        private bool _available;
        private DateTime? _explicitNextAvailabilityChangeUtc;

        public FakeWindowQueueAggregator(
            WindowType windowType,
            ITimeProvider timeProvider = null,
            bool available = true,
            IWindowData payload = null)
        {
            WindowType = windowType;
            _timeProvider = timeProvider;
            _available = available;
            Payload = payload;
        }

        public event Action AvailabilityChanged;

        public WindowType WindowType { get; }

        public bool Available
        {
            get => _available;
            set
            {
                if (_available == value)
                {
                    return;
                }

                _available = value;
                AvailabilityChanged?.Invoke();
            }
        }

        public IWindowData Payload { get; set; }

        public DateTime? AvailableFromUtc { get; set; }

        public DateTime? NextAvailabilityChangeUtc
        {
            get
            {
                if (_explicitNextAvailabilityChangeUtc.HasValue)
                {
                    return _explicitNextAvailabilityChangeUtc;
                }

                if (!AvailableFromUtc.HasValue)
                {
                    return null;
                }

                return IsAvailable() ? null : AvailableFromUtc;
            }
            set => _explicitNextAvailabilityChangeUtc = value;
        }

        public bool IsAvailable()
        {
            if (!AvailableFromUtc.HasValue)
            {
                return _available;
            }

            if (_timeProvider == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(FakeWindowQueueAggregator)} for {WindowType} was given " +
                    $"{nameof(AvailableFromUtc)} but no clock. Schedule-shaped availability is " +
                    "decided by the clock - pass one to the constructor.");
            }

            return _timeProvider.UtcNow >= AvailableFromUtc.Value;
        }

        public IWindowData CreatePayload() => Payload;
    }
}
