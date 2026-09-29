using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup)), UpdateAfter(typeof(GateCollisionSystem))]
    public partial struct DamageAndBreakSystem : ISystem
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
            EntityQuery catQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            NativeArray<Entity> cats = catQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < cats.Length; i++)
                if (manager.GetComponentData<Cat>(cats[i]).IsSpent)
                    DropStageFactory.ReturnOrDestroy(manager, cats[i]);
            cats.Dispose();

            EntityQuery brickQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            NativeArray<Entity> bricks = brickQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Health > 0) continue;
                Stage stage = manager.GetComponentData<Stage>(brick.Stage);
                if (brick.Kind == BrickKind.Normal)
                    stage.GoldEarned = (int)System.Math.Min(int.MaxValue,
                        (long)stage.GoldEarned + brick.GoldReward);
                else if (brick.Kind == BrickKind.Gold)
                    stage.GoldTicketsEarned = (int)System.Math.Min(int.MaxValue, (long)stage.GoldTicketsEarned + 1);
                else if (brick.Kind == BrickKind.Item)
                    stage.ItemsEarned = (int)System.Math.Min(int.MaxValue, (long)stage.ItemsEarned + 1);
                manager.SetComponentData(brick.Stage, stage);
                Entity evt = manager.CreateEntity(typeof(BrickBreakEvent));
                manager.SetComponentData(evt, new BrickBreakEvent { Stage = brick.Stage, Row = brick.Row, Column = brick.Column, Gold = brick.GoldReward, Kind = brick.Kind });
                DropStageFactory.ReturnOrDestroy(manager, bricks[i]);
            }
            bricks.Dispose();
        }
    }
}
