using System;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Preloader;
using PopupSystem.UI.Runtime.Manager;
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
            catch (Exception ex)
            {
                // A failure showing the very first automatic popup must not prevent idle
                // monitoring from starting below - that would silently stop every future popup
                // for the rest of the session, not just this first one.
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
