using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Core;

namespace PopupSystem.UI.Runtime.Factory
{
    public interface IWindowFactory
    {
        UniTask<WindowInstance> CreateAsync(WindowRequest request, CancellationToken cancellationToken);

        /// <summary>Returns the view to the pool instead of destroying it.</summary>
        void Release(WindowInstance instance);
    }
}
