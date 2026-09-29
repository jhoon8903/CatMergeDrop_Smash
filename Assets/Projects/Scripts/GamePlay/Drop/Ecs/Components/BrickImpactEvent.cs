using Unity.Entities;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    public struct BrickImpactEvent : IComponentData
    {
        public Entity Stage;
        public int Row;
        public int Column;
        public int RemainingHealth;
        public int MaxHealth;
        public int Gold;
        public BrickKind Kind;
    }
}
