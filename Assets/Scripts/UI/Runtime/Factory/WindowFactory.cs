using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using UnityEngine;
using PopupSystem.UI.Core;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Infrastructure;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Registry;
using Zenject;

namespace PopupSystem.UI.Runtime.Factory
{
    public sealed class WindowFactory : IWindowFactory, System.IDisposable
    {
        private const int MaxPooledViewsPerType = 2;

        private readonly IWindowRegistry _registry;
        private readonly IWindowControllerResolver _controllerResolver;
        private readonly IUILayerProvider _layerProvider;
        private readonly IUiPrefabProvider _prefabProvider;
        private readonly PrefabFactory<WindowView> _viewFactory;
        private readonly Dictionary<WindowType, Stack<WindowView>> _pooledViews = new();

        private bool _isDisposed;

        public WindowFactory(
            IWindowRegistry registry,
            IWindowControllerResolver controllerResolver,
            IUILayerProvider layerProvider,
            IUiPrefabProvider prefabProvider,
            PrefabFactory<WindowView> viewFactory)
        {
            _registry = registry;
            _controllerResolver = controllerResolver;
            _layerProvider = layerProvider;
            _prefabProvider = prefabProvider;
            _viewFactory = viewFactory;
        }

        public async UniTask<WindowInstance> CreateAsync(WindowRequest request, CancellationToken cancellationToken)
        {
            var definition = _registry.Get(request.Type);
            var parent = _layerProvider.GetLayer(definition.Layer);
            var view = await AcquireViewAsync(definition, request.Type, parent, cancellationToken);
            var controller = _controllerResolver.Create(definition.Type);
            var lifetimeCts = new CancellationTokenSource();

            view.SetTransition(definition.Transition);

            view.Hide();

            return new WindowInstance(request.Type, definition, view, controller, lifetimeCts);
        }

        public void Release(WindowInstance instance)
        {
            var view = instance.View;
            if (view == null)
            {
                return;
            }

            view.ResetForPool();
            view.Hide();

            if (_isDisposed)
            {
                UnityEngine.Object.Destroy(view.gameObject);
                return;
            }

            if (!_pooledViews.TryGetValue(instance.Type, out var stack))
            {
                stack = new Stack<WindowView>();
                _pooledViews[instance.Type] = stack;
            }

            if (stack.Count >= MaxPooledViewsPerType)
            {
                UnityEngine.Object.Destroy(view.gameObject);
                return;
            }

            stack.Push(view);
        }

        public void Dispose()
        {
            _isDisposed = true;

            foreach (var stack in _pooledViews.Values)
            {
                while (stack.Count > 0)
                {
                    var view = stack.Pop();
                    if (view != null)
                    {
                        UnityEngine.Object.Destroy(view.gameObject);
                    }
                }
            }

            _pooledViews.Clear();
        }

        private async UniTask<WindowView> AcquireViewAsync(
            WindowDefinition definition,
            WindowType type,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (_pooledViews.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                var pooledView = stack.Pop();
                pooledView.transform.SetParent(parent, false);
                // A pooled view keeps its previous sibling index; stacking and backdrop placement read sibling order.
                pooledView.transform.SetAsLastSibling();
                return pooledView;
            }

            return await CreateViewAsync(definition, type, parent, cancellationToken);
        }

        private async UniTask<WindowView> CreateViewAsync(
            WindowDefinition definition,
            WindowType type,
            Transform parent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(definition.PrefabAddress))
            {
                throw new System.InvalidOperationException(
                    $"WindowFactory: {type} has no PrefabAddress configured. Every window must " +
                    "reference a content prefab (see its WindowModule) - there is no code-built fallback.");
            }

            var prefab = await _prefabProvider.LoadAsync(definition.PrefabAddress, cancellationToken);

            var prefabView = _viewFactory.Create(prefab);
            AssertViewMatchesDefinition(definition, type, prefabView);

            var prefabRect = prefabView.GetComponent<RectTransform>();
            prefabRect.SetParent(parent, false);
            prefabRect.anchorMin = Vector2.zero;
            prefabRect.anchorMax = Vector2.one;
            prefabRect.offsetMin = Vector2.zero;
            prefabRect.offsetMax = Vector2.zero;
            return prefabView;
        }

        private static void AssertViewMatchesDefinition(
            WindowDefinition definition,
            WindowType type,
            WindowView view)
        {
            if (definition.ViewType == null || definition.ViewType.IsInstanceOfType(view))
            {
                return;
            }

            var actualTypeName = view.GetType().Name;

            UnityEngine.Object.Destroy(view.gameObject);

            throw new System.InvalidOperationException(
                $"WindowFactory: {type} declares ViewType {definition.ViewType.Name}, but the " +
                $"prefab at Addressables address '{definition.PrefabAddress}' has a " +
                $"{actualTypeName} on its root. Fix whichever of the two is wrong - the address " +
                "in the WindowModule, or the view component on the prefab.");
        }
    }
}
