using System;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace PopupSystem.EditorTools
{
    /// <summary>Kit primitives shared by the prefab builders: palette tokens, sprite/font lookups, and the
    /// handful of rect helpers every builder needs. Numbers only — no layout decisions live here.</summary>
    internal static class UiKitBuild
    {
        public const string WindowsFolder = "Assets/Content/UI/Windows/";
        public const string SpritesFolder = "Assets/Content/UI/Sprites/";
        public const string FontsFolder = "Assets/Content/UI/Fonts/";
        public const string AddressableGroup = "UI";

        public static readonly Color Ink = Hex("#3A2A16");
        public static readonly Color MutedInk = Hex("#7A6642");
        public static readonly Color DarkOnCream = Hex("#8A5A08");
        public static readonly Color CreamCard = Hex("#F7E9CC");

        public static Image NewImage(RectTransform parent, string name, string spriteName, Color color, bool sliced, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = spriteName != null ? LoadSprite(spriteName) : null;
            image.type = sliced ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static TextMeshProUGUI NewText(
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

        public static RectTransform NewRect(Transform parent, string name)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        /// <summary>Full width minus <paramref name="sideInset"/> on each side, <paramref name="height"/> tall,
        /// top edge at <paramref name="top"/> (negative = down from the parent's top).</summary>
        public static void TopStretch(RectTransform rect, float top, float height, float sideInset)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-2f * sideInset, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }

        public static void DestroyChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        public static Transform Require(Transform parent, string path)
        {
            return parent.Find(path) ?? throw new InvalidOperationException($"'{parent.name}' has no child '{path}'.");
        }

        public static Sprite LoadSprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritesFolder + name + ".png");
            return sprite != null
                ? sprite
                : throw new InvalidOperationException($"Sprite {name} not found under {SpritesFolder}.");
        }

        public static TMP_FontAsset Lilita() => Font("LilitaOne-Regular SDF");

        public static TMP_FontAsset Nunito() => Font("Nunito-Regular SDF");

        private static TMP_FontAsset Font(string name)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontsFolder + name + ".asset");
            return font != null
                ? font
                : throw new InvalidOperationException($"Font {name} not found under {FontsFolder}.");
        }

        public static void MarkAddressable(string assetPath, string address)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings.FindGroup(AddressableGroup) ?? settings.DefaultGroup;
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                throw new InvalidOperationException($"No asset at {assetPath} to mark addressable.");
            }

            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = address;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);
        }

        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var color)
                ? color
                : throw new ArgumentException("Bad colour " + hex);
        }
    }
}
