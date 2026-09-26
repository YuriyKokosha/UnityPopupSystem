using System;
using System.Collections.Generic;
using PopupSystem.UI.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.UI.Windows.Inventory
{
    /// <summary>A fixed grid of pooled cells laid out in code (no LayoutGroup, per the kit's rules). Cells are
    /// created on demand, hidden rather than destroyed, and re-bound on every render, so the pooled window shows
    /// nothing from a previous open.</summary>
    public sealed class InventoryWindowView : WindowView
    {
        public readonly struct SlotModel
        {
            public SlotModel(long stackId, string displayName, int count, bool isStackable, bool canUse, Sprite icon)
            {
                StackId = stackId;
                DisplayName = displayName;
                Count = count;
                IsStackable = isStackable;
                CanUse = canUse;
                Icon = icon;
            }

            public long StackId { get; }
            public Sprite Icon { get; }
            public string DisplayName { get; }
            public int Count { get; }
            public bool IsStackable { get; }
            public bool CanUse { get; }
        }

        [SerializeField] private TMP_Text _titleLabel;
        [SerializeField] private TMP_Text _slotsLabel;
        [SerializeField] private Button _closeButton;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private RectTransform _gridRoot;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private InventorySlotView _cellTemplate;
        [SerializeField] private int _columns = 4;
        [SerializeField] private Vector2 _cellSize = new(200f, 200f);
        [SerializeField] private Vector2 _cellSpacing = new(20f, 20f);

        // The panel is as tall as its design (all rows visible in portrait), but never taller than the screen
        // leaves room for. With reference 1080x1920 and match 0.5 a 1920x1080 screen is a 1920x1080 canvas, so a
        // 1148 panel would run off both edges; here it shrinks to fit and the grid scrolls inside it instead.
        [SerializeField] private float _designPanelHeight = 1148f;
        [SerializeField] private float _screenMargin = 40f;

        private readonly List<InventorySlotView> _cells = new();

        public event Action<long> UseClicked;
        public event Action<long> DiscardClicked;

        private void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(RequestClose);
            }

            if (_cellTemplate != null)
            {
                _cellTemplate.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            FitPanelToScreen();
        }

        // Fires on the root because it stretches over the whole layer, so a resolution or orientation change
        // reaches it even while the window is open.
        private void OnRectTransformDimensionsChange()
        {
            FitPanelToScreen();
        }

        private void FitPanelToScreen()
        {
            if (_panel == null || _panel.parent is not RectTransform parent)
            {
                return;
            }

            var available = parent.rect.height - 2f * _screenMargin;
            if (available <= 0f)
            {
                return; // not laid out yet; the next dimensions change gets it
            }

            var height = Mathf.Min(_designPanelHeight, available);
            if (!Mathf.Approximately(_panel.rect.height, height))
            {
                _panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(RequestClose);
            }

            for (var i = 0; i < _cells.Count; i++)
            {
                Unsubscribe(_cells[i]);
            }
        }

        internal override void ResetForPool()
        {
            // A pooled window must not reopen mid-fling or scrolled to where the last player left it.
            if (_scroll != null)
            {
                _scroll.StopMovement();
            }

            if (_gridRoot != null)
            {
                _gridRoot.anchoredPosition = Vector2.zero;
            }

            for (var i = 0; i < _cells.Count; i++)
            {
                _cells[i].BindEmpty();
                _cells[i].gameObject.SetActive(false);
            }
        }

        public void SetTitle(string title)
        {
            if (_titleLabel != null)
            {
                _titleLabel.text = title;
            }
        }

        public void SetSlotsSummary(int usedSlots, int slotLimit)
        {
            if (_slotsLabel != null)
            {
                _slotsLabel.text = slotLimit > 0 ? $"{usedSlots} / {slotLimit}" : usedSlots.ToString();
            }
        }

        /// <summary>Draws <paramref name="totalCells"/> cells; the first <c>slots.Count</c> are filled, the rest
        /// are empty placeholders.</summary>
        public void Render(IReadOnlyList<SlotModel> slots, int totalCells)
        {
            // Also here, not only in OnEnable: the editor render check runs in Edit Mode, where OnEnable never
            // fires, and a render that skipped the fit would show a layout the game never has.
            FitPanelToScreen();

            var cellCount = Math.Max(totalCells, slots.Count);
            EnsureCells(cellCount);

            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = _cells[i];

                if (i >= cellCount)
                {
                    cell.gameObject.SetActive(false);
                    continue;
                }

                cell.gameObject.SetActive(true);
                Place(cell, i);

                if (i < slots.Count)
                {
                    var slot = slots[i];
                    cell.Bind(slot.StackId, slot.DisplayName, slot.Count, slot.IsStackable, slot.CanUse, slot.Icon);
                }
                else
                {
                    cell.BindEmpty();
                }
            }

            if (_gridRoot != null && _columns > 0)
            {
                var rows = (cellCount + _columns - 1) / _columns;
                var height = rows * _cellSize.y + Math.Max(0, rows - 1) * _cellSpacing.y;
                _gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            }
        }

        private void EnsureCells(int count)
        {
            if (_cellTemplate == null || _gridRoot == null)
            {
                return;
            }

            while (_cells.Count < count)
            {
                var cell = Instantiate(_cellTemplate, _gridRoot);
                cell.name = $"Cell_{_cells.Count}";
                cell.UseClicked += OnCellUseClicked;
                cell.DiscardClicked += OnCellDiscardClicked;
                _cells.Add(cell);
            }
        }

        private void Place(InventorySlotView cell, int index)
        {
            if (_columns <= 0)
            {
                return;
            }

            var column = index % _columns;
            var row = index / _columns;
            var rect = (RectTransform)cell.transform;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = _cellSize;
            rect.anchoredPosition = new Vector2(
                column * (_cellSize.x + _cellSpacing.x),
                -row * (_cellSize.y + _cellSpacing.y));
        }

        private void Unsubscribe(InventorySlotView cell)
        {
            if (cell == null)
            {
                return;
            }

            cell.UseClicked -= OnCellUseClicked;
            cell.DiscardClicked -= OnCellDiscardClicked;
        }

        private void OnCellUseClicked(long stackId) => UseClicked?.Invoke(stackId);

        private void OnCellDiscardClicked(long stackId) => DiscardClicked?.Invoke(stackId);
    }
}
