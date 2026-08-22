using Cysharp.Threading.Tasks;
using PopupSystem.UI.Preloader;

namespace PopupSystem.App.States
{
    public sealed class AppInitState : IAppState
    {
        private readonly PreloaderOverlayView _preloaderOverlay;

        public AppInitState(PreloaderOverlayView preloaderOverlay)
        {
            _preloaderOverlay = preloaderOverlay;
        }

        public UniTask EnterAsync()
        {
            _preloaderOverlay.Show("Initializing application...");
            _preloaderOverlay.SetProgress(0.15f);
            return UniTask.CompletedTask;
        }

        public UniTask ExitAsync()
        {
            return UniTask.CompletedTask;
        }
    }
}
