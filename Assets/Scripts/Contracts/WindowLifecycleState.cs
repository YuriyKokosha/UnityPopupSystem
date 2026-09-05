namespace PopupSystem.Contracts
{
    /// <summary>Initializing -> Opening -> Active -> Closing -> Disposed.</summary>
    public enum WindowLifecycleState
    {
        None = 0,
        Initializing = 1,
        Opening = 2,
        Active = 3,
        Closing = 4,
        Disposed = 5,
    }
}
