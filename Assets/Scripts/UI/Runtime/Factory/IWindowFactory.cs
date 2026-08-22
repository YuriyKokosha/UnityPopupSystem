using PopupSystem.UI.Core;

namespace PopupSystem.UI.Runtime.Factory
{
    public interface IWindowFactory
    {
        WindowInstance Create(WindowRequest request);

        /// <summary>
        /// Returns a closed instance's view to the pool for reuse by a later Create() of the same
        /// window type, instead of destroying it - see WindowFactory for why.
        /// </summary>
        void Release(WindowInstance instance);
    }
}
