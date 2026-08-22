using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using PopupSystem.UI.Core;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Infrastructure;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Registry;
using Zenject;

namespace PopupSystem.UI.Runtime.Factory
{
    /// <summary>
    /// Creates window views and pools them on close instead of Instantiate/Destroy-ing a fresh
    /// GameObject on every single Open - windows like Settings/DailyReward/Offer/RewardPopup can
    /// be opened and closed many times over a session, so recycling their view is a real,
    /// recurring saving rather than a one-off. Only the WindowController is ever fresh per Open
    /// (it holds per-open state and is a cheap plain object); the view (GameObject + components)
    /// is reused whenever one of the same WindowType is sitting in the pool. Prefab assets are
    /// also cached here after their first Resources.Load, for the same reason.
    /// </summary>
    public sealed class WindowFactory : IWindowFactory
    {
        private readonly IWindowRegistry _registry;
        private readonly IWindowControllerResolver _controllerResolver;
        private readonly UIRoot _uiRoot;
        private readonly DiContainer _container;
        private readonly Dictionary<string, GameObject> _prefabCache = new();
        private readonly Dictionary<WindowType, Stack<WindowView>> _pooledViews = new();

        public WindowFactory(
            IWindowRegistry registry,
            IWindowControllerResolver controllerResolver,
            UIRoot uiRoot,
            DiContainer container)
        {
            _registry = registry;
            _controllerResolver = controllerResolver;
            _uiRoot = uiRoot;
            _container = container;
        }

        public WindowInstance Create(WindowRequest request)
        {
            var definition = _registry.Get(request.Type);
            var parent = _uiRoot.GetLayer(definition.Layer);
            var view = AcquireView(definition, request.Type, parent);
            var controller = _controllerResolver.Create(definition.Type);
            var lifetimeCts = new CancellationTokenSource();

            // Wire up this window's chosen open/close animation (may be null - instant) before
            // anything else touches the view. Re-assigned even for a pooled view: harmless if
            // it's the same transition instance as before, and correct if a WindowDefinition is
            // ever reconfigured between runs.
            view.SetTransition(definition.Transition);

            // The window must stay hidden until the manager explicitly drives it through the
            // Opening stage (WindowView.PlayOpenAsync) - creation itself is only "Initializing".
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

            // Let the view clear anything a fresh controller won't overwrite on the next Init
            // (e.g. a downloaded texture) before it sits in the pool, possibly for a while.
            view.ResetForPool();
            view.Hide();

            if (!_pooledViews.TryGetValue(instance.Type, out var stack))
            {
                stack = new Stack<WindowView>();
                _pooledViews[instance.Type] = stack;
            }

            stack.Push(view);
        }

        private WindowView AcquireView(WindowDefinition definition, WindowType type, Transform parent)
        {
            if (_pooledViews.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                var pooledView = stack.Pop();
                // Defensive: layer assignment is static per WindowDefinition today, so this is
                // always a no-op, but re-parenting here rather than trusting the pooled view's
                // existing parent costs nothing and avoids a subtle bug if that ever changes.
                pooledView.transform.SetParent(parent, false);
                // A freshly Instantiate-d view always lands as the last sibling, which is what
                // makes "most recently opened" naturally render on top and what the backdrop/
                // stacking logic in WindowsManager relies on (it reads sibling index to find the
                // topmost active instance per layer). A pooled view keeps whatever sibling index
                // it had from its previous life, so without this it could reappear underneath
                // windows opened after it - move it to the back of the render order explicitly to
                // match Instantiate's behaviour.
                pooledView.transform.SetAsLastSibling();
                return pooledView;
            }

            return CreateView(definition, type, parent);
        }

        private WindowView CreateView(WindowDefinition definition, WindowType type, Transform parent)
        {
            // All window content lives in a prefab now - nothing is ever built from code here.
            if (string.IsNullOrEmpty(definition.PrefabResourcePath))
            {
                throw new System.InvalidOperationException(
                    $"WindowFactory: {type} has no PrefabResourcePath configured. Every window must " +
                    "reference a content prefab (see its WindowModule) - there is no code-built fallback.");
            }

            var prefab = GetCachedPrefab(definition.PrefabResourcePath);
            if (prefab == null)
            {
                throw new System.InvalidOperationException(
                    $"WindowFactory: no prefab found at Resources path '{definition.PrefabResourcePath}' " +
                    $"for {type}. There is no code-built fallback - fix or restore the prefab asset.");
            }

            var prefabView = _container.InstantiatePrefabForComponent<WindowView>(prefab);
            var prefabRect = prefabView.GetComponent<RectTransform>();
            prefabRect.SetParent(parent, false);
            prefabRect.anchorMin = Vector2.zero;
            prefabRect.anchorMax = Vector2.one;
            prefabRect.offsetMin = Vector2.zero;
            prefabRect.offsetMax = Vector2.zero;
            return prefabView;
        }

        private GameObject GetCachedPrefab(string resourcePath)
        {
            // Resources.Load itself is cached by Unity, but still costs a lookup on every call;
            // caching the reference here means each prefab path is only ever looked up once for
            // the life of the app. A failed load (null) is cached too, so a missing/misnamed
            // asset doesn't retry a doomed load on every single Open.
            if (_prefabCache.TryGetValue(resourcePath, out var cached))
            {
                return cached;
            }

            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                // There is no code-built fallback any more - a missing/misnamed prefab is a hard
                // failure (see CreateView), so make sure it's visible in the console too.
                Debug.LogError(
                    $"WindowFactory: no prefab found at Resources path '{resourcePath}'.");
            }

            _prefabCache[resourcePath] = prefab;
            return prefab;
        }
    }
}
