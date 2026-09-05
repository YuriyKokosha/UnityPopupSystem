using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace PopupSystem.UI.Runtime.Content
{
    public interface IUiPrefabProvider
    {
        UniTask<GameObject> LoadAsync(string address, CancellationToken cancellationToken);

        /// <summary>The already-loaded prefab, or null. For call sites that cannot await - see
        /// ModalBackdropPresenter.</summary>
        GameObject GetLoaded(string address);

        /// <summary>Blocks. Only for what is shown *while* everything else loads; anything with something
        /// already on screen awaits <see cref="LoadAsync"/>.</summary>
        GameObject LoadBlocking(string address);
    }
}
