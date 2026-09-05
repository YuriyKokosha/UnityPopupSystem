using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime;
using UnityEngine;

namespace PopupSystem.UI.Transitions
{
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
            // Load-bearing because views are pooled: PlayCloseAsync turned both off, and a reopened window
            // would otherwise render but ignore every click.
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            SetUniformScale(rectTransform, _fromScale);

            return AnimateAsync(canvasGroup, rectTransform, 0f, 1f, _fromScale, 1f, cancellationToken);
        }

        public UniTask PlayCloseAsync(WindowView view, CancellationToken cancellationToken)
        {
            var canvasGroup = GetOrAddCanvasGroup(view);

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var rectTransform = view.transform as RectTransform;

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
