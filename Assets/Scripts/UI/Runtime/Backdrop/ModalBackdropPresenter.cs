using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.UI.Runtime.Content;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PopupSystem.UI.Runtime.Backdrop
{
    public sealed class ModalBackdropPresenter
    {
        public const string BackdropPrefabAddress = "UI/ModalBackdrop";

        private readonly Func<WindowHandle, UniTask> _closeByHandleAsync;
        private readonly IUiPrefabProvider _prefabProvider;
        private readonly Dictionary<Transform, Backdrop> _backdrops = new();

        private readonly Dictionary<Transform, WindowInstance> _topByLayer = new();

        private GameObject _cachedPrefab;
        private bool _prefabLookupAttempted;

        public ModalBackdropPresenter(
            Func<WindowHandle, UniTask> closeByHandleAsync,
            IUiPrefabProvider prefabProvider)
        {
            _closeByHandleAsync = closeByHandleAsync;
            _prefabProvider = prefabProvider;
        }

        public void Refresh(IReadOnlyList<WindowInstance> activeInstances)
        {
            _topByLayer.Clear();

            for (var i = 0; i < activeInstances.Count; i++)
            {
                ConsiderForBackdrop(_topByLayer, activeInstances[i]);
            }

            foreach (var pair in _topByLayer)
            {
                if (pair.Value.Definition.IsModal)
                {
                    ShowBackdropBehind(pair.Key, pair.Value);
                }
                else
                {
                    HideBackdrop(pair.Key);
                }
            }

            foreach (var layerTransform in _backdrops.Keys)
            {
                if (!_topByLayer.ContainsKey(layerTransform))
                {
                    HideBackdrop(layerTransform);
                }
            }
        }

        private static void ConsiderForBackdrop(Dictionary<Transform, WindowInstance> topByLayer, WindowInstance instance)
        {
            if (instance == null || instance.Handle == null || instance.Handle.IsClosed || instance.View == null)
            {
                return;
            }

            var layerTransform = instance.View.transform.parent;
            if (layerTransform == null)
            {
                return;
            }

            if (!topByLayer.TryGetValue(layerTransform, out var current) ||
                instance.View.transform.GetSiblingIndex() > current.View.transform.GetSiblingIndex())
            {
                topByLayer[layerTransform] = instance;
            }
        }

        private void ShowBackdropBehind(Transform layerTransform, WindowInstance topInstance)
        {
            var backdrop = GetOrCreateBackdrop(layerTransform);
            if (backdrop == null)
            {
                return;
            }

            backdrop.GameObject.SetActive(true);
            backdrop.GameObject.transform.SetAsLastSibling();
            topInstance.View.transform.SetAsLastSibling();
            WireBackdropDismiss(backdrop, topInstance);
        }

        private void HideBackdrop(Transform layerTransform)
        {
            if (_backdrops.TryGetValue(layerTransform, out var backdrop) && backdrop.GameObject != null)
            {
                backdrop.GameObject.SetActive(false);
            }
        }

        private Backdrop GetOrCreateBackdrop(Transform layerTransform)
        {
            if (_backdrops.TryGetValue(layerTransform, out var existing) && existing.GameObject != null)
            {
                return existing;
            }

            var prefab = GetCachedPrefab();
            if (prefab == null)
            {
                return null;
            }

            var gameObject = UnityEngine.Object.Instantiate(prefab, layerTransform, false);

            var rectTransform = gameObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }

            var backdrop = new Backdrop(gameObject, gameObject.GetComponent<Button>());

            if (backdrop.Button == null)
            {
                Debug.LogWarning(
                    $"ModalBackdropPresenter: prefab at '{BackdropPrefabAddress}' has no Button component - " +
                    "backdrop-tap dismissal will not work.");
            }

            _backdrops[layerTransform] = backdrop;
            return backdrop;
        }

        private GameObject GetCachedPrefab()
        {
            if (_prefabLookupAttempted)
            {
                return _cachedPrefab;
            }

            _prefabLookupAttempted = true;

            // GetLoaded, not LoadAsync: Refresh runs inside synchronous open/close bookkeeping. AppEntryPoint
            // preloads this address at startup.
            _cachedPrefab = _prefabProvider.GetLoaded(BackdropPrefabAddress);

            if (_cachedPrefab == null)
            {
                Debug.LogError(
                    $"ModalBackdropPresenter: nothing loaded at Addressables address '{BackdropPrefabAddress}'. " +
                    "It should have been preloaded at startup; check that the prefab is marked Addressable " +
                    "with that address and that AppEntryPoint still preloads it. Modal windows will open " +
                    "without a dimming/input-blocking backdrop until it is there.");
            }

            return _cachedPrefab;
        }

        private void WireBackdropDismiss(Backdrop backdrop, WindowInstance topInstance)
        {
            if (backdrop.Button == null)
            {
                return;
            }

            if (backdrop.DismissHandler != null)
            {
                backdrop.Button.onClick.RemoveListener(backdrop.DismissHandler);
                backdrop.DismissHandler = null;
            }

            if (!topInstance.Definition.CloseOnBackdropClick)
            {
                return;
            }

            var handle = topInstance.Handle;
            backdrop.DismissHandler = () => _closeByHandleAsync(handle).Forget();
            backdrop.Button.onClick.AddListener(backdrop.DismissHandler);
        }

        private sealed class Backdrop
        {
            public Backdrop(GameObject gameObject, Button button)
            {
                GameObject = gameObject;
                Button = button;
            }

            public GameObject GameObject { get; }
            public Button Button { get; }
            public UnityAction DismissHandler { get; set; }
        }
    }
}
