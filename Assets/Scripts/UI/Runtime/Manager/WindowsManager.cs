using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Core;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Factory;

namespace PopupSystem.UI.Runtime.Manager
{
    public sealed class WindowsManager : IWindowsManager
    {
        private readonly IWindowFactory _windowFactory;
        private readonly ModalBackdropPresenter _backdropPresenter;
        private readonly Stack<WindowInstance> _windowStack = new();
        private readonly Stack<WindowInstance> _popupStack = new();
        private WindowInstance _baseWindow;

        public bool IsQueueIdle => _popupStack.Count == 0 && _windowStack.Count == 0;

        public WindowsManager(IWindowFactory windowFactory)
        {
            _windowFactory = windowFactory;
            _backdropPresenter = new ModalBackdropPresenter(CloseInstanceByHandleAsync);
        }

        public async UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null)
        {
            if (type == WindowType.MainGame && _baseWindow != null && !_baseWindow.Handle.IsClosed)
            {
                return _baseWindow.Handle;
            }

            var instance = _windowFactory.Create(new WindowRequest(type, payload));
            instance.Handle = new WindowHandle(type, () => CloseInstanceByHandleAsync(instance.Handle));
            instance.CloseRequestedHandler = () => OnCloseRequested(instance);
            BindClose(instance);

            if (instance.Definition.Kind == UIEntryKind.Popup)
            {
                _popupStack.Push(instance);
                RefreshBackdrops();
                await OpenInstanceAsync(instance, payload);
                return instance.Handle;
            }

            if (type == WindowType.MainGame && _baseWindow == null)
            {
                _baseWindow = instance;
                RefreshBackdrops();
                await OpenInstanceAsync(instance, payload);
                return instance.Handle;
            }

            _windowStack.Push(instance);
            RefreshBackdrops();
            await OpenInstanceAsync(instance, payload);
            return instance.Handle;
        }

        public UniTask CloseCurrentWindowAsync()
        {
            return CloseCurrentWindowInternalAsync();
        }

