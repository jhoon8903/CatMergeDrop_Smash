using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// GameSessionSystem이 다음 업데이트에 처리하는 단발성 상태 전환 요청.
    /// ReasonCode는 UI 문구가 아닌 게임 도메인의 불투명한 식별자다.
    /// 게임별 enum을 byte로 변환해 넣고, 0은 원인 없음으로 예약한다.
    /// </summary>
    public struct SessionTransitionRequest : IComponentData
    {
        public SessionTransition Transition;
        public byte ReasonCode;
    }
}
