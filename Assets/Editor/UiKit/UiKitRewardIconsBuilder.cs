using System;
using System.Collections.Generic;
using PopupSystem.UI.Runtime.Widgets;
using PopupSystem.UI.Windows.DailyReward;
using PopupSystem.UI.Windows.MainGame;
using PopupSystem.UI.Windows.Offer;
using PopupSystem.UI.Windows.RewardPopup;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static PopupSystem.EditorTools.UiKitBuild;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Replaces the currency/item text in MainGame, RewardPopup, DailyReward and Offer with rows of icons
    /// (<see cref="IconAmountStripView"/>), and gives the icon sprites the Addressables addresses the runtime
    /// asks for. A script for the same reason as the inventory builder: the layout is a set of numbers that can
    /// be reviewed and re-run after a kit change. Every menu item is idempotent — it removes what it added
    /// last time before adding it again.
    /// </summary>
    public static class UiKitRewardIconsBuilder
    {
        private const string MainGamePath = WindowsFolder + "MainGameWindow.prefab";
        private const string RewardPopupPath = WindowsFolder + "RewardPopupWindow.prefab";
        private const string DailyRewardPath = WindowsFolder + "DailyRewardWindow.prefab";
        private const string OfferPath = WindowsFolder + "OfferWindow.prefab";
        private const string RendersFolder = "Claude outputs/UIKit/Renders/";

        /// <summary>Sprite file → address. Items use the address their catalog entry carries
        /// (<c>ItemDefinition.IconAddress</c>); currencies use <c>RewardIcons.CurrencyAddress(id)</c>.</summary>
        public static readonly IReadOnlyDictionary<string, string> IconAddresses = new Dictionary<string, string>
        {
            { "icon_coin", "UI/Currencies/gold" },
            { "icon_gem", "UI/Currencies/gems" },
            { "icon_energy", "UI/Currencies/energy" },
            { "item_sword", "UI/Items/Sword" },
            { "item_potion", "UI/Items/HealthPotion" },
            { "item_arrows", "UI/Items/Arrows" },
            { "item_chest", "UI/Items/Chest" },
            { "item_ore", "UI/Items/Ore" },
        };

        private readonly struct StripSpec
        {
            public StripSpec(bool vertical, float entryWidth, float spacing, float iconSize, float amountSize, Color amountColor)
            {
                Vertical = vertical;
                EntryWidth = entryWidth;
                Spacing = spacing;
                IconSize = iconSize;
                AmountSize = amountSize;
                AmountColor = amountColor;
            }

            public bool Vertical { get; }
            public float EntryWidth { get; }
            public float Spacing { get; }
            public float IconSize { get; }
            public float AmountSize { get; }
            public Color AmountColor { get; }
        }

        [MenuItem("Tools/UI Kit/Icons/Mark icon sprites addressable")]
        public static void MarkIconsAddressable()
        {
            foreach (var pair in IconAddresses)
            {
                MarkAddressable(SpritesFolder + pair.Key + ".png", pair.Value);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UI Kit] {IconAddresses.Count} icon sprites marked addressable in group '{AddressableGroup}'.");
        }

        [MenuItem("Tools/UI Kit/Icons/Add icon rows to windows")]
        public static void AddIconRows()
        {
            EditPrefab(MainGamePath, BuildMainGame);
            EditPrefab(RewardPopupPath, BuildRewardPopup);
            EditPrefab(DailyRewardPath, BuildDailyReward);
            EditPrefab(OfferPath, BuildOffer);
            Debug.Log("[UI Kit] Icon rows added to MainGame, RewardPopup, DailyReward and Offer.");
        }

        [MenuItem("Tools/UI Kit/Icons/Render icon screens")]
        public static void RenderIconScreens()
        {
            UiKitPrefabRenderer.Render(Load(MainGamePath), RendersFolder + "icons_main_game.png", root =>
            {
                var view = root.GetComponent<MainGameWindowView>();
                view.SetPlayerData("Yuriy", 12, "8f2a-11");
                view.SetBalances(new[] { Model("icon_coin", "12 480"), Model("icon_gem", "248"), Model("icon_energy", "50") });
                view.SetInventorySlots(3, 12);
            });

            UiKitPrefabRenderer.Render(Load(RewardPopupPath), RendersFolder + "icons_reward_popup.png", root =>
            {
                var view = root.GetComponent<RewardPopupView>();
                view.SetLoading(false);
                view.SetTitle("Daily Reward Claimed");
                view.SetRewards(new[] { Model("icon_coin", "500"), Model("icon_gem", "10"), Model("item_potion", "x2") });
            });

            UiKitPrefabRenderer.Render(Load(RewardPopupPath), RendersFolder + "icons_reward_popup_error.png", root =>
            {
                var view = root.GetComponent<RewardPopupView>();
                view.SetLoading(false);
                view.SetTitle("Reward");
                view.SetRewardText("Your inventory is full. Free some slots and try again.");
            });

            UiKitPrefabRenderer.Render(Load(DailyRewardPath), RendersFolder + "icons_daily_reward.png", root =>
            {
                var view = root.GetComponent<DailyRewardWindowView>();
                view.SetContent("Daily Reward", "Your daily reward is ready.", "Claim");
                view.SetReward(new[] { Model("icon_coin", "500"), Model("icon_gem", "10"), Model("item_potion", "x2") });
            });

            UiKitPrefabRenderer.Render(Load(OfferPath), RendersFolder + "icons_offer.png", root =>
            {
                var view = root.GetComponent<OfferWindowView>();
                view.SetContent("Starter Offer", "A limited-time bundle at a discounted price.", "Buy");
                view.SetBannerLoading(false);
                view.SetBannerFallback();
                view.SetReward(new[] { Model("icon_gem", "500"), Model("icon_energy", "50"), Model("item_sword", "x1") });
            });

            // A missing sprite: the entry shows its label instead of an empty hole.
            UiKitPrefabRenderer.Render(Load(OfferPath), RendersFolder + "icons_offer_missing_icon.png", root =>
            {
                var view = root.GetComponent<OfferWindowView>();
                view.SetContent("Starter Offer", "A limited-time bundle at a discounted price.", "Buy");
                view.SetBannerLoading(false);
                view.SetBannerFallback();
                view.SetReward(new[] { Model("icon_gem", "500"), new IconAmountModel(null, "50", "energy"), Model("item_sword", "x1") });
            });

            UiKitPrefabRenderer.Render(Load(OfferPath), RendersFolder + "icons_offer_landscape.png", root =>
            {
                var view = root.GetComponent<OfferWindowView>();
                view.SetContent("Starter Offer", "A limited-time bundle at a discounted price.", "Buy");
                view.SetBannerLoading(false);
                view.SetBannerFallback();
                view.SetReward(new[] { Model("icon_gem", "500"), Model("icon_energy", "50"), Model("item_sword", "x1") });
            }, width: 1920, height: 1080);
        }

        // ---- windows --------------------------------------------------------------------------------------------

        // The balances card is 120 high: one horizontal row, icon left of its number.
        private static void BuildMainGame(GameObject root)
        {
            var card = (RectTransform)Require(root.transform, "Background/Panel/BalancesCard");
            DestroyChild(card, "Balances");
            DestroyChild(card, "BalancesStrip");

            var strip = BuildStrip(card, "BalancesStrip",
                new StripSpec(vertical: false, entryWidth: 230f, spacing: 24f, iconSize: 64f, amountSize: 38f, Ink));
            var rect = (RectTransform)strip.transform;
            Stretch(rect);
            rect.sizeDelta = new Vector2(-56f, -28f);

            Assign(root.GetComponent<MainGameWindowView>(), "_balances", strip);
        }

        // The row takes the big reward label's slot; the label stays for the one sentence the popup still says
        // (an error), at body size rather than the 64 pt it had as the reward itself.
        private static void BuildRewardPopup(GameObject root)
        {
            var panel = (RectTransform)Require(root.transform, "Background/Panel");
            DestroyChild(panel, "RewardStrip");

            var label = Require(panel, "RewardLabel").GetComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.font = Nunito();
            label.fontSize = 30f;
            label.color = Ink;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;

            var strip = BuildStrip(panel, "RewardStrip",
                new StripSpec(vertical: true, entryWidth: 140f, spacing: 12f, iconSize: 72f, amountSize: 34f, DarkOnCream));
            TopStretch((RectTransform)strip.transform, -140f, 118f, 40f);
            strip.transform.SetSiblingIndex(label.transform.GetSiblingIndex() + 1);

            Assign(root.GetComponent<RewardPopupView>(), "_rewards", strip);
        }

        // The card used to hold a static "Chest x1"; it now shows the bundle the claim actually grants.
        private static void BuildDailyReward(GameObject root)
        {
            var card = (RectTransform)Require(root.transform, "Background/Panel/RewardCard");
            DestroyChild(card, "IconBox");
            DestroyChild(card, "Amount");
            DestroyChild(card, "RewardStrip");

            var caption = Require(card, "Caption").GetComponent<TextMeshProUGUI>();
            caption.text = "Today's reward";
            caption.alignment = TextAlignmentOptions.Center;
            TopStretch(caption.rectTransform, -16f, 34f, 28f);

            var strip = BuildStrip(card, "RewardStrip",
                new StripSpec(vertical: true, entryWidth: 150f, spacing: 24f, iconSize: 80f, amountSize: 34f, Ink));
            var rect = (RectTransform)strip.transform;
            TopStretch(rect, -54f, 132f, 28f);

            Assign(root.GetComponent<DailyRewardWindowView>(), "_reward", strip);
        }

        // Offer: banner, a two-line description (the remote copy no longer lists the contents), the reward on its
        // own card, then Buy. The panel grows by the card: 900 → 960.
        private const float OfferPanelHeight = 960f;

        private static void BuildOffer(GameObject root)
        {
            var panel = (RectTransform)Require(root.transform, "Background/Panel");
            DestroyChild(panel, "RewardCard");

            panel.sizeDelta = new Vector2(panel.sizeDelta.x, OfferPanelHeight);

            var description = Require(panel, "Description").GetComponent<TextMeshProUGUI>();
            TopStretch(description.rectTransform, -566f, 76f, 44f);

            var card = NewImage(panel, "RewardCard", "card_9s", CreamCard, sliced: true, raycast: false);
            TopStretch(card.rectTransform, -650f, 120f, 44f);
            card.transform.SetSiblingIndex(description.transform.GetSiblingIndex() + 1);

            var strip = BuildStrip(card.rectTransform, "RewardStrip",
                new StripSpec(vertical: true, entryWidth: 150f, spacing: 24f, iconSize: 64f, amountSize: 32f, Ink));
            var rect = (RectTransform)strip.transform;
            Stretch(rect);
            rect.sizeDelta = new Vector2(-40f, -16f);

            var buy = (RectTransform)Require(panel, "BuyButton");
            buy.anchoredPosition = new Vector2(buy.anchoredPosition.x, -786f);

            Assign(root.GetComponent<OfferWindowView>(), "_reward", strip);
        }

        // ---- the strip ------------------------------------------------------------------------------------------

        private static IconAmountStripView BuildStrip(RectTransform parent, string name, StripSpec spec)
        {
            var stripRect = NewRect(parent, name);
            var strip = stripRect.gameObject.AddComponent<IconAmountStripView>();

            var entry = NewRect(stripRect, "EntryTemplate");
            entry.anchorMin = new Vector2(0.5f, 0f);
            entry.anchorMax = new Vector2(0.5f, 1f);
            entry.sizeDelta = new Vector2(spec.EntryWidth, 0f);

            var icon = NewImage(entry, "Icon", null, Color.white, sliced: false, raycast: false);
            icon.preserveAspect = true;
            icon.enabled = false;

            var amount = NewText(entry, "Amount", "0", Lilita(), spec.AmountSize, spec.AmountColor,
                spec.Vertical ? TextAlignmentOptions.Bottom : TextAlignmentOptions.Left);

            // Drawn in the icon's place when the icon did not load: small, wrapped, the old text. Not called
            // "Label": Build fonts and apply would turn anything named that into the display face.
            var label = NewText(entry, "Fallback", string.Empty, Nunito(), 22f, MutedInk, TextAlignmentOptions.Center);
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.gameObject.SetActive(false);

            if (spec.Vertical)
            {
                // Icon at the top, its number under it.
                Place(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(spec.IconSize, spec.IconSize));
                Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(spec.EntryWidth, spec.IconSize));
                var amountRect = amount.rectTransform;
                amountRect.anchorMin = new Vector2(0f, 0f);
                amountRect.anchorMax = new Vector2(1f, 0f);
                amountRect.pivot = new Vector2(0.5f, 0f);
                amountRect.sizeDelta = new Vector2(0f, spec.AmountSize * 1.2f);
                amountRect.anchoredPosition = Vector2.zero;
            }
            else
            {
                // Icon on the left, the number filling the rest of the entry.
                Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(spec.IconSize, spec.IconSize));
                Place(label.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(spec.IconSize + 24f, spec.IconSize));
                var amountRect = amount.rectTransform;
                amountRect.anchorMin = new Vector2(0f, 0f);
                amountRect.anchorMax = new Vector2(1f, 1f);
                amountRect.pivot = new Vector2(0f, 0.5f);
                amountRect.offsetMin = new Vector2(spec.IconSize + 14f, 0f);
                amountRect.offsetMax = Vector2.zero;
            }

            var view = entry.gameObject.AddComponent<IconAmountView>();
            Assign(view, "_icon", icon);
            Assign(view, "_amount", amount);
            Assign(view, "_fallbackLabel", label);

            var so = new SerializedObject(strip);
            so.FindProperty("_template").objectReferenceValue = view;
            so.FindProperty("_entryWidth").floatValue = spec.EntryWidth;
            so.FindProperty("_spacing").floatValue = spec.Spacing;
            so.ApplyModifiedPropertiesWithoutUndo();

            entry.gameObject.SetActive(false);
            return strip;
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ---- plumbing -------------------------------------------------------------------------------------------

        private static void EditPrefab(string path, Action<GameObject> edit)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);

            try
            {
                edit(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, path, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("SaveAsPrefabAsset failed for " + path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void Assign(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            if (target == null)
            {
                throw new InvalidOperationException($"No component to assign '{property}' on.");
            }

            var so = new SerializedObject(target);
            var field = so.FindProperty(property) ??
                        throw new InvalidOperationException($"{target.GetType().Name} has no serialized '{property}'.");
            field.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Load(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException("Missing " + path);

        private static IconAmountModel Model(string sprite, string amount) => new(LoadSprite(sprite), amount, sprite);
    }
}
