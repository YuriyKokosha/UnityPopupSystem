using System;
using PopupSystem.UI.Windows.DailyReward;
using TMPro;
using UnityEditor;
using UnityEngine;
using static PopupSystem.EditorTools.UiKitBuild;

namespace PopupSystem.EditorTools
{
    /// <summary>
    /// Adds the cooldown countdown to <c>DailyRewardWindow.prefab</c>: a frame-coloured <c>chip_9s</c> timer chip
    /// (the kit's badge) that takes the claim button's slot while the reward is on cooldown. One slot, two
    /// states, toggled by <see cref="DailyRewardWindowView"/>; the chip is centred in the button's 140 px slot so
    /// nothing else on the panel moves between them. Idempotent: it removes what it added last time.
    /// </summary>
    public static class UiKitDailyRewardTimerBuilder
    {
        private const string DailyRewardPath = WindowsFolder + "DailyRewardWindow.prefab";
        private const string RendersFolder = "Claude outputs/UIKit/Renders/";
        private const string TimerName = "CooldownTimer";

        // The claim button's slot, measured from the prefab: top -490, 140 tall.
        private const float SlotTop = -490f;
        private const float SlotHeight = 140f;
        private static readonly Vector2 ChipSize = new(360f, 96f);
        private const float LabelSize = 44f;

        private static readonly Color Frame = Hex("#0A4468");
        private static readonly Color Cream = Hex("#FFF6E3");

        [MenuItem("Tools/UI Kit/Daily Reward/Add cooldown timer")]
        public static void AddCooldownTimer()
        {
            var contents = PrefabUtility.LoadPrefabContents(DailyRewardPath);

            try
            {
                var panel = (RectTransform)Require(contents.transform, "Background/Panel");
                DestroyChild(panel, TimerName);

                var chip = NewImage(panel, TimerName, "chip_9s", Frame, sliced: true, raycast: false).rectTransform;
                chip.anchorMin = chip.anchorMax = new Vector2(0.5f, 1f);
                chip.pivot = new Vector2(0.5f, 1f);
                chip.sizeDelta = ChipSize;
                chip.anchoredPosition = new Vector2(0f, SlotTop - (SlotHeight - ChipSize.y) / 2f);

                var label = NewText(chip, "Label", "00:00", Lilita(), LabelSize, Cream, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.rectTransform.anchoredPosition = new Vector2(0f, 2f);

                chip.gameObject.SetActive(false);

                var view = contents.GetComponent<DailyRewardWindowView>();
                Assign(view, "_cooldownRoot", chip.gameObject);
                Assign(view, "_cooldownText", label);

                PrefabUtility.SaveAsPrefabAsset(contents, DailyRewardPath, out var saved);
                if (!saved)
                {
                    throw new InvalidOperationException("SaveAsPrefabAsset failed for " + DailyRewardPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            Debug.Log("[UI Kit] Cooldown timer added to DailyRewardWindow.");
        }

        [MenuItem("Tools/UI Kit/Daily Reward/Render cooldown states")]
        public static void RenderCooldownStates()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DailyRewardPath);

            UiKitPrefabRenderer.Render(prefab, RendersFolder + "daily_reward_ready.png", root =>
            {
                var view = root.GetComponent<DailyRewardWindowView>();
                view.SetContent("Daily Reward", "Your daily reward is ready.", "Claim");
                view.ShowClaim();
            });

            UiKitPrefabRenderer.Render(prefab, RendersFolder + "daily_reward_cooldown.png", root =>
            {
                var view = root.GetComponent<DailyRewardWindowView>();
                view.SetContent("Daily Reward", "Your next reward is on its way.", "Claim");
                view.ShowCooldown("00:42");
            });
        }

        private static void Assign(UnityEngine.Object target, string property, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var field = so.FindProperty(property) ??
                        throw new InvalidOperationException($"{target.GetType().Name} has no serialized '{property}'.");
            field.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
