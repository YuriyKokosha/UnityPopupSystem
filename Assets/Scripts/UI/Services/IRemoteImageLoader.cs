using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PopupSystem.UI.Services
{
    /// <summary>Loads a popup image from an external source. The implementation owns and caches what it
    /// hands back - consumers display, they never destroy it.</summary>
    public interface IRemoteImageLoader
    {
        UniTask<Texture2D> LoadAsync(string relativeOrAbsoluteUrl, CancellationToken cancellationToken);
    }
}
