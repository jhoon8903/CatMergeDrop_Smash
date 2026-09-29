using System;
using System.Collections.Generic;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    /// <summary>Places normal-brick checkpoints throughout a lane while preserving reward counts and the stage shape.</summary>
    public static class StageLaneBarrierPolicy
    {
        public static int BarrierLevelForStage(int stage, BrickConfig bricks, CatConfig cats)
        {
            int level = StageBalanceConfig.ExpectedCatLevelForStage(stage);
            // Scale against the baseline cat's finite impact budget, not stage number alone.
            long target = (17L * cats.ImpactDamageFor(level) * (cats.BounceCountFor(level) + 1L) + 19L) / 20L;
            for (int brickLevel = 1; brickLevel <= bricks.LevelCount; brickLevel++)
                if (bricks.HealthForLevel(brickLevel) >= target) return brickLevel;
            return bricks.LevelCount;
        }

        public static int[] SelectedRows(StageCandidate source, int lane)
        {
            int[] targets = lane % 2 != 0
                ? new[] { 2, 3, 5, 7, 9 } : new[] { 2, 4, 6, 8, 9 };
            var selected = new List<int>();
            foreach (int target in targets)
            {
                int row = Math.Min(source.BrickRows - 1, target);
                if (row >= 2 && !selected.Contains(row)) selected.Add(row);
            }
            return selected.ToArray();
        }

        public static StageCandidate Reinforce(StageCandidate source, int stage, BrickConfig bricks, CatConfig cats)
        {
            if (source == null || bricks == null || cats == null) throw new ArgumentNullException();
            StageBrickCell[] cells = (StageBrickCell[])source.Cells.Clone();
            int level = BarrierLevelForStage(stage, bricks, cats);
            var protectedCells = new HashSet<int>();
            for (int lane = 0; lane < source.BoardColumns; lane++)
                foreach (int row in SelectedRows(source, lane)) protectedCells.Add(row * source.BoardColumns + lane);
            for (int lane = 0; lane < source.BoardColumns; lane++)
                foreach (int row in SelectedRows(source, lane))
                {
                    int index = row * source.BoardColumns + lane;
                    if (!cells[index].Present) continue;
                    if (cells[index].Kind != BrickKind.Normal)
                    {
                        int donor = FindRewardDestination(cells, protectedCells, source.BoardColumns, lane, row, true);
                        if (donor < 0) donor = FindRewardDestination(cells, protectedCells, source.BoardColumns, lane, row, false);
                        if (donor < 0) throw new InvalidOperationException("Not enough ordinary cells to preserve rewards outside barriers.");
                        cells[donor].Kind = cells[index].Kind;
                        cells[index].Kind = BrickKind.Normal;
                    }
                    cells[index].Level = Mathf.Max(cells[index].Level, level);
                }
            return new StageCandidate(source.Id, source.BoardColumns, source.BrickRows,
                cells, source.EasyLane, source.RiskRewardLane);
        }
        private static int FindRewardDestination(StageBrickCell[] cells, HashSet<int> protectedCells,
            int columns, int lane, int row, bool preserveFront)
        {
            int best = -1, score = int.MaxValue;
            for (int i = 0; i < cells.Length; i++)
            {
                if (protectedCells.Contains(i) || !cells[i].Present || cells[i].Kind != BrickKind.Normal ||
                    (preserveFront && i / columns < 2)) continue;
                int candidateScore = (i % columns == lane ? 100000 : 0) + Math.Abs(i / columns - row) * cells.Length + i;
                if (candidateScore < score) { best = i; score = candidateScore; }
            }
            return best;
        }
    }
}
