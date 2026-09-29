using Framework.Poolable;
using Framework.Poolable.Components;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Creates isolated runtime drop state. It never mutates board snapshots.</summary>
    public static class DropStageFactory
    {
        private static readonly PoolId BrickPoolId = PoolId.FromName("drop-brick");
        private static readonly PoolId CatPoolId = PoolId.FromName("drop-cat");

        public static Entity Create(EntityManager manager, BrickConfig brickConfig,
            StageBalanceConfig stageBalanceConfig, int stageIndex)
            => Create(manager, brickConfig, stageBalanceConfig, stageIndex, null);

        public static Entity Create(EntityManager manager, BrickConfig brickConfig,
            StageBalanceConfig stageBalanceConfig, int stageIndex, StageDefinition overrideDefinition, int? catalogRevision = null)
        {
            if (brickConfig == null) throw new System.ArgumentNullException(nameof(brickConfig));
            if (stageBalanceConfig == null) throw new System.ArgumentNullException(nameof(stageBalanceConfig));
            Entity stage = manager.CreateEntity(typeof(Stage));
            manager.SetComponentData(stage, new Stage
            {
                Phase = Phase.Ready, 
                ActiveRow = 0
            });
            manager.AddBuffer<DropInitialBrick>(stage);
            ConfigureBricks(manager, stage, brickConfig, stageBalanceConfig, stageIndex, overrideDefinition, catalogRevision);
            return stage;
        }

        public static void ConfigureBricks(EntityManager manager, Entity stage,
            BrickConfig brickConfig, StageBalanceConfig stageBalanceConfig, int stageIndex)
            => ConfigureBricks(manager, stage, brickConfig, stageBalanceConfig, stageIndex, null);

        public static void ConfigureBricks(EntityManager manager, Entity stage,
            BrickConfig brickConfig, StageBalanceConfig stageBalanceConfig, int stageIndex,
            StageDefinition overrideDefinition, int? catalogRevision = null)
        {
            if (brickConfig == null) throw new System.ArgumentNullException(nameof(brickConfig));
            if (stageBalanceConfig == null) throw new System.ArgumentNullException(nameof(stageBalanceConfig));
            StageDefinition definition = overrideDefinition ?? stageBalanceConfig.GetStageDefinition(stageIndex);
            StageLayout layout = definition == null
                ? stageBalanceConfig.LayoutForStage(stageIndex)
                : StageBalanceConfig.LayoutFor(definition, catalogRevision);
            Stage state = manager.GetComponentData<Stage>(stage);
            state.BoardColumns = layout.BoardColumns;
            state.BoardRows = layout.BoardRows;
            state.BrickColumns = layout.BrickColumns;
            state.BrickRows = layout.BrickRows;
            state.Phase = Phase.Ready;
            state.ActiveRow = 0;
            state.GoldEarned = 0;
            state.GoldTicketsEarned = 0;
            state.ItemsEarned = 0;
            manager.SetComponentData(stage, state);
            DynamicBuffer<DropInitialBrick> blueprint = manager.GetBuffer<DropInitialBrick>(stage);
            blueprint.Clear();
            int lastOccupiedRow = -1;
            for (int row = 0; row < layout.BrickRows; row++)
            for (int column = 0; column < layout.BrickColumns; column++)
            {
                StageBrickCell cell = definition == null ? default : definition.GetCell(row, column, catalogRevision ?? definition.CatalogRevision);
                if (definition == null
                    ? !stageBalanceConfig.HasBrick(stageIndex, row, column)
                    : !cell.Present) continue;
                int level = definition == null ? 2 : cell.Level;
                if (level <= 0)
                    throw new System.InvalidOperationException(
                        $"Stage {stageIndex} Brick ({row}, {column}) needs a positive Level.");
                BrickKind kind = definition == null ? StageBrickPattern.GetKind(row, column) : cell.Kind;
                int health = brickConfig.HealthFor(level, kind);
                int gold = kind == BrickKind.Normal ? brickConfig.GoldForLevel(level) : 0;
                blueprint.Add(new DropInitialBrick { Row = row, Column = column, Level = level,
                    Health = health, GoldReward = gold, Kind = kind,
                    ChipSide = StageBrickPattern.ChipSideFor(stageIndex, row, column) });
                lastOccupiedRow = row;
            }

            state.GoalProgress = WorldPlayfieldLayout.GoalProgressForLastBrickRow(lastOccupiedRow);
            manager.SetComponentData(stage, state);

            DestroyCats(manager, stage);
            RestoreBricks(manager, stage);
        }

        public static bool RequestDrop(EntityManager manager, Entity stage, CatConfig catConfig, BoardCatSnapshot[] cats)
        {
            if (!manager.Exists(stage) || !manager.HasComponent<Stage>(stage) || catConfig == null || cats == null) return false;
            Stage state = manager.GetComponentData<Stage>(stage);
            if (state.Phase == Phase.Falling || cats.Length == 0) return false;
            int spawnCount = 0;
            for (int i = 0; i < cats.Length; i++)
                if (cats[i].Column >= 0 && cats[i].Column < state.BoardColumns &&
                    cats[i].Row >= 0 && cats[i].Row < state.BoardRows && cats[i].Level > 0) spawnCount++;
            if (spawnCount == 0) return false;

            DestroyCats(manager, stage);
            state.Phase = Phase.Falling;
            state.ActiveRow = 0;
            state.GoldEarned = 0;
            state.GoldTicketsEarned = 0;
            state.ItemsEarned = 0;
            state.RunId++;
            manager.SetComponentData(stage, state);

            for (int i = 0; i < cats.Length; i++)
            {
                BoardCatSnapshot snapshot = cats[i];
                if (snapshot.Column < 0 
                    || snapshot.Column >= state.BoardColumns 
                    || snapshot.Row < 0 
                    || snapshot.Row >= state.BoardRows 
                    || snapshot.Level <= 0) continue;
                int level = snapshot.Level;
                float initialY = (WorldPlayfieldLayout.DropStartY -
                    WorldPlayfieldLayout.BoardRowY(snapshot.Row, state.BoardRows)) /
                    WorldPlayfieldLayout.DropTravel;
                float bounceHeight = catConfig.BounceHeight;
                float bounceDuration = catConfig.EffectiveBounceDuration;
                float initialX = WorldPlayfieldLayout.ColumnX(snapshot.Column, state.BoardColumns);
                Entity cat = Rent<Cat>(manager, CatPoolId, stage);
                manager.SetComponentData(cat, new Cat
                {
                    Stage = stage, Slot = snapshot.Slot,
                    Column = snapshot.Column, Level = level,
                    BouncesRemaining = catConfig.BounceCountFor(level),
                    ImpactDamage = catConfig.ImpactDamageFor(level),
                    IsSpent = false,
                    X = initialX,
                    HorizontalVelocity = 0f,
                    Y = initialY,
                    PreviousY = initialY,
                    BounceOriginY = initialY,
                    BounceCeilingY = initialY - bounceHeight / WorldPlayfieldLayout.DropTravel,
                    BounceHeight = bounceHeight,
                    BounceDuration = bounceDuration,
                    BounceElapsed = bounceDuration,
                    IsBouncing = false,
                    Velocity = 0f,
                    FallSpeed = catConfig.FallSpeed,
                    FallAcceleration = catConfig.FallAcceleration
                });
            }
            return CountCats(manager, stage) > 0;
        }

        /// <summary>Returns active brick health by row-major board position; zero represents no live brick.</summary>
        public static int[] CaptureBrickHealth(EntityManager manager, Entity stage)
        {
            Stage state = GetStage(manager, stage);
            int[] health = new int[BrickHealthLength(state)];
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            using NativeArray<Entity> bricks = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Stage != stage || brick.Health <= 0) continue;
                int index = BrickHealthIndex(state, brick.Row, brick.Column);
                if (health[index] != 0)
                    throw new System.InvalidOperationException(
                        $"Stage {stage.Index} has multiple active bricks at ({brick.Row}, {brick.Column}).");
                health[index] = brick.Health;
            }
            query.Dispose();
            return health;
        }

        /// <summary>Returns sorted board slots for active, unspent cats belonging to the stage.</summary>
        public static int[] CaptureSurvivingCatSlots(EntityManager manager, Entity stage)
        {
            Stage state = GetStage(manager, stage);
            int slotCount;
            try { slotCount = checked(state.BoardColumns * state.BoardRows); }
            catch (System.OverflowException exception)
            {
                throw new System.InvalidOperationException("Stage board dimensions are too large.", exception);
            }

            if (slotCount <= 0) return System.Array.Empty<int>();

            bool[] occupiedSlots = new bool[slotCount];
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            using NativeArray<Entity> cats = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < cats.Length; i++)
            {
                Cat cat = manager.GetComponentData<Cat>(cats[i]);
                if (cat.Stage != stage || cat.IsSpent || cat.Slot < 0 || cat.Slot >= slotCount) continue;
                occupiedSlots[cat.Slot] = true;
            }
            query.Dispose();

            int survivorCount = 0;
            for (int slot = 0; slot < occupiedSlots.Length; slot++)
                if (occupiedSlots[slot]) survivorCount++;
            int[] survivors = new int[survivorCount];
            for (int slot = 0, index = 0; slot < occupiedSlots.Length; slot++)
                if (occupiedSlots[slot]) survivors[index++] = slot;
            return survivors;
        }

        /// <summary>Restores the blueprint and applies the supplied row-major brick health snapshot.</summary>
        public static void ApplyBrickHealth(EntityManager manager, Entity stage, int[] health)
        {
            Stage state = GetStage(manager, stage);
            if (health == null) throw new System.ArgumentNullException(nameof(health));
            int expectedLength = BrickHealthLength(state);
            if (health.Length != expectedLength)
                throw new System.ArgumentException(
                    $"Stage {stage.Index} needs {expectedLength} brick health values, received {health.Length}.",
                    nameof(health));

            int[] blueprintHealth = BlueprintHealth(manager, stage, state);
            for (int i = 0; i < health.Length; i++)
            {
                if (blueprintHealth[i] == 0)
                {
                    if (health[i] != 0)
                        throw new System.ArgumentOutOfRangeException(nameof(health),
                            $"Brick health at row {i / state.BrickColumns}, column {i % state.BrickColumns} has no blueprint brick.");
                    continue;
                }

                if (health[i] < 0 || health[i] > blueprintHealth[i])
                    throw new System.ArgumentOutOfRangeException(nameof(health),
                        $"Brick health at row {i / state.BrickColumns}, column {i % state.BrickColumns} must be between 0 and {blueprintHealth[i]}.");
            }

            RestoreBricks(manager, stage);
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            using NativeArray<Entity> bricks = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Stage != stage) continue;
                int value = health[BrickHealthIndex(state, brick.Row, brick.Column)];
                if (value == 0)
                {
                    ReturnOrDestroy(manager, bricks[i]);
                    continue;
                }

                brick.Health = value;
                brick.ChipStage = BrickChipProfile.StageForHealth(brick.Health, brick.MaxHealth);
                manager.SetComponentData(bricks[i], brick);
            }
            query.Dispose();
        }

        /// <summary>Returns the stage to its initial brick layout without changing its run identity.</summary>
        public static void RestartStage(EntityManager manager, Entity stage)
        {
            Stage state = GetStage(manager, stage);
            DestroyCats(manager, stage);
            RestoreBricks(manager, stage);
            state.Phase = Phase.Ready;
            state.ActiveRow = 0;
            state.GoldEarned = 0;
            state.GoldTicketsEarned = 0;
            state.ItemsEarned = 0;
            manager.SetComponentData(stage, state);
        }

        public static void Destroy(EntityManager manager, Entity stage)
        {
            if (!manager.Exists(stage)) return;
            DestroyForStage<Cat>(manager, stage);
            DestroyForStage<Brick>(manager, stage);
            DestroyForStage<BrickBreakEvent>(manager, stage);
            DestroyForStage<BrickImpactEvent>(manager, stage);
            manager.DestroyEntity(stage);
        }

        internal static void RestoreBricks(EntityManager manager, Entity stage)
        {
            DestroyForStage<Brick>(manager, stage);
            using NativeArray<DropInitialBrick> blueprint = manager.GetBuffer<DropInitialBrick>(stage)
                .ToNativeArray(Allocator.Temp);
            for (int i = 0; i < blueprint.Length; i++)
            {
                DropInitialBrick initial = blueprint[i];
                Entity brick = Rent<Brick>(manager, BrickPoolId, stage);
                manager.SetComponentData(brick, new Brick
                {
                    Stage = stage, Row = initial.Row, Column = initial.Column, Level = initial.Level,
                    Health = initial.Health,
                    MaxHealth = initial.Health, GoldReward = initial.GoldReward, Kind = initial.Kind,
                    ChipStage = 0, ChipSide = initial.ChipSide
                });
            }
        }

        internal static void DestroyCats(EntityManager manager, Entity stage) => DestroyForStage<Cat>(manager, stage);

        internal static int CountCats(EntityManager manager, Entity stage)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < entities.Length; i++) if (manager.GetComponentData<Cat>(entities[i]).Stage == stage) count++;
            entities.Dispose();
            return count;
        }

        internal static void DestroyForStage<T>(EntityManager manager, Entity stage) where T : unmanaged, IComponentData
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                if (manager.GetComponentData<T>(entities[i]) is Cat cat && cat.Stage == stage ||
                    manager.GetComponentData<T>(entities[i]) is Brick brick && brick.Stage == stage ||
                    manager.GetComponentData<T>(entities[i]) is BrickBreakEvent evt && evt.Stage == stage ||
                    manager.GetComponentData<T>(entities[i]) is BrickImpactEvent impact && impact.Stage == stage)
                    ReturnOrDestroy(manager, entities[i]);
            entities.Dispose();
        }

        private static Entity Rent<T>(EntityManager manager, PoolId id, Entity stage)
            where T : unmanaged, IComponentData
        {
            if (!TryGetConfiguredPool(manager, id, out Entity poolEntity))
                return manager.CreateEntity(typeof(T));

            using (EntityPoolAccess access = EntityPoolAccess.Open(manager, poolEntity))
            {
                Entity entity = access.Rent(manager, id, LocalTransform.Identity, stage);
                if (entity == Entity.Null)
                    throw new System.InvalidOperationException(
                        $"Drop entity pool '{id}' is exhausted while creating stage {stage.Index}.");
                return entity;
            }
        }

        internal static void ReturnOrDestroy(EntityManager manager, Entity entity)
        {
            if (!manager.HasComponent<PooledEntity>(entity) ||
                !TryGetConfiguredPool(manager, manager.GetComponentData<PooledEntity>(entity).Id,
                    out Entity poolEntity))
            {
                manager.DestroyEntity(entity);
                return;
            }

            using (EntityPoolAccess access = EntityPoolAccess.Open(manager, poolEntity))
                access.Return(manager, entity);
        }

        private static bool TryGetConfiguredPool(EntityManager manager, PoolId expectedId, out Entity poolEntity)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<EntityPool>());
            if (query.CalculateEntityCount() != 1)
            {
                query.Dispose();
                poolEntity = Entity.Null;
                return false;
            }

            poolEntity = query.GetSingletonEntity();
            query.Dispose();
            EntityPool pool = manager.GetComponentData<EntityPool>(poolEntity);
            if (!pool.IsCreated || pool.PoolCount == 0) return false;
            if (!pool.TryGetStats(expectedId, out _, out _, out _))
                throw new System.InvalidOperationException(
                    $"Configured EntityPool is missing required drop pool '{expectedId}'.");
            return true;
        }

        private static Stage GetStage(EntityManager manager, Entity stage)
        {
            if (!manager.Exists(stage) || !manager.HasComponent<Stage>(stage))
                throw new System.ArgumentException("Stage entity does not exist or has no Stage component.", nameof(stage));
            return manager.GetComponentData<Stage>(stage);
        }

        private static int BrickHealthLength(in Stage state)
        {
            if (state.BrickColumns < 0 || state.BrickRows < 0)
                throw new System.InvalidOperationException("Stage brick dimensions cannot be negative.");
            try { return checked(state.BrickColumns * state.BrickRows); }
            catch (System.OverflowException exception)
            {
                throw new System.InvalidOperationException("Stage brick dimensions are too large.", exception);
            }
        }

        private static int BrickHealthIndex(in Stage state, int row, int column)
        {
            if (row < 0 || row >= state.BrickRows || column < 0 || column >= state.BrickColumns)
                throw new System.InvalidOperationException($"Brick ({row}, {column}) is outside the stage layout.");
            return row * state.BrickColumns + column;
        }

        private static int[] BlueprintHealth(EntityManager manager, Entity stage, in Stage state)
        {
            int[] health = new int[BrickHealthLength(state)];
            DynamicBuffer<DropInitialBrick> blueprint = manager.GetBuffer<DropInitialBrick>(stage);
            for (int i = 0; i < blueprint.Length; i++)
            {
                DropInitialBrick initial = blueprint[i];
                int index = BrickHealthIndex(state, initial.Row, initial.Column);
                if (initial.Health <= 0)
                    throw new System.InvalidOperationException(
                        $"Blueprint brick ({initial.Row}, {initial.Column}) must have positive health.");
                if (health[index] != 0)
                    throw new System.InvalidOperationException(
                        $"Blueprint contains multiple bricks at ({initial.Row}, {initial.Column}).");
                health[index] = initial.Health;
            }
            return health;
        }
    }
}
