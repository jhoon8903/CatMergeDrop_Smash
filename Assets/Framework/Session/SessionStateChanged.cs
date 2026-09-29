using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// 가장 최근의 세션 상태 전환 기록.
    /// UI나 게임 시스템은 변경 필터로 이 컴포넌트를 관찰할 수 있다.
    /// </summary>
    public struct SessionStateChanged : IComponentData
    {
        public GamePhase Previous;
        public GamePhase Current;
        public uint RunId;
    }
}
