using UnityEngine;

namespace PopupSystem.UI.Runtime.Widgets
{
    /// <summary>One "icon + how many" entry. <see cref="Label"/> is what is drawn instead of the icon when the
    /// icon did not load, so a missing sprite degrades to plain text rather than to an empty slot.</summary>
    public readonly struct IconAmountModel
    {
        public IconAmountModel(Sprite icon, string amount, string label)
        {
            Icon = icon;
            Amount = amount;
            Label = label;
        }

        public Sprite Icon { get; }
        public string Amount { get; }
        public string Label { get; }
    }
}
