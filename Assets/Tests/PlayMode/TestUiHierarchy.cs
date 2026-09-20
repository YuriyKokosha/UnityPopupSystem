using System;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Infrastructure;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.Tests.PlayMode
{
    public sealed class TestUiHierarchy : IUILayerProvider, IDisposable
    {
        private const int WindowsBand = 1000;
        private const int PopupsBand = 2000;
        private const int NotificationsBand = 3000;
        private const int SystemBand = 4000;

        private readonly GameObject _root;
        private readonly Transform _windows;
        private readonly Transform _popups;
        private readonly Transform _notifications;
        private readonly Transform _system;

        public TestUiHierarchy()
        {
            // Deliberately no EventSystem or input module: StandaloneInputModule reads legacy UnityEngine.Input,
            // which throws every frame under the Input System package.
            _root = new GameObject(
                "TestUIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _windows = CreateLayer("WindowsLayer", WindowsBand);
            _popups = CreateLayer("PopupsLayer", PopupsBand);
            _notifications = CreateLayer("NotificationsLayer", NotificationsBand);
            _system = CreateLayer("SystemLayer", SystemBand);
        }

        public Transform GetLayer(UILayerType layerType)
        {
            return layerType switch
            {
                UILayerType.Windows => _windows,
                UILayerType.Popups => _popups,
                UILayerType.Notifications => _notifications,
                UILayerType.System => _system,
                _ => _windows,
            };
        }

        public int GetLayerSortingOrder(UILayerType layerType)
        {
            return GetLayer(layerType).GetComponent<Canvas>().sortingOrder;
        }

        public void Dispose()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
            }
        }

        private Transform CreateLayer(string name, int sortingOrder)
        {
            var layer = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));

            var rectTransform = (RectTransform)layer.transform;
            rectTransform.SetParent(_root.transform, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var canvas = layer.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            return rectTransform;
        }
    }
}
