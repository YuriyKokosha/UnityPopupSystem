using System;
using PopupSystem.Game.Services.Time;

namespace PopupSystem.Tests.EditMode.Fakes
{
    public sealed class FakeTimeProvider : ITimeProvider
    {
        public FakeTimeProvider()
            : this(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc))
        {
        }

        public FakeTimeProvider(DateTime startUtc)
        {
            UtcNow = startUtc;
        }

        public DateTime UtcNow { get; set; }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
        }

        public void AdvanceSeconds(double seconds)
        {
            Advance(TimeSpan.FromSeconds(seconds));
        }
    }
}
