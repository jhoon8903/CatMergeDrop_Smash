using UnityEngine;

using UnityEngine.Scripting.APIUpdating;

namespace Framework.UI.Animation
{
    /// <summary>
    /// View Inspector에 할당하는 Open/Close 연출 설정.
    /// Exit는 기본적으로 이 프로필을 시간 역순으로 재생한다.
    /// </summary>
    [MovedFrom(true, "Framework.UI.Transition", null, "UITransitionProfile")]
    public sealed class UIAnimationProfile : ScriptableObject
    {
        [SerializeField] private UIAnimationType type = UIAnimationType.Fade;
        [SerializeField, Min(0f)] private float duration = 0.2f;
        [SerializeField, Min(0f)] private float delay;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField, Range(0f, 1f)] private float hiddenAlpha;
        [SerializeField, Min(0.01f)] private float hiddenScale = 0.9f;
        [SerializeField] private Vector2 hiddenOffset = new(0f, -80f);

        public UIAnimationType Type => type;
        public float Duration => duration;
        public float Delay => delay;
        public bool UseUnscaledTime => useUnscaledTime;
        public AnimationCurve Curve => curve;
        public float HiddenAlpha => hiddenAlpha;
        public float HiddenScale => hiddenScale;
        public Vector2 HiddenOffset => hiddenOffset;
    }
}
