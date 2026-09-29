using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup)), UpdateAfter(typeof(DamageAndBreakSystem))]
    public partial struct DropRowAdvanceSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            
        }

        public void OnDestroy(ref SystemState state)
        {
            
        }
        
        public void OnUpdate(ref SystemState state)
        {
            EntityManager manager = state.EntityManager;
            foreach ((RefRW<Stage> stageRef, Entity stageEntity) in SystemAPI.Query<RefRW<Stage>>().WithEntityAccess())
            {
                Stage stage = stageRef.ValueRO;
                if (stage.Phase != Phase.Falling 
                    || stage.ActiveRow >= stage.BrickRows
                    || HasBrickInRow(manager, stageEntity, stage.ActiveRow)) continue;
                stage.ActiveRow++;
                stageRef.ValueRW = stage;
            }
        }

        private static bool HasBrickInRow(EntityManager manager, Entity stage, int row)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            NativeArray<Entity> bricks = query.ToEntityArray(Allocator.Temp);
            bool found = false;
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Stage != stage || brick.Row != row) continue;
                found = true;
                break;
            }
            bricks.Dispose();
            return found;
        }
    }
}
