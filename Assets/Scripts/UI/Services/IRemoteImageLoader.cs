using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PopupSystem.UI.Services
{
    /// <summary>
    /// Loads an image asset for a popup from an external source (a real CDN in production,
    /// a local StreamingAssets URL for this demo). Popup code only ever depends on this
    /// abstraction, never on UnityWebRequest directly, so swapping the actual source later
    /// does not touch any window/controller code.
    /// </summary>
    public interface IRemoteImageLoader
    {
        UniTask<Texture2D> LoadAsync(string relativeOrAbsoluteUrl, CancellationToken cancellationToken);
    }
}
