namespace PopupSystem.UI.Core
{
    /// <summary>
    /// Shared payload marker for windows that need no input at all (MainGame, Settings,
    /// DailyReward, Offer today). Use this instead of declaring a one-off empty struct per
    /// window, or - worse - reusing an unrelated domain/service type as a stand-in: this way
    /// "this window intentionally takes nothing" is a single, greppable type instead of several
    /// incidental ones.
    /// </summary>
    public readonly struct EmptyWindowData : IWindowData
    {
    }
}
