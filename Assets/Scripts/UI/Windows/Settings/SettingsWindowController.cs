using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Windows.Settings
{
    public sealed class SettingsWindowController : WindowController<EmptyWindowData, SettingsWindowView>
    {
        protected override UniTask OnInitializeAsync(EmptyWindowData data, CancellationToken cancellationToken)
        {
            View.SetLoading(false);
            View.SetStatus("Settings window is open");
            return UniTask.CompletedTask;
        }
    }
}
