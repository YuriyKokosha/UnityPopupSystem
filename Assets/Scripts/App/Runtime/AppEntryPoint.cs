using Cysharp.Threading.Tasks;
using Zenject;

namespace PopupSystem.App.Runtime
{
    public sealed class AppEntryPoint : IInitializable
    {
        private readonly AppStateManager _appStateManager;
        private bool _isStarted;

        public AppEntryPoint(AppStateManager appStateManager)
        {
            _appStateManager = appStateManager;
        }

        public async void Initialize()
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
            await StartAsync();
        }

        public UniTask StartAsync()
        {
            return _appStateManager.RunAsync();
        }
    }
}