        public UniTask CloseTopPopupAsync()
        {
            if (_popupStack.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            var instance = _popupStack.Pop();
            return CloseInstanceAsync(instance);
        }

        private void BindClose(WindowInstance instance)
        {
            instance.View.CloseRequested += instance.CloseRequestedHandler;
        }

        private void OnCloseRequested(WindowInstance instance)
        {
            if (instance.Definition.Kind == UIEntryKind.Popup)
            {
                CloseInstanceByHandleAsync(instance.Handle).Forget();
                return;
            }

            if (_popupStack.Count > 0)
            {
                CloseTopPopupAsync().Forget();
                return;
            }

            CloseInstanceByHandleAsync(instance.Handle).Forget();
        }

        private UniTask CloseCurrentWindowInternalAsync()
        {
            if (_windowStack.Count == 0)
            {
                return UniTask.CompletedTask;
            }

            var instance = _windowStack.Pop();
            return CloseInstanceAsync(instance);
        }

        private UniTask CloseInstanceByHandleAsync(WindowHandle handle)
        {
            if (handle == null || handle.IsClosed)
            {
                return UniTask.CompletedTask;
            }

            var popupInstance = TryExtractInstance(_popupStack, handle);
            if (popupInstance != null)
            {
                return CloseInstanceAsync(popupInstance);
            }

            if (_baseWindow != null && _baseWindow.Handle == handle)
            {
                var baseWindow = _baseWindow;
                _baseWindow = null;
                return CloseInstanceAsync(baseWindow);
            }

            var windowInstance = TryExtractInstance(_windowStack, handle);
            if (windowInstance != null)
            {
                return CloseInstanceAsync(windowInstance);
            }

            return UniTask.CompletedTask;
        }

        private static WindowInstance TryExtractInstance(Stack<WindowInstance> stack, WindowHandle handle)
        {
            if (stack.Count == 0)
            {
                return null;
            }

            var buffer = new Stack<WindowInstance>();
            WindowInstance target = null;

            while (stack.Count > 0)
            {
                var instance = stack.Pop();
                if (target == null && instance.Handle == handle)
                {
                    target = instance;
                    break;
                }

                buffer.Push(instance);
            }

            while (buffer.Count > 0)
            {
                stack.Push(buffer.Pop());
            }

            return target;
        }

        private async UniTask CloseInstanceAsync(WindowInstance instance)
        {
            if (instance == null || instance.IsClosing || instance.Handle.IsClosed)
            {
                return;
            }

            // Guard re-entrancy immediately: nothing below may yield before this is set, or a
            // second close request arriving while we're already closing could run this twice.
            instance.IsClosing = true;
            instance.View.CloseRequested -= instance.CloseRequestedHandler;
            instance.LifetimeCts.Cancel();
            instance.Handle.SetState(WindowLifecycleState.Closing);

            try
            {
                // Closing must always run to completion and reach Disposal, even if the instance
                // was cancelled mid-open - so it uses its own token, not the (already cancelled)
                // lifetime token.
                await instance.View.PlayCloseAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                // A misbehaving transition must not prevent the instance from being disposed.
            }

            instance.Controller.Dispose();

            // Pool the view instead of destroying it - Controller.Dispose() above already
            // unsubscribed every controller-level handler this instance added, so the next Open
            // of this WindowType starts from a clean slate once its own (fresh) controller
            // subscribes again in its own Init.
            _windowFactory.Release(instance);
            instance.LifetimeCts.Dispose();
            instance.Handle.MarkClosed();

            // The caller already removed this instance from whichever stack held it (or cleared
            // _baseWindow) before invoking this method, so recomputing now correctly reflects
            // "this window is gone" - the backdrop behind it disappears together with it, once
            // its own close transition has actually finished.
            RefreshBackdrops();
        }

        private async UniTask OpenInstanceAsync(WindowInstance instance, IWindowData payload)
        {
            try
            {
                instance.Handle.SetState(WindowLifecycleState.Initializing);
                await instance.Controller.InitializeAsync(
                    instance.View,
                    instance.Handle,
                    payload,
                    instance.LifetimeCts.Token);

                instance.Handle.SetState(WindowLifecycleState.Opening);
                await instance.View.PlayOpenAsync(instance.LifetimeCts.Token);

                instance.Handle.SetState(WindowLifecycleState.Active);
            }
            catch (OperationCanceledException)
            {
                await AbortOpeningInstanceAsync(instance);
            }
            catch
            {
                await AbortOpeningInstanceAsync(instance);
                throw;
            }
        }

        /// <summary>
        /// Cancellation/failure while opening can now happen for real (e.g. a real transition
        /// gets interrupted mid-animation), unlike when Open was always instant. The instance was
        /// already pushed onto its stack (or set as _baseWindow) before OpenInstanceAsync started,
        /// so - unlike an explicit CloseXAsync call - nobody has removed it from tracking yet.
        /// Doing that here first is what keeps IsQueueIdle (and RefreshBackdrops) correct; without
        /// it, a closed-but-still-in-the-stack instance would wedge the idle queue permanently.
        /// </summary>
        private UniTask AbortOpeningInstanceAsync(WindowInstance instance)
        {
            if (_baseWindow == instance)
            {
                _baseWindow = null;
            }
            else
            {
                TryExtractInstance(_windowStack, instance.Handle);
                TryExtractInstance(_popupStack, instance.Handle);
            }

            return CloseInstanceAsync(instance);
        }

        // --- Modality / backdrop -------------------------------------------------------------
        //
        // The actual compositing (which layer gets a dimming/input-blocking scrim, where it sits,
        // whether tapping it closes anything) lives in ModalBackdropPresenter - this class only
        // tells it what's currently active. See that class for why the split.

        private void RefreshBackdrops()
        {
            _backdropPresenter.Refresh(GetActiveInstances());
        }

        private IEnumerable<WindowInstance> GetActiveInstances()
        {
            if (_baseWindow != null)
            {
                yield return _baseWindow;
            }

            foreach (var instance in _windowStack)
            {
                yield return instance;
            }

            foreach (var instance in _popupStack)
            {
                yield return instance;
            }
        }
    }
}
