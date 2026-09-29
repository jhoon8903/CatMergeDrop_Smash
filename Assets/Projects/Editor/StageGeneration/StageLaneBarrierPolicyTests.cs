using NUnit.Framework;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public sealed class StageLaneBarrierPolicyTests
    {
        [TestCase(1)] [TestCase(40)] [TestCase(43)] [TestCase(999)]
        public void Reinforcement_IsIdempotentAndPreservesShapeAndRewardCounts(int stage)
        {
            var balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>("Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            var bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>("Assets/Projects/Assets/Configuration/BrickConfig.asset");
            var cats = AssetDatabase.LoadAssetAtPath<CatConfig>("Assets/Projects/Assets/Configuration/CatConfig.asset");
            StageDefinition definition = balance.GetStageDefinition(stage);
            var cells = new StageBrickCell[definition.BoardColumns * definition.BrickRows];
            for (int i = 0; i < cells.Length; i++) cells[i] = definition.GetCell(i / definition.BoardColumns, i % definition.BoardColumns);
            var original = new StageCandidate(stage, definition.BoardColumns, definition.BrickRows, cells, 0, 1);
            var once = StageLaneBarrierPolicy.Reinforce(original, stage, bricks, cats);
            var twice = StageLaneBarrierPolicy.Reinforce(once, stage, bricks, cats);
            Assert.That(once.Count(BrickKind.Gold), Is.EqualTo(original.Count(BrickKind.Gold)));
            Assert.That(once.Count(BrickKind.Item), Is.EqualTo(original.Count(BrickKind.Item)));
            CollectionAssert.AreEqual(once.Cells, twice.Cells);
            for (int i = 0; i < cells.Length; i++)
            {
                Assert.That(once.Cells[i].Present, Is.EqualTo(cells[i].Present));
                Assert.That(once.Cells[i].Level, Is.GreaterThanOrEqualTo(cells[i].Level));
            }
            for (int lane = 0; lane < definition.BoardColumns; lane++)
                foreach (int row in StageLaneBarrierPolicy.SelectedRows(once, lane))
                {
                    StageBrickCell barrier = once.Cells[row * definition.BoardColumns + lane];
                    Assert.That(barrier.Kind, Is.EqualTo(BrickKind.Normal));
                    Assert.That(barrier.Level, Is.GreaterThanOrEqualTo(StageLaneBarrierPolicy.BarrierLevelForStage(stage, bricks, cats)));
                }
        }
        [Test]
        public void ClonedDefinition_ConfigurePreservesRevisionHistoryAndAvoidsIdempotentBumps()
        {
            const int columns = 3;
            const int rows = 10;
            StageDefinition original = ScriptableObject.CreateInstance<StageDefinition>();
            StageDefinition clone = null;
            try
            {
                original.Configure(43, columns, rows, Cells(columns, rows, 1));
                original.Configure(43, columns, rows, Cells(columns, rows, 2));
                Assert.That(original.CatalogRevision, Is.EqualTo(1));

                clone = Object.Instantiate(original);
                StageBrickCell[] thirdRevision = Cells(columns, rows, 3);
                clone.Configure(43, columns, rows, thirdRevision);

                Assert.That(clone.CatalogRevision, Is.EqualTo(2));
                Assert.That(clone.GetCell(2, 0, 0).Level, Is.EqualTo(1));
                Assert.That(clone.GetCell(2, 0, 1).Level, Is.EqualTo(2));
                Assert.That(clone.GetCell(2, 0, 2).Level, Is.EqualTo(3));

                clone.Configure(43, columns, rows, thirdRevision);
                Assert.That(clone.CatalogRevision, Is.EqualTo(2));
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                Object.DestroyImmediate(original);
            }
        }

        private static StageBrickCell[] Cells(int columns, int rows, int level)
        {
            var cells = new StageBrickCell[columns * rows];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new StageBrickCell { Present = true, Kind = BrickKind.Normal, Level = level };
            return cells;
        }

    }
}
