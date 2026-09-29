using Framework.Clock;
using Unity.Entities;

namespace Framework.Session
{
    public struct GameSession : IComponentData, IGameClock
    {
        public GameSession(GamePhase phase, float elapsedTime, byte runId = 0)
        {
            Phase = phase;
            ElapsedTime = elapsedTime;
            RunId = runId;
        }

        /// <summary>
        /// 세션 수명주기 상태.
        /// 승패 같은 게임별 결과는 포함하지 않는다.
        /// </summary>
        public GamePhase Phase;

        /// <summary>
        /// Running 상태에서만 누적되는 플레이 시간(초).
        /// </summary>
        public float ElapsedTime;

        /// <summary>
        /// Start 전환마다 증가하는 플레이 회차 식별자.
        /// </summary>
        public byte RunId;

        GamePhase IGameClock.Phase => Phase;
        float IGameClock.ElapsedTime => ElapsedTime;
    }
}
