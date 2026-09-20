using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Runtime.Widgets
{
    /// <summary>
    /// Keeps a button's label readable while the button is disabled.
    /// </summary>
    /// <remarks>
    /// Unity's ColorTint transition only recolours the Selectable's <c>targetGraphic</c>, so the
    /// kit's dark label on the greyed-out fill drops to about 1.7:1 on its own. Both colours are
    /// serialized, so they stay in the prefab next to the rest of the styling.
    /// </remarks>
    [RequireComponent(typeof(Selectable))]
    public sealed class ButtonLabelTint : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Color _normalColor = Color.black;
        [SerializeField] private Color _disabledColor = Color.white;

        private Selectable _selectable;
        private bool _lastInteractable;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        private void OnEnable()
        {
            Apply(true);
        }

        // Selectable raises no event when interactable flips, and subclassing Button to override
        // DoStateTransition would mean a custom component on every button. A guarded poll is the
        // cheaper trade: it assigns a colour only on an actual change.
        private void LateUpdate()
        {
            Apply(false);
        }

        private void Apply(bool force)
        {
            if (_label == null || _selectable == null)
            {
                return;
            }

            var interactable = _selectable.IsInteractable();
            if (!force && interactable == _lastInteractable)
            {
                return;
            }

            _lastInteractable = interactable;
            _label.color = interactable ? _normalColor : _disabledColor;
        }
    }
}
