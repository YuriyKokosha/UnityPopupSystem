using System.Collections.Generic;
using UnityEngine;

namespace PopupSystem.UI.Runtime.Widgets
{
    /// <summary>A centred row of <see cref="IconAmountView"/>s laid out in code (no LayoutGroup, per the kit):
    /// fixed entry width, fixed spacing, the row as a whole centred on the strip. Entries are created on demand
    /// from a hidden template and hidden rather than destroyed.</summary>
    public sealed class IconAmountStripView : MonoBehaviour
    {
        [SerializeField] private IconAmountView _template;
        [SerializeField] private float _entryWidth = 150f;
        [SerializeField] private float _spacing = 16f;

        private readonly List<IconAmountView> _entries = new();

        public int VisibleCount { get; private set; }

        public IReadOnlyList<IconAmountView> Entries => _entries;

        private void Awake()
        {
            if (_template != null)
            {
                _template.gameObject.SetActive(false);
            }
        }

        public void Render(IReadOnlyList<IconAmountModel> models)
        {
            var count = models?.Count ?? 0;
            EnsureEntries(count);

            // Entries share the strip's centre: the row is count * width + (count - 1) * spacing wide.
            var rowWidth = count * _entryWidth + Mathf.Max(0, count - 1) * _spacing;
            var left = -rowWidth / 2f + _entryWidth / 2f;

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                if (i >= count)
                {
                    entry.Clear();
                    entry.gameObject.SetActive(false);
                    continue;
                }

                var rect = (RectTransform)entry.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(_entryWidth, 0f);
                rect.anchoredPosition = new Vector2(left + i * (_entryWidth + _spacing), 0f);

                entry.gameObject.SetActive(true);
                entry.Bind(models[i]);
            }

            VisibleCount = count;
        }

        public void Clear()
        {
            Render(null);
        }

        private void EnsureEntries(int count)
        {
            if (_template == null)
            {
                return;
            }

            // The template may be disabled already (the editor render check runs without Awake).
            _template.gameObject.SetActive(false);

            while (_entries.Count < count)
            {
                var entry = Instantiate(_template, _template.transform.parent);
                entry.name = $"Entry_{_entries.Count}";
                _entries.Add(entry);
            }
        }
    }
}
