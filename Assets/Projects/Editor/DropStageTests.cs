using NUnit.Framework;
using Framework.Poolable.Components;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Drop.Ecs.Systems;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageTests
    {
        private World _world;
        private EntityManager _manager;
        private CatConfig _catConfig;
        private BrickConfig _brickConfig;
        private StageBalanceConfig _stageBalanceConfig;

        [SetUp]
        public void SetUp()
        {
            _world = new World("StageTests");
            _manager = _world.EntityManager;
            _catConfig = ScriptableObject.CreateInstance<CatConfig>();
            _brickConfig = ScriptableObject.CreateInstance<BrickConfig>();
            _stageBalanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_catConfig);
            Object.DestroyImmediate(_brickConfig);
            Object.DestroyImmediate(_stageBalanceConfig);
        }

        [Test]
        public void StageFactory_UsesConfiguredPoolAndReturnsStageEntities()
        {
            Entity brickPrefab = _manager.CreateEntity(typeof(Brick));
            Entity catPrefab = _manager.CreateEntity(typeof(Cat));
            _manager.AddComponent<Prefab>(brickPrefab);
            _manager.AddComponent<Prefab>(catPrefab);
            Entity poolEntity = _manager.CreateEntity();
            EntityPool pool = new EntityPool(2, Allocator.Persistent);
            _manager.AddComponentData(poolEntity, pool);
            try
            {
                PoolId brickId = PoolId.FromName("drop-brick");
                PoolId catId = PoolId.FromName("drop-cat");
                pool.Register(_manager, new PoolDefinition
                {
                    Id = brickId,
                    Prefab = brickPrefab,
                    InitialCapacity = WorldPlayfieldLayout.BrickCount,
                    MaxCapacity = WorldPlayfieldLayout.BrickCount
                });
                pool.Register(_manager, new PoolDefinition
                {
                    Id = catId,
                    Prefab = catPrefab,
                    InitialCapacity = 1,
                    MaxCapacity = 1
                });
                pool.Prewarm(_manager, brickId);
                pool.Prewarm(_manager, catId);
                _manager.SetComponentData(poolEntity, pool);

                Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
                Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                    new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
                NativeArray<Entity> spawnedCats = Query<Cat>();
                Entity firstCat = spawnedCats[0];
                spawnedCats.Dispose();
                Assert.That(_manager.HasComponent<PooledEntity>(firstCat), Is.True);
                Assert.That(_manager.IsEnabled(firstCat), Is.True);

                Cat staleCat = _manager.GetComponentData<Cat>(firstCat);
                staleCat.IsSpent = true;
                staleCat.ImpactSerial = 99;
                _manager.SetComponentData(firstCat, staleCat);
                Stage state = _manager.GetComponentData<Stage>(stage);
                state.Phase = Phase.Ready;
                _manager.SetComponentData(stage, state);

                Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                    new[] { new BoardCatSnapshot(0, 0, 0, 2) }), Is.True);
                spawnedCats = Query<Cat>();
                Assert.That(spawnedCats[0], Is.EqualTo(firstCat));
                Cat resetCat = _manager.GetComponentData<Cat>(spawnedCats[0]);
                Assert.That((resetCat.IsSpent, resetCat.ImpactSerial, resetCat.Level), Is.EqualTo((false, 0, 2)));
                spawnedCats.Dispose();

                DropStageFactory.Destroy(_manager, stage);

                NativeArray<Entity> activeCats = Query<Cat>();
                NativeArray<Entity> activeBricks = Query<Brick>();
                Assert.That(activeCats.Length, Is.Zero);
                Assert.That(activeBricks.Length, Is.Zero);
                activeCats.Dispose();
                activeBricks.Dispose();

                pool = _manager.GetComponentData<EntityPool>(poolEntity);
                Assert.That(pool.TryGetStats(catId, out int freeCats, out int activeCatsCount, out _), Is.True);
                Assert.That((freeCats, activeCatsCount), Is.EqualTo((1, 0)));
                Assert.That(pool.TryGetStats(brickId, out int freeBricks, out int activeBricksCount, out _), Is.True);
                Assert.That((freeBricks, activeBricksCount), Is.EqualTo((WorldPlayfieldLayout.BrickCount, 0)));
            }
            finally
            {
                if (_manager.Exists(poolEntity))
                {
                    pool = _manager.GetComponentData<EntityPool>(poolEntity);
                    pool.Dispose();
                }
            }
        }

        [Test]
        public void RequestDrop_SpawnsEverySnapshotWithoutChangingIt()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            var snapshots = new[]
            {
                new BoardCatSnapshot(0, 0, 0, 1),
                new BoardCatSnapshot(7, 3, 2, 3)
            };

            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, snapshots), Is.True);

            NativeArray<Entity> cats = Query<Cat>();
            Assert.That(cats.Length, Is.EqualTo(2));
            Assert.That(WorldPlayfieldLayout.DropY(_manager.GetComponentData<Cat>(cats[0]).Y),
                Is.EqualTo(WorldPlayfieldLayout.BoardRowY(0)).Within(.0001f));
            Assert.That(WorldPlayfieldLayout.DropY(_manager.GetComponentData<Cat>(cats[1]).Y),
                Is.EqualTo(WorldPlayfieldLayout.BoardRowY(2)).Within(.0001f));
            Assert.That(snapshots[0].Level, Is.EqualTo(1));
            Assert.That(snapshots[1].Slot, Is.EqualTo(7));
            cats.Dispose();
        }

        [Test]
        public void RequestDrop_UsesConfiguredDamageAndBounceCountForEachLevel()
        {
            typeof(CatConfig).GetField("combatStatsByLevel",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_catConfig, new[]
                {
                    new CatLevelCombatStats { impactDamage = 4, bounceCount = 1 },
                    new CatLevelCombatStats { impactDamage = 7, bounceCount = 3 }
                });
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[]
            {
                new BoardCatSnapshot(0, 0, 0, 1),
                new BoardCatSnapshot(1, 1, 0, 2)
            }), Is.True);

            NativeArray<Entity> cats = Query<Cat>();
            Cat first = _manager.GetComponentData<Cat>(cats[0]);
            Cat second = _manager.GetComponentData<Cat>(cats[1]);
            Assert.That((first.ImpactDamage, first.BouncesRemaining), Is.EqualTo((4, 1)));
            Assert.That((second.ImpactDamage, second.BouncesRemaining), Is.EqualTo((7, 3)));
            cats.Dispose();
        }

        [Test]
        public void AuthoredCatConfig_HasCombatStatsForAllSpriteLevels()
        {
            CatConfig authored = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            Assert.That(authored, Is.Not.Null);
            SerializedObject serialized = new SerializedObject(authored);
            SerializedProperty stats = serialized.FindProperty("combatStatsByLevel");
            Assert.That(stats.arraySize, Is.EqualTo(24));
            for (int level = 1; level <= stats.arraySize; level++)
            {
                Assert.That(authored.ImpactDamageFor(level), Is.GreaterThan(0), $"Lv{level} damage");
                Assert.That(authored.BounceCountFor(level), Is.GreaterThanOrEqualTo(0), $"Lv{level} bounces");
            }
        }

        [Test]
        public void StageLayouts_KeepBrickColumnsAlignedWithBoard()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 2);
            Stage second = _manager.GetComponentData<Stage>(stage);
            Assert.That((second.BoardColumns, second.BoardRows, second.BrickColumns, second.BrickRows),
                Is.EqualTo((4, 3, 4, 10)));
            NativeArray<Entity> stageTwoBricks = Query<Brick>();
            Assert.That(stageTwoBricks.Length, Is.EqualTo(40));
            stageTwoBricks.Dispose();

            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(2, 2, 0, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            Assert.That(cat.X, Is.EqualTo(WorldPlayfieldLayout.ColumnX(2, 4)).Within(.0001f));
            Assert.That(cat.Column, Is.EqualTo(2));
            cats.Dispose();

            DropStageFactory.ConfigureBricks(_manager, stage, _brickConfig, _stageBalanceConfig, 3);
            Stage third = _manager.GetComponentData<Stage>(stage);
            Assert.That((third.BoardColumns, third.BoardRows, third.BrickColumns, third.BrickRows),
                Is.EqualTo((4, 3, 4, 10)));
            NativeArray<Entity> stageThreeBricks = Query<Brick>();
            Assert.That(stageThreeBricks.Length, Is.EqualTo(40));
            stageThreeBricks.Dispose();
        }

        [Test]
        public void SixColumnStage_AcceptsCatAndBricksInLastColumn()
        {
            StageDefinition definition = ScriptableObject.CreateInstance<StageDefinition>();
            var cells = new StageBrickCell[6 * 10];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 1 };
            definition.Configure(27, 6, 10, cells);
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 27, definition);
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That((state.BoardColumns, state.BoardRows, state.BrickColumns, state.BrickRows),
                Is.EqualTo((6, 3, 6, 10)));
            Assert.That(FindBrick(stage, 0, 5), Is.Not.EqualTo(Entity.Null));

            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(17, 5, 2, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Assert.That(cats.Length, Is.EqualTo(1));
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            Assert.That((cat.Slot, cat.Column), Is.EqualTo((17, 5)));
            Assert.That(cat.X, Is.EqualTo(WorldPlayfieldLayout.ColumnX(5, 6)).Within(.0001f));
            cats.Dispose();
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Cat_RemainsInLaunchColumnAcrossFallAndBrickImpact()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(2, 2, 0, 1) }), Is.True);
            Entity laneBrick = FindBrick(stage, 0, 2);
            Entity otherColumnBrick = FindBrick(stage, 0, 1);
            int otherColumnHealth = _manager.GetComponentData<Brick>(otherColumnBrick).Health;
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();

            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.X = WorldPlayfieldLayout.ColumnX(0, _stageBalanceConfig.LayoutForStage(1).BoardColumns);
            cat.HorizontalVelocity = 10f;
            _manager.SetComponentData(catEntity, cat);

            SystemHandle fall = _world.CreateSystem<FallSystem>();
            _world.SetTime(new TimeData(0, .1f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Column, Is.EqualTo(2));
            Assert.That(cat.X, Is.EqualTo(WorldPlayfieldLayout.ColumnX(2, _stageBalanceConfig.LayoutForStage(1).BoardColumns))
                .Within(.0001f));
            Assert.That(cat.HorizontalVelocity, Is.Zero);

            cat.X = WorldPlayfieldLayout.ColumnX(0, _stageBalanceConfig.LayoutForStage(1).BoardColumns);
            cat.HorizontalVelocity = -10f;
            cat.PreviousY = WorldPlayfieldLayout.TileContactProgress(0) - .01f;
            cat.Y = WorldPlayfieldLayout.TileContactProgress(0) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);
            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(laneBrick).Health, Is.LessThan(2));
            Assert.That(_manager.GetComponentData<Brick>(otherColumnBrick).Health,
                Is.EqualTo(otherColumnHealth));
            Assert.That(cat.Column, Is.EqualTo(2));
            Assert.That(cat.X, Is.EqualTo(WorldPlayfieldLayout.ColumnX(2, _stageBalanceConfig.LayoutForStage(1).BoardColumns))
                .Within(.0001f));
            Assert.That(cat.HorizontalVelocity, Is.Zero);

            _world.SetTime(new TimeData(.1f, cat.BounceDuration * .5f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Column, Is.EqualTo(2));
            Assert.That(cat.X, Is.EqualTo(WorldPlayfieldLayout.ColumnX(2, _stageBalanceConfig.LayoutForStage(1).BoardColumns))
                .Within(.0001f));
            Assert.That(cat.HorizontalVelocity, Is.Zero);
        }

        [Test]
        public void RequestDrop_StartsFromRestAndAcceleratesIntoFreeFall()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);

            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            float initialY = cat.Y;
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Velocity, Is.Zero);

            SystemHandle fall = _world.CreateSystem<FallSystem>();
            _world.SetTime(new TimeData(0, .25f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Y, Is.GreaterThan(initialY));
            Assert.That(cat.IsBouncing, Is.False);
            float firstVelocity = cat.Velocity;
            Assert.That(firstVelocity, Is.GreaterThan(0f).And.LessThan(_catConfig.FallSpeed));

            _world.SetTime(new TimeData(.25f, .25f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Velocity, Is.GreaterThan(firstVelocity));

            _world.SetTime(new TimeData(.5f, .25f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Velocity, Is.EqualTo(_catConfig.FallSpeed).Within(.0001f));
        }

        [Test]
        public void CatConfig_FallMotionControlsTheSpawnedCatsAccelerationAndSpeedCap()
        {
            var serialized = new SerializedObject(_catConfig);
            serialized.FindProperty("fallSpeed").floatValue = .2f;
            serialized.FindProperty("fallAcceleration").floatValue = .4f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.FallSpeed, Is.EqualTo(.2f).Within(.0001f));
            Assert.That(cat.FallAcceleration, Is.EqualTo(.4f).Within(.0001f));

            SystemHandle fall = _world.CreateSystem<FallSystem>();
            _world.SetTime(new TimeData(0, .25f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Velocity, Is.EqualTo(.1f).Within(.0001f));

            _world.SetTime(new TimeData(.25f, .5f));
            fall.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Velocity, Is.EqualTo(.2f).Within(.0001f));
        }

        [Test]
        public void Collision_ClearedLaneReachesLowerTileWhileOtherColumnsRemain()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            _manager.DestroyEntity(FindBrick(stage, 0, 0));
            Entity lowerBrick = FindBrick(stage, 1, 0);
            int lowerHealth = _manager.GetComponentData<Brick>(lowerBrick).Health;
            NativeArray<Entity> cats = Query<Cat>();
            Entity cat = cats[0];
            cats.Dispose();
            Cat catData = _manager.GetComponentData<Cat>(cat);
            catData.Y = WorldPlayfieldLayout.TileProgress(1) + .01f;
            catData.Velocity = _catConfig.FallSpeed;
            _manager.SetComponentData(cat, catData);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            catData = _manager.GetComponentData<Cat>(cat);
            Assert.That(_manager.GetComponentData<Brick>(lowerBrick).Health, Is.LessThan(lowerHealth));
            Assert.That(_manager.GetComponentData<Stage>(stage).ActiveRow, Is.EqualTo(0));
            Assert.That(catData.Y, Is.GreaterThan(WorldPlayfieldLayout.TileProgress(0)));
            Assert.That(catData.Y, Is.EqualTo(WorldPlayfieldLayout.TileContactProgress(1) - .001f)
                .Within(.0001f));
            Assert.That(catData.Velocity, Is.LessThan(0f));
            Assert.That(WorldPlayfieldLayout.DropY(catData.BounceCeilingY) -
                WorldPlayfieldLayout.DropY(catData.BounceOriginY),
                Is.EqualTo(_catConfig.BounceHeight).Within(.02f));

            _world.SetTime(new TimeData(0, catData.BounceDuration * .5f));
            _world.CreateSystem<FallSystem>().Update(_world.Unmanaged);
            catData = _manager.GetComponentData<Cat>(cat);
            Assert.That(catData.Y, Is.EqualTo(catData.BounceCeilingY).Within(.0001f));
            Assert.That(catData.Velocity, Is.Zero.Within(.0001f));
        }

        [Test]
        public void RowAdvance_IgnoresUnrelatedBrickBeforeCheckingCurrentRow()
        {
            _manager.CreateEntity(typeof(Brick));
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.Phase = Phase.Falling;
            _manager.SetComponentData(stage, state);

            _world.CreateSystem<DropRowAdvanceSystem>().Update(_world.Unmanaged);

            Assert.That(_manager.GetComponentData<Stage>(stage).ActiveRow, Is.Zero);
        }

        [Test]
        public void ConfigureBricks_UsesAuthoredLevelAndHealth()
        {
            StageBalanceConfig authored = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            Assert.That(authored, Is.Not.Null);
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, authored, 1);
            DropStageFactory.ConfigureBricks(_manager, stage, _brickConfig, authored, 10);

            StageDefinition definition = authored.GetStageDefinition(10);
            NativeArray<Entity> bricks = Query<Brick>();
            Assert.That(bricks.Length, Is.EqualTo(authored.LayoutForStage(10).BrickSlotCount));
            var levels = new System.Collections.Generic.HashSet<int>();
            int goldTiles = 0;
            int itemTiles = 0;
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = _manager.GetComponentData<Brick>(bricks[i]);
                StageBrickCell cell = definition.GetCell(brick.Row, brick.Column);
                Assert.That(brick.Level, Is.EqualTo(cell.Level));
                Assert.That(brick.Health, Is.EqualTo(_brickConfig.HealthFor(cell.Level, cell.Kind)));
                Assert.That(brick.GoldReward, Is.EqualTo(brick.Kind == BrickKind.Normal
                    ? _brickConfig.GoldForLevel(brick.Level) : 0));
                levels.Add(brick.Level);
                if (brick.Kind == BrickKind.Gold) goldTiles++;
                if (brick.Kind == BrickKind.Item) itemTiles++;
            }
            bricks.Dispose();
            Assert.That(levels.Count, Is.GreaterThan(1));
            Assert.That(goldTiles, Is.GreaterThan(0));
            Assert.That(itemTiles, Is.GreaterThan(0));
        }

        [Test]
        public void SpecialBricks_BankOneOpeningEachWithoutImmediateGold()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Entity firstGold = FindBrick(stage, 1, 1);
            Entity secondGold = FindBrick(stage, 4, 1);
            Entity item = FindBrick(stage, 2, 2);
            foreach (Entity entity in new[] { firstGold, secondGold, item })
            {
                Brick brick = _manager.GetComponentData<Brick>(entity);
                brick.Health = 0;
                _manager.SetComponentData(entity, brick);
            }

            SystemHandle damage = _world.CreateSystem<DamageAndBreakSystem>();
            damage.Update(_world.Unmanaged);
            damage.Update(_world.Unmanaged);

            Stage result = _manager.GetComponentData<Stage>(stage);
            Assert.That(result.GoldEarned, Is.Zero);
            Assert.That(result.GoldTicketsEarned, Is.EqualTo(2));
            Assert.That(result.ItemsEarned, Is.EqualTo(1));
        }

        [Test]
        public void CatConfig_BounceStrengthChangesReboundHeightOverConfiguredDuration()
        {
            var serialized = new SerializedObject(_catConfig);
            serialized.FindProperty("baseBounceHeight").floatValue = .5f;
            serialized.FindProperty("bounceStrength").floatValue = 2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.Y = WorldPlayfieldLayout.TileProgress(0) + .01f;
            cat.Velocity = _catConfig.FallSpeed;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.BounceDuration, Is.EqualTo(_catConfig.BounceDuration).Within(.0001f));
            Assert.That(cat.IsBouncing, Is.True);
            Assert.That(WorldPlayfieldLayout.DropY(cat.BounceCeilingY) -
                WorldPlayfieldLayout.DropY(cat.BounceOriginY),
                Is.EqualTo(1f).Within(.02f));

            _world.SetTime(new TimeData(0, cat.BounceDuration * .5f));
            _world.CreateSystem<FallSystem>().Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Y, Is.EqualTo(cat.BounceCeilingY).Within(.0001f));
            Assert.That(cat.Velocity, Is.Zero.Within(.0001f));

            _world.SetTime(new TimeData(cat.BounceDuration * .5f, cat.BounceDuration * .5f));
            _world.CreateSystem<FallSystem>().Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Y, Is.EqualTo(cat.BounceOriginY).Within(.0001f));
            float landingVelocity = 4f * (cat.BounceHeight / WorldPlayfieldLayout.DropTravel) /
                cat.BounceDuration;
            Assert.That(cat.Velocity, Is.EqualTo(landingVelocity).Within(.0001f));
            Assert.That(cat.IsBouncing, Is.False);

            _world.SetTime(new TimeData(cat.BounceDuration, .1f));
            _world.CreateSystem<FallSystem>().Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.Velocity, Is.GreaterThan(landingVelocity));
        }

        [Test]
        public void StrongerBounceExtendsDurationWithoutIncreasingLandingSpeed()
        {
            var serialized = new SerializedObject(_catConfig);
            serialized.FindProperty("bounceStrength").floatValue = 5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            cats.Dispose();

            Assert.That(cat.BounceDuration, Is.GreaterThan(_catConfig.BounceDuration));
            Assert.That(4f * cat.BounceHeight /
                (WorldPlayfieldLayout.DropTravel * cat.BounceDuration),
                Is.LessThanOrEqualTo(_catConfig.FallSpeed + .0001f));
        }

        [Test]
        public void ReboundUsesDropSpriteFromItsApex()
        {
            Cat cat = new Cat { IsBouncing = true, Velocity = -.1f };
            Assert.That(DropStageController.PoseFor(cat), Is.EqualTo(CatSpritePose.Jump));

            cat.Velocity = 0f;
            Assert.That(DropStageController.PoseFor(cat), Is.EqualTo(CatSpritePose.Drop));

            cat.Velocity = .1f;
            Assert.That(DropStageController.PoseFor(cat), Is.EqualTo(CatSpritePose.Drop));
        }

        [Test]
        public void CatConfig_PoseSpritesUseIdleFallbackWhenPoseEntriesAreMissing()
        {
            Texture2D texture = new Texture2D(1, 1);
            Sprite idleOne = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
            Sprite idleTwo = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
            Sprite jumpOne = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
            Sprite dropTwo = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.one * .5f);
            try
            {
                SetSprites("levelSprites", idleOne, idleTwo);

                Assert.That(_catConfig.SpriteFor(1, CatSpritePose.Jump), Is.SameAs(idleOne));
                Assert.That(_catConfig.SpriteFor(2, CatSpritePose.Drop), Is.SameAs(idleTwo));

                SetSprites("jumpSprites", jumpOne);
                SetSprites("dropSprites", null, dropTwo);

                Assert.That(_catConfig.SpriteFor(2, CatSpritePose.Jump), Is.SameAs(jumpOne));
                Assert.That(_catConfig.SpriteFor(1, CatSpritePose.Drop), Is.SameAs(idleOne));
                Assert.That(_catConfig.SpriteFor(2, CatSpritePose.Drop), Is.SameAs(dropTwo));
            }
            finally
            {
                Object.DestroyImmediate(idleOne);
                Object.DestroyImmediate(idleTwo);
                Object.DestroyImmediate(jumpOne);
                Object.DestroyImmediate(dropTwo);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void BreakingTile_PreservesAnotherCatsReboundInTheSameLane()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[]
            {
                new BoardCatSnapshot(0, 0, 0, 1),
                new BoardCatSnapshot(1, 0, 0, 1)
            }), Is.True);
            Entity brick = FindBrick(stage, 0, 0);
            NativeArray<Entity> cats = Query<Cat>();
            Entity firstCat = cats[0];
            Entity secondCat = cats[1];
            cats.Dispose();

            Cat first = _manager.GetComponentData<Cat>(firstCat);
            first.Y = WorldPlayfieldLayout.TileProgress(0) + .002f;
            first.Velocity = _catConfig.FallSpeed;
            _manager.SetComponentData(firstCat, first);
            Cat second = _manager.GetComponentData<Cat>(secondCat);
            second.Y = WorldPlayfieldLayout.TileProgress(0) + .002f;
            second.Velocity = _catConfig.FallSpeed;
            _manager.SetComponentData(secondCat, second);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            first = _manager.GetComponentData<Cat>(firstCat);
            second = _manager.GetComponentData<Cat>(secondCat);
            Assert.That(_manager.GetComponentData<Brick>(brick).Health, Is.LessThanOrEqualTo(0));
            Assert.That(first.BouncesRemaining, Is.EqualTo(1));
            Assert.That(first.IsBouncing, Is.True);
            Assert.That(first.Y, Is.EqualTo(WorldPlayfieldLayout.TileContactProgress(0) - .001f).Within(.0001f));
            Assert.That(first.Velocity, Is.LessThan(0f));
            Assert.That(first.ImpactSerial, Is.EqualTo(1));
            Assert.That(second.BouncesRemaining, Is.EqualTo(1));
            Assert.That(second.IsBouncing, Is.False);
            Assert.That(second.Velocity, Is.EqualTo(_catConfig.FallSpeed).Within(.0001f));
            Assert.That(second.ImpactSerial, Is.EqualTo(1));
        }

        [Test]
        public void LethalHit_ClearsStaleBounceStateAndKeepsTheCatsPosition()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 3) }), Is.True);
            Entity brick = FindBrick(stage, 0, 0);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            float impactY = WorldPlayfieldLayout.TileProgress(0) + .002f;
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.Y = impactY;
            cat.Velocity = _catConfig.FallSpeed;
            cat.IsBouncing = true;
            cat.BounceElapsed = cat.BounceDuration * .5f;
            cat.BounceOriginRow = 3;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(brick).Health, Is.LessThanOrEqualTo(0));
            Assert.That(cat.BouncesRemaining, Is.EqualTo(5));
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Y, Is.EqualTo(impactY).Within(.0001f));
            Assert.That(cat.Velocity, Is.EqualTo(_catConfig.FallSpeed).Within(.0001f));
            Assert.That(cat.ImpactSerial, Is.EqualTo(1));
        }

        [Test]
        public void Loss_PreservesBrickDamageAndRetainsRunRewards()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            Entity damagedEntity = FindBrick(stage, 0, 0);
            Brick damaged = _manager.GetComponentData<Brick>(damagedEntity);
            damaged.Health--;
            _manager.SetComponentData(damagedEntity, damaged);
            NativeArray<Entity> cats = Query<Cat>();
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            cat.IsSpent = true;
            _manager.SetComponentData(cats[0], cat);
            cats.Dispose();
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.GoldEarned = 7;
            state.ItemsEarned = 2;
            state.ActiveRow = 2;
            _manager.SetComponentData(stage, state);

            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            _world.CreateSystem<DropRunResultSystem>().Update(_world.Unmanaged);

            state = _manager.GetComponentData<Stage>(stage);
            Assert.That(state.Phase, Is.EqualTo(Phase.Lost));
            Assert.That(state.ActiveRow, Is.Zero);
            Assert.That(state.GoldEarned, Is.EqualTo(7));
            Assert.That(state.ItemsEarned, Is.EqualTo(2));
            Assert.That(_manager.GetComponentData<Brick>(damagedEntity).Health, Is.EqualTo(damaged.Health));
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
        }

        [Test]
        public void BreakEvents_AreRemovedBeforeTheNextSimulationFrame()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Entity evt = _manager.CreateEntity(typeof(BrickBreakEvent));
            _manager.SetComponentData(evt, new BrickBreakEvent { Stage = stage, Row = 0, Column = 0 });

            _world.CreateSystem<BreakEventCleanupSystem>().Update(_world.Unmanaged);

            NativeArray<Entity> events = Query<BrickBreakEvent>();
            Assert.That(events.Length, Is.Zero);
            events.Dispose();
        }

        [Test]
        public void ClearedLane_DoesNotDepleteCatWhilePassingThrough()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig, new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            _manager.DestroyEntity(FindBrick(stage, 0, 0));

            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.BouncesRemaining = 1;
            float passedFirstTileY = WorldPlayfieldLayout.TileProgress(0) + .002f;
            cat.Y = passedFirstTileY;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);
            cats.Dispose();

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.BouncesRemaining, Is.EqualTo(1));
            Assert.That(cat.Velocity, Is.GreaterThan(0f));
            Assert.That(cat.Y, Is.EqualTo(passedFirstTileY).Within(.0001f));
        }

        [Test]
        public void FirstTileBreak_LeavesLevelOneCatAliveToContinueDropping()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            Entity brick = FindBrick(stage, 0, 0);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();

            for (int impact = 0; impact < 2; impact++)
            {
                Cat cat = _manager.GetComponentData<Cat>(catEntity);
                cat.Y = WorldPlayfieldLayout.TileContactProgress(0) + .002f;
                cat.Velocity = .6f;
                _manager.SetComponentData(catEntity, cat);
                _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);
                _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            }

            Assert.That(_manager.Exists(brick), Is.False);
            Assert.That(_manager.GetComponentData<Cat>(catEntity).BouncesRemaining, Is.Zero);
            NativeArray<Entity> impacts = Query<BrickImpactEvent>();
            Assert.That(impacts.Length, Is.EqualTo(2));
            for (int i = 0; i < impacts.Length; i++)
            {
                BrickImpactEvent impact = _manager.GetComponentData<BrickImpactEvent>(impacts[i]);
                Assert.That(impact.Row, Is.Zero);
                Assert.That(impact.Column, Is.Zero);
                Assert.That(impact.MaxHealth, Is.EqualTo(2));
            }
            impacts.Dispose();
        }

        [Test]
        public void LethalHits_PreserveZeroBounceCatAndPassThroughConsecutiveRows()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 3) }), Is.True);
            Entity firstBrick = FindBrick(stage, 0, 0);
            Entity secondBrick = FindBrick(stage, 1, 0);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();

            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.BouncesRemaining = 0;
            cat.Y = WorldPlayfieldLayout.TileProgress(0) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(firstBrick).Health, Is.LessThanOrEqualTo(0));
            Assert.That(cat.BouncesRemaining, Is.Zero);
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Velocity, Is.EqualTo(.6f).Within(.0001f));
            Assert.That(cat.Y, Is.EqualTo(WorldPlayfieldLayout.TileProgress(0) + .002f).Within(.0001f));

            // The same still-present lethal brick cannot be damaged again before cleanup.
            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);
            Assert.That(_manager.GetComponentData<Brick>(firstBrick).Health, Is.LessThanOrEqualTo(0));

            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            cat.Y = WorldPlayfieldLayout.TileProgress(1) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(secondBrick).Health, Is.LessThanOrEqualTo(0));
            Assert.That(cat.BouncesRemaining, Is.Zero);
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Velocity, Is.EqualTo(.6f).Within(.0001f));

            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            Assert.That(_manager.Exists(firstBrick), Is.False);
            Assert.That(_manager.Exists(secondBrick), Is.False);
            Assert.That(_manager.Exists(catEntity), Is.True);
        }

        [Test]
        public void OneShotEqualToTotalBrickHp_ConsumesBounceCountAndFallsThrough()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 2) }), Is.True);
            Entity brick = FindBrick(stage, 0, 0);
            Brick target = _manager.GetComponentData<Brick>(brick);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.ImpactDamage = target.MaxHealth;
            cat.BouncesRemaining = 1;
            cat.Y = WorldPlayfieldLayout.TileContactProgress(0) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(brick).Health, Is.LessThanOrEqualTo(0));
            Assert.That(cat.BouncesRemaining, Is.Zero);
            Assert.That(cat.IsSpent, Is.False);
            Assert.That(cat.IsBouncing, Is.False);
            Assert.That(cat.Velocity, Is.GreaterThan(0f));
            Assert.That(cat.Y, Is.GreaterThan(WorldPlayfieldLayout.TileContactProgress(0)));
            NativeArray<Entity> impacts = Query<BrickImpactEvent>();
            Assert.That(impacts.Length, Is.EqualTo(1));
            Assert.That(_manager.GetComponentData<BrickImpactEvent>(impacts[0]).Gold,
                Is.EqualTo(target.GoldReward));
            impacts.Dispose();

            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            Assert.That(_manager.GetComponentData<Stage>(stage).GoldEarned,
                Is.EqualTo(target.GoldReward));
            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            Assert.That(_manager.GetComponentData<Stage>(stage).GoldEarned,
                Is.EqualTo(target.GoldReward), "A destroyed brick must not pay twice.");
        }

        [Test]
        public void BrickImage_BakesIntoSixtyFourDistinctTexturePieces()
        {
            Sprite brickSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Projects/Art/Build/Brick/brick_level_16_v1.png");
            Assert.That(brickSprite, Is.Not.Null);
            Rect source = brickSprite.textureRect;
            Assert.That(_brickConfig.FinalBurstDebris, Is.EqualTo(64));
            Sprite[] pieces = new Sprite[BrickConfig.DebrisPieceCount];
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = _brickConfig.DebrisSpriteFor(brickSprite, i);
                Assert.That(pieces[i].texture, Is.SameAs(brickSprite.texture));
                Assert.That(pieces[i].rect.width, Is.EqualTo(source.width / 8f));
                Assert.That(pieces[i].rect.height, Is.EqualTo(source.height / 8f));
                Assert.That(pieces[i].rect.x, Is.EqualTo(source.x + i % 8 * source.width / 8f));
                Assert.That(pieces[i].rect.y, Is.EqualTo(source.y + i / 8 * source.height / 8f));
            }
            Assert.That(pieces, Is.Unique);
        }

        [Test]
        public void SpecialBricks_UseTheirOwnSpritesAndDebris()
        {
            BrickConfig config = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            Sprite gold = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Projects/Art/Build/Brick/brick_gold_v1.png");
            Sprite item = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Projects/Art/Build/Brick/brick_item_v1.png");
            Assert.That(config, Is.Not.Null);
            Assert.That(gold, Is.Not.Null);
            Assert.That(item, Is.Not.Null);
            Assert.That(config.SpriteFor(1, BrickKind.Normal), Is.Not.SameAs(gold));
            Assert.That(config.SpriteFor(1, BrickKind.Gold), Is.SameAs(gold));
            Assert.That(config.SpriteFor(1, BrickKind.Item), Is.SameAs(item));
            Assert.That(config.DebrisSpriteFor(gold, 0).texture, Is.SameAs(gold.texture));
            Assert.That(config.DebrisSpriteFor(item, 0).texture, Is.SameAs(item.texture));
        }

        [Test]
        public void SurvivingBrick_UsesLastBounceThenStopsCatOnNextHit()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            Entity brick = FindBrick(stage, 0, 0);
            Brick target = _manager.GetComponentData<Brick>(brick);
            target.Health = target.MaxHealth = 4;
            _manager.SetComponentData(brick, target);
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.BouncesRemaining = 1;
            cat.Y = WorldPlayfieldLayout.TileContactProgress(0) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);
            SystemHandle collision = _world.CreateSystem<GateCollisionSystem>();

            collision.Update(_world.Unmanaged);
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(_manager.GetComponentData<Brick>(brick).Health, Is.EqualTo(3));
            Assert.That(cat.BouncesRemaining, Is.Zero);
            Assert.That(cat.IsBouncing, Is.True);
            Assert.That(cat.IsSpent, Is.False);

            cat.Y = WorldPlayfieldLayout.TileContactProgress(0) + .002f;
            cat.Velocity = .6f;
            _manager.SetComponentData(catEntity, cat);
            collision.Update(_world.Unmanaged);
            Assert.That(_manager.GetComponentData<Brick>(brick).Health, Is.EqualTo(2));
            Assert.That(_manager.GetComponentData<Cat>(catEntity).IsSpent, Is.True);
            _world.CreateSystem<DamageAndBreakSystem>().Update(_world.Unmanaged);
            Assert.That(_manager.Exists(catEntity), Is.False);
        }

        [Test]
        public void LongFrame_ProcessesEveryBrickCrossedByALethalCat()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 20) }), Is.True);
            Entity[] crossedBricks =
            {
                FindBrick(stage, 0, 0),
                FindBrick(stage, 1, 0),
                FindBrick(stage, 2, 0)
            };
            NativeArray<Entity> cats = Query<Cat>();
            Entity catEntity = cats[0];
            cats.Dispose();
            Cat cat = _manager.GetComponentData<Cat>(catEntity);
            cat.PreviousY = WorldPlayfieldLayout.TileContactProgress(0) - .01f;
            cat.Y = WorldPlayfieldLayout.TileContactProgress(2) + .002f;
            cat.Velocity = _catConfig.FallSpeed;
            _manager.SetComponentData(catEntity, cat);

            _world.CreateSystem<GateCollisionSystem>().Update(_world.Unmanaged);

            for (int i = 0; i < crossedBricks.Length; i++)
                Assert.That(_manager.GetComponentData<Brick>(crossedBricks[i]).Health,
                    Is.LessThanOrEqualTo(0), $"Brick row {i} was skipped.");
            cat = _manager.GetComponentData<Cat>(catEntity);
            Assert.That(cat.ImpactSerial, Is.EqualTo(3));
            Assert.That(cat.IsBouncing, Is.False);
        }

        [Test]
        public void OneCatReachingGoal_WinsWhileOtherLinesAndBricksRemain()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1), new BoardCatSnapshot(1, 1, 0, 1) }), Is.True);
            NativeArray<Entity> cats = Query<Cat>();
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            cat.Y = WorldPlayfieldLayout.GoalProgress;
            _manager.SetComponentData(cats[0], cat);
            cats.Dispose();

            _world.CreateSystem<DropRunResultSystem>().Update(_world.Unmanaged);

            cats = Query<Cat>();
            Assert.That(cats.Length, Is.EqualTo(2));
            cats.Dispose();
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That(state.ActiveRow, Is.Zero);
            Assert.That(state.Phase, Is.EqualTo(Phase.Won));
            NativeArray<Entity> bricks = Query<Brick>();
            Assert.That(bricks.Length, Is.EqualTo(_stageBalanceConfig.LayoutForStage(1).BrickSlotCount));
            bricks.Dispose();
        }

        [Test]
        public void LastRowCleared_AdvancesPastTheExtendedField()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.Phase = Phase.Falling;
            state.ActiveRow = WorldPlayfieldLayout.BrickRows - 1;
            _manager.SetComponentData(stage, state);

            for (int column = 0; column < state.BrickColumns; column++)
                _manager.DestroyEntity(FindBrick(stage, WorldPlayfieldLayout.BrickRows - 1, column));

            _world.CreateSystem<DropRowAdvanceSystem>().Update(_world.Unmanaged);

            Assert.That(_manager.GetComponentData<Stage>(stage).ActiveRow,
                Is.EqualTo(WorldPlayfieldLayout.BrickRows));
        }

        [Test]
        public void GoalAfterLastRow_EndsTheRunAsWon()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.ActiveRow = WorldPlayfieldLayout.BrickRows;
            _manager.SetComponentData(stage, state);

            NativeArray<Entity> cats = Query<Cat>();
            Cat cat = _manager.GetComponentData<Cat>(cats[0]);
            cat.Y = WorldPlayfieldLayout.GoalProgress;
            _manager.SetComponentData(cats[0], cat);
            cats.Dispose();

            _world.CreateSystem<DropRunResultSystem>().Update(_world.Unmanaged);

            Assert.That(_manager.GetComponentData<Stage>(stage).Phase, Is.EqualTo(Phase.Won));
        }

        [Test]
        public void RequestDrop_ClampsMaximumLevelBounceCount()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _stageBalanceConfig, 1);
            Assert.That(DropStageFactory.RequestDrop(_manager, stage, _catConfig,
                new[] { new BoardCatSnapshot(0, 0, 0, int.MaxValue) }), Is.True);

            NativeArray<Entity> cats = Query<Cat>();
            Assert.That(_manager.GetComponentData<Cat>(cats[0]).BouncesRemaining, Is.EqualTo(int.MaxValue));
            cats.Dispose();
        }

        private void SetSprites(string fieldName, params Sprite[] sprites)
        {
            typeof(CatConfig).GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_catConfig, sprites);
        }

        private Entity FindBrick(Entity stage, int row, int column)
        {
            NativeArray<Entity> bricks = Query<Brick>();
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = _manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Stage == stage && brick.Row == row && brick.Column == column)
                {
                    Entity result = bricks[i];
                    bricks.Dispose();
                    return result;
                }
            }
            bricks.Dispose();
            Assert.Fail("Brick was not found.");
            return Entity.Null;
        }

        private NativeArray<Entity> Query<T>() where T : unmanaged, IComponentData =>
            _manager.CreateEntityQuery(ComponentType.ReadOnly<T>()).ToEntityArray(Allocator.Temp);
    }
}
