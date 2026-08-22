using System;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Transitions;

namespace PopupSystem.UI.Definitions
{
    public sealed class WindowDefinition
    {
        public WindowType Type { get; }
        public UIEntryKind Kind { get; }
        public UILayerType Layer { get; }
        public bool IsModal { get; }
        public Type ViewType { get; }
        public string PrefabResourcePath { get; }

        /// <summary>
        /// How this window animates in/out. Null means instant show/hide - see WindowView.
        /// </summary>
        public IWindowTransition Transition { get; }

        /// <summary>
        /// Only meaningful when <see cref="IsModal"/> is true: whether tapping the backdrop
        /// behind this window closes it. Defaults to false so an engagement/monetization popup
        /// (e.g. an offer) is never dismissed by an accidental tap outside it.
        /// </summary>
        public bool CloseOnBackdropClick { get; }

        public WindowDefinition(
            WindowType type,
            UIEntryKind kind,
            UILayerType layer,
            bool isModal,
            Type viewType,
            string prefabResourcePath = null,
            IWindowTransition transition = null,
            bool closeOnBackdropClick = false)
        {
            Type = type;
            Kind = kind;
            Layer = layer;
            IsModal = isModal;
            ViewType = viewType;
            PrefabResourcePath = prefabResourcePath;
            Transition = transition;
            CloseOnBackdropClick = closeOnBackdropClick;
        }
    }
}
