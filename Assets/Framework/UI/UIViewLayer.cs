namespace Framework.UI
{
    /// <summary>뷰의 중첩과 동시 표시 규칙을 결정하는 레이어.</summary>
    public enum UIViewLayer : byte
    {
        Screen, // 레이어에서 하나만 열림 
        Popup, // Stack 
        Overlay // 로딩, 토스트 등 (최상단)
    }
}
