namespace PopupSystem.UI.Core
{
    /// <summary>
    /// Marker for every type used as a window's payload - either passive data known up front
    /// (conventionally named XxxWindowData) or an in-flight operation/callback the window itself
    /// observes (conventionally named XxxWindowRequest, e.g. RewardPopupRequest). Implementing
    /// this is what lets a type travel through IWindowsManager.OpenAsync and WindowController's
    /// generic TData slot - it exists so a new window's payload is always a small, purpose-built
    /// type declared next to that window (its View/Controller/Module), never an unrelated
    /// domain/service type reused just because it happens to satisfy the compiler. See
    /// <see cref="NoWindowData"/> for windows that take no payload at all.
    /// </summary>
    public interface IWindowData
    {
    }
}
