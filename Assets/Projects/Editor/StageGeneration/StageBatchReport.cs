using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public static class StageBatchReport
    {
        private const string StartKey = "CatMergeDrop.StageGeneration.BatchStart";
        private const string EndKey = "CatMergeDrop.StageGeneration.BatchEnd";

        [MenuItem("Tools/Cat Merge Drop/Generate Stage Draft Report")]
        private static void GenerateDefaultReport()
        {
            BrickConfig bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>("Assets/Projects/Assets/Configuration/BrickConfig.asset");
            CatConfig cats = AssetDatabase.LoadAssetAtPath<CatConfig>("Assets/Projects/Assets/Configuration/CatConfig.asset");
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>("Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            if (bricks == null || cats == null || balance == null)
                throw new InvalidOperationException("Brick, Cat, and Stage Balance configurations are required for batch generation.");
            Run(EditorPrefs.GetInt(StartKey, 1), EditorPrefs.GetInt(EndKey, 10), 12345, 3, 8, 480, .05f,
                .55f, .75f, bricks, cats, balance);
        }

        public static string Run(int startStage, int endStage, int seed, int expectedCatLevel,
            int candidatesPerStage, int maximumSteps, float fixedDeltaTime, float targetWinRateMinimum,
            float targetWinRateMaximum, BrickConfig brickConfig, CatConfig catConfig, StageBalanceConfig balanceConfig)
        {
            if (brickConfig == null || catConfig == null || balanceConfig == null)
                throw new ArgumentNullException("Batch generation requires all configuration assets.");
            int first = Mathf.Max(1, Mathf.Min(startStage, endStage));
            int last = Mathf.Max(first, Mathf.Max(startStage, endStage));
            int candidateCount = Mathf.Clamp(candidatesPerStage, 1, 128);
            const int MaximumFullEvaluationsPerStage = 8;
            var stopwatch = Stopwatch.StartNew();
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Synthetic stage-draft proxy; stages {first}-{last}; seed {seed}; expected cat level {expectedCatLevel} is a fixed proxy, not per-stage player calibration; reinforced ordinary barriers; expected+1 stacked formations accumulate damage through each stage's drop budget; candidates {candidateCount}; full ECS evaluations capped at {MaximumFullEvaluationsPerStage} per stage.");
            bool cancelled = false;
            try
            {
                for (int stage = first; stage <= last; stage++)
                {
                    var request = new StageGenerationRequest
                    {
                        Seed = seed + stage * 7919,
                        StageIndex = stage,
                        ExpectedCatLevel = Mathf.Max(1, expectedCatLevel),
                        CandidateCount = candidateCount,
                        PreviousBoardColumns = stage > 1 ? balanceConfig.LayoutForStage(stage - 1).BoardColumns : 0,
                        MaximumBrickLevel = balanceConfig.MaximumBrickLevelForStage(stage, brickConfig.LevelCount),
                        MinimumBrickLevel = balanceConfig.MinimumBrickLevelForStage(stage, brickConfig.LevelCount)
                    };
                    var candidates = StageCandidateGenerator.Generate(request, brickConfig);
                    List<int> evaluationIndexes = SelectForFullEvaluation(candidates,
                        catConfig.ImpactDamageFor(request.ExpectedCatLevel), MaximumFullEvaluationsPerStage, brickConfig);
                    int bestIndex = evaluationIndexes[0];
                    StageCandidate bestCandidate = null;
                    StageCandidateMetrics bestMetrics = default;
                    for (int i = 0; i < evaluationIndexes.Count; i++)
                    {
                        float progress = ((stage - first) + (float)i / evaluationIndexes.Count) / (last - first + 1);
                        if (EditorUtility.DisplayCancelableProgressBar("Generating Stage Draft Report",
                            $"Stage {stage}, candidate {i + 1}/{evaluationIndexes.Count}", progress))
                        {
                            cancelled = true;
                            break;
                        }
                        int index = evaluationIndexes[i];
                        StageCandidate candidate = StageLaneBarrierPolicy.Reinforce(candidates[index], stage,
                            brickConfig, catConfig);
                        StageCandidateMetrics metrics = StageCandidateSimulator.Evaluate(candidate, stage,
                            request.ExpectedCatLevel + 1, maximumSteps, fixedDeltaTime, brickConfig, catConfig,
                            balanceConfig, expectedStackedOnly: true, maximumDrops: balanceConfig.DropBudgetFor(stage));
                        if (i == 0 || Score(metrics, candidate, targetWinRateMinimum, targetWinRateMaximum,
                            catConfig.ImpactDamageFor(request.ExpectedCatLevel), brickConfig) > Score(bestMetrics, bestCandidate, targetWinRateMinimum,
                            targetWinRateMaximum, catConfig.ImpactDamageFor(request.ExpectedCatLevel), brickConfig))
                        {
                            bestIndex = index;
                            bestCandidate = candidate;
                            bestMetrics = metrics;
                        }
                    }
                    if (cancelled) break;
                    StageCandidate best = bestCandidate;
                    report.AppendLine($"Stage {stage}: seed {request.Seed}; expected-cat-level {request.ExpectedCatLevel}; candidate {best.Id}; {best.BoardColumns}x{best.BrickRows}; " +
                        $"wins {bestMetrics.Wins}/{bestMetrics.Runs}; frames {bestMetrics.Frames}; normal-gold {bestMetrics.NormalGold}; " +
                        $"tickets {bestMetrics.GoldTickets}/{bestMetrics.ItemTickets}; winning-lanes {bestMetrics.DistinctWinningLanes}; " +
                        $"route-cost {best.RouteCostForLane(best.EasyLane, catConfig.ImpactDamageFor(request.ExpectedCatLevel), brickConfig)}/" +
                        $"{best.RouteCostForLane(best.RiskRewardLane, catConfig.ImpactDamageFor(request.ExpectedCatLevel), brickConfig)}.");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            stopwatch.Stop();
            report.AppendLine($"Status: {(cancelled ? "cancelled" : "complete")}; elapsed seconds {stopwatch.Elapsed.TotalSeconds:F2}.");
            string directory = Path.Combine("Library", "StageGeneration");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"stage-draft-report-{first:000}-{last:000}.txt");
            File.WriteAllText(path, report.ToString());
            return path;
        }

        public static void SetMenuRange(int startStage, int endStage)
        {
            EditorPrefs.SetInt(StartKey, Mathf.Max(1, startStage));
            EditorPrefs.SetInt(EndKey, Mathf.Max(1, endStage));
        }

        private static float Score(StageCandidateMetrics metrics, StageCandidate candidate,
            float minimum, float maximum, int expectedCatLevel, BrickConfig brickConfig)
        {
            float distance = metrics.WinRate < minimum ? minimum - metrics.WinRate :
                metrics.WinRate > maximum ? metrics.WinRate - maximum : 0f;
            float routeDifference = candidate.RouteCostForLane(candidate.RiskRewardLane, expectedCatLevel, brickConfig) -
                candidate.RouteCostForLane(candidate.EasyLane, expectedCatLevel, brickConfig);
            return -distance * 1000f + (metrics.GoldTickets + metrics.ItemTickets) * 4f +
                metrics.DistinctWinningLanes + Mathf.Max(0f, routeDifference);
        }

        private static List<int> SelectForFullEvaluation(IReadOnlyList<StageCandidate> candidates,
            int expectedCatLevel, int maximum, BrickConfig brickConfig)
        {
            var selected = new List<int>();
            for (int pick = 0; pick < Mathf.Min(maximum, candidates.Count); pick++)
            {
                int best = -1;
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (selected.Contains(index)) continue;
                    StageCandidate candidate = candidates[index];
                    float routeDifference = candidate.RouteCostForLane(candidate.RiskRewardLane, expectedCatLevel, brickConfig) -
                        candidate.RouteCostForLane(candidate.EasyLane, expectedCatLevel, brickConfig);
                    float structuralScore = candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold) +
                        candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item) + Mathf.Max(0f, routeDifference);
                    if (best < 0 || structuralScore >
                        candidates[best].Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold) +
                        candidates[best].Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item) +
                        Mathf.Max(0f, candidates[best].RouteCostForLane(candidates[best].RiskRewardLane, expectedCatLevel, brickConfig) -
                            candidates[best].RouteCostForLane(candidates[best].EasyLane, expectedCatLevel, brickConfig))) best = index;
                }
                if (best >= 0) selected.Add(best);
            }
            return selected;
        }
    }
}
