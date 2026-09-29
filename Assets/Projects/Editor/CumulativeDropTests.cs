using NUnit.Framework;
using Framework.Poolable.Components;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Drop.Ecs.Systems;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class CumulativeDropTests
    {
        private World _world;
        private EntityManager _manager;
        private CatConfig _cats;
        private BrickConfig _bricks;
        private StageBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CumulativeDropTests");
            _manager = _world.EntityManager;
            _cats = ScriptableObject.CreateInstance<CatConfig>();
            _bricks = ScriptableObject.CreateInstance<BrickConfig>();
            _balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_cats);
            Object.DestroyImmediate(_bricks);
            Object.DestroyImmediate(_balance);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CatalogRevision_RestoresOriginalRewardIdentity(bool rewardAlreadyDestroyed)
        {
            var definition = ScriptableObject.CreateInstance<StageDefinition>();
            try
            {
                definition.Configure(1, 2, 1, new[] {
                    new StageBrickCell { Present = true, Kind = BrickKind.Item, Level = 1 },
                    new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 2 } });
                Entity original = DropStageFactory.Create(_manager, _bricks, _balance, 1, definition);
                int[] health = DropStageFactory.CaptureBrickHealth(_manager, original);
                health[rewardAlreadyDestroyed ? 0 : 1] = 0;
                var save = new Projects.Scripts.Data.PlayerSaveData();
                save.BeginStageAttempt(4, definition.CatalogRevision);
                save.ReserveAttemptDrop();
                save.CommitAttemptDrop(health);
                definition.Configure(1, 3, 1, new[] {
                    new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 10 },
                    new StageBrickCell { Present = true, Kind = BrickKind.Item, Level = 2 },
                    new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 2 } });
                Assert.That(definition.CatalogRevision, Is.EqualTo(1));
                var loaded = JsonUtility.FromJson<Projects.Scripts.Data.PlayerSaveData>(JsonUtility.ToJson(save));
                Entity restored = DropStageFactory.Create(_manager, _bricks, _balance, 1, definition,
                    loaded.AttemptCatalogRevision);
                Assert.That(_manager.GetComponentData<Stage>(restored).BoardColumns, Is.EqualTo(2));
                DropStageFactory.ApplyBrickHealth(_manager, restored, loaded.GetAttemptBrickHealth());
                Assert.That(DropStageFactory.CaptureBrickHealth(_manager, restored), Is.EqualTo(health));
                EntityQuery query = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
                using var entities = query.ToEntityArray(Allocator.Temp);
                int rewards = 0;
                foreach (var entity in entities)
                {
                    Brick brick = _manager.GetComponentData<Brick>(entity);
                    if (brick.Stage == restored && brick.Kind == BrickKind.Item) rewards++;
                }
                query.Dispose();
                Assert.That(rewards, Is.EqualTo(rewardAlreadyDestroyed ? 0 : 1));
                loaded.RestartStageAttempt(definition.CatalogRevision);
                DropStageFactory.ConfigureBricks(_manager, restored, _bricks, _balance, 1, definition,
                    loaded.AttemptCatalogRevision);
                Assert.That(loaded.GetAttemptBrickHealth(), Is.Empty);
                Assert.That(_manager.GetComponentData<Stage>(restored).BoardColumns, Is.EqualTo(3));
                EntityQuery latestQuery = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
                using var latestEntities = latestQuery.ToEntityArray(Allocator.Temp);
                foreach (var entity in latestEntities)
                {
                    Brick brick = _manager.GetComponentData<Brick>(entity);
                    if (brick.Stage == restored)
                        Assert.That(brick.Kind, Is.EqualTo(brick.Column == 1 ? BrickKind.Item : BrickKind.Normal));
                }
                latestQuery.Dispose();
            }
            finally { Object.DestroyImmediate(definition); }
        }

        [Test]
        public void DropBudget_UsesStageOverrideOrSharedDefault()
        {
            var definition = ScriptableObject.CreateInstance<StageDefinition>();
            try
            {
                var balance = new UnityEditor.SerializedObject(_balance);
                var definitions = balance.FindProperty("stageDefinitions");
                definitions.arraySize = 1;
                definitions.GetArrayElementAtIndex(0).objectReferenceValue = definition;
                balance.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(_balance.DropBudgetFor(1), Is.EqualTo(4));
                var authored = new UnityEditor.SerializedObject(definition);
                authored.FindProperty("dropBudget").intValue = 6;
                authored.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(_balance.DropBudgetFor(1), Is.EqualTo(6));
            }
            finally { Object.DestroyImmediate(definition); }
        }

        [Test]
        public void RequestDrop_PreservesPartialBrickHealth()
        {
            Entity stage = CreateStage();
            Entity entity = FirstBrick(stage);
            Brick brick = _manager.GetComponentData<Brick>(entity);
            brick.Health--;
            _manager.SetComponentData(entity, brick);

            Assert.That(RequestDrop(stage), Is.True);

            Assert.That(_manager.GetComponentData<Brick>(FirstBrick(stage)).Health,
                Is.EqualTo(brick.Health));
        }

        [Test]
        public void DestroyedBrick_IsNotRewardedAgainOnLaterDrop()
        {
            Entity stage = CreateStage();
            Entity entity = FirstNormalBrick(stage);
            Brick brick = _manager.GetComponentData<Brick>(entity);
            brick.Health = 0;
            _manager.SetComponentData(entity, brick);
            SystemHandle damage = _world.CreateSystem<DamageAndBreakSystem>();

            damage.Update(_world.Unmanaged);
            int earnedAfterBreak = _manager.GetComponentData<Stage>(stage).GoldEarned;
            Assert.That(earnedAfterBreak, Is.EqualTo(brick.GoldReward));

            Assert.That(RequestDrop(stage), Is.True);
            damage.Update(_world.Unmanaged);

            Assert.That(_manager.GetComponentData<Stage>(stage).GoldEarned, Is.Zero,
                "A new drop has its own reward tally, and the removed brick cannot reward it.");
            int[] health = DropStageFactory.CaptureBrickHealth(_manager, stage);
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That(health[brick.Row * state.BrickColumns + brick.Column], Is.Zero);
        }

        [Test]
        public void RestartStage_RestoresBlueprintAndKeepsRunId()
        {
            Entity stage = CreateStage();
            Entity entity = FirstBrick(stage);
            Brick brick = _manager.GetComponentData<Brick>(entity);
            brick.Health--;
            _manager.SetComponentData(entity, brick);
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.Phase = Phase.Lost;
            state.RunId = 13;
            state.GoldEarned = 9;
            state.GoldTicketsEarned = 2;
            state.ItemsEarned = 1;
            _manager.SetComponentData(stage, state);

            DropStageFactory.RestartStage(_manager, stage);

            state = _manager.GetComponentData<Stage>(stage);
            Brick restored = _manager.GetComponentData<Brick>(FirstBrick(stage));
            Assert.That((state.Phase, state.ActiveRow, state.GoldEarned, state.GoldTicketsEarned,
                    state.ItemsEarned, state.RunId),
                Is.EqualTo((Phase.Ready, 0, 0, 0, 0, 13)));
            Assert.That(restored.Health, Is.EqualTo(restored.MaxHealth));
        }

        [Test]
        public void BrickHealth_RoundTripsDestroyedAndDamagedCells()
        {
            Entity stage = CreateStage();
            Entity damagedEntity = FirstBrick(stage);
            Brick damaged = _manager.GetComponentData<Brick>(damagedEntity);
            damaged.Health--;
            _manager.SetComponentData(damagedEntity, damaged);
            Entity destroyedEntity = AnotherBrick(stage, damagedEntity);
            Brick destroyed = _manager.GetComponentData<Brick>(destroyedEntity);
            _manager.DestroyEntity(destroyedEntity);

            int[] captured = DropStageFactory.CaptureBrickHealth(_manager, stage);
            DropStageFactory.ApplyBrickHealth(_manager, stage, captured);

            CollectionAssert.AreEqual(captured, DropStageFactory.CaptureBrickHealth(_manager, stage));
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That(captured[destroyed.Row * state.BrickColumns + destroyed.Column], Is.Zero);
        }

        [Test]
        public void WonStage_CanStartNextDropWithoutRestoringBricks()
        {
            Entity stage = CreateStage();
            Entity entity = FirstBrick(stage);
            Brick brick = _manager.GetComponentData<Brick>(entity);
            brick.Health--;
            _manager.SetComponentData(entity, brick);
            Stage state = _manager.GetComponentData<Stage>(stage);
            state.Phase = Phase.Won;
            _manager.SetComponentData(stage, state);

            Assert.That(RequestDrop(stage), Is.True);

            Assert.That(_manager.GetComponentData<Stage>(stage).Phase, Is.EqualTo(Phase.Falling));
            Assert.That(_manager.GetComponentData<Brick>(FirstBrick(stage)).Health,
                Is.EqualTo(brick.Health));
        }

        [Test]
        public void CaptureSurvivingCatSlots_ReturnsSortedActiveUnspentSlotsForWonStage()
        {
            Entity stage = CreateStage();
            Stage stageState = _manager.GetComponentData<Stage>(stage);
            stageState.Phase = Phase.Won;
            _manager.SetComponentData(stage, stageState);
            Entity otherStage = CreateStage();

            Entity survivor = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(survivor, new Cat
            {
                Stage = stage,
                Slot = 7,
                Y = stageState.GoalProgress - 0.1f
            });
            Entity secondSurvivor = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(secondSurvivor, new Cat { Stage = stage, Slot = 2 });
            Entity duplicateSlot = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(duplicateSlot, new Cat { Stage = stage, Slot = 7 });
            Entity spent = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(spent, new Cat { Stage = stage, Slot = 4, IsSpent = true });
            Entity otherStageCat = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(otherStageCat, new Cat { Stage = otherStage, Slot = 1 });
            Entity pooled = _manager.CreateEntity(typeof(Cat), typeof(PooledEntity));
            _manager.SetComponentData(pooled, new Cat { Stage = stage, Slot = 5 });
            _manager.SetEnabled(pooled, false);
            Entity invalidSlot = _manager.CreateEntity(typeof(Cat));
            _manager.SetComponentData(invalidSlot, new Cat
            {
                Stage = stage,
                Slot = stageState.BoardColumns * stageState.BoardRows
            });

            Assert.That(DropStageFactory.CaptureSurvivingCatSlots(_manager, stage),
                Is.EqualTo(new[] { 2, 7 }));
        }

        private Entity CreateStage() => DropStageFactory.Create(_manager, _bricks, _balance, 1);

        private bool RequestDrop(Entity stage) => DropStageFactory.RequestDrop(_manager, stage, _cats,
            new[] { new BoardCatSnapshot(0, 0, 0, 1) });

        private Entity FirstBrick(Entity stage)
        {
            using NativeArray<Entity> entities = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                if (_manager.GetComponentData<Brick>(entities[i]).Stage == stage) return entities[i];
            Assert.Fail("Expected a brick in the stage.");
            return Entity.Null;
        }

        private Entity AnotherBrick(Entity stage, Entity excluded)
        {
            using NativeArray<Entity> entities = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                if (entities[i] != excluded && _manager.GetComponentData<Brick>(entities[i]).Stage == stage)
                    return entities[i];
            Assert.Fail("Expected a second brick in the stage.");
            return Entity.Null;
        }

        private Entity FirstNormalBrick(Entity stage)
        {
            using NativeArray<Entity> entities = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                Brick brick = _manager.GetComponentData<Brick>(entities[i]);
                if (brick.Stage == stage && brick.Kind == BrickKind.Normal) return entities[i];
            }
            Assert.Fail("Expected a normal brick in the stage.");
            return Entity.Null;
        }
    }
}
