using NUnit.Framework;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEditor;
using Unity.Entities;

namespace Projects.Editor.StageGeneration
{
    public sealed class StageBulkGeneratorTests
    {
        [Test]
        public void ExpectedCatLevel_GrowsSlowlyAcrossTheCatalog()
        {
            Assert.That(StageBulkGenerator.ExpectedCatLevel(1), Is.EqualTo(1));
            Assert.That(StageBulkGenerator.ExpectedCatLevel(16), Is.EqualTo(5));
            Assert.That(StageBulkGenerator.ExpectedCatLevel(1000), Is.EqualTo(10));
            Assert.That(StageBulkGenerator.SoloRouteCatLevel(1), Is.EqualTo(2));
        }

        [Test]
        public void GeneratedCatalog_ContainsOneThousandConnectedPlayableStages()
        {
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            CatConfig cats = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            Assert.That(balance, Is.Not.Null);
            Assert.That(cats, Is.Not.Null);
            Assert.That(bricks, Is.Not.Null);
            int previousColumns = 4;
            for (int stage = 1; stage <= 1000; stage++)
            {
                StageDefinition definition = balance.GetStageDefinition(stage);
                Assert.That(definition, Is.Not.Null, $"Stage {stage} has no definition.");
                Assert.That(definition.BoardColumns, Is.InRange(3, 6));
                Assert.That(definition.BrickRows, Is.EqualTo(10));
                Assert.That(System.Math.Abs(definition.BoardColumns - previousColumns), Is.LessThanOrEqualTo(1));
                previousColumns = definition.BoardColumns;
                Assert.That(balance.LayoutForStage(stage).BrickColumns, Is.EqualTo(definition.BoardColumns));
                int gold = 0, item = 0;
                int soloRouteLevel = StageBulkGenerator.SoloRouteCatLevel(stage);
                int damage = cats.ImpactDamageFor(soloRouteLevel);
                int bounces = cats.BounceCountFor(soloRouteLevel);
                int rewardLaneIndex = StageBulkGenerator.RewardLaneForStage(stage, definition.BoardColumns);
                var cells = new StageBrickCell[definition.BoardColumns * definition.BrickRows];
                for (int row = 0; row < definition.BrickRows; row++)
                for (int column = 0; column < definition.BoardColumns; column++)
                    cells[row * definition.BoardColumns + column] = definition.GetCell(row, column);
                var candidate = new StageCandidate(stage, definition.BoardColumns, definition.BrickRows,
                    cells, rewardLaneIndex, (rewardLaneIndex + 1) % definition.BoardColumns);
                int normalCap = balance.MaximumBrickLevelForStage(stage, bricks.LevelCount);
                int barrierLevel = StageLaneBarrierPolicy.BarrierLevelForStage(stage, bricks, cats);
                bool safeRoute = false, rewardRoute = false;
                int lowestLevel = int.MaxValue, highestLevel = 0;
                int rewardLaneGold = 0, rewardLaneItem = 0;
                int rewardLaneLowestLevel = int.MaxValue, rewardLaneHighestLevel = 0;
                for (int column = 0; column < definition.BoardColumns; column++)
                {
                    int cost = 0;
                    bool hasSpecial = false;
                    int[] selectedRows = StageLaneBarrierPolicy.SelectedRows(candidate, column);
                    Assert.That(selectedRows.Length, Is.InRange(1, 5),
                        $"Stage {stage} lane {column} has no bounded barrier selection.");
                    int selectedNormalCount = 0;
                    for (int row = 0; row < definition.BrickRows; row++)
                    {
                        StageBrickCell cell = definition.GetCell(row, column);
                        Assert.That(cell.Present, Is.True, $"Stage {stage} has an empty cell at ({row}, {column}).");
                        Assert.That(cell.Level, Is.GreaterThanOrEqualTo(balance.MinimumBrickLevelForStage(stage, bricks.LevelCount)),
                            $"Stage {stage} exposes an obsolete low-level brick at ({row}, {column}).");
                        Assert.That(cell.Level, Is.LessThanOrEqualTo(bricks.LevelCount));
                        bool selectedNormal = cell.Kind == BrickKind.Normal
                            && System.Array.IndexOf(selectedRows, row) >= 0;
                        if (selectedNormal) selectedNormalCount++;
                        int allowedLevel = selectedNormal ? System.Math.Max(normalCap, barrierLevel) : normalCap;
                        Assert.That(cell.Level, Is.LessThanOrEqualTo(allowedLevel),
                            $"Stage {stage} has an unauthorized elevated Brick at ({row}, {column}).");
                        lowestLevel = System.Math.Min(lowestLevel, cell.Level);
                        highestLevel = System.Math.Max(highestLevel, cell.Level);
                        if (cell.Kind == BrickKind.Gold) { gold++; hasSpecial = true; }
                        if (cell.Kind == BrickKind.Item) { item++; hasSpecial = true; }
                        cost += System.Math.Max(0, (bricks.HealthFor(cell.Level, cell.Kind) - 1) / damage);
                        if (column == rewardLaneIndex)
                        {
                            rewardLaneLowestLevel = System.Math.Min(rewardLaneLowestLevel, cell.Level);
                            rewardLaneHighestLevel = System.Math.Max(rewardLaneHighestLevel, cell.Level);
                            if (cell.Kind == BrickKind.Gold) rewardLaneGold++;
                            if (cell.Kind == BrickKind.Item) rewardLaneItem++;
                        }
                    }
                    Assert.That(selectedNormalCount, Is.EqualTo(selectedRows.Length),
                        $"Stage {stage} lane {column} does not retain every selected barrier as a normal Brick.");
                    if (cost + definition.BrickRows <= 3 * (bounces + 1) * balance.DropBudgetFor(stage))
                    {
                        safeRoute = true;
                        if (hasSpecial) rewardRoute = true;
                    }
                }
                Assert.That(gold, Is.GreaterThan(0), $"Stage {stage} has no Gold Brick.");
                Assert.That(item, Is.GreaterThan(0), $"Stage {stage} has no Item Brick.");
                Assert.That(highestLevel, Is.GreaterThan(lowestLevel), $"Stage {stage} has no brick level variety.");
                Assert.That(safeRoute, Is.True, $"Stage {stage} has no planned-level route within its drop budget.");
                Assert.That(rewardRoute, Is.True, $"Stage {stage} has no planned-level reward route within its drop budget.");
                Assert.That(rewardLaneGold, Is.GreaterThan(0), $"Stage {stage} reward lane has no Gold Brick.");
                Assert.That(rewardLaneItem, Is.GreaterThan(0), $"Stage {stage} reward lane has no Item Brick.");
                Assert.That(rewardLaneHighestLevel, Is.GreaterThan(rewardLaneLowestLevel),
                    $"Stage {stage} reward lane is uniformly low level.");
            }
        }

