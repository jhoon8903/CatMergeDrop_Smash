using Framework.Session;

namespace Framework.Clock
{
    public interface IGameClock
    {
        GamePhase Phase { get; }
        float ElapsedTime { get; }
    }
}