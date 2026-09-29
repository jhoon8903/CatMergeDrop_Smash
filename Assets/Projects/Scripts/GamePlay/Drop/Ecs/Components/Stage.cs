using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    public struct Stage : IComponentData
    {
        public Phase Phase;
        public int ActiveRow;
        public int GoldEarned;
        public int GoldTicketsEarned;
        public int ItemsEarned;
        public int RunId;
        public int BoardColumns;
        public int BoardRows;
        public int BrickColumns;
        public int BrickRows;
        public float GoalProgress;
    }
}
