using PopupSystem.UI.Enum;

namespace PopupSystem.UI.Core
{
    public readonly struct WindowRequest
    {
        public WindowType Type { get; }
        public IWindowData Payload { get; }

        public WindowRequest(WindowType type, IWindowData payload = null)
        {
            Type = type;
            Payload = payload;
        }
    }
}
