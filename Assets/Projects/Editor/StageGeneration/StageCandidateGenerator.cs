using System;
using System.Collections.Generic;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    [Serializable]
    public struct StageGenerationRequest
    {
        public int Seed;
        public int StageIndex;
        public int ExpectedCatLevel;
        public int CandidateCount;
        public int PreviousBoardColumns;
        public int MaximumBrickLevel;
        public int MinimumBrickLevel;
    }

    public sealed class StageCandidate
    {
        public readonly int Id;
        public readonly int BoardColumns;
        public readonly int BrickRows;
        public readonly StageBrickCell[] Cells;
        public readonly int EasyLane;
        public readonly int RiskRewardLane;

        internal StageCandidate(int id, int boardColumns, int brickRows, StageBrickCell[] cells,
            int easyLane, int riskRewardLane)
        {
            Id = id;
            BoardColumns = boardColumns;
            BrickRows = brickRows;
            Cells = cells;
            EasyLane = easyLane;
            RiskRewardLane = riskRewardLane;
        }

        public void Configure(StageDefinition definition, int stageIndex)
            => definition.Configure(stageIndex, BoardColumns, BrickRows, Cells);

        public int Count(BrickKind kind)
        {
            int count = 0;
            for (int i = 0; i < Cells.Length; i++)
                if (Cells[i].Present && Cells[i].Kind == kind) count++;
            return count;
        }

        public int RouteCostForLane(int lane, int damage, BrickConfig bricks)
        {
            int safeDamage = Mathf.Max(1, damage);
            int cost = 0;
            for (int row = 0; row < BrickRows; row++)
            {
                StageBrickCell cell = Cells[row * BoardColumns + lane];
                if (!cell.Present) continue;
                int health = bricks.HealthFor(cell.Level, cell.Kind);
                cost += Mathf.Max(0, (health + safeDamage - 1) / safeDamage - 1);
            }
            return cost;
        }
    }

    public static class StageCandidateGenerator
    {
        public static IReadOnlyList<StageCandidate> Generate(StageGenerationRequest request, BrickConfig bricks)
        {
            if (bricks == null) throw new ArgumentNullException(nameof(bricks));
            int candidateCount = Mathf.Clamp(request.CandidateCount, 1, 128);
            int expectedLevel = Mathf.Max(1, request.ExpectedCatLevel);
            int maximumBrickLevel = request.MaximumBrickLevel > 0
                ? Mathf.Min(bricks.LevelCount, request.MaximumBrickLevel) : bricks.LevelCount;
            int minimumBrickLevel = request.MinimumBrickLevel > 0
                ? Mathf.Clamp(request.MinimumBrickLevel, 1, maximumBrickLevel)
                : Mathf.Clamp(StageBalanceConfig.ExpectedCatLevelForStage(request.StageIndex) - 1, 1, maximumBrickLevel);
            int minimumColumns = request.PreviousBoardColumns > 0
                ? Mathf.Max(3, request.PreviousBoardColumns - 1) : 3;
            int maximumColumns = request.PreviousBoardColumns > 0
                ? Mathf.Min(MergeBoardModel.Columns, request.PreviousBoardColumns + 1) : MergeBoardModel.Columns;
            var random = new System.Random(request.Seed);
            var candidates = new List<StageCandidate>(candidateCount);
            for (int id = 0; id < candidateCount; id++)
            {
                int columns = random.Next(minimumColumns, maximumColumns + 1);
                int maximumRows = Mathf.Min(WorldPlayfieldLayout.BrickRows,
                    WorldPlayfieldLayout.BrickCount / columns);
                // A complete playfield makes route costs visible during generation.
                int rows = maximumRows;
                int easyLane = random.Next(columns);
                int riskLane = (easyLane + random.Next(1, columns)) % columns;
                StageBrickCell[] cells = BuildCells(random, columns, rows, expectedLevel,
                    easyLane, riskLane, minimumBrickLevel, maximumBrickLevel, bricks);
                candidates.Add(new StageCandidate(id + 1, columns, rows, cells, easyLane, riskLane));
            }
            return candidates;
        }

        public static StageCandidate Mutate(StageCandidate source, int seed, int id, BrickConfig bricks,
            int maximumBrickLevel = 0, int minimumBrickLevel = 1)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (bricks == null) throw new ArgumentNullException(nameof(bricks));
            int levelLimit = maximumBrickLevel > 0
                ? Mathf.Min(bricks.LevelCount, maximumBrickLevel) : bricks.LevelCount;
            int levelFloor = Mathf.Clamp(minimumBrickLevel, 1, levelLimit);
            var random = new System.Random(seed);
            StageBrickCell[] cells = (StageBrickCell[])source.Cells.Clone();
            int mutations = Mathf.Max(1, cells.Length / 8);
            for (int i = 0; i < mutations; i++)
            {
                int index = random.Next(cells.Length);
                StageBrickCell cell = cells[index];
                if (!cell.Present)
                {
                    cell.Present = true;
                    cell.Kind = BrickKind.Normal;
                    cell.Level = random.Next(levelFloor, levelLimit + 1);
                }
                else if (random.Next(2) == 0)
                    cell.Level = Mathf.Clamp(cell.Level + random.Next(-2, 3), levelFloor, levelLimit);
                else
                    cell.Kind = cell.Kind == BrickKind.Normal
                        ? (random.Next(2) == 0 ? BrickKind.Gold : BrickKind.Item)
                        : BrickKind.Normal;
                cell.Level = Mathf.Clamp(cell.Level, levelFloor, levelLimit);
                cells[index] = cell;
            }
            ClampLevels(cells, levelFloor, levelLimit);
            return new StageCandidate(id, source.BoardColumns, source.BrickRows, cells,
                source.EasyLane, source.RiskRewardLane);
        }

        private static StageBrickCell[] BuildCells(System.Random random, int columns, int rows,
            int expectedLevel, int easyLane, int riskLane, int minimumBrickLevel, int maximumBrickLevel,
            BrickConfig bricks)
        {
            var cells = new StageBrickCell[columns * rows];
            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int health = column == easyLane ? 1 : column == riskLane
                        ? Mathf.Max(1, expectedLevel + random.Next(expectedLevel, expectedLevel * 3 + 1))
                        : Mathf.Max(1, expectedLevel + random.Next(-1, expectedLevel + 2));
                    cells[row * columns + column] = new StageBrickCell
                    {
                        Present = true,
                        Kind = BrickKind.Normal,
                        Level = Mathf.Clamp(bricks.LevelClosestToHealth(health), minimumBrickLevel, maximumBrickLevel)
                    };
                }
            }

            var riskCells = new List<int>();
            var otherCells = new List<int>();
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Present)
                    (i % columns == riskLane ? riskCells : otherCells).Add(i);
            AssignSpecials(random, cells, riskCells, otherCells, BrickKind.Gold);
            AssignSpecials(random, cells, riskCells, otherCells, BrickKind.Item);
            ClampLevels(cells, minimumBrickLevel, maximumBrickLevel);
            return cells;
        }

        private static void ClampLevels(StageBrickCell[] cells, int minimumBrickLevel, int maximumBrickLevel)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                StageBrickCell cell = cells[i];
                cell.Level = Mathf.Clamp(cell.Level, minimumBrickLevel, maximumBrickLevel);
                cells[i] = cell;
            }
        }

        private static void AssignSpecials(System.Random random, StageBrickCell[] cells,
            List<int> riskCells, List<int> otherCells, BrickKind kind)
        {
            int count = Mathf.Min(3, riskCells.Count + otherCells.Count);
            count = count == 0 ? 0 : random.Next(1, count + 1);
            for (int i = 0; i < count; i++)
            {
                List<int> source = riskCells.Count > 0 ? riskCells : otherCells;
                int pick = random.Next(source.Count);
                int index = source[pick];
                StageBrickCell cell = cells[index];
                cell.Kind = kind;
                cells[index] = cell;
                source.RemoveAt(pick);
            }
        }
    }
}
