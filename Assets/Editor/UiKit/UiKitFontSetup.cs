using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Builds TMP font assets for the UI kit faces and assigns them across the window prefabs.
    /// The .ttf files ship with the repository under the SIL Open Font Licence (`OFL.txt` sits
    /// next to each face), so this reads what is already in <see cref="FontsFolder"/>.
    /// </summary>
    public static class UiKitFontSetup
    {
        private const string FontsFolder = "Assets/Content/UI/Fonts";

        // Faces the kit is drawn in. The match is by file name, case-insensitive.
        private const string DisplayFace = "Lilita";
        private const string BodyFace = "Nunito";

        private const int SamplingPointSize = 90;
        private const int AtlasPadding = 9;
        private const int AtlasSize = 1024;

        private static readonly string[] WindowPrefabs =
        {
            "Assets/Content/UI/Windows/MainGameWindow.prefab",
            "Assets/Content/UI/Windows/SettingsWindow.prefab",
            "Assets/Content/UI/Windows/DailyRewardWindow.prefab",
            "Assets/Content/UI/Windows/OfferWindow.prefab",
            "Assets/Content/UI/Windows/RewardPopupWindow.prefab",
        };

        // Objects that carry the display face; everything else in a window gets the body face.
        private static readonly string[] DisplayObjects =
        {
            "Title", "Label", "Amount", "RewardLabel",
        };

        [MenuItem("Tools/UI Kit/Build fonts and apply")]
        public static void BuildFontsAndApply()
        {
            var display = BuildFontAsset(DisplayFace);
            var body = BuildFontAsset(BodyFace);

            if (display == null || body == null)
            {
                Debug.LogError(
                    $"[UI Kit] Put the kit's .ttf files into {FontsFolder} first: " +
                    $"a '{DisplayFace}' face for titles and buttons, a '{BodyFace}' face for body text. " +
                    "Both are free (SIL Open Font License) but are not checked into this repository.");
                return;
            }

            var applied = 0;
            foreach (var path in WindowPrefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.font = DisplayObjects.Contains(text.gameObject.name) ? display : body;
                    applied++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[UI Kit] Fonts applied to {applied} text objects across {WindowPrefabs.Length} prefabs.");
        }

        private static bool NameContains(string assetPath, string token) =>
            Path.GetFileName(assetPath).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Finds a .ttf/.otf by face name and turns it into a TMP font asset next to it.</summary>
        private static TMP_FontAsset BuildFontAsset(string face)
        {
            if (!AssetDatabase.IsValidFolder(FontsFolder))
            {
                return null;
            }

            // The folder holds one upright Regular per face. The filters below are the guard for
            // the next time a Google Fonts archive is dropped in whole: it brings italics and a
            // variable font along, and an italic variable face sorts first. Pick the upright
            // static Regular - TMP treats a variable font as its default instance anyway.
            var sourcePath = AssetDatabase.FindAssets("t:Font", new[] { FontsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => NameContains(p, face))
                .Where(p => !NameContains(p, "Italic"))
                .OrderBy(p => NameContains(p, "Variable") ? 1 : 0)
                .ThenBy(p => NameContains(p, "Regular") ? 0 : 1)
                .ThenBy(p => Path.GetFileName(p).Length)
                .FirstOrDefault();

            if (sourcePath == null)
            {
                return null;
            }

            var assetPath = Path.ChangeExtension(sourcePath, null) + " SDF.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
            {
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                source, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic);

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // The atlas texture and the material have to live inside the asset, or the font
            // renders as blank quads after a domain reload.
            if (fontAsset.atlasTextures is { Length: > 0 })
            {
                fontAsset.atlasTextures[0].name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            }

            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UI Kit] Built {assetPath}");
            return fontAsset;
        }
    }
}
