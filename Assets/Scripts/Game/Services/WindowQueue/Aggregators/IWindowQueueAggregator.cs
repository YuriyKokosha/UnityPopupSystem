using System;
using PopupSystem.Contracts;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public interface IWindowQueueAggregator
    {
        WindowType WindowType { get; }

        /// <summary>Raise when the answer <see cref="IsAvailable"/> gives changed because something happened.
        /// Changes driven purely by time belong in <see cref="NextAvailabilityChangeUtc"/> instead.</summary>
        event Action AvailabilityChanged;

        /// <summary>When availability flips on its own, with no event behind it. Null when nothing is scheduled;
        /// the runner sleeps until the earliest of these.</summary>
        DateTime? NextAvailabilityChangeUtc { get; }

        bool IsAvailable();

        IWindowData CreatePayload();
    }
}
