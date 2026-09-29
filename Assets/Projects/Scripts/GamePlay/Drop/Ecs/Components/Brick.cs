using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    public struct Brick : IComponentData
    {
        public Entity Stage;
        public int Row;
        public int Column;
        public int Level;
        public int Health;
        public int MaxHealth;
        public int GoldReward;
        public BrickKind Kind;
        public int ChipStage;
        public int ChipSide;
    }
}
