using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;

namespace Projects.Scripts.GamePlay.Drop.Ecs.Systems
{
    [UpdateInGroup(typeof(SimulationSystemGroup)), UpdateAfter(typeof(FallSystem))]
    public partial struct GateCollisionSystem : ISystem
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
            EntityQuery catQuery = manager.CreateEntityQuery(ComponentType.ReadWrite<Cat>());
            NativeArray<Entity> cats = catQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < cats.Length; i++)
            {
                Cat cat = manager.GetComponentData<Cat>(cats[i]);
                if (!manager.Exists(cat.Stage) || cat.Velocity <= 0f) continue;
                Stage stage = manager.GetComponentData<Stage>(cat.Stage);
                if (stage.Phase != Phase.Falling) continue;

                int column = System.Math.Max(0, System.Math.Min(cat.Column, stage.BrickColumns - 1));
                cat.Column = column;
                cat.X = WorldPlayfieldLayout.ColumnX(column, stage.BoardColumns);
                cat.HorizontalVelocity = 0f;
                // Cats stay in their launch column, so only that column can rebound them.
                for (int row = 0; row < stage.BrickRows; row++)
                {
                    if (cat.Y < WorldPlayfieldLayout.TileContactProgress(row)) break;
                    Entity brickEntity = FindActiveBrick(manager, cat.Stage, row, column);
                    if (brickEntity == Entity.Null) continue;

                    Brick brick = manager.GetComponentData<Brick>(brickEntity);
                    float contactY = WorldPlayfieldLayout.TileContactProgress(row);
                    if (cat.Y < contactY) break;
                    if (cat.PreviousY > contactY + .001f) continue;
                    brick.Health -= cat.ImpactDamage;
                    brick.ChipStage = BrickChipProfile.StageForHealth(brick.Health, brick.MaxHealth);
                    cat.ImpactSerial++;
                    bool canBounce = cat.BouncesRemaining > 0;
                    if (canBounce) cat.BouncesRemaining--;
                    if (brick.Health > 0)
                    {
                        // Place the sprite above the tile before rebounding, so it does
                        // not appear to pass through the block during the hit effect.
                        cat.Y = contactY - 0.001f;
                        if (canBounce)
                        {
                            cat.BounceOriginY = cat.Y;
                            cat.BounceCeilingY = cat.BounceOriginY - cat.BounceHeight / WorldPlayfieldLayout.DropTravel;
                            cat.BounceElapsed = 0f;
                            cat.BounceOriginRow = row;
                            cat.IsBouncing = true;
                            // The first FallSystem update evaluates the upward portion of the curve.
                            cat.Velocity = -4f * (cat.BounceHeight / WorldPlayfieldLayout.DropTravel) /
                                cat.BounceDuration;
                        }
                        else
                        {
                            cat.IsSpent = true;
                            cat.IsBouncing = false;
                            cat.Velocity = 0f;
                        }
                    }
                    else
                    {
                        // Breaking the tile keeps the cat's downward momentum.
                        cat.IsBouncing = false;
                        cat.BounceElapsed = cat.BounceDuration;
                    }
                    manager.SetComponentData(brickEntity, brick);
                    manager.SetComponentData(cats[i], cat);
                    Entity impact = manager.CreateEntity(typeof(BrickImpactEvent));
                    manager.SetComponentData(impact, new BrickImpactEvent
                    {
                        Stage = cat.Stage, Row = row, Column = column,
                        RemainingHealth = brick.Health, MaxHealth = brick.MaxHealth,
                        Gold = brick.Health <= 0 ? brick.GoldReward : 0,
                        Kind = brick.Kind
                    });
                    // A delayed frame can carry a strong cat through several rows.
                    // Keep checking crossed rows after a lethal hit; otherwise its
                    // next frame starts below the untouched bricks and skips them.
                    if (brick.Health > 0) break;
                }
            }
            cats.Dispose();
        }

        private static Entity FindActiveBrick(EntityManager manager, Entity stage, int row, int column)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            NativeArray<Entity> bricks = query.ToEntityArray(Allocator.Temp);
            Entity result = Entity.Null;
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Stage != stage || brick.Row != row || brick.Column != column || brick.Health <= 0) continue;
                result = bricks[i]; break;
            }
            bricks.Dispose();
            return result;
        }

        internal static float RowY(int row) => WorldPlayfieldLayout.TileProgress(row);
    }
}
