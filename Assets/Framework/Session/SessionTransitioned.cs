using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// 유효한 상태 전환이 완료됐을 때만 발행되는 한 프레임짜리 이벤트.
    /// UI, 사운드, 분석 모듈은 이 이벤트를 읽어 ReasonCode를 각자의 방식으로 처리한다.
    /// </summary>
    public struct SessionTransitioned : IComponentData
    {
        public GamePhase Previous;
        public GamePhase Current;
        public byte RunId;
        public byte ReasonCode;
    }
}
