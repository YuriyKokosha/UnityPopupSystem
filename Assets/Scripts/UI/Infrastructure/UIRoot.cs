using PopupSystem.UI.Enum;
using UnityEngine;

namespace PopupSystem.UI.Infrastructure
{
    public sealed class UIRoot : MonoBehaviour
    {
        [SerializeField] private Transform _windowsLayer;
        [SerializeField] private Transform _popupsLayer;
        [SerializeField] private Transform _notificationsLayer;
        [SerializeField] private Transform _systemLayer;

        public Transform WindowsLayer => _windowsLayer;
        public Transform PopupsLayer => _popupsLayer;
        public Transform NotificationsLayer => _notificationsLayer;
        public Transform SystemLayer => _systemLayer;

        public Transform GetLayer(UILayerType layerType)
        {
            return layerType switch
            {
                UILayerType.Windows => WindowsLayer,
                UILayerType.Popups => PopupsLayer,
                UILayerType.Notifications => NotificationsLayer,
                UILayerType.System => SystemLayer,
                _ => WindowsLayer,
            };
        }
    }
}
