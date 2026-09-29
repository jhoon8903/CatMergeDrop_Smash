using NUnit.Framework;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public sealed class StageGenerationTests
    {
        private BrickConfig _bricks;

        [SetUp]
        public void SetUp() => _bricks = ScriptableObject.CreateInstance<BrickConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_bricks);

        [Test]
        public void StageFourCandidate_RespectsBrickProgressionCap()
        {
            StageBalanceConfig balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
            try
            {
                int maximum = balance.MaximumBrickLevelForStage(4, _bricks.LevelCount);
                Assert.That(maximum, Is.EqualTo(4));
                var candidates = StageCandidateGenerator.Generate(new StageGenerationRequest
                {
                    Seed = 20260928, StageIndex = 4, ExpectedCatLevel = 3,
                    CandidateCount = 8, MaximumBrickLevel = maximum
                }, _bricks);
                foreach (StageCandidate candidate in candidates)
                foreach (StageBrickCell cell in candidate.Cells)
                    Assert.That(cell.Level, Is.InRange(1, maximum));
            }
            finally { Object.DestroyImmediate(balance); }
        }

        [Test]
        public void StageFortyThreeCandidateAndMutation_RespectBrickLevelFloorAndCap()
        {
            const int minimum = 5;
            const int maximum = 8;
            StageCandidate generated = StageCandidateGenerator.Generate(new StageGenerationRequest
            {
                Seed = 43, StageIndex = 43, ExpectedCatLevel = 6, CandidateCount = 1,
                MinimumBrickLevel = minimum, MaximumBrickLevel = maximum
            }, _bricks)[0];
            foreach (StageBrickCell cell in generated.Cells)
                Assert.That(cell.Level, Is.InRange(minimum, maximum));

            for (int i = 0; i < generated.Cells.Length; i++)
                generated.Cells[i].Level = i % 2 == 0 ? 1 : 10;
            StageCandidate mutated = StageCandidateGenerator.Mutate(generated, 43, 2, _bricks, maximum, minimum);
            foreach (StageBrickCell cell in mutated.Cells)
                Assert.That(cell.Level, Is.InRange(minimum, maximum));
        }

        [Test]
        public void Generator_IsDeterministicForTheSameRequest()
        {
            var request = new StageGenerationRequest
            {
                Seed = 84, StageIndex = 7, ExpectedCatLevel = 4, CandidateCount = 4
            };
            var first = StageCandidateGenerator.Generate(request, _bricks);
            var second = StageCandidateGenerator.Generate(request, _bricks);
            Assert.That(first.Count, Is.EqualTo(second.Count));
            for (int candidate = 0; candidate < first.Count; candidate++)
            {
                Assert.That((first[candidate].BoardColumns, first[candidate].BrickRows),
                    Is.EqualTo((second[candidate].BoardColumns, second[candidate].BrickRows)));
                for (int cell = 0; cell < first[candidate].Cells.Length; cell++)
                    Assert.That(JsonUtility.ToJson(first[candidate].Cells[cell]),
                        Is.EqualTo(JsonUtility.ToJson(second[candidate].Cells[cell])));
                Assert.That(first[candidate].EasyLane, Is.Not.EqualTo(first[candidate].RiskRewardLane));
                int lowestLevel = int.MaxValue;
                int highestLevel = 0;
                foreach (StageBrickCell cell in first[candidate].Cells)
                {
                    Assert.That(cell.Present, Is.True, "Every brick field cell should be filled.");
                    lowestLevel = System.Math.Min(lowestLevel, cell.Level);
                    highestLevel = System.Math.Max(highestLevel, cell.Level);
                }
                Assert.That(highestLevel, Is.GreaterThan(lowestLevel), "Brick levels should vary within a stage.");
                Assert.That(first[candidate].Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold), Is.GreaterThan(0));
                Assert.That(first[candidate].Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item), Is.GreaterThan(0));
                Assert.That(first[candidate].RouteCostForLane(first[candidate].RiskRewardLane, 4, _bricks),
                    Is.GreaterThan(first[candidate].RouteCostForLane(first[candidate].EasyLane, 4, _bricks)));
            }
        }

        [Test]
        public void Generator_KeepsColumnsWithinOneOfPreviousStage()
        {
            var candidates = StageCandidateGenerator.Generate(new StageGenerationRequest
            {
                Seed = 3, StageIndex = 8, ExpectedCatLevel = 4, CandidateCount = 20, PreviousBoardColumns = 6
            }, _bricks);
            for (int i = 0; i < candidates.Count; i++)
                Assert.That(candidates[i].BoardColumns, Is.InRange(5, 6));
        }

        [Test]
        public void Simulator_EvaluatesEveryCandidateLane()
        {
            StageCandidate candidate = StageCandidateGenerator.Generate(new StageGenerationRequest
            {
                Seed = 22, StageIndex = 1, ExpectedCatLevel = 3, CandidateCount = 1
            }, _bricks)[0];
            CatConfig cats = ScriptableObject.CreateInstance<CatConfig>();
            StageBalanceConfig balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
            try
            {
                StageCandidateMetrics result = StageCandidateSimulator.Evaluate(candidate, 1, 3,
                    600, .05f, _bricks, cats, balance);
                Assert.That(result.Runs, Is.EqualTo(candidate.BoardColumns * 6));
                Assert.That(result.Frames, Is.InRange(0, result.Runs * 600));
                Assert.That(result.Wins, Is.InRange(0, result.Runs));
                Assert.That(result.DistinctWinningLanes, Is.InRange(0, candidate.BoardColumns));
                Assert.That(result.Scenarios, Has.Length.EqualTo(6));
                StageCandidateMetrics stacked = StageCandidateSimulator.Evaluate(candidate, 1, 3,
                    600, .05f, _bricks, cats, balance, expectedStackedOnly: true);
                Assert.That(stacked.Runs, Is.EqualTo(candidate.BoardColumns));
                Assert.That(stacked.Scenarios, Has.Length.EqualTo(1));
                Assert.That(stacked.Wins, Is.EqualTo(result.Scenarios[3].Wins));
            }
            finally
            {
                Object.DestroyImmediate(cats);
                Object.DestroyImmediate(balance);
            }
        }
    }
}
