using Cysharp.Threading.Tasks;

namespace PopupSystem.App.States
{
    public interface IAppState
    {
        UniTask EnterAsync();
        UniTask ExitAsync();
    }
}
