using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    internal struct DropInitialBrick : IBufferElementData
    {
        public int Row;
        public int Column;
        public int Level;
        public int Health;
        public int GoldReward;
        public BrickKind Kind;
        public int ChipSide;
    }
}
