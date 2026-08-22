using Cysharp.Threading.Tasks;
using PopupSystem.App.States;

namespace PopupSystem.App.Runtime
{
    public sealed class AppStateManager
    {
        private readonly AppInitState _appInitState;
        private readonly AppConnectServerState _connectServerState;
        private readonly AppMainGameState _mainGameState;

        private IAppState _currentState;

        public AppStateManager(
            AppInitState appInitState,
            AppConnectServerState connectServerState,
            AppMainGameState mainGameState)
        {
            _appInitState = appInitState;
            _connectServerState = connectServerState;
            _mainGameState = mainGameState;
        }

        public async UniTask RunAsync()
        {
            await ChangeStateAsync(_appInitState);
            await ChangeStateAsync(_connectServerState);
            await ChangeStateAsync(_mainGameState);
        }

        private async UniTask ChangeStateAsync(IAppState nextState)
        {
            if (_currentState != null)
            {
                await _currentState.ExitAsync();
            }

            _currentState = nextState;
            await _currentState.EnterAsync();
        }
    }
}
