using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Runtime.Backdrop
{
    public sealed class ModalBackdropPresenter
    {
        private const string BackdropPrefabResourcePath = "UI/ModalBackdrop";

        private readonly Func<WindowHandle, UniTask> _closeByHandleAsync;
        private readonly Dictionary<Transform, GameObject> _backdrops = new();
        private GameObject _cachedPrefab;
        private bool _prefabLookupAttempted;

        public ModalBackdropPresenter(Func<WindowHandle, UniTask> closeByHandleAsync)
        {
            _closeByHandleAsync = closeByHandleAsync;
        }

        public void Refresh(IEnumerable<WindowInstance> activeInstances)
        {
            var topByLayer = new Dictionary<Transform, WindowInstance>();

            foreach (var instance in activeInstances)
            {
                ConsiderForBackdrop(topByLayer, instance);
            }

            foreach (var pair in topByLayer)
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
                if (!topByLayer.ContainsKey(layerTransform))
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

            backdrop.SetActive(true);
            backdrop.transform.SetAsLastSibling();
            topInstance.View.transform.SetAsLastSibling();
            WireBackdropDismiss(backdrop, topInstance);
        }

        private void HideBackdrop(Transform layerTransform)
        {
            if (_backdrops.TryGetValue(layerTransform, out var backdrop) && backdrop != null)
            {
                backdrop.SetActive(false);
            }
        }

        private GameObject GetOrCreateBackdrop(Transform layerTransform)
        {
            if (_backdrops.TryGetValue(layerTransform, out var existing) && existing != null)
            {
                return existing;
            }

            var prefab = GetCachedPrefab();
            if (prefab == null)
            {
                return null;
            }

            var backdrop = UnityEngine.Object.Instantiate(prefab, layerTransform, false);
            var rectTransform = backdrop.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
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
            _cachedPrefab = Resources.Load<GameObject>(BackdropPrefabResourcePath);

            if (_cachedPrefab == null)
            {
                Debug.LogError(
                    $"ModalBackdropPresenter: no prefab found at Resources path '{BackdropPrefabResourcePath}'. " +
                    "Create one with a full-stretch RectTransform, an Image (or other Graphic) for the dim, " +
                    "and a Button covering it for backdrop-tap dismissal. Modal windows will open without a " +
                    "dimming/input-blocking backdrop until it exists.");
            }

            return _cachedPrefab;
        }

        private void WireBackdropDismiss(GameObject backdrop, WindowInstance topInstance)
        {
            var button = backdrop.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning(
                    $"ModalBackdropPresenter: prefab at '{BackdropPrefabResourcePath}' has no Button component - " +
                    "backdrop-tap dismissal will not work.");
                return;
            }

            button.onClick.RemoveAllListeners();

            if (topInstance.Definition.CloseOnBackdropClick)
            {
                var handle = topInstance.Handle;
                button.onClick.AddListener(() => _closeByHandleAsync(handle).Forget());
            }
        }
    }
}