        [Test]
        public void StageFortyThreeBulkTuning_DoesNotReintroduceLevelOneRoute()
        {
            var balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>("Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            var cats = AssetDatabase.LoadAssetAtPath<CatConfig>("Assets/Projects/Assets/Configuration/CatConfig.asset");
            var bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>("Assets/Projects/Assets/Configuration/BrickConfig.asset");
            int minimum = balance.MinimumBrickLevelForStage(43, bricks.LevelCount);
            int maximum = balance.MaximumBrickLevelForStage(43, bricks.LevelCount);
            Assert.That(minimum, Is.EqualTo(5));
            StageCandidate source = StageCandidateGenerator.Generate(new StageGenerationRequest
            {
                Seed = 43, StageIndex = 43, ExpectedCatLevel = 6, CandidateCount = 1,
                MinimumBrickLevel = minimum, MaximumBrickLevel = maximum
            }, bricks)[0];
            int level = StageBulkGenerator.SoloRouteCatLevel(43);
            StageCandidate tuned = StageBulkGenerator.Tune(source, 43, cats.ImpactDamageFor(level),
                cats.BounceCountFor(level), maximum, bricks, minimum);
            StageCandidate reinforced = StageLaneBarrierPolicy.Reinforce(tuned, 43, bricks, cats);
            foreach (StageBrickCell cell in reinforced.Cells)
                Assert.That(cell.Level, Is.GreaterThanOrEqualTo(minimum));
            var baseline = StageCandidateSimulator.Evaluate(reinforced, 43,
                StageBulkGenerator.ExpectedCatLevel(43), 480, .05f, bricks, cats, balance,
                expectedStackedOnly: true);
            Assert.That(baseline.Wins, Is.Zero);
            var result = StageCandidateSimulator.Evaluate(reinforced, 43, level, 480, .05f, bricks, cats,
                balance, expectedStackedOnly: true, maximumDrops: balance.DropBudgetFor(43));
            Assert.That(result.Wins, Is.GreaterThan(0));
            Assert.That(result.GoldTickets + result.ItemTickets, Is.GreaterThan(0));
        }

        [Test]
        public void GeneratedBrickGold_DependsOnLevelOnly()
        {
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            int weakGold = bricks.GoldForLevel(1);
            int highLevelGold = bricks.GoldForLevel(10);
            Assert.That(highLevelGold, Is.GreaterThan(weakGold));
            using var world = new World("GeneratedStageGoldTest");
            foreach (int stage in new[] { 1, 1000 })
            {
                Entity run = DropStageFactory.Create(world.EntityManager, bricks, balance, stage);
                EntityQuery query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
                using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
                int normalCount = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Brick brick = world.EntityManager.GetComponentData<Brick>(entities[i]);
                    if (brick.Stage != run || brick.Kind != BrickKind.Normal) continue;
                    Assert.That(brick.GoldReward,
                        Is.EqualTo(bricks.GoldForLevel(brick.Level)));
                    normalCount++;
                }
                Assert.That(normalCount, Is.GreaterThan(0));
                DropStageFactory.Destroy(world.EntityManager, run);
            }
        }

        [Test]
        public void RepresentativeStages_AllowExpectedPlusOneLevelStackedClearInRealDropSimulation()
        {
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            CatConfig cats = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            foreach (int stage in new[] { 1, 4, 10, 16, 42, 43, 44, 64, 100, 500, 1000 })
            {
                StageDefinition definition = balance.GetStageDefinition(stage);
                var cells = new StageBrickCell[definition.BoardColumns * definition.BrickRows];
                for (int row = 0; row < definition.BrickRows; row++)
                for (int column = 0; column < definition.BoardColumns; column++)
                    cells[row * definition.BoardColumns + column] = definition.GetCell(row, column);
                int rewardLane = StageBulkGenerator.RewardLaneForStage(stage, definition.BoardColumns);
                var candidate = new StageCandidate(stage, definition.BoardColumns,
                    definition.BrickRows, cells, rewardLane, (rewardLane + 1) % definition.BoardColumns);
                StageCandidateMetrics result = StageCandidateSimulator.Evaluate(candidate, stage,
                    StageBulkGenerator.SoloRouteCatLevel(stage), 480, .05f, bricks, cats, balance,
                    expectedStackedOnly: true, maximumDrops: balance.DropBudgetFor(stage));
                Assert.That(result.Wins, Is.GreaterThan(0),
                    $"Stage {stage} has no expected-level-plus-one stacked clear in the ECS simulation.");
                Assert.That(result.GoldTickets + result.ItemTickets, Is.GreaterThan(0),
                    $"Stage {stage} has no reachable special-brick reward.");
            }
        }
    }
}
