using UnityEngine;

namespace PopupSystem.UI.Infrastructure
{
    public static class UILayerSorter
    {
        public static void Apply(Transform layer)
        {
            if (layer == null)
            {
                return;
            }

            var layerCanvas = layer.GetComponent<Canvas>();
            var baseOrder = layerCanvas != null ? layerCanvas.sortingOrder : 0;

            for (var i = 0; i < layer.childCount; i++)
            {
                var canvas = layer.GetChild(i).GetComponent<Canvas>();
                if (canvas == null)
                {
                    continue;
                }

                canvas.overrideSorting = true;
                canvas.sortingOrder = baseOrder + i + 1;
            }
        }
    }
}
