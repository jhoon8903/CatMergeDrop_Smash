using System;
using System.Collections.Generic;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    /// <summary>Applies the stage level ceiling to the existing generated catalog.</summary>
    public static class StageBrickLevelCapMigration
    {
        private sealed class FloorJob
        {
            public StageBalanceConfig Balance;
            public BrickConfig Bricks;
            public CatConfig Cats;
            public int Stage = 1;
            public int ChangedCells;
            public readonly List<(StageDefinition definition, StageBrickCell[] cells)> Changes = new();
        }

        private static FloorJob _floorJob;
        public static bool IsFloorRunning => _floorJob != null;
        public static string FloorStatus { get; private set; } = "Not started";

        [MenuItem("Tools/Cat Merge Drop/Apply Progression Floor to 1000 Stages")]
        public static void ApplyFloor()
        {
            if (_floorJob != null) throw new InvalidOperationException("Floor validation is already running.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before changing the stage catalog.");
            var job = new FloorJob
            {
                Balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>("Assets/Projects/Assets/Configuration/StageBalanceConfig.asset"),
                Bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>("Assets/Projects/Assets/Configuration/BrickConfig.asset"),
                Cats = AssetDatabase.LoadAssetAtPath<CatConfig>("Assets/Projects/Assets/Configuration/CatConfig.asset")
            };
            if (job.Balance == null || job.Bricks == null || job.Cats == null)
                throw new InvalidOperationException("Stage, Brick and Cat configs are required.");
            _floorJob = job;
            FloorStatus = "Validating stage 1/1000; no assets changed";
            EditorApplication.update += AdvanceFloor;
        }

        private static void AdvanceFloor()
        {
            FloorJob job = _floorJob;
            if (job == null) { EditorApplication.update -= AdvanceFloor; return; }
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Editor state changed during floor validation.");
                for (int batch = 0; batch < 3 && job.Stage <= 1000; batch++, job.Stage++)
                {
                    StageDefinition definition = job.Balance.GetStageDefinition(job.Stage);
                    if (definition == null || !AssetDatabase.GetAssetPath(definition).Contains("GeneratedStages_001_1000"))
                        throw new InvalidOperationException($"Stage {job.Stage} is not in the generated catalog.");
                    int minimum = job.Balance.MinimumBrickLevelForStage(job.Stage, job.Bricks.LevelCount);
                    var cells = new StageBrickCell[definition.BoardColumns * definition.BrickRows];
                    bool changed = false;
                    for (int row = 0; row < definition.BrickRows; row++)
                    for (int lane = 0; lane < definition.BoardColumns; lane++)
                    {
                        StageBrickCell cell = definition.GetCell(row, lane);
                        if (cell.Present && cell.Level < minimum)
                        {
                            cell.Level = minimum;
                            changed = true;
                            job.ChangedCells++;
                        }
                        cells[row * definition.BoardColumns + lane] = cell;
                    }
                    if (!changed) continue;
                    int rewardLane = StageBulkGenerator.RewardLaneForStage(job.Stage, definition.BoardColumns);
                    var candidate = new StageCandidate(job.Stage, definition.BoardColumns,
                        definition.BrickRows, cells, rewardLane, (rewardLane + 1) % definition.BoardColumns);
                    StageCandidateMetrics result = StageCandidateSimulator.Evaluate(candidate, job.Stage,
                        StageBulkGenerator.SoloRouteCatLevel(job.Stage), 480, .05f, job.Bricks, job.Cats,
                        job.Balance, rewardLane, expectedStackedOnly: true);
                    if (result.Wins == 0 || result.GoldTickets + result.ItemTickets == 0)
                        throw new InvalidOperationException($"Stage {job.Stage} has no validated stacked clear/reward after applying floor {minimum}.");
                    job.Changes.Add((definition, cells));
                }
                FloorStatus = $"Validated {Mathf.Min(1000, job.Stage - 1)}/1000; no assets changed";
                if (job.Stage <= 1000) return;
                // Keep existing object identities, stage layouts, special kinds and save data.
                // Nothing is written until every changed stage passes real ECS simulation.
                foreach (var change in job.Changes)
                {
                    change.definition.Configure(change.definition.StageIndex, change.definition.BoardColumns,
                        change.definition.BrickRows, change.cells);
                    EditorUtility.SetDirty(change.definition);
                }
                AssetDatabase.SaveAssets();
                FloorStatus = $"Complete: {job.Changes.Count} stages, {job.ChangedCells} cells raised; all changed stages passed ECS stacked-clear/reward validation.";
                Debug.Log(FloorStatus);
                _floorJob = null;
                EditorApplication.update -= AdvanceFloor;
            }
            catch (Exception error)
            {
                FloorStatus = "Failed: " + error.Message;
                _floorJob = null;
                EditorApplication.update -= AdvanceFloor;
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Cat Merge Drop/Apply Brick Level Caps to 1000 Stages")]
        public static void Apply()
        {
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            CatConfig cats = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            if (balance == null || bricks == null || cats == null)
                throw new InvalidOperationException("Stage, Brick and Cat configs are required.");

            var changes = new List<(StageDefinition definition, StageBrickCell[] cells)>();
            int changedCells = 0;
            for (int stage = 1; stage <= 1000; stage++)
            {
                StageDefinition definition = balance.GetStageDefinition(stage);
                if (definition == null || !AssetDatabase.GetAssetPath(definition).Contains("GeneratedStages_001_1000"))
                    throw new InvalidOperationException($"Stage {stage} is not in the generated catalog.");
                int columns = definition.BoardColumns;
                int rows = definition.BrickRows;
                int maximum = balance.MaximumBrickLevelForStage(stage, bricks.LevelCount);
                var cells = new StageBrickCell[columns * rows];
                bool changed = false;
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    StageBrickCell cell = definition.GetCell(row, column);
                    if (cell.Present && cell.Level > maximum)
                    {
                        cell.Level = maximum;
                        changed = true;
                        changedCells++;
                    }
                    cells[index] = cell;
                }

                int lane = StageBulkGenerator.RewardLaneForStage(stage, columns);
                int catLevel = StageBulkGenerator.SoloRouteCatLevel(stage);
                int damage = cats.ImpactDamageFor(catLevel);
                int bounceBudget = cats.BounceCountFor(catLevel);
                int routeCost = 0;
                for (int row = 0; row < rows; row++)
                {
                    StageBrickCell cell = cells[row * columns + lane];
                    if (cell.Present)
                        routeCost += Mathf.Max(0, (bricks.HealthFor(cell.Level, cell.Kind) - 1) / damage);
                }
                if (routeCost < 1 || routeCost > bounceBudget)
                    throw new InvalidOperationException(
                        $"Stage {stage} reward lane cost {routeCost} exceeds the playable range 1..{bounceBudget}.");
                if (changed) changes.Add((definition, cells));
            }

            foreach ((StageDefinition definition, StageBrickCell[] cells) in changes)
            {
                definition.Configure(definition.StageIndex, definition.BoardColumns,
                    definition.BrickRows, cells);
                EditorUtility.SetDirty(definition);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Brick level caps applied: {changes.Count} stages, {changedCells} cells adjusted.");
        }
    }
}
