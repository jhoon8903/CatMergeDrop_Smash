namespace Framework.UI.Animation
{
    /// <summary>
    /// 기본 Open/Close 연출 모양. Pop 효과는 Scale 타입과 overshoot Curve 조합으로 만든다.
    /// </summary>
    public enum UIAnimationType : byte
    {
        None,
        Fade,
        Scale,
        Slide,
        FadeScale,
        FadeSlide
    }
}
