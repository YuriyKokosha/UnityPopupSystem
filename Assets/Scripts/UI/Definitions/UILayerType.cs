namespace PopupSystem.UI.Definitions
{
    /// <summary>Which UIRoot layer a window parents under. One Canvas + GraphicRaycaster per layer.</summary>
    public enum UILayerType
    {
        Windows = 0,
        Popups = 1,
        Notifications = 2,
        System = 3,
    }
}
