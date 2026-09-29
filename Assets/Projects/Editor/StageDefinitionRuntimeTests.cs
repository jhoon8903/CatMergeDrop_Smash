using NUnit.Framework;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Drop.Ecs.Systems;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageDefinitionRuntimeTests
    {
        private World _world;
        private EntityManager _manager;
        private BrickConfig _brickConfig;
        private StageBalanceConfig _balanceConfig;
        private StageDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _world = new World("StageDefinitionRuntimeTests");
            _manager = _world.EntityManager;
            _brickConfig = ScriptableObject.CreateInstance<BrickConfig>();
            _balanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
            _definition = ScriptableObject.CreateInstance<StageDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_brickConfig);
            Object.DestroyImmediate(_balanceConfig);
            Object.DestroyImmediate(_definition);
        }

        [Test]
        public void ConfigureBricks_OverrideDefinitionUsesAuthoredCellsAndLevelValues()
        {
            _definition.Configure(99, 3, 2, new[]
            {
                new StageBrickCell { Present = true, Kind = BrickKind.Item, Level = 7 },
                default, new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 2 },
                default, new StageBrickCell { Present = true, Kind = BrickKind.Gold, Level = 4 }
            });

            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 2, _definition);
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That((state.BoardColumns, state.BoardRows, state.BrickColumns, state.BrickRows),
                Is.EqualTo((3, 3, 3, 2)));

            NativeArray<Entity> bricks = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            Assert.That(bricks.Length, Is.EqualTo(3));
            Brick first = BrickAt(bricks, 0, 0);
            Brick second = BrickAt(bricks, 1, 1);
            Brick normal = BrickAt(bricks, 0, 2);
            Assert.That((first.Level, first.Health, first.GoldReward, first.Kind),
                Is.EqualTo((7, 1, 0, BrickKind.Item)));
            Assert.That((second.Level, second.Kind), Is.EqualTo((4, BrickKind.Gold)));
            Assert.That(second.Health, Is.EqualTo(1));
            Assert.That(second.GoldReward, Is.Zero);
            Assert.That(normal.GoldReward, Is.EqualTo(_brickConfig.GoldForLevel(2)));
            bricks.Dispose();
        }

        [Test]
        public void ConfigureBricks_NormalRewardsAndHealthFollowLevel()
        {
            _definition.Configure(88, 3, 1, new[]
            {
                new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 1 },
                new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 4 },
                new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 4 }
            });
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 999, _definition);
            NativeArray<Entity> bricks = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            try
            {
                Brick weak = BrickAt(bricks, 0, 0);
                Brick strong = BrickAt(bricks, 0, 1);
                Brick sameLevel = BrickAt(bricks, 0, 2);
                Assert.That(weak.GoldReward, Is.EqualTo(_brickConfig.GoldForLevel(weak.Level)));
                Assert.That(strong.GoldReward, Is.EqualTo(_brickConfig.GoldForLevel(strong.Level)));
                Assert.That(strong.GoldReward, Is.GreaterThan(weak.GoldReward));
                Assert.That(strong.MaxHealth, Is.GreaterThan(weak.MaxHealth));
                Assert.That(strong.MaxHealth, Is.EqualTo(sameLevel.MaxHealth));
                Assert.That(strong.GoldReward, Is.EqualTo(sameLevel.GoldReward));
            }
            finally { bricks.Dispose(); }
        }

        [Test]
        public void EditedBrickLevelStats_ApplyToEveryBrickOfThatLevel()
        {
            var serialized = new SerializedObject(_brickConfig);
            serialized.FindProperty("levelConfigs").GetArrayElementAtIndex(3)
                .FindPropertyRelative("maxHealth").intValue = 27;
            serialized.FindProperty("levelConfigs").GetArrayElementAtIndex(3)
                .FindPropertyRelative("goldReward").intValue = 71;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            _definition.Configure(88, 3, 1, new[]
            {
                new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 4 },
                new StageBrickCell { Present = true, Kind = BrickKind.Gold, Level = 4 },
                new StageBrickCell { Present = true, Kind = BrickKind.Item, Level = 4 }
            });

            DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 88, _definition);
            NativeArray<Entity> bricks = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            try
            {
                Assert.That(bricks.Length, Is.EqualTo(3));
                for (int i = 0; i < bricks.Length; i++)
                {
                    Brick brick = _manager.GetComponentData<Brick>(bricks[i]);
                    Assert.That(brick.MaxHealth, Is.EqualTo(brick.Kind == BrickKind.Normal ? 27 : 1));
                    Assert.That(brick.GoldReward, Is.EqualTo(brick.Kind == BrickKind.Normal ? 71 : 0));
                }
            }
            finally { bricks.Dispose(); }
        }

        [Test]
        public void ConfigureBricks_RejectsMissingAuthoredLevel()
        {
            _definition.Configure(88, 3, 1, new[]
            {
                new StageBrickCell { Present = true, Kind = BrickKind.Normal },
                default,
                default
            });

            Assert.Throws<System.InvalidOperationException>(() =>
                DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 88, _definition));
        }

        [Test]
        public void GoalLine_FollowsTheLowestOccupiedBrickAndControlsTheClear()
        {
            var cells = new StageBrickCell[3 * 5];
            cells[0] = new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 1 };
            cells[2 * 3 + 1] = new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 1 };
            _definition.Configure(12, 3, 5, cells);
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 12, _definition);
            Stage state = _manager.GetComponentData<Stage>(stage);
            float expectedGoal = WorldPlayfieldLayout.GoalProgressForLastBrickRow(2);
            Assert.That(state.BrickRows, Is.EqualTo(5));
            Assert.That(state.GoalProgress, Is.EqualTo(expectedGoal).Within(.0001f));
            Assert.That(state.GoalProgress, Is.LessThan(WorldPlayfieldLayout.GoalProgress));

            CatConfig cats = ScriptableObject.CreateInstance<CatConfig>();
            try
            {
                Assert.That(DropStageFactory.RequestDrop(_manager, stage, cats,
                    new[] { new BoardCatSnapshot(0, 0, 0, 1) }), Is.True);
                EntityQuery query = _manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
                using var entities = query.ToEntityArray(Allocator.Temp);
                Cat cat = _manager.GetComponentData<Cat>(entities[0]);
                cat.Y = expectedGoal - .001f;
                _manager.SetComponentData(entities[0], cat);
                SystemHandle result = _world.CreateSystem<DropRunResultSystem>();
                result.Update(_world.Unmanaged);
                Assert.That(_manager.GetComponentData<Stage>(stage).Phase, Is.EqualTo(Phase.Falling));
                cat.Y = expectedGoal;
                _manager.SetComponentData(entities[0], cat);
                result.Update(_world.Unmanaged);
                Assert.That(_manager.GetComponentData<Stage>(stage).Phase, Is.EqualTo(Phase.Won));
            }
            finally { Object.DestroyImmediate(cats); }
        }

        [Test]
        public void ConfigureBricks_WithoutDefinitionUsesNeutralLayout()
        {
            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 2);
            Stage state = _manager.GetComponentData<Stage>(stage);
            Assert.That((state.BoardColumns, state.BoardRows, state.BrickColumns, state.BrickRows),
                Is.EqualTo((4, 3, 4, 10)));
            NativeArray<Entity> bricks = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            Assert.That(bricks.Length, Is.EqualTo(40));
            bricks.Dispose();
        }

        [Test]
        public void AuthoredDefinition_OverridesLegacyLayoutAndPresence()
        {
            _definition.Configure(2, 4, 2, new[]
            {
                default, new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = 1 },
                default, default, default, default, default, default
            });
            SerializedObject serialized = new SerializedObject(_balanceConfig);
            SerializedProperty definitions = serialized.FindProperty("stageDefinitions");
            definitions.arraySize = 1;
            definitions.GetArrayElementAtIndex(0).objectReferenceValue = _definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(_balanceConfig.LayoutForStage(2).BrickRows, Is.EqualTo(2));
            Assert.That(_balanceConfig.HasBrick(2, 0, 1), Is.True);
            Assert.That(_balanceConfig.HasBrick(2, 0, 0), Is.False);

            Entity stage = DropStageFactory.Create(_manager, _brickConfig, _balanceConfig, 2);
            NativeArray<Entity> bricks = _manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>())
                .ToEntityArray(Allocator.Temp);
            Assert.That(bricks.Length, Is.EqualTo(1));
            bricks.Dispose();
        }

        private Brick BrickAt(NativeArray<Entity> bricks, int row, int column)
        {
            for (int i = 0; i < bricks.Length; i++)
            {
                Brick brick = _manager.GetComponentData<Brick>(bricks[i]);
                if (brick.Row == row && brick.Column == column) return brick;
            }
            Assert.Fail($"No brick at ({row}, {column}).");
            return default;
        }
    }
}
