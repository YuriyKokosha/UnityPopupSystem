using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime;
using UnityEngine;

namespace PopupSystem.UI.Transitions
{
    /// <summary>
    /// Fades alpha (via a CanvasGroup, added on demand) while scaling the window in/out around
    /// its own pivot. A dependency-free, per-frame lerp - no external tweening package required.
    /// Stateless and reusable: a single instance can be shared by every window that wants the
    /// same look (see WindowRegistry).
    /// </summary>
    public sealed class FadeScaleWindowTransition : IWindowTransition
    {
        private readonly float _durationSeconds;
        private readonly float _fromScale;

        public FadeScaleWindowTransition(float durationSeconds = 0.18f, float fromScale = 0.9f)
        {
            _durationSeconds = durationSeconds;
            _fromScale = fromScale;
        }

        public UniTask PlayOpenAsync(WindowView view, CancellationToken cancellationToken)
        {
            var canvasGroup = GetOrAddCanvasGroup(view);
            var rectTransform = view.transform as RectTransform;

            canvasGroup.alpha = 0f;
            // Defensive, not load-bearing today (views aren't pooled yet): guarantees the window
            // is actually clickable even if its CanvasGroup came in with these left off somehow.
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            SetUniformScale(rectTransform, _fromScale);

            return AnimateAsync(canvasGroup, rectTransform, 0f, 1f, _fromScale, 1f, cancellationToken);
        }

        public UniTask PlayCloseAsync(WindowView view, CancellationToken cancellationToken)
        {
            var canvasGroup = GetOrAddCanvasGroup(view);

            // Stop accepting input the moment closing starts, not only once it's fully gone -
            // otherwise a second tap during the fade-out (e.g. mashing "Buy" again) could still
            // reach the window's buttons while it's visibly on its way out.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var rectTransform = view.transform as RectTransform;

            // Fades from wherever it currently is, not a hardcoded 1 - if this window was itself
            // interrupted mid-open (see WindowQueueRunner), it may still be mid-fade-in.
            return AnimateAsync(canvasGroup, rectTransform, canvasGroup.alpha, 0f, 1f, _fromScale, cancellationToken);
        }

        private async UniTask AnimateAsync(
            CanvasGroup canvasGroup,
            RectTransform rectTransform,
            float fromAlpha,
            float toAlpha,
            float fromScale,
            float toScale,
            CancellationToken cancellationToken)
        {
            var elapsed = 0f;

            // Yielding with `cancellationToken` here is what makes PlayOpenAsync actually throw
            // OperationCanceledException when the window is interrupted mid-animation (it runs on
            // the window's lifetime token) - WindowsManager relies on that to abort into its
            // close path. PlayCloseAsync is always called with CancellationToken.None by
            // WindowsManager, so for closing this loop simply can never be cancelled.
            while (elapsed < _durationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = EaseOutCubic(Mathf.Clamp01(elapsed / _durationSeconds));

                canvasGroup.alpha = Mathf.LerpUnclamped(fromAlpha, toAlpha, t);
                SetUniformScale(rectTransform, Mathf.LerpUnclamped(fromScale, toScale, t));

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            canvasGroup.alpha = toAlpha;
            SetUniformScale(rectTransform, toScale);
        }

        private static void SetUniformScale(RectTransform rectTransform, float scale)
        {
            if (rectTransform != null)
            {
                rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
        }

        private static float EaseOutCubic(float t)
        {
            var inverse = 1f - t;
            return 1f - inverse * inverse * inverse;
        }

        private static CanvasGroup GetOrAddCanvasGroup(WindowView view)
        {
            var canvasGroup = view.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = view.gameObject.AddComponent<CanvasGroup>();
            }

            return canvasGroup;
        }
    }
}
