using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Framework.UI.Animation
{
    /// <summary>
    /// UIAnimationProfile을 CanvasGroup과 RectTransform에 적용한다.
    /// 하나의 Profile을 Open 정방향 / Close 역방향으로 사용한다.
    /// </summary>
    [DisallowMultipleComponent]
    [MovedFrom(true, "Framework.UI.Transition", null, "UITransitionPlayer")]
    public sealed class UIAnimationPlayer : MonoBehaviour, IUIAnimation
    {
        [SerializeField] private UIAnimationProfile profile;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform target;

        private Vector2 _visiblePosition;
        private Vector3 _visibleScale;
        private bool _cached;

        private void Awake() => CacheTargets();

        public void ApplyImmediate(bool visible)
        {
            CacheTargets();
            Apply(visible ? 1f : 0f);
        }

        public void SetInputEnabled(bool enabled)
        {
            if (canvasGroup == null) return;
            canvasGroup.interactable = enabled;
            canvasGroup.blocksRaycasts = enabled;
        }

        public async UniTask PlayAsync(bool opening, CancellationToken cancellationToken)
        {
            CacheTargets();
            if (profile == null || profile.Type == UIAnimationType.None || profile.Duration <= 0f)
            {
                Apply(opening ? 1f : 0f);
                return;
            }

            if (profile.Delay > 0f) await UniTask.Delay(System.TimeSpan.FromSeconds(profile.Delay), ignoreTimeScale: profile.UseUnscaledTime, cancellationToken: cancellationToken);

            float elapsed = 0f;
            while (elapsed < profile.Duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += profile.UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / profile.Duration);
                float progress = opening ? profile.Curve.Evaluate(normalized) : profile.Curve.Evaluate(1f - normalized);
                Apply(progress);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            Apply(opening ? 1f : 0f);
        }

        private void CacheTargets()
        {
            if (_cached) return;
            if (target == null) target = transform as RectTransform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (target != null)
            {
                _visiblePosition = target.anchoredPosition;
                _visibleScale = target.localScale;
            }
            _cached = true;
        }

        private void Apply(float progress)
        {
            if (profile == null || profile.Type == UIAnimationType.None) return;
            bool fade = profile.Type is UIAnimationType.Fade or UIAnimationType.FadeScale or UIAnimationType.FadeSlide;
            bool scale = profile.Type is UIAnimationType.Scale or UIAnimationType.FadeScale;
            bool slide = profile.Type is UIAnimationType.Slide or UIAnimationType.FadeSlide;
            if (fade && canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(profile.HiddenAlpha, 1f, progress);
            if (scale && target != null) target.localScale = _visibleScale * Mathf.Lerp(profile.HiddenScale, 1f, progress);
            if (slide && target != null) target.anchoredPosition = _visiblePosition + Vector2.Lerp(profile.HiddenOffset, Vector2.zero, progress);
        }
    }
}
