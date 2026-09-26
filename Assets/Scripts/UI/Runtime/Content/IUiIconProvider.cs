using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PopupSystem.UI.Runtime.Content
{
    /// <summary>Icon sprites by Addressables address (<c>UI/Items/*</c>, <c>UI/Currencies/*</c>). The provider owns
    /// every sprite it hands out; callers never release one.</summary>
    public interface IUiIconProvider
    {
        /// <summary>Throws when nothing is at <paramref name="address"/>; a failed load is not cached.</summary>
        UniTask<Sprite> LoadAsync(string address, CancellationToken cancellationToken);

        /// <summary>The already-loaded sprite, or null. For views that draw synchronously after a preload.</summary>
        Sprite GetLoaded(string address);
    }
}
