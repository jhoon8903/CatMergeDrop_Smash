using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup)), UpdateAfter(typeof(DropRowAdvanceSystem))]
    public partial struct DropRunResultSystem : ISystem
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
            EntityQuery stageQuery = manager.CreateEntityQuery(ComponentType.ReadWrite<Stage>());
            NativeArray<Entity> stages = stageQuery.ToEntityArray(Allocator.Temp);
            for (int stageIndex = 0; stageIndex < stages.Length; stageIndex++)
            {
                Entity stageEntity = stages[stageIndex];
                Stage stage = manager.GetComponentData<Stage>(stageEntity);
                if (stage.Phase != Phase.Falling) continue;
                bool reachedGoal = false;
                EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
                NativeArray<Entity> cats = query.ToEntityArray(Allocator.Temp);
                int catCount = 0;
                for (int i = 0; i < cats.Length; i++)
                {
                    Cat cat = manager.GetComponentData<Cat>(cats[i]);
                    if (cat.Stage != stageEntity) continue;
                    if (cat.Y >= stage.GoalProgress)
                    {
                        reachedGoal = true;
                        break;
                    }
                    catCount++;
                }
                cats.Dispose();
                if (reachedGoal) stage.Phase = Phase.Won;
                else if (catCount == 0)
                {
                    stage.Phase = Phase.Lost;
                    stage.ActiveRow = 0;
                }
                manager.SetComponentData(stageEntity, stage);
            }
            stages.Dispose();
        }
    }
}
