using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.UI.Core;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Infrastructure;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Registry;
using UnityEngine;

namespace PopupSystem.UI.Runtime.Manager
{
    public sealed class WindowsManager : IWindowsManager, IDisposable
    {
        private readonly IWindowFactory _windowFactory;
        private readonly IWindowRegistry _registry;
        private readonly ModalBackdropPresenter _backdropPresenter;
        private readonly Stack<WindowInstance> _windowStack = new();
        private readonly Stack<WindowInstance> _popupStack = new();

        private readonly HashSet<WindowHandle> _closingHandles = new();

        private readonly Stack<WindowInstance> _extractBuffer = new();
        private readonly List<WindowInstance> _activeInstances = new();

        private WindowInstance _baseWindow;
        private UniTaskCompletionSource<WindowHandle> _baseScreenOpen;

        private int _pendingOpens;
        private bool _isDisposed;

        public bool IsQueueIdle =>
            _pendingOpens == 0 && _popupStack.Count == 0 && _windowStack.Count == 0 && _closingHandles.Count == 0;

        public event Action QueueBecameIdle;

        public bool HasOpenPopups => _popupStack.Count > 0;

        public WindowsManager(
            IWindowFactory windowFactory,
            IWindowRegistry registry,
            IUiPrefabProvider prefabProvider)
        {
            _windowFactory = windowFactory;
            _registry = registry;
            _backdropPresenter = new ModalBackdropPresenter(CloseInstanceByHandleAsync, prefabProvider);
        }

        public async UniTask<WindowHandle> OpenAsync(WindowType type, IWindowData payload = null)
        {
            ThrowIfDisposed();

            var definition = _registry.Get(type);

            if (!definition.IsBaseScreen)
            {
                return await OpenNewInstanceAsync(type, payload);
            }

            // Checked before _baseWindow: that slot is filled before the open reaches Active, and a concurrent
            // caller must get the same "returns once Active" result as the first one.
            var inFlight = _baseScreenOpen;
            if (inFlight != null)
            {
                return await inFlight.Task;
            }

            if (_baseWindow != null && !_baseWindow.Handle.IsClosed)
            {
                return _baseWindow.Handle;
            }

            // Every caller, the first included, awaits the shared source, so a failure is always observed.
            var source = new UniTaskCompletionSource<WindowHandle>();
            _baseScreenOpen = source;
            RunBaseScreenOpenAsync(source, type, payload).Forget();
            return await source.Task;
        }

        private async UniTaskVoid RunBaseScreenOpenAsync(
            UniTaskCompletionSource<WindowHandle> source,
            WindowType type,
            IWindowData payload)
        {
            WindowHandle handle;
            try
            {
                handle = await OpenNewInstanceAsync(type, payload);
            }
            catch (Exception ex)
            {
                ClearBaseScreenOpen(source);
                source.TrySetException(ex);
                return;
            }

            // Cleared before completing: a resumed waiter that opens again must see the base slot, not this.
            ClearBaseScreenOpen(source);
            source.TrySetResult(handle);
        }

        private void ClearBaseScreenOpen(UniTaskCompletionSource<WindowHandle> source)
        {
            if (_baseScreenOpen == source)
            {
                _baseScreenOpen = null;
            }
        }

        private async UniTask<WindowHandle> OpenNewInstanceAsync(WindowType type, IWindowData payload)
        {
            WindowInstance instance;

            // Busy from here, not from the push below: the prefab load is asynchronous, and the queue must never
            // read "idle" while a window is already on its way onto the screen.
            _pendingOpens++;
            try
            {
                instance = await _windowFactory.CreateAsync(
                    new WindowRequest(type, payload), CancellationToken.None);

                // The load is not cancelled on Dispose (it may be shared with other callers of the provider), so
                // a window that finishes loading after Dispose is released here instead of being opened.
                if (_isDisposed)
                {
                    DiscardUnopenedInstance(instance);
                    ThrowIfDisposed();
                }

                instance.Handle = new WindowHandle(type, () => CloseInstanceByHandleAsync(instance.Handle));
                instance.CloseRequestedHandler = () => OnCloseRequested(instance);
                BindClose(instance);

                if (instance.Definition.Kind == UIEntryKind.Popup)
                {
                    _popupStack.Push(instance);
                }
                else if (instance.Definition.IsBaseScreen)
                {
                    _baseWindow = instance;
                }
                else
                {
                    _windowStack.Push(instance);
                }
            }
            catch
            {
                _pendingOpens--;

                if (!_isDisposed && IsQueueIdle)
                {
                    QueueBecameIdle?.Invoke();
                }

                throw;
            }

            _pendingOpens--;

            RefreshBackdrops();
            await OpenInstanceAsync(instance, payload);
            return instance.Handle;
        }

