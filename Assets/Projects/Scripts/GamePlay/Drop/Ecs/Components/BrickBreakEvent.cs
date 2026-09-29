using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Components
{
    public struct BrickBreakEvent : IComponentData
    {
        public Entity Stage;
        public int Row;
        public int Column; 
        public int Gold;
        public BrickKind Kind;
    }
}