using PopupSystem.UI.Definitions;
using UnityEngine;

namespace PopupSystem.UI.Infrastructure
{
    /// <summary>Where a window of a given layer is parented. The engine depends on this port rather than on
    /// UIRoot, so a test can supply plain Transforms without a scene.</summary>
    public interface IUILayerProvider
    {
        Transform GetLayer(UILayerType layerType);
    }
}
