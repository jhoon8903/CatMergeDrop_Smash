namespace Framework.Session
{
    /// <summary>
    /// GameSession에 요청할 수 있는 범용 상태 전환.
    /// </summary>
    public enum SessionTransition : byte
    {
        Start,
        Pause,
        Resume,
        End,
        Reset
    }
}
