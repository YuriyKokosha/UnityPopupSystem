using System;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.UI.Preloader;
using UnityEngine;

namespace PopupSystem.App.States
{
    public sealed class AppMainGameState : IAppState
    {
        private readonly PreloaderOverlayView _preloaderOverlay;
        private readonly IWindowsManager _windowsManager;
        private readonly WindowQueueRunner _windowQueueRunner;

        public AppMainGameState(
            PreloaderOverlayView preloaderOverlay,
            IWindowsManager windowsManager,
            WindowQueueRunner windowQueueRunner)
        {
            _preloaderOverlay = preloaderOverlay;
            _windowsManager = windowsManager;
            _windowQueueRunner = windowQueueRunner;
        }

        public async UniTask EnterAsync()
        {
            _preloaderOverlay.SetProgress(1f);
            _preloaderOverlay.Hide();
            await _windowsManager.OpenAsync(WindowType.MainGame);

            try
            {
                await _windowQueueRunner.ShowAvailableWindowsAsync();
            }
            catch (OperationCanceledException)
            {
                // The window engine was disposed under the startup burst (scene unload, quit). Not an error, and
                // there is nothing left to monitor: let the cancellation end startup quietly (AppEntryPoint).
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            _windowQueueRunner.StartIdleMonitoring();
        }

        public UniTask ExitAsync()
        {
            _windowQueueRunner.StopIdleMonitoring();
            return UniTask.CompletedTask;
        }
    }
}
