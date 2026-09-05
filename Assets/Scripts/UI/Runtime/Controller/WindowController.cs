using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;

namespace PopupSystem.UI.Runtime.Controller
{
    public abstract class WindowController<TData, TView> : IWindowController
        where TData : IWindowData
        where TView : WindowView
    {
        protected TView View { get; private set; }
        protected WindowHandle Handle { get; private set; }

        public async UniTask InitializeAsync(WindowView view, WindowHandle handle, IWindowData payload, CancellationToken cancellationToken)
        {
            View = view as TView ?? throw new InvalidOperationException(
                $"Expected view of type {typeof(TView).Name}, got {view.GetType().Name}");
            Handle = handle;

            await OnInitializeAsync(ConvertPayload(payload), cancellationToken);
        }

        public virtual void Dispose()
        {
            View = null;
            Handle = null;
        }

        protected abstract UniTask OnInitializeAsync(TData data, CancellationToken cancellationToken);

        protected virtual TData ConvertPayload(IWindowData payload)
        {
            if (payload == null)
            {
                return default;
            }

            if (payload is TData data)
            {
                return data;
            }

            throw new InvalidOperationException(
                $"Expected payload of type {typeof(TData).Name}, got {payload.GetType().Name}");
        }
    }
}
