using System;
using System.Collections.Generic;
using System.IO;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public static class StageLaneBarrierMigration
    {
        private sealed class Job
        {
            public StageBalanceConfig Balance;
            public BrickConfig Bricks;
            public CatConfig Cats;
            public int Stage = 1, ChangedCells, Lanes;
            public readonly List<(StageDefinition definition, StageCandidate candidate)> Changes = new();
            public readonly List<string> Lines = new();
        }
        private static Job _job;
        public static bool IsRunning => _job != null;
        public static string Status { get; private set; } = "Not started";

        [MenuItem("Tools/Cat Merge Drop/Validate and Apply Lane Barriers")]
        public static void Start()
        {
            if (_job != null || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Finish the current job and stop Play Mode first.");
            _job = new Job
            {
                Balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>("Assets/Projects/Assets/Configuration/StageBalanceConfig.asset"),
                Bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>("Assets/Projects/Assets/Configuration/BrickConfig.asset"),
                Cats = AssetDatabase.LoadAssetAtPath<CatConfig>("Assets/Projects/Assets/Configuration/CatConfig.asset")
            };
            Status = "Validating; no assets changed";
            EditorApplication.update += Advance;
        }

        private static void Advance()
        {
            Job job = _job;
            if (job == null) { EditorApplication.update -= Advance; return; }
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    throw new InvalidOperationException("Editor state changed during validation.");
                for (int batch = 0; batch < 3 && job.Stage <= job.Balance.LastDefinedStage; batch++, job.Stage++)
                {
                    StageDefinition definition = job.Balance.GetStageDefinition(job.Stage);
                    if (definition == null) throw new InvalidOperationException("Missing stage " + job.Stage);
                    int cols = definition.BoardColumns, rows = definition.BrickRows;
                    var cells = new StageBrickCell[cols * rows];
                    for (int row = 0; row < rows; row++)
                        for (int col = 0; col < cols; col++) cells[row * cols + col] = definition.GetCell(row, col);
                    int rewardLane = StageBulkGenerator.RewardLaneForStage(job.Stage, cols);
                    var source = new StageCandidate(job.Stage, cols, rows, cells, rewardLane, (rewardLane + 1) % cols);
                    StageCandidate candidate = StageLaneBarrierPolicy.Reinforce(source, job.Stage, job.Bricks, job.Cats);
                    foreach (BrickKind kind in new[] { BrickKind.Gold, BrickKind.Item })
                        if (source.Count(kind) != candidate.Count(kind)) throw new InvalidOperationException("Reward count changed.");
                    int level = StageBalanceConfig.ExpectedCatLevelForStage(job.Stage);
                    var direct = StageCandidateSimulator.Evaluate(candidate, job.Stage, level, 480, .05f,
                        job.Bricks, job.Cats, job.Balance, expectedStackedOnly: true);
                    if (direct.Wins != 0) throw new InvalidOperationException($"Stage {job.Stage} still permits {direct.Wins} baseline one-Drop routes.");
                    var cumulative = StageCandidateSimulator.Evaluate(candidate, job.Stage, level + 1, 480, .05f,
                        job.Bricks, job.Cats, job.Balance, expectedStackedOnly: true,
                        maximumDrops: job.Balance.DropBudgetFor(job.Stage));
                    if (cumulative.Wins == 0) throw new InvalidOperationException($"Stage {job.Stage} has no cumulative prepared route.");
                    for (int i = 0; i < cells.Length; i++)
                        if (cells[i].Level != candidate.Cells[i].Level || cells[i].Kind != candidate.Cells[i].Kind) job.ChangedCells++;
                    job.Lanes += cols;
                    job.Changes.Add((definition, candidate));
                    job.Lines.Add($"Stage {job.Stage}: baseline Lv{level} x3 one-Drop {direct.Wins}/{direct.Runs}; prepared Lv{level + 1} x3 within {job.Balance.DropBudgetFor(job.Stage)} Drops {cumulative.Wins}/{cumulative.Runs}");
                }
                Status = $"Validated {job.Stage - 1}/{job.Balance.LastDefinedStage}; no assets changed";
                if (job.Stage <= job.Balance.LastDefinedStage) return;
                string output = "Library/BalanceValidation";
                Directory.CreateDirectory(output);
                var backedUp = new HashSet<string>();
                foreach (var change in job.Changes)
                {
                    string path = AssetDatabase.GetAssetPath(change.definition);
                    if (backedUp.Add(path)) File.Copy(path, Path.Combine(output, Path.GetFileName(path) + ".before-barriers"), true);
                }
                var objects = new UnityEngine.Object[job.Changes.Count];
                for (int i = 0; i < objects.Length; i++) objects[i] = job.Changes[i].definition;
                Undo.RecordObjects(objects, "Apply validated lane barriers");
                foreach (var change in job.Changes)
                {
                    change.candidate.Configure(change.definition, change.definition.StageIndex);
                    EditorUtility.SetDirty(change.definition);
                }
                AssetDatabase.SaveAssets();
                Status = $"Applied {job.Changes.Count} stages, {job.Lanes} lanes, {job.ChangedCells} changed cells; all baseline one-Drop routes blocked and each stage has a prepared cumulative route.";
                job.Lines.Insert(0, Status);
                File.WriteAllLines(Path.Combine(output, "lane-barrier-ecs-report.txt"), job.Lines);
                _job = null;
                EditorApplication.update -= Advance;
            }
            catch (Exception exception)
            {
                Status = "Failed: " + exception.Message;
                _job = null;
                EditorApplication.update -= Advance;
                Debug.LogException(exception);
            }
        }
    }
}
