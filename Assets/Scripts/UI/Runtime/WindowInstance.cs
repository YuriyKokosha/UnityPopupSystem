using System.Threading;
using System;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Runtime.Controller;

namespace PopupSystem.UI.Runtime
{
    public sealed class WindowInstance
    {
        public WindowType Type { get; }
        public WindowDefinition Definition { get; }
        public WindowView View { get; }
        public IWindowController Controller { get; }
        public CancellationTokenSource LifetimeCts { get; }
        public WindowHandle Handle { get; set; }
        public Action CloseRequestedHandler { get; set; }
        public bool IsClosing { get; set; }

        public WindowInstance(
            WindowType type,
            WindowDefinition definition,
            WindowView view,
            IWindowController controller,
            CancellationTokenSource lifetimeCts)
        {
            Type = type;
            Definition = definition;
            View = view;
            Controller = controller;
            LifetimeCts = lifetimeCts;
        }
    }
}
