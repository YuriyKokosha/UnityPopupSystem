using System;

namespace PopupSystem.Game.Services.Time
{
    /// <summary>The only clock the game layer may read. Nothing in Game touches DateTime.UtcNow: cooldowns
    /// are a monetisation surface and a device clock is player-controlled.</summary>
    public interface ITimeProvider
    {
        DateTime UtcNow { get; }
    }
}
