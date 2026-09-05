using System;
using PopupSystem.Contracts;
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
        public string PrefabAddress { get; }

        /// <summary>Null means instant show/hide.</summary>
        public IWindowTransition Transition { get; }

        /// <summary>Modal windows only. Defaults to false so a monetisation popup is never lost to a stray tap.</summary>
        public bool CloseOnBackdropClick { get; }

        /// <summary>The persistent screen everything else opens on top of: single-instance, its own slot, and not
        /// "busy" for the queue. Exactly one window type sets it.</summary>
        public bool IsBaseScreen { get; }

        public WindowDefinition(
            WindowType type,
            UIEntryKind kind,
            UILayerType layer,
            bool isModal,
            Type viewType,
            string prefabAddress = null,
            IWindowTransition transition = null,
            bool closeOnBackdropClick = false,
            bool isBaseScreen = false)
        {
            Type = type;
            Kind = kind;
            Layer = layer;
            IsModal = isModal;
            ViewType = viewType;
            PrefabAddress = prefabAddress;
            Transition = transition;
            CloseOnBackdropClick = closeOnBackdropClick;
            IsBaseScreen = isBaseScreen;
        }
    }
}
