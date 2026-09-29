using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Unity.Collections;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(FallSystem))]
    public partial struct BreakEventCleanupSystem : ISystem
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
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<BrickBreakEvent>());
            NativeArray<Entity> events = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < events.Length; i++) manager.DestroyEntity(events[i]);
            events.Dispose();
            EntityQuery impactQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<BrickImpactEvent>());
            NativeArray<Entity> impacts = impactQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < impacts.Length; i++) manager.DestroyEntity(impacts[i]);
            impacts.Dispose();
        }
    }
}
