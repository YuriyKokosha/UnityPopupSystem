using System;
using System.Collections.Generic;
using PopupSystem.UI.Runtime.Widgets;
using PopupSystem.UI.Windows.Inventory;
using PopupSystem.UI.Windows.MainGame;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Assembles <c>InventoryWindow.prefab</c> from the Lagoon Gold kit and adds the inventory entry point to
    /// <c>MainGameWindow.prefab</c>. It is a script rather than hand work in the editor so the layout is a set of
    /// numbers that can be re-run after a kit change (see .claude/skills/ui-kit/SKILL.md). Every position is
    /// explicit — no LayoutGroup — because the view pool reuses instances.
    /// </summary>
    public static class UiKitInventoryWindowBuilder
    {
        private const string WindowsFolder = "Assets/Content/UI/Windows/";
        private const string SpritesFolder = "Assets/Content/UI/Sprites/";
        private const string FontsFolder = "Assets/Content/UI/Fonts/";
        private const string InventoryPrefabPath = WindowsFolder + "InventoryWindow.prefab";
        private const string SettingsPrefabPath = WindowsFolder + "SettingsWindow.prefab";
        private const string MainGamePrefabPath = WindowsFolder + "MainGameWindow.prefab";
        private const string InventoryAddress = "UI/Windows/InventoryWindow";
        private const string AddressableGroup = "UI";

        // Palette (SKILL.md).
        private static readonly Color Ink = Hex("#3A2A16");
        private static readonly Color MutedInk = Hex("#7A6642");
        private static readonly Color DarkOnCream = Hex("#8A5A08");
        private static readonly Color CreamCard = Hex("#F7E9CC");
        private static readonly Color Gold = Hex("#EFC24E");
        private static readonly Color GoldLight = Hex("#FFE39B");
        private static readonly Color Ring = Hex("#FFEDC4");
        private static readonly Color Frame = Hex("#0A4468");
        private static readonly Color FrameShadow = Hex("#073553");
        private static readonly Color Cta = Hex("#E29A1F");
        private static readonly Color CtaPressed = Hex("#CC8813");
        private static readonly Color DisabledFill = Hex("#5E5A50");
        private static readonly Color DisabledInk = Hex("#E7E1D3");

        // Layout. Panel 860 wide, 34 inset → 792 inner; the grid is 3 × 236 + 2 × 22 = 752, centred. Three
        // columns rather than four so a cell has room for two real buttons side by side; four made them 72 wide
        // and the first render showed exactly that.
        private const float PanelWidth = 860f;
        private const float GridTop = 190f;
        private const int Columns = 3;
        private const int Rows = 4; // 12 demo slots
        private static readonly Vector2 CellSize = new(236f, 216f);
        private static readonly Vector2 CellSpacing = new(22f, 20f);
        private static readonly Vector2 CellButtonSize = new(100f, SmallPillHeight);
        private const float SmallPillHeight = 72f; // btn_pill_small: 66 body + 6 shadow, fixed like btn_pill
        private const float CellInset = 12f;
        private const float IconSize = 104f; // cell 216 high − 12 inset − 72 pills − 12 inset − 16 gap
        private static float PanelHeight => GridTop + Rows * CellSize.y + (Rows - 1) * CellSpacing.y + 34f;

        [MenuItem("Tools/UI Kit/Inventory/Build InventoryWindow prefab")]
        public static void BuildInventoryWindow()
        {
            var settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SettingsPrefabPath);
            if (settingsPrefab == null)
            {
                throw new InvalidOperationException($"Missing {SettingsPrefabPath}; the inventory window starts from it.");
            }

            // Start from Settings: root Canvas + GraphicRaycaster + CanvasGroup, transparent Background, Panel,
            // Title, TitleUnderline and the measured CloseButton all carry over unchanged.
            var root = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab);

            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "InventoryWindow";

                var oldView = root.GetComponent<MonoBehaviour>();
                foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour.GetType().Name.EndsWith("WindowView", StringComparison.Ordinal))
                    {
                        oldView = behaviour;
                    }
                }

                UnityEngine.Object.DestroyImmediate(oldView);

                var panel = (RectTransform)root.transform.Find("Background/Panel");
                DestroyChild(panel, "Status");
                DestroyChild(panel, "Loading");
                DestroyChild(panel, "StatusCard");
                panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);

                var title = panel.Find("Title").GetComponent<TMP_Text>();
                title.text = "Inventory";

                var slots = Text(panel, "Slots", "0 / 12", Nunito(), 28f, MutedInk, TextAlignmentOptions.Center);
                TopStretch(slots.rectTransform, -134f, 40f, 88f);

                // The grid sits in a masked, vertically scrolling viewport that runs from GridTop to the bottom
                // inset. In portrait the panel is tall enough for every row and nothing scrolls; when the view
                // shortens the panel to fit a landscape screen, the rows below the fold scroll into view.
                var gridWidth = Columns * CellSize.x + (Columns - 1) * CellSpacing.x;
                var viewport = new GameObject("GridViewport", typeof(RectTransform), typeof(RectMask2D))
                    .GetComponent<RectTransform>();
                viewport.SetParent(panel, false);
                viewport.anchorMin = new Vector2(0.5f, 0f);
                viewport.anchorMax = new Vector2(0.5f, 1f);
                viewport.pivot = new Vector2(0.5f, 1f);
                viewport.sizeDelta = new Vector2(gridWidth, -(GridTop + 34f));
                viewport.anchoredPosition = new Vector2(0f, -GridTop);

                // The drag surface. A ScrollRect only hears a drag that starts on a raycast target under it, and
                // everything in a cell is raycastTarget = false except the buttons — so without this the grid
                // scrolled only from Use/Drop. Invisible (alpha 0, culled), it catches every point of the
                // viewport the cells do not; a drag that starts on a button still bubbles to the ScrollRect.
                var dragSurface = viewport.gameObject.AddComponent<Image>();
                dragSurface.color = new Color(1f, 1f, 1f, 0f);
                dragSurface.raycastTarget = true;
                viewport.GetComponent<CanvasRenderer>().cullTransparentMesh = true;

                var grid = new GameObject("Grid", typeof(RectTransform)).GetComponent<RectTransform>();
                grid.SetParent(viewport, false);
                grid.anchorMin = new Vector2(0f, 1f);
                grid.anchorMax = new Vector2(1f, 1f);
                grid.pivot = new Vector2(0.5f, 1f);
                grid.sizeDelta = new Vector2(0f, Rows * CellSize.y + (Rows - 1) * CellSpacing.y);
                grid.anchoredPosition = Vector2.zero;

                var scroll = viewport.gameObject.AddComponent<ScrollRect>();
                scroll.content = grid;
                scroll.viewport = viewport;
                scroll.horizontal = false;
                scroll.vertical = true;
                // Elastic, not Clamped: with about one row to scroll in landscape, a clamped fling hits the end
                // before its inertia shows. Elastic lets it run on, overshoot and spring back — the mobile feel.
                // 0.135 is Unity's default and iOS's normal deceleration (0.998 per ms), spelled out on purpose.
                scroll.movementType = ScrollRect.MovementType.Elastic;
                scroll.elasticity = 0.1f;
                scroll.inertia = true;
                scroll.decelerationRate = 0.135f;
                scroll.scrollSensitivity = 30f;

                var cell = BuildCell(grid);
                cell.gameObject.SetActive(false);

                var view = root.AddComponent<InventoryWindowView>();
                var so = new SerializedObject(view);
                so.FindProperty("_titleLabel").objectReferenceValue = title;
                so.FindProperty("_slotsLabel").objectReferenceValue = slots;
                so.FindProperty("_closeButton").objectReferenceValue = panel.Find("CloseButton").GetComponent<Button>();
                so.FindProperty("_panel").objectReferenceValue = panel;
                so.FindProperty("_designPanelHeight").floatValue = PanelHeight;
                so.FindProperty("_screenMargin").floatValue = 40f;
                so.FindProperty("_gridRoot").objectReferenceValue = grid;
                so.FindProperty("_scroll").objectReferenceValue = scroll;
                so.FindProperty("_cellTemplate").objectReferenceValue = cell;
                so.FindProperty("_columns").intValue = Columns;
                so.FindProperty("_cellSize").vector2Value = CellSize;
                so.FindProperty("_cellSpacing").vector2Value = CellSpacing;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, InventoryPrefabPath, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("SaveAsPrefabAsset failed for " + InventoryPrefabPath);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            MarkAddressable(InventoryPrefabPath, InventoryAddress);
            Debug.Log($"[UI Kit] Built {InventoryPrefabPath} (address {InventoryAddress}).");
        }

        [MenuItem("Tools/UI Kit/Inventory/Add inventory button to MainGameWindow")]
        public static void AddInventoryButtonToMainGame()
        {
            var contents = PrefabUtility.LoadPrefabContents(MainGamePrefabPath);

            try
            {
                // The corner buttons sit on the stage (Background), beside the panel, as the mockup draws them.
                var panel = (RectTransform)contents.transform.Find("Background");
                var settingsButton = (RectTransform)panel.Find("SettingsButton");
                if (settingsButton == null)
                {
                    throw new InvalidOperationException("MainGameWindow has no SettingsButton to mirror.");
                }

                DestroyChild(panel, "InventoryButton");
                DestroyChild(panel, "InventoryText");

                // Mirror of the gear: same round button, top-left corner of the screen, chest icon.
                var inventoryButton = UnityEngine.Object.Instantiate(settingsButton.gameObject, panel);
                inventoryButton.name = "InventoryButton";
                var rect = (RectTransform)inventoryButton.transform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(48f, -132f);
                rect.sizeDelta = settingsButton.sizeDelta;

                var icon = rect.Find("Icon").GetComponent<Image>();
                icon.sprite = Sprite("icon_chest");
                icon.color = GoldLight;
                icon.rectTransform.sizeDelta = new Vector2(52f, 52f);

                // "3 / 12" under a chest: the icon says "inventory", the label only has to say how full. A longer
                // string centred under a corner button runs off the screen edge (it did on the first render).
                var count = Text(panel, "InventoryText", "0 / 12", Lilita(), 26f, GoldLight, TextAlignmentOptions.Center);
                var countRect = count.rectTransform;
                countRect.anchorMin = new Vector2(0f, 1f);
                countRect.anchorMax = new Vector2(0f, 1f);
                countRect.pivot = new Vector2(0.5f, 1f);
                countRect.sizeDelta = new Vector2(160f, 34f);
                // Centred under the button: its centre is at x = 48 + 96 / 2.
                countRect.anchoredPosition = new Vector2(48f + settingsButton.sizeDelta.x / 2f, -132f - settingsButton.sizeDelta.y - 8f);

                var view = contents.GetComponent<MainGameWindowView>();
                var so = new SerializedObject(view);
                so.FindProperty("_inventoryButton").objectReferenceValue = inventoryButton.GetComponent<Button>();
                so.FindProperty("_inventoryText").objectReferenceValue = count;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, MainGamePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log("[UI Kit] MainGameWindow: inventory button and counter wired.");
        }

        [MenuItem("Tools/UI Kit/Inventory/Render inventory screens")]
        public static void RenderInventoryScreens()
        {
            var inventory = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPrefabPath);
            var mainGame = AssetDatabase.LoadAssetAtPath<GameObject>(MainGamePrefabPath);

            UiKitPrefabRenderer.Render(inventory, "Claude outputs/UIKit/Renders/inventory_partial.png", root =>
            {
                var view = root.GetComponent<InventoryWindowView>();
                view.SetTitle("Inventory");
                view.SetSlotsSummary(3, 12);
                view.Render(new List<InventoryWindowView.SlotModel>
                {
                    new(1, "Sword", 1, isStackable: false, canUse: false, Sprite("item_sword")),
                    new(2, "Health Potion", 7, isStackable: true, canUse: true, Sprite("item_potion")),
                    new(3, "Arrows", 50, isStackable: true, canUse: true, Sprite("item_arrows")),
                }, 12);
            });

            UiKitPrefabRenderer.Render(inventory, "Claude outputs/UIKit/Renders/inventory_empty.png", root =>
            {
                var view = root.GetComponent<InventoryWindowView>();
                view.SetTitle("Inventory");
                view.SetSlotsSummary(0, 12);
                view.Render(Array.Empty<InventoryWindowView.SlotModel>(), 12);
            });

            UiKitPrefabRenderer.Render(inventory, "Claude outputs/UIKit/Renders/inventory_full.png", root =>
            {
                var view = root.GetComponent<InventoryWindowView>();
                view.SetTitle("Inventory");
                view.SetSlotsSummary(12, 12);
                var full = new List<InventoryWindowView.SlotModel>();
                string[] names = { "Sword", "Health Potion", "Arrows", "Chest", "Ore", "Health Potion" };
                string[] icons = { "item_sword", "item_potion", "item_arrows", "item_chest", "item_ore", "item_potion" };
                for (var i = 0; i < 12; i++)
                {
                    // Cell 11 has no icon on purpose: the render shows the name fallback next to real icons.
                    var icon = i == 10 ? null : Sprite(icons[i % icons.Length]);
                    full.Add(new InventoryWindowView.SlotModel(i + 1, names[i % names.Length], (i % 4) * 9 + 1, isStackable: i % 3 != 0, canUse: i % 2 == 1, icon));
                }

                view.Render(full, 12);
            });

            // Landscape: same content, 1920 x 1080. Whatever the scaler does to the panel shows here first.
            UiKitPrefabRenderer.Render(inventory, "Claude outputs/UIKit/Renders/inventory_landscape.png", root =>
            {
                var view = root.GetComponent<InventoryWindowView>();
                view.SetTitle("Inventory");
                view.SetSlotsSummary(3, 12);
                view.Render(new List<InventoryWindowView.SlotModel>
                {
                    new(1, "Sword", 1, isStackable: false, canUse: false, Sprite("item_sword")),
                    new(2, "Health Potion", 7, isStackable: true, canUse: true, Sprite("item_potion")),
                    new(3, "Arrows", 50, isStackable: true, canUse: true, Sprite("item_arrows")),
                }, 12);
            }, width: 1920, height: 1080);

            UiKitPrefabRenderer.Render(
                AssetDatabase.LoadAssetAtPath<GameObject>(WindowsFolder + "DailyRewardWindow.prefab"),
                "Claude outputs/UIKit/Renders/daily_reward_landscape.png", null, width: 1920, height: 1080);

            UiKitPrefabRenderer.Render(mainGame, "Claude outputs/UIKit/Renders/main_game_with_inventory.png", root =>
            {
                var view = root.GetComponent<MainGameWindowView>();
                view.SetPlayerData("Yuriy", 12, "8f2a-11");
                view.SetBalances(new[]
                {
                    new PopupSystem.UI.Runtime.Widgets.IconAmountModel(Sprite("icon_coin"), "12 480", "gold"),
                    new PopupSystem.UI.Runtime.Widgets.IconAmountModel(Sprite("icon_gem"), "248", "gems"),
                });
                view.SetInventorySlots(3, 12);
            });
        }

        // ---- cell -----------------------------------------------------------------------------------------------

        private static InventorySlotView BuildCell(RectTransform grid)
        {
            var cell = Image(grid, "CellTemplate", "card_9s", CreamCard, sliced: true, raycast: false);
            var rect = cell.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = CellSize;
            rect.anchoredPosition = Vector2.zero;

            // Empty state: one quiet word on the card.
            var empty = new GameObject("Empty", typeof(RectTransform)).GetComponent<RectTransform>();
            empty.SetParent(rect, false);
            Stretch(empty);
            var emptyLabel = Text(empty, "Label", "Empty", Nunito(), 22f, MutedInk, TextAlignmentOptions.Center);
            Stretch(emptyLabel.rectTransform);

            // Filled state: the item's icon big in the top half, its count in the icon's bottom-right corner, two
            // small pills at the foot. The name is only the fallback for an icon that did not load, drawn in the
            // icon's place (InventorySlotView toggles the two).
            var filled = new GameObject("Filled", typeof(RectTransform)).GetComponent<RectTransform>();
            filled.SetParent(rect, false);
            Stretch(filled);

            var icon = Image(filled, "Icon", null, Color.white, sliced: false, raycast: false);
            icon.enabled = false;
            icon.preserveAspect = true;
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            icon.rectTransform.pivot = new Vector2(0.5f, 1f);
            icon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            icon.rectTransform.anchoredPosition = new Vector2(0f, -CellInset);

            var name = Text(filled, "Name", "Item", Nunito(), 24f, Ink, TextAlignmentOptions.Center);
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;
            TopStretch(name.rectTransform, -CellInset - (IconSize - 62f) / 2f, 62f, 20f); // two lines of 24 pt, centred on the icon slot

            // Right-aligned against the cell, bottom edge on the icon's: it overlaps only the icon's transparent
            // corner, so a three-digit stack still reads.
            var amount = Text(filled, "Amount", "x1", Lilita(), 30f, DarkOnCream, TextAlignmentOptions.BottomRight);
            amount.rectTransform.anchorMin = amount.rectTransform.anchorMax = new Vector2(1f, 1f);
            amount.rectTransform.pivot = new Vector2(1f, 1f);
            amount.rectTransform.sizeDelta = new Vector2(90f, 36f);
            amount.rectTransform.anchoredPosition = new Vector2(-16f, -CellInset - IconSize + 30f);

            var use = SmallPillButton(filled, "UseButton", "Use", Cta, CtaPressed, Ink, DisabledInk);
            var useRect = (RectTransform)use.transform;
            useRect.anchorMin = useRect.anchorMax = new Vector2(0f, 0f);
            useRect.pivot = new Vector2(0f, 0f);
            useRect.sizeDelta = CellButtonSize;
            useRect.anchoredPosition = new Vector2(CellInset, CellInset);

            var discard = SmallPillButton(filled, "DiscardButton", "Drop", Frame, FrameShadow, Ring, DisabledInk);
            var discardRect = (RectTransform)discard.transform;
            discardRect.anchorMin = discardRect.anchorMax = new Vector2(1f, 0f);
            discardRect.pivot = new Vector2(1f, 0f);
            discardRect.sizeDelta = CellButtonSize;
            discardRect.anchoredPosition = new Vector2(-CellInset, CellInset);

            var view = cell.gameObject.AddComponent<InventorySlotView>();
            var so = new SerializedObject(view);
            so.FindProperty("_iconImage").objectReferenceValue = icon;
            so.FindProperty("_nameLabel").objectReferenceValue = name;
            so.FindProperty("_countLabel").objectReferenceValue = amount;
            so.FindProperty("_useButton").objectReferenceValue = use;
            so.FindProperty("_discardButton").objectReferenceValue = discard;
            so.FindProperty("_emptyState").objectReferenceValue = empty.gameObject;
            so.FindProperty("_filledState").objectReferenceValue = filled.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            return view;
        }

        /// <summary>A small action on <c>btn_pill_small</c> — the kit's primary button at a fixed 72 high, same
        /// bevel and shadow as <c>btn_pill</c>, which cannot shrink below 140. (The first cut used <c>chip_9s</c>
        /// at 40 high; its 44 px borders are taller than that, so the 9-slice collapsed it into an oval.)</summary>
        private static Button SmallPillButton(
            RectTransform parent, string name, string label, Color normal, Color pressed, Color ink, Color disabledInk)
        {
            var image = Image(parent, name, "btn_pill_small", Color.white, sliced: true, raycast: true);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Color.Lerp(normal, Color.white, 0.12f);
            colors.pressedColor = pressed;
            colors.selectedColor = normal;
            colors.disabledColor = DisabledFill;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            var text = Text(image.rectTransform, "Label", label, Lilita(), 28f, ink, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            // Centre the label on the 66 px body, not on the 72 px sprite: the bottom 6 px are drop shadow.
            text.rectTransform.sizeDelta = new Vector2(0f, -6f);
            text.rectTransform.anchoredPosition = new Vector2(0f, 3f);

            var tint = image.gameObject.AddComponent<ButtonLabelTint>();
            var so = new SerializedObject(tint);
            so.FindProperty("_label").objectReferenceValue = text;
            so.FindProperty("_normalColor").colorValue = ink;
            so.FindProperty("_disabledColor").colorValue = disabledInk;
            so.ApplyModifiedPropertiesWithoutUndo();

            return button;
        }

        // ---- primitives -----------------------------------------------------------------------------------------

        private static Image Image(RectTransform parent, string name, string spriteName, Color color, bool sliced, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = spriteName != null ? Sprite(spriteName) : null;
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TextMeshProUGUI Text(
            RectTransform parent, string name, string text, TMP_FontAsset font, float size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.font = font;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Full width minus <paramref name="sideInset"/> on each side, <paramref name="height"/> tall,
        /// top edge at <paramref name="top"/> (negative = down from the parent's top).</summary>
        private static void TopStretch(RectTransform rect, float top, float height, float sideInset)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-2f * sideInset, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }

        private static void DestroyChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        private static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritesFolder + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException($"Sprite {name} not found under {SpritesFolder}.");
            }

            return sprite;
        }

        private static TMP_FontAsset Lilita() => Font("LilitaOne-Regular SDF");

        private static TMP_FontAsset Nunito() => Font("Nunito-Regular SDF");

        private static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsFolder + name + ".asset");
            if (font == null)
            {
                throw new InvalidOperationException($"Font {name} not found under {FontsFolder}.");
            }

            return font;
        }

        private static void MarkAddressable(string assetPath, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(AddressableGroup) ?? settings.DefaultGroup;
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
            settings.SetDirty(UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
            AssetDatabase.SaveAssets();
        }

        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color)
                ? color
                : throw new ArgumentException("Bad colour " + hex);
        }
    }
}
