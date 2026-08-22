using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;

namespace PopupSystem.Game.Services.WindowQueue.Aggregators
{
    public interface IWindowQueueAggregator
    {
        WindowType WindowType { get; }
        bool IsAvailable();
        IWindowData CreatePayload();
    }
}
