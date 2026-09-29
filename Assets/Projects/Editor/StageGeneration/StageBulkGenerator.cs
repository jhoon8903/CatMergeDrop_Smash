using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    /// <summary>Generates a complete, reproducible stage set without committing partial output.</summary>
    public static class StageBulkGenerator
    {
        private const int StageCount = 1000;
        private const int Seed = 20260928;
        private const int CandidateCount = 4;
        private const int MaximumSteps = 480;
        private const float FixedDeltaTime = .05f;
        private const string CatalogPath = "Assets/Projects/Assets/Configuration/GeneratedStages_001_1000.asset";
        private const string ReportPath = "Library/StageGeneration/stage-production-001-1000.txt";
        private static Job _job;

        private sealed class Job
        {
            public BrickConfig Bricks;
            public CatConfig Cats;
            public StageBalanceConfig Balance;
            public readonly List<StageCandidate> Candidates = new List<StageCandidate>(StageCount);
            public readonly List<string> Lines = new List<string>(StageCount + 4);
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public int Stage = 1;
            public int PreviousColumns = 4;
            public int TotalWins;
            public int TotalRuns;
            public int TotalGoldBricks;
            public int TotalItemBricks;
        }

        [MenuItem("Tools/Cat Merge Drop/Generate and Apply 1000 Stages")]
        private static void StartMenu()
        {
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            CatConfig cats = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            Start(bricks, cats, balance);
        }

        public static void Start(BrickConfig bricks, CatConfig cats, StageBalanceConfig balance)
        {
            if (_job != null) throw new InvalidOperationException("Stage production is already running.");
            if (bricks == null || cats == null || balance == null)
                throw new ArgumentNullException("Stage production requires Brick, Cat and Stage Balance assets.");
            for (int stage = 1; stage <= StageCount; stage++)
            {
                StageDefinition existing = balance.GetStageDefinition(stage);
                if (existing != null && !AssetDatabase.GetAssetPath(existing).Contains("GeneratedStages_001_1000"))
                    throw new InvalidOperationException($"Stage {stage} has an authored definition; bulk generation will not replace it.");
            }
            _job = new Job { Bricks = bricks, Cats = cats, Balance = balance };
            _job.Lines.Add($"Stage production: 1-{StageCount}; seed {Seed}; candidates {CandidateCount}; expected level 1+floor(log2(stage)); ECS Solo/Multi validation. Synthetic proxy, not measured player win rate.");
            EditorApplication.update += Advance;
        }

        public static int ExpectedCatLevel(int stage)
        {
            return StageBalanceConfig.ExpectedCatLevelForStage(stage);
        }

        /// <summary>First cat level that can traverse a full minimum-HP brick lane.</summary>
        public static int SoloRouteCatLevel(int stage) => ExpectedCatLevel(stage) + 1;

        public static int RewardLaneForStage(int stage, int columns)
        {
            int safeColumns = Mathf.Max(1, columns);
            return (int)(unchecked((uint)(Seed + stage * 7919)) % (uint)safeColumns);
        }

        private static void Advance()
        {
            Job job = _job;
            if (job == null) { EditorApplication.update -= Advance; return; }
            try
            {
                if (EditorUtility.DisplayCancelableProgressBar("Creating 1000 Playable Stages",
                    $"Stage {job.Stage}/{StageCount}", (job.Stage - 1f) / StageCount))
                {
                    Finish(job, false, "cancelled");
                    return;
                }
                // Bound each editor update; the ECS loops stay inside code and never produce per-run logs.
                for (int i = 0; i < 3 && job.Stage <= StageCount; i++) GenerateOne(job);
                if (job.Stage > StageCount)
                {
                    Apply(job);
                    Finish(job, true, "complete");
                }
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogException(error);
                Finish(job, false, "failed: " + error.Message);
            }
        }

        private static void GenerateOne(Job job)
        {
            int stage = job.Stage;
            int expected = ExpectedCatLevel(stage);
            int soloRouteLevel = SoloRouteCatLevel(stage);
            int damage = job.Cats.ImpactDamageFor(soloRouteLevel);
            int bounces = job.Cats.BounceCountFor(soloRouteLevel);
            var request = new StageGenerationRequest
            {
                Seed = unchecked(Seed + stage * 7919), StageIndex = stage,
                ExpectedCatLevel = expected, CandidateCount = CandidateCount,
                PreviousBoardColumns = job.PreviousColumns,
                MaximumBrickLevel = job.Balance.MaximumBrickLevelForStage(stage, job.Bricks.LevelCount),
                MinimumBrickLevel = job.Balance.MinimumBrickLevelForStage(stage, job.Bricks.LevelCount)
            };
            IReadOnlyList<StageCandidate> generated = StageCandidateGenerator.Generate(request, job.Bricks);
            StageCandidate selected = null;
            StageCandidateMetrics selectedMetrics = default;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < generated.Count; i++)
            {
                StageCandidate candidate = Tune(generated[i], stage, damage, bounces,
                    request.MaximumBrickLevel, job.Bricks, request.MinimumBrickLevel);
                candidate = StageLaneBarrierPolicy.Reinforce(candidate, stage, job.Bricks, job.Cats);
                StageCandidateMetrics baseline = StageCandidateSimulator.Evaluate(candidate, stage,
                    expected, MaximumSteps, FixedDeltaTime, job.Bricks, job.Cats, job.Balance,
                    expectedStackedOnly: true);
                if (baseline.Wins != 0) continue;
                StageCandidateMetrics metrics = StageCandidateSimulator.Evaluate(candidate, stage,
                    soloRouteLevel, MaximumSteps, FixedDeltaTime, job.Bricks, job.Cats, job.Balance,
                    expectedStackedOnly: true, maximumDrops: job.Balance.DropBudgetFor(stage));
                if (metrics.Wins == 0) continue;
                int specials = candidate.Count(BrickKind.Gold) + candidate.Count(BrickKind.Item);
                float score = 20f;
                score += specials;
                score += metrics.GoldTickets + metrics.ItemTickets > 0 ? 10f : 0f;
                score += metrics.Wins;
                if (score > bestScore)
                {
                    bestScore = score;
                    selected = candidate;
                    selectedMetrics = metrics;
                }
                // One validated, rewarding route is sufficient; avoid unnecessary simulations.
                if (selected != null && selectedMetrics.GoldTickets + selectedMetrics.ItemTickets > 0 &&
                    selectedMetrics.Wins > 0) break;
            }
            if (selected == null || selectedMetrics.GoldTickets + selectedMetrics.ItemTickets == 0)
                throw new InvalidOperationException($"No ECS-validated clear and special-reward route for stage {stage} (seed {request.Seed}).");
            job.Candidates.Add(selected);
            job.PreviousColumns = selected.BoardColumns;
            job.TotalWins += selectedMetrics.Wins;
            job.TotalRuns += selectedMetrics.Runs;
            job.TotalGoldBricks += selected.Count(BrickKind.Gold);
            job.TotalItemBricks += selected.Count(BrickKind.Item);
            job.Lines.Add($"{stage}: seed={request.Seed} candidate={selected.Id} size={selected.BoardColumns}x{selected.BrickRows} expectedLv={expected} soloRouteLv={soloRouteLevel} wins={selectedMetrics.Wins}/{selectedMetrics.Runs} goldBricks={selected.Count(BrickKind.Gold)} itemBricks={selected.Count(BrickKind.Item)} rewardTickets={selectedMetrics.GoldTickets + selectedMetrics.ItemTickets}");
            job.Stage++;
        }

        internal static StageCandidate Tune(StageCandidate candidate, int stage, int damage,
            int bounceCount, int maximumBrickLevel, BrickConfig bricks, int minimumBrickLevel)
        {
            StageBrickCell[] cells = (StageBrickCell[])candidate.Cells.Clone();
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].Present) continue;
                cells[i].Level = Mathf.Clamp(cells[i].Level, 1, maximumBrickLevel);
            }
            int rewardLane = RewardLaneForStage(stage, candidate.BoardColumns);
            int riskLane = (rewardLane + 1) % candidate.BoardColumns;
            SetPlayableRewardLane(cells, candidate.BoardColumns, candidate.BrickRows, rewardLane,
                stage, damage, bounceCount, maximumBrickLevel, bricks);
            EnsureLaneSpecials(cells, candidate.BoardColumns, candidate.BrickRows, rewardLane);
            SetLaneLevel(cells, candidate.BoardColumns, candidate.BrickRows, riskLane, 1);
            for (int row = 0; row < candidate.BrickRows; row++)
                cells[row * candidate.BoardColumns + riskLane].Kind = BrickKind.Normal;
            SetLaneCost(cells, candidate.BoardColumns, candidate.BrickRows, riskLane,
                damage, bounceCount, maximumBrickLevel, bricks);
            for (int lane = 0; lane < candidate.BoardColumns; lane++)
                if (lane != rewardLane && lane != riskLane)
                    SetLaneCost(cells, candidate.BoardColumns, candidate.BrickRows, lane,
                        damage, bounceCount + Mathf.Max(2, bounceCount / 3),
                        maximumBrickLevel, bricks);
            // Tuning used to reintroduce Lv.1 filler even in late stages. Apply the
            // progression floor after every lane operation, before ECS validation.
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Present)
                    cells[i].Level = Mathf.Clamp(cells[i].Level, minimumBrickLevel, maximumBrickLevel);
            return new StageCandidate(candidate.Id, candidate.BoardColumns, candidate.BrickRows,
                cells, rewardLane, riskLane);
        }

        private static void SetLaneCost(StageBrickCell[] cells, int columns, int rows, int lane,
            int damage, int targetCost, int maximumBrickLevel, BrickConfig bricks)
        {
            var indexes = new List<int>();
            for (int row = 0; row < rows; row++)
            {
                int index = row * columns + lane;
                if (cells[index].Present) indexes.Add(index);
            }
            if (indexes.Count == 0) return;
            int baseCost = targetCost / indexes.Count;
            int remainder = targetCost % indexes.Count;
            for (int i = 0; i < indexes.Count; i++)
                cells[indexes[i]].Level = bricks.LevelForRouteCost(damage,
                    baseCost + (i < remainder ? 1 : 0), maximumBrickLevel);
        }

        private static void SetLaneLevel(StageBrickCell[] cells, int columns, int rows, int lane, int level)
        {
            for (int row = 0; row < rows; row++)
                cells[row * columns + lane].Level = level;
        }

        private static void SetPlayableRewardLane(StageBrickCell[] cells, int columns, int rows, int lane,
            int stage, int damage, int bounceCount, int maximumBrickLevel, BrickConfig bricks)
        {
            SetLaneLevel(cells, columns, rows, lane, 1);
            int remainingCost = Mathf.Max(1, bounceCount - 1);
            for (int offset = 0; offset < 3; offset++)
            {
                int desiredCost = 1 + (stage + offset) % 3;
                int level = bricks.LevelForRouteCost(damage, desiredCost, maximumBrickLevel);
                int actualCost = ExtraBounceCost(level, damage, bricks);
                if (actualCost <= 0 || actualCost > remainingCost) continue;
                // Rows 0 and 1 are reserved for one-hit reward bricks.
                int row = 2 + (int)(unchecked((uint)(Seed + stage * 7919 + offset * 3)) % (uint)(rows - 2));
                cells[row * columns + lane].Kind = BrickKind.Normal;
                cells[row * columns + lane].Level = level;
                remainingCost -= actualCost;
            }
            if (remainingCost == Mathf.Max(1, bounceCount - 1))
                throw new InvalidOperationException($"No mixed playable reward lane for stage {stage}.");
        }

        private static int ExtraBounceCost(int level, int damage, BrickConfig bricks)
            => Mathf.Max(0, (bricks.HealthForLevel(level) - 1) / Mathf.Max(1, damage));

        private static void EnsureLaneSpecials(StageBrickCell[] cells, int columns, int rows, int lane)
        {
            cells[lane].Kind = BrickKind.Gold;
            cells[(rows > 1 ? columns : 0) + lane].Kind = BrickKind.Item;
        }

        public static bool IsStructurallyPlayable(StageCandidate candidate, int damage, int bounces,
            BrickConfig bricks)
        {
            if (candidate == null || candidate.BoardColumns < 3 || candidate.BoardColumns > 6)
                return false;
            if (candidate.Count(BrickKind.Gold) == 0 || candidate.Count(BrickKind.Item) == 0)
                return false;
            int safeDamage = Mathf.Max(1, damage);
            // This is only a cheap stacked-formation feasibility filter. Extra-hit
            // cost is not a solo bounce budget: lethal impacts also spend bounces.
            // The following ECS evaluation determines whether a route really clears.
            long stackedBudget = 3L * (Mathf.Max(0, bounces) + 1L);
            for (int lane = 0; lane < candidate.BoardColumns; lane++)
            {
                long hits = 0;
                for (int row = 0; row < candidate.BrickRows; row++)
                {
                    StageBrickCell cell = candidate.Cells[row * candidate.BoardColumns + lane];
                    if (cell.Present) hits += 1L + (bricks.HealthFor(cell.Level, cell.Kind) - 1L) / safeDamage;
                }
                if (hits <= stackedBudget) return true;
            }
            return false;
        }

        private static void Apply(Job job)
        {
            if (job.Candidates.Count != StageCount)
                throw new InvalidOperationException("The stage catalog is incomplete.");
            string path = AssetDatabase.GenerateUniqueAssetPath(CatalogPath);
            StageDefinition existingRoot = job.Balance.GetStageDefinition(1);
            StageDefinition root = existingRoot != null
                ? UnityEngine.Object.Instantiate(existingRoot) : ScriptableObject.CreateInstance<StageDefinition>();
            root.name = "Stage_0001";
            job.Candidates[0].Configure(root, 1);
            AssetDatabase.CreateAsset(root, path);
            var definitions = new StageDefinition[StageCount];
            definitions[0] = root;
            try
            {
                for (int i = 1; i < StageCount; i++)
                {
                    StageDefinition existing = job.Balance.GetStageDefinition(i + 1);
                    StageDefinition definition = existing != null
                        ? UnityEngine.Object.Instantiate(existing) : ScriptableObject.CreateInstance<StageDefinition>();
                    definition.name = $"Stage_{i + 1:0000}";
                    job.Candidates[i].Configure(definition, i + 1);
                    AssetDatabase.AddObjectToAsset(definition, root);
                    definitions[i] = definition;
                }
                AssetDatabase.SaveAssets();
                var serialized = new SerializedObject(job.Balance);
                SerializedProperty property = serialized.FindProperty("stageDefinitions");
                if (property == null) throw new InvalidOperationException("StageBalanceConfig.stageDefinitions is missing.");
                // Keep authored stages outside this catalog's range, if any.
                var preserved = new List<StageDefinition>();
                for (int i = 0; i < property.arraySize; i++)
                {
                    StageDefinition existing = property.GetArrayElementAtIndex(i).objectReferenceValue as StageDefinition;
                    if (existing != null && (existing.StageIndex < 1 || existing.StageIndex > StageCount))
                        preserved.Add(existing);
                }
                Undo.RecordObject(job.Balance, "Apply 1000 Generated Stages");
                property.arraySize = StageCount + preserved.Count;
                for (int i = 0; i < StageCount; i++)
                    property.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
                for (int i = 0; i < preserved.Count; i++)
                    property.GetArrayElementAtIndex(StageCount + i).objectReferenceValue = preserved[i];
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(job.Balance);
                AssetDatabase.SaveAssets();
                job.Lines.Add("Catalog: " + path);
            }
            catch
            {
                AssetDatabase.DeleteAsset(path);
                throw;
            }
        }

        private static void Finish(Job job, bool applied, string status)
        {
            EditorApplication.update -= Advance;
            EditorUtility.ClearProgressBar();
            job.Clock.Stop();
            job.Lines.Add($"Status: {status}; generated={job.Candidates.Count}; applied={applied}; wins={job.TotalWins}/{job.TotalRuns}; GoldBricks={job.TotalGoldBricks}; ItemBricks={job.TotalItemBricks}; elapsedSeconds={job.Clock.Elapsed.TotalSeconds:F2}");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllLines(ReportPath, job.Lines);
            _job = null;
            UnityEngine.Debug.Log($"Stage production {status}: {job.Candidates.Count} stages; report {ReportPath}");
        }
    }
}
