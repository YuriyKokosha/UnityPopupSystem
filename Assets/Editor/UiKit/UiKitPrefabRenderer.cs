using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Renders a window prefab off-screen and writes a PNG, so a layout claim can be checked by
    /// looking at the result rather than by reasoning about it.
    /// Everything is built inside a temporary additive scene that is closed again in a finally
    /// block, so a render that throws half-way cannot leave objects in the scene that is open.
    /// </summary>
    public static class UiKitPrefabRenderer
    {
        public const int DefaultWidth = 1080;
        public const int DefaultHeight = 1920;

        /// <summary>The flat stage colour the kit's screens sit on (#0E5C8C).</summary>
        public static readonly Color StageBackground = new Color32(0x0E, 0x5C, 0x8C, 0xFF);

        private const string RendersFolder = "Claude outputs/UIKit/Renders";
        private const int UiLayer = 5;
        private const float ReferencePixelsPerUnit = 100f;
        private const float CanvasPlaneDistance = 100f;

        [MenuItem("Tools/UI Kit/Render prefab")]
        public static void RenderSelection()
        {
            foreach (var prefab in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                if (!PrefabUtility.IsPartOfPrefabAsset(prefab))
                {
                    continue;
                }

                var path = Render(prefab);
                if (path != null)
                {
                    Debug.Log($"[UI Kit] Rendered {prefab.name} -> {path}");
                }
            }
        }

        [MenuItem("Tools/UI Kit/Render prefab", true)]
        private static bool CanRenderSelection()
        {
            foreach (var prefab in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                if (PrefabUtility.IsPartOfPrefabAsset(prefab))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Renders <paramref name="prefab"/> to a PNG and returns the file path, or null if the
        /// render could not run. Drive a window's states from <paramref name="configure"/> through
        /// the view's own public methods - that renders the path the game actually takes, which
        /// arranging objects by hand does not.
        /// </summary>
        /// <param name="outputPath">
        /// Absolute or project-relative path. Defaults to <see cref="RendersFolder"/>: outside
        /// Assets/, so the PNG does not become an imported asset, and inside the one scratch
        /// folder the .gitignore covers.
        /// </param>
        public static string Render(
            GameObject prefab,
            string outputPath = null,
            Action<GameObject> configure = null,
            Color? background = null,
            int width = DefaultWidth,
            int height = DefaultHeight)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "[UI Kit] Render prefab needs Edit Mode - opening a scene is not allowed while playing.");
                return null;
            }

            var path = ResolveOutputPath(outputPath, prefab.name);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            var scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;
            RenderTexture target = null;
            Texture2D readback = null;

            try
            {
                root = new GameObject("UiKitRender");

                // Before anything else can throw: everything hangs off this root, and the root
                // lives in the scratch scene rather than in whichever scene happens to be active.
                SceneManager.MoveGameObjectToScene(root, scratch);

                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

                // The camera carries the target texture before the canvas is built, because a
                // ScreenSpaceCamera canvas sizes itself from the camera's pixel rect.
                var camera = BuildCamera(root.transform, background ?? StageBackground, target);
                var canvas = BuildCanvas(root.transform, camera, width, height);

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
                Stretch(instance);
                SetLayerRecursively(instance, UiLayer);

                configure?.Invoke(instance);

                Canvas.ForceUpdateCanvases();
                foreach (var text in instance.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.ForceMeshUpdate();
                }

                Canvas.ForceUpdateCanvases();
                camera.Render();

                readback = ReadBack(target, width, height);
                File.WriteAllBytes(path, readback.EncodeToPNG());
                return path;
            }
            finally
            {
                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }

                if (target != null)
                {
                    if (RenderTexture.active == target)
                    {
                        RenderTexture.active = null;
                    }

                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (scratch.IsValid())
                {
                    EditorSceneManager.CloseScene(scratch, true);
                }
            }
        }

        private static Camera BuildCamera(Transform parent, Color background, RenderTexture target)
        {
            var go = new GameObject("Camera", typeof(Camera));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -CanvasPlaneDistance * 2f);

            var camera = go.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;

            // Only the UI layer, so nothing in the scenes that stay open can drift into the frame.
            camera.cullingMask = 1 << UiLayer;
            camera.targetTexture = target;

            // Disabled: it renders on demand and never contributes to the editor's own frames.
            camera.enabled = false;
            return camera;
        }

        private static Canvas BuildCanvas(Transform parent, Camera camera, int width, int height)
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler))
            {
                layer = UiLayer,
            };
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = CanvasPlaneDistance;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = ReferencePixelsPerUnit;
            return canvas;
        }

        private static void Stretch(GameObject instance)
        {
            if (instance.transform is not RectTransform rect)
            {
                return;
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static Texture2D ReadBack(RenderTexture target, int width, int height)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();

            RenderTexture.active = previous;
            return texture;
        }

        private static string ResolveOutputPath(string outputPath, string prefabName)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? ".";
            if (string.IsNullOrEmpty(outputPath))
            {
                return Path.Combine(projectRoot, RendersFolder, prefabName + ".png");
            }

            return Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(projectRoot, outputPath);
        }
    }
}
