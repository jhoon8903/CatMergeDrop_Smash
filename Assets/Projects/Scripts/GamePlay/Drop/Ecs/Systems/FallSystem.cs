using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct FallSystem : ISystem
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
            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (var (stage, stageEntity) in SystemAPI.Query<RefRO<Stage>>().WithEntityAccess())
            {
                if (stage.ValueRO.Phase != Phase.Falling) continue;
                EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadWrite<Cat>());
                NativeArray<Entity> cats = query.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < cats.Length; i++)
                {
                    Cat cat = manager.GetComponentData<Cat>(cats[i]);
                    if (cat.Stage != stageEntity) continue;
                    cat.PreviousY = cat.Y;
                    if (cat.IsBouncing)
                    {
                        cat.BounceElapsed = Mathf.Min(cat.BounceElapsed + deltaTime, cat.BounceDuration);
                        float normalizedTime = cat.BounceElapsed / cat.BounceDuration;
                        float height = cat.BounceHeight / WorldPlayfieldLayout.DropTravel;
                        cat.Y = cat.BounceOriginY - height * 4f * normalizedTime * (1f - normalizedTime);
                        cat.Velocity = height * 4f * (2f * normalizedTime - 1f) / cat.BounceDuration;
                        if (normalizedTime >= 1f) cat.IsBouncing = false;
                    }
                    else
                    {
                        float nextVelocity = Mathf.Min(cat.FallSpeed, cat.Velocity + cat.FallAcceleration * deltaTime);
                        cat.Y += (cat.Velocity + nextVelocity) * .5f * deltaTime;
                        cat.Velocity = nextVelocity;
                    }
                    cat.Column = Mathf.Clamp(cat.Column, 0, stage.ValueRO.BoardColumns - 1);
                    cat.X = WorldPlayfieldLayout.ColumnX(cat.Column, stage.ValueRO.BoardColumns);
                    cat.HorizontalVelocity = 0f;
                    manager.SetComponentData(cats[i], cat);
                }
                cats.Dispose();
            }
        }
    }
}
