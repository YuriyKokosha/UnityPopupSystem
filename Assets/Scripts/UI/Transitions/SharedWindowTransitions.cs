namespace PopupSystem.UI.Transitions
{
    /// <summary>
    /// Ready-made transition instances that window modules can reference directly instead of each
    /// constructing its own. FadeScaleWindowTransition is stateless, so sharing one instance across
    /// every popup that wants this look is safe and avoids one allocation per window type.
    /// </summary>
    public static class SharedWindowTransitions
    {
        public static readonly IWindowTransition PopupDefault = new FadeScaleWindowTransition();
    }
}