        public void Dispose()
        {
            _isDisposed = true;
            _closingHandles.Clear();

            while (_popupStack.Count > 0)
            {
                DisposeInstance(_popupStack.Pop());
            }

            while (_windowStack.Count > 0)
            {
                DisposeInstance(_windowStack.Pop());
            }

            if (_baseWindow != null)
            {
                DisposeInstance(_baseWindow);
                _baseWindow = null;
            }
        }

        // Cancellation, not ObjectDisposedException: the runner ends on it and the UI callers drop it without logging.
        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new OperationCanceledException("WindowsManager has been disposed.");
            }
        }

        private void DiscardUnopenedInstance(WindowInstance instance)
        {
            CancelLifetime(instance);
            ReleaseInstance(instance);
        }

        // Controller and view code runs here (token callbacks); a throw must not skip the rest of the teardown.
        private static void CancelLifetime(WindowInstance instance)
        {
            try
            {
                instance.LifetimeCts.Cancel();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        // Each step guarded on its own: a throwing controller Dispose must still release the view to the pool.
        private void ReleaseInstance(WindowInstance instance)
        {
            try
            {
                instance.Controller.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            try
            {
                _windowFactory.Release(instance);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            instance.LifetimeCts.Dispose();
        }

        private void DisposeInstance(WindowInstance instance)
        {
            if (instance == null || instance.Handle == null || instance.Handle.IsClosed)
            {
                return;
            }

            instance.IsClosing = true;

            if (instance.View != null)
            {
                instance.View.CloseRequested -= instance.CloseRequestedHandler;
            }

            CancelLifetime(instance);
            instance.Handle.SetState(WindowLifecycleState.Closing);
            ReleaseInstance(instance);
            instance.Handle.MarkClosed();
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

            if (_closingHandles.Contains(handle))
            {
                return handle.WaitForCloseAsync();
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

        private WindowInstance TryExtractInstance(Stack<WindowInstance> stack, WindowHandle handle)
        {
            if (stack.Count == 0)
            {
                return null;
            }

            _extractBuffer.Clear();
            WindowInstance target = null;

            while (stack.Count > 0)
            {
                var instance = stack.Pop();
                if (target == null && instance.Handle == handle)
                {
                    target = instance;
                    break;
                }

                _extractBuffer.Push(instance);
            }

            while (_extractBuffer.Count > 0)
            {
                stack.Push(_extractBuffer.Pop());
            }

            return target;
        }

        private async UniTask CloseInstanceAsync(WindowInstance instance)
        {
            if (instance == null || instance.IsClosing || instance.Handle.IsClosed)
            {
                return;
            }

            instance.IsClosing = true;
            _closingHandles.Add(instance.Handle);
            instance.View.CloseRequested -= instance.CloseRequestedHandler;
            CancelLifetime(instance);
            instance.Handle.SetState(WindowLifecycleState.Closing);

            try
            {
                // Non-cancellable: Closing must always reach Disposed, even when the lifetime token is already cancelled.
                await instance.View.PlayCloseAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            ReleaseInstance(instance);

            // Stop counting the window as busy before completing the handle: MarkClosed can resume a waiter
            // synchronously, and it must not observe a stale "not idle".
            _closingHandles.Remove(instance.Handle);
            instance.Handle.MarkClosed();

            try
            {
                if (IsQueueIdle)
                {
                    QueueBecameIdle?.Invoke();
                }
            }
            finally
            {
                RefreshBackdrops();
            }
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

                // Closed while initializing, by a controller that ignored its token: the view is already back in
                // the pool, so it must not be shown. SetState would ignore the backward move anyway.
                if (instance.IsClosing)
                {
                    throw new OperationCanceledException();
                }

                instance.Handle.SetState(WindowLifecycleState.Opening);

                instance.View.Show();
                // Activate, then order, then animate: Canvas.overrideSorting is ignored while the object is inactive.
                UILayerSorter.Apply(instance.View.transform.parent);

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

        private void RefreshBackdrops()
        {
            CollectActiveInstances(_activeInstances);

            _backdropPresenter.Refresh(_activeInstances);

            ReapplyLayerSorting(_activeInstances);
        }

        private static void ReapplyLayerSorting(List<WindowInstance> instances)
        {
            for (var i = 0; i < instances.Count; i++)
            {
                var view = instances[i].View;
                if (view == null)
                {
                    continue;
                }

                UILayerSorter.Apply(view.transform.parent);
            }
        }

        private void CollectActiveInstances(List<WindowInstance> into)
        {
            into.Clear();

            if (_baseWindow != null)
            {
                into.Add(_baseWindow);
            }

            foreach (var instance in _windowStack)
            {
                into.Add(instance);
            }

            foreach (var instance in _popupStack)
            {
                into.Add(instance);
            }
        }
    }
}
