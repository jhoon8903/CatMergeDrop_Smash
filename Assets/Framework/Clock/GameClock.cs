using Framework.Session;

namespace Framework.Clock
{
    public class GameClock : IGameClock
    {
        public GameClock(GamePhase phase, float elapsedTime)
        {
            Phase = phase;
            ElapsedTime = elapsedTime;
        }

        public GamePhase Phase { get; }
        public float ElapsedTime { get; }
    }
}