using System.Collections.Generic;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Brings the UI kit sprites to the import settings they need and packs them into an atlas.
    /// The 9-slice borders live here rather than only in the .meta files, so they show up in a
    /// diff and survive regenerating the PNGs.
    /// </summary>
    public static class UiKitSpriteSetup
    {
        private const string SpritesFolder = "Assets/Content/UI/Sprites";

        // Sprite Atlas V1 and V2 are different asset types with different extensions:
        // .spriteatlas is handled by NativeFormatImporter, .spriteatlasv2 by SpriteAtlasImporter.
        private const string AtlasPathV1 = "Assets/Content/UI/UI.spriteatlas";
        private const string AtlasPathV2 = "Assets/Content/UI/UI.spriteatlasv2";
        private const float PixelsPerUnit = 100f;

        /// <summary>9-slice borders in Unity order: left, bottom, right, top.</summary>
        private static readonly Dictionary<string, Vector4> Borders = new Dictionary<string, Vector4>
        {
            { "panel_9s", new Vector4(56f, 56f, 56f, 56f) },
            { "card_9s", new Vector4(34f, 34f, 34f, 34f) },
            { "chip_9s", new Vector4(44f, 44f, 44f, 44f) },
            // The button only stretches horizontally: the sprite's height is the button's height,
            // so the top and bottom are left unsliced - otherwise the bevel band stretches with it.
            { "btn_pill", new Vector4(74f, 0f, 74f, 0f) },
        };

        [MenuItem("Tools/UI Kit/Configure sprites")]
        public static void ConfigureSprites()
        {
            var changed = Configure();
            Debug.Log($"[UI Kit] Sprites configured: {changed}.");
        }

        [MenuItem("Tools/UI Kit/Configure sprites and build atlas")]
        public static void ConfigureSpritesAndBuildAtlas()
        {
            var changed = Configure();
            var atlas = BuildAtlas();
            Debug.Log($"[UI Kit] Sprites configured: {changed}. Atlas: {atlas}.");
        }

        private static int Configure()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { SpritesFolder });
            var changed = 0;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                {
                    continue;
                }

                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                var border = Borders.TryGetValue(name, out var b) ? b : Vector4.zero;

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);

                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteBorder = border;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePixelsPerUnit = PixelsPerUnit;
                settings.alphaIsTransparency = true;
                settings.mipmapEnabled = false;
                settings.wrapMode = TextureWrapMode.Clamp;
                settings.filterMode = FilterMode.Bilinear;
                settings.readable = false;

                importer.SetTextureSettings(settings);
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }

        private static string BuildAtlas()
        {
            var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(SpritesFolder);
            if (folder == null)
            {
                Debug.LogError($"[UI Kit] Folder not found: {SpritesFolder}");
                return "not created";
            }

            var packing = new SpriteAtlasPackingSettings
            {
                enableRotation = false,
                enableTightPacking = false,
                padding = 4,
            };

            var textureSettings = new SpriteAtlasTextureSettings
            {
                sRGB = true,
                filterMode = FilterMode.Bilinear,
                generateMipMaps = false,
            };

            var platform = new TextureImporterPlatformSettings
            {
                maxTextureSize = 2048,
                format = TextureImporterFormat.Automatic,
                textureCompression = TextureImporterCompression.CompressedHQ,
            };

            // A Unity 6 project may be switched to Sprite Atlas V2: different asset type,
            // different extension, and the settings live on the importer, not on the asset.
            var isV2 = EditorSettings.spritePackerMode.ToString().Contains("V2");
            var atlasPath = isV2 ? AtlasPathV2 : AtlasPathV1;

            if (isV2)
            {
                var asset = new SpriteAtlasAsset { name = "UI" };
                asset.Add(new Object[] { folder });
                SpriteAtlasAsset.Save(asset, atlasPath);
                AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);

                if (AssetImporter.GetAtPath(atlasPath) is SpriteAtlasImporter atlasImporter)
                {
                    atlasImporter.packingSettings = packing;
                    atlasImporter.textureSettings = textureSettings;
                    atlasImporter.SetPlatformSettings(platform);
                    atlasImporter.includeInBuild = true;
                    atlasImporter.SaveAndReimport();
                }
                else
                {
                    Debug.LogError($"[UI Kit] {atlasPath} was not imported as a Sprite Atlas - check Sprite Packer Mode.");
                }
            }
            else
            {
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                if (atlas == null)
                {
                    atlas = new SpriteAtlas { name = "UI" };
                    AssetDatabase.CreateAsset(atlas, atlasPath);
                }

                atlas.SetIncludeInBuild(true);
                atlas.SetPackingSettings(packing);
                atlas.SetTextureSettings(textureSettings);
                atlas.SetPlatformSettings(platform);

                var packables = atlas.GetPackables();
                if (packables is { Length: > 0 })
                {
                    atlas.Remove(packables);
                }

                atlas.Add(new Object[] { folder });
                EditorUtility.SetDirty(atlas);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return atlasPath;
        }
    }
}
