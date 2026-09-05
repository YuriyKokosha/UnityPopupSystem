using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Content;
using UnityEngine;
using Zenject;

namespace PopupSystem.App.Runtime
{
    public sealed class AppEntryPoint : IInitializable
    {
        private readonly AppStateManager _appStateManager;
        private readonly IUiPrefabProvider _prefabProvider;
        private bool _isStarted;

        public AppEntryPoint(AppStateManager appStateManager, IUiPrefabProvider prefabProvider)
        {
            _appStateManager = appStateManager;
            _prefabProvider = prefabProvider;
        }

        public void Initialize()
        {
            if (_isStarted)
            {
                return;
            }

            _isStarted = true;
            RunStartupAsync().Forget();
        }

        public async UniTask StartAsync()
        {
            await PreloadAlwaysNeededPrefabsAsync();
            await _appStateManager.RunAsync();
        }

        private async UniTaskVoid RunStartupAsync()
        {
            try
            {
                await StartAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    "Application startup did not complete - the app is in a partially initialised " +
                    "state and the screen you are looking at may never finish loading.");
                Debug.LogException(ex);
            }
        }

        private UniTask PreloadAlwaysNeededPrefabsAsync()
        {
            return _prefabProvider
                .LoadAsync(ModalBackdropPresenter.BackdropPrefabAddress, CancellationToken.None)
                .AsUniTask();
        }
    }
}
