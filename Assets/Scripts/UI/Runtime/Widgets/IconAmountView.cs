using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Runtime.Widgets
{
    /// <summary>An icon with its amount under it. <see cref="Bind"/> overwrites every visual, so a pooled entry
    /// carries nothing over.</summary>
    public sealed class IconAmountView : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _amount;
        [SerializeField] private TMP_Text _fallbackLabel;

        public Sprite Icon => _icon != null && _icon.enabled ? _icon.sprite : null;

        public void Bind(IconAmountModel model)
        {
            var hasIcon = model.Icon != null;

            if (_icon != null)
            {
                _icon.sprite = model.Icon;
                _icon.enabled = hasIcon;
            }

            if (_fallbackLabel != null)
            {
                _fallbackLabel.text = hasIcon ? string.Empty : model.Label;
                _fallbackLabel.gameObject.SetActive(!hasIcon);
            }

            if (_amount != null)
            {
                _amount.text = model.Amount ?? string.Empty;
            }
        }

        public void Clear()
        {
            Bind(default);
        }
    }
}
