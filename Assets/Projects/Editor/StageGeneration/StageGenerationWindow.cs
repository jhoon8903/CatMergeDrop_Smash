using System;
using System.Collections.Generic;
using System.IO;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public sealed class StageGenerationWindow : EditorWindow
    {
        private StageGenerationRequest _request = new StageGenerationRequest
        {
            Seed = 12345, StageIndex = 1, ExpectedCatLevel = 3, CandidateCount = 8
        };
        private int _maximumSteps = 480;
        private float _fixedDeltaTime = .05f;
        private float _targetWinRateMinimum = .55f;
        private float _targetWinRateMaximum = .75f;
        private int _refinementRounds = 1;
        private int _batchStartStage = 1;
        private int _batchEndStage = 10;
        private BrickConfig _brickConfig;
        private CatConfig _catConfig;
        private StageBalanceConfig _balanceConfig;
        private IReadOnlyList<StageCandidate> _candidates;
        private readonly List<StageCandidateMetrics> _metrics = new List<StageCandidateMetrics>();
        private Vector2 _scroll;
        private int _selectedIndex;
        private string _message;

        [MenuItem("Cat Merge Drop/Stage Generation")]
        private static void Open() => GetWindow<StageGenerationWindow>("Stage Generation");

        private void OnEnable()
        {
            _brickConfig = LoadIfMissing(_brickConfig, "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            _catConfig = LoadIfMissing(_catConfig, "Assets/Projects/Assets/Configuration/CatConfig.asset");
            _balanceConfig = LoadIfMissing(_balanceConfig, "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Offline Stage Candidates", EditorStyles.boldLabel);
            _request.Seed = EditorGUILayout.IntField("Seed", _request.Seed);
            _request.StageIndex = Mathf.Max(1, EditorGUILayout.IntField("Stage Index", _request.StageIndex));
            _request.ExpectedCatLevel = Mathf.Max(1, EditorGUILayout.IntField("Expected Cat Level", _request.ExpectedCatLevel));
            _request.CandidateCount = Mathf.Clamp(EditorGUILayout.IntField("Candidate Count", _request.CandidateCount), 1, 128);
            _maximumSteps = Mathf.Clamp(EditorGUILayout.IntField("Max Steps per Lane", _maximumSteps), 1, 10000);
            _fixedDeltaTime = Mathf.Clamp(EditorGUILayout.FloatField("Fixed Delta Time", _fixedDeltaTime), .005f, 1f);
            _targetWinRateMinimum = Mathf.Clamp01(EditorGUILayout.FloatField("Target Win Rate Min", _targetWinRateMinimum));
            _targetWinRateMaximum = Mathf.Clamp(EditorGUILayout.FloatField("Target Win Rate Max", _targetWinRateMaximum), _targetWinRateMinimum, 1f);
            _refinementRounds = Mathf.Clamp(EditorGUILayout.IntField("Refinement Rounds", _refinementRounds), 0, 4);
            _brickConfig = (BrickConfig)EditorGUILayout.ObjectField("Brick Config", _brickConfig, typeof(BrickConfig), false);
            _catConfig = (CatConfig)EditorGUILayout.ObjectField("Cat Config", _catConfig, typeof(CatConfig), false);
            _balanceConfig = (StageBalanceConfig)EditorGUILayout.ObjectField("Balance Config", _balanceConfig, typeof(StageBalanceConfig), false);
            if (_balanceConfig != null && _request.StageIndex > 1)
                EditorGUILayout.LabelField("Previous Stage Columns", _balanceConfig.LayoutForStage(_request.StageIndex - 1).BoardColumns.ToString());
            EditorGUILayout.HelpBox("Candidates use reinforced ordinary-brick barriers. Evaluation checks expected+1 stacked formations cumulatively through the configured drop budget.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate")) Generate();
                using (new EditorGUI.DisabledScope(_candidates == null || !HasConfigs()))
                    if (GUILayout.Button("Evaluate")) Evaluate();
                using (new EditorGUI.DisabledScope(!HasConfigs()))
                    if (GUILayout.Button("Search")) Search();
            }
            _batchStartStage = Mathf.Max(1, EditorGUILayout.IntField("Batch Start Stage", _batchStartStage));
            _batchEndStage = Mathf.Max(_batchStartStage, EditorGUILayout.IntField("Batch End Stage", _batchEndStage));
            using (new EditorGUI.DisabledScope(!HasConfigs()))
                if (GUILayout.Button("Generate Batch Draft Report")) GenerateBatchReport();
            int estimatedSteps = _request.CandidateCount * 6 * 6 * _maximumSteps * (_refinementRounds + 1);
            if (estimatedSteps > 500000)
                EditorGUILayout.HelpBox($"Search may simulate up to {estimatedSteps:N0} frames. Lower candidates, steps, or refinement rounds for a faster pass.", MessageType.Warning);
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Info);
            if (_candidates == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Generated: {_candidates.Count} candidates", EditorStyles.boldLabel);
            if (_metrics.Count == _candidates.Count) DrawTopSummary();
            _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _candidates.Count - 1);
            _selectedIndex = EditorGUILayout.Popup("Selected Candidate", _selectedIndex, CandidateLabels());
            DrawGrid(_candidates[_selectedIndex]);
            using (new EditorGUI.DisabledScope(_metrics.Count != _candidates.Count))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Export Report")) ExportReport();
                    if (GUILayout.Button("Export Selected Definition")) ExportDefinition();
                    using (new EditorGUI.DisabledScope(_balanceConfig == null))
                        if (GUILayout.Button("Apply Selected")) ApplySelected();
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _candidates.Count; i++) DrawCandidate(i);
            EditorGUILayout.EndScrollView();
        }

        private void Generate()
        {
            StageGenerationRequest request = _request;
            request.PreviousBoardColumns = _balanceConfig != null && request.StageIndex > 1
                ? _balanceConfig.LayoutForStage(request.StageIndex - 1).BoardColumns : 0;
            request.MaximumBrickLevel = _balanceConfig != null
                ? _balanceConfig.MaximumBrickLevelForStage(request.StageIndex, _brickConfig.LevelCount)
                : _brickConfig.LevelCount;
            request.MinimumBrickLevel = _balanceConfig != null
                ? _balanceConfig.MinimumBrickLevelForStage(request.StageIndex, _brickConfig.LevelCount)
                : Mathf.Clamp(Mathf.Max(1, request.ExpectedCatLevel - 1), 1, request.MaximumBrickLevel);
            IReadOnlyList<StageCandidate> generated = StageCandidateGenerator.Generate(request, _brickConfig);
            _candidates = Reinforce(generated);
            _metrics.Clear();
            _selectedIndex = 0;
            _message = "Candidates generated. No configuration asset was changed.";
        }

        private void Evaluate()
        {
            _metrics.Clear();
            _candidates = Reinforce(_candidates);
            int plannedLevel = Mathf.Max(1, _request.ExpectedCatLevel + 1);
            int dropBudget = _balanceConfig.DropBudgetFor(_request.StageIndex);
            for (int i = 0; i < _candidates.Count; i++)
                _metrics.Add(StageCandidateSimulator.Evaluate(_candidates[i], _request.StageIndex,
                    plannedLevel, _maximumSteps, _fixedDeltaTime,
                    _brickConfig, _catConfig, _balanceConfig, expectedStackedOnly: true,
                    maximumDrops: dropBudget));
            _message = $"Offline cumulative ECS evaluation complete: expected+1 stacked formations, up to {dropBudget} drops.";
        }

        private void Search()
        {
            Generate();
            Evaluate();
            for (int round = 0; round < _refinementRounds; round++)
            {
                _candidates = Refine(round);
                Evaluate();
            }
            _message = $"Quality-diverse search complete after {_refinementRounds + 1} passes.";
        }

        private void GenerateBatchReport()
        {
            StageBatchReport.SetMenuRange(_batchStartStage, _batchEndStage);
            string path = StageBatchReport.Run(_batchStartStage, _batchEndStage, _request.Seed,
                _request.ExpectedCatLevel, _request.CandidateCount, _maximumSteps, _fixedDeltaTime,
                _targetWinRateMinimum, _targetWinRateMaximum, _brickConfig, _catConfig, _balanceConfig);
            _message = "Aggregate batch report written to " + path + ".";
        }

        private void DrawTopSummary()
        {
            int best = BestCandidateIndex();
            StageCandidate candidate = _candidates[best];
            StageCandidateMetrics metrics = _metrics[best];
            EditorGUILayout.HelpBox($"Top: #{candidate.Id}  Win {metrics.Wins}/{metrics.Runs}  " +
                $"Frames {metrics.Frames}  Gold {metrics.NormalGold}  Tickets G/I {metrics.GoldTickets}/{metrics.ItemTickets}  " +
                $"Route cost E/R {candidate.RouteCostForLane(candidate.EasyLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}/" +
                $"{candidate.RouteCostForLane(candidate.RiskRewardLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}",
                MessageType.None);
        }

        private void DrawCandidate(int index)
        {
            StageCandidate candidate = _candidates[index];
            string detail = $"#{candidate.Id}  {candidate.BoardColumns} cols × {candidate.BrickRows} rows  " +
                $"Gold/Item {candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold)}/" +
                $"{candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item)}";
            if (_metrics.Count == _candidates.Count)
            {
                StageCandidateMetrics result = _metrics[index];
                detail += $"  Win {result.Wins}/{result.Runs}, Frames {result.Frames}, Gold {result.NormalGold}, " +
                    $"Tickets {result.GoldTickets}/{result.ItemTickets}, Lanes {result.DistinctWinningLanes}, " +
                    ScenarioSummary(result);
            }
            EditorGUILayout.LabelField(detail, index == _selectedIndex ? EditorStyles.boldLabel : EditorStyles.label);
        }

        private void DrawGrid(StageCandidate candidate)
        {
            EditorGUILayout.LabelField($"Layout preview — easy lane {candidate.EasyLane + 1}, risk/reward lane {candidate.RiskRewardLane + 1}",
                EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField($"Nonlethal-hit route cost at expected damage: easy " +
                $"{candidate.RouteCostForLane(candidate.EasyLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}, risk/reward " +
                $"{candidate.RouteCostForLane(candidate.RiskRewardLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}", EditorStyles.miniLabel);
            for (int row = 0; row < candidate.BrickRows; row++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int column = 0; column < candidate.BoardColumns; column++)
                    {
                        StageBrickCell cell = candidate.Cells[row * candidate.BoardColumns + column];
                        string label = !cell.Present ? "·" : cell.Kind == Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold ? "G" :
                            cell.Kind == Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item ? "I" : "N" + cell.Level;
                        GUILayout.Label(label, EditorStyles.miniButton, GUILayout.Width(34f));
                    }
                }
            }
        }

        private void ExportReport()
        {
            string path = EditorUtility.SaveFilePanel("Export Stage Generation Report", Application.dataPath,
                "stage-generation-report", "txt");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, ReportText());
            _message = "Report exported: " + path;
        }

        private void ExportDefinition()
        {
            string path = EditorUtility.SaveFilePanelInProject("Export Stage Definition",
                $"Stage_{_request.StageIndex:000}", "asset", "Choose an asset path.");
            if (string.IsNullOrEmpty(path)) return;
            StageDefinition definition = CreateInstance<StageDefinition>();
            _candidates[_selectedIndex].Configure(definition, _request.StageIndex);
            AssetDatabase.CreateAsset(definition, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = definition;
            _message = "Stage definition exported. Assign it manually to Stage Balance Config when ready.";
        }

        private void ApplySelected()
        {
            StageCandidate candidate = StageLaneBarrierPolicy.Reinforce(_candidates[_selectedIndex],
                _request.StageIndex, _brickConfig, _catConfig);
            if (!CanApply(candidate, out string reason))
            {
                _message = reason;
                return;
            }
            string path = EditorUtility.SaveFilePanelInProject("Create and Apply Stage Definition",
                $"Stage_{_request.StageIndex:000}", "asset", "Choose an asset path.");
            if (string.IsNullOrEmpty(path)) return;
            StageDefinition existingDefinition = _balanceConfig.GetStageDefinition(_request.StageIndex);
            StageDefinition definition = existingDefinition != null
                ? UnityEngine.Object.Instantiate(existingDefinition) : CreateInstance<StageDefinition>();
            candidate.Configure(definition, _request.StageIndex);
            AssetDatabase.CreateAsset(definition, path);
            Undo.RecordObject(_balanceConfig, "Apply Stage Definition");
            SerializedObject serialized = new SerializedObject(_balanceConfig);
            SerializedProperty definitions = serialized.FindProperty("stageDefinitions");
            int target = definitions.arraySize;
            for (int i = 0; i < definitions.arraySize; i++)
            {
                StageDefinition existing = definitions.GetArrayElementAtIndex(i).objectReferenceValue as StageDefinition;
                if (existing != null && existing.StageIndex == _request.StageIndex) { target = i; break; }
            }
            if (target == definitions.arraySize) definitions.arraySize++;
            definitions.GetArrayElementAtIndex(target).objectReferenceValue = definition;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(_balanceConfig);
            AssetDatabase.SaveAssets();
            Selection.activeObject = definition;
            _message = "Selected definition created and assigned to this stage. Other balance settings were preserved.";
        }

        private string ReportText()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Stage {_request.StageIndex}; seed {_request.Seed}; expected cat level {_request.ExpectedCatLevel}; synthetic Solo/Multi matrix proxy, not calibrated player win rate.");
            for (int i = 0; i < _candidates.Count; i++)
            {
                StageCandidate candidate = _candidates[i];
                StageCandidateMetrics result = _metrics[i];
                report.AppendLine($"#{candidate.Id}: {candidate.BoardColumns}x{candidate.BrickRows}; " +
                    $"wins {result.Wins}/{result.Runs}; frames {result.Frames}; gold {result.NormalGold}; " +
                    $"tickets {result.GoldTickets}/{result.ItemTickets}; lanes {result.DistinctWinningLanes}; " +
                    $"route-cost E/R {candidate.RouteCostForLane(candidate.EasyLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}/" +
                    $"{candidate.RouteCostForLane(candidate.RiskRewardLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig)}; {ScenarioSummary(result)}");
            }
            return report.ToString();
        }

        private int BestCandidateIndex()
        {
            int best = 0;
            for (int i = 1; i < _metrics.Count; i++)
            {
                StageCandidateMetrics next = _metrics[i];
                StageCandidateMetrics current = _metrics[best];
                if (RankScore(next, _candidates[i]) > RankScore(current, _candidates[best]))
                    best = i;
            }
            return best;
        }

        private string[] CandidateLabels()
        {
            var labels = new string[_candidates.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = "#" + _candidates[i].Id;
            return labels;
        }

        private bool HasConfigs() => _brickConfig != null && _catConfig != null && _balanceConfig != null;

        private IReadOnlyList<StageCandidate> Refine(int round)
        {
            var parentIndexes = new List<int>();
            for (int i = 0; i < _candidates.Count && parentIndexes.Count < 6; i++)
            {
                int best = -1;
                for (int candidate = 0; candidate < _candidates.Count; candidate++)
                {
                    if (parentIndexes.Contains(candidate)) continue;
                    if (!IsDiverseParent(candidate, parentIndexes)) continue;
                    if (best < 0 || RankScore(_metrics[candidate], _candidates[candidate]) >
                        RankScore(_metrics[best], _candidates[best])) best = candidate;
                }
                if (best < 0) break;
                parentIndexes.Add(best);
            }
            if (parentIndexes.Count == 0) parentIndexes.Add(BestCandidateIndex());
            var next = new List<StageCandidate>(_candidates.Count);
            for (int i = 0; i < _candidates.Count; i++)
            {
                StageCandidate parent = _candidates[parentIndexes[i % parentIndexes.Count]];
                StageCandidate mutated = StageCandidateGenerator.Mutate(parent,
                    _request.Seed + (round + 1) * 1009 + i, i + 1, _brickConfig,
                    _balanceConfig != null
                        ? _balanceConfig.MaximumBrickLevelForStage(_request.StageIndex, _brickConfig.LevelCount)
                        : _brickConfig.LevelCount,
                    _balanceConfig != null
                        ? _balanceConfig.MinimumBrickLevelForStage(_request.StageIndex, _brickConfig.LevelCount)
                        : Mathf.Clamp(Mathf.Max(1, _request.ExpectedCatLevel - 1), 1, _brickConfig.LevelCount));
                next.Add(StageLaneBarrierPolicy.Reinforce(mutated, _request.StageIndex, _brickConfig, _catConfig));
            }
            return next;
        }

        private IReadOnlyList<StageCandidate> Reinforce(IReadOnlyList<StageCandidate> candidates)
        {
            var reinforced = new List<StageCandidate>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                reinforced.Add(StageLaneBarrierPolicy.Reinforce(candidates[i], _request.StageIndex,
                    _brickConfig, _catConfig));
            return reinforced;
        }

        private bool CanApply(StageCandidate candidate, out string reason)
        {
            StageCandidateMetrics baseline = StageCandidateSimulator.Evaluate(candidate, _request.StageIndex,
                _request.ExpectedCatLevel, _maximumSteps, _fixedDeltaTime, _brickConfig, _catConfig,
                _balanceConfig, expectedStackedOnly: true);
            if (baseline.Wins > 0)
            {
                reason = "Apply blocked: an expected-level three-cat formation clears in one drop.";
                return false;
            }
            int dropBudget = _balanceConfig.DropBudgetFor(_request.StageIndex);
            StageCandidateMetrics planned = StageCandidateSimulator.Evaluate(candidate, _request.StageIndex,
                _request.ExpectedCatLevel + 1, _maximumSteps, _fixedDeltaTime, _brickConfig, _catConfig,
                _balanceConfig, expectedStackedOnly: true, maximumDrops: dropBudget);
            if (planned.Wins == 0)
            {
                reason = $"Apply blocked: no expected+1 three-cat route clears within {dropBudget} drops.";
                return false;
            }
            reason = null;
            return true;
        }

        private bool IsDiverseParent(int candidateIndex, List<int> selectedIndexes)
        {
            StageCandidate candidate = _candidates[candidateIndex];
            int specials = candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold) +
                candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item);
            for (int i = 0; i < selectedIndexes.Count; i++)
            {
                StageCandidate selected = _candidates[selectedIndexes[i]];
                int selectedSpecials = selected.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold) +
                    selected.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item);
                if (candidate.BoardColumns == selected.BoardColumns && candidate.BrickRows == selected.BrickRows &&
                    specials == selectedSpecials) return false;
            }
            return true;
        }

        private float RankScore(StageCandidateMetrics metrics, StageCandidate candidate)
        {
            float bandDistance = metrics.WinRate < _targetWinRateMinimum
                ? _targetWinRateMinimum - metrics.WinRate
                : metrics.WinRate > _targetWinRateMaximum ? metrics.WinRate - _targetWinRateMaximum : 0f;
            float reward = metrics.GoldTickets + metrics.ItemTickets;
            float diversity = metrics.DistinctWinningLanes + candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Gold) +
                candidate.Count(Projects.Scripts.GamePlay.Drop.Ecs.Enums.BrickKind.Item);
            float routeDifference = candidate.RouteCostForLane(candidate.RiskRewardLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig) -
                candidate.RouteCostForLane(candidate.EasyLane, _catConfig.ImpactDamageFor(_request.ExpectedCatLevel), _brickConfig);
            return -bandDistance * 1000f + reward * 4f + diversity + Mathf.Max(0f, routeDifference) - metrics.Frames * .0001f;
        }

        private static string ScenarioSummary(StageCandidateMetrics metrics)
        {
            var summary = new System.Text.StringBuilder("Scenarios ");
            for (int i = 0; i < metrics.Scenarios.Length; i++)
            {
                if (i > 0) summary.Append(", ");
                StageScenarioMetrics scenario = metrics.Scenarios[i];
                summary.Append(scenario.Name).Append(' ').Append(scenario.Wins).Append('/').Append(scenario.Runs);
            }
            return summary.ToString();
        }

        private static T LoadIfMissing<T>(T current, string path) where T : UnityEngine.Object
            => current != null ? current : AssetDatabase.LoadAssetAtPath<T>(path);
    }
}
