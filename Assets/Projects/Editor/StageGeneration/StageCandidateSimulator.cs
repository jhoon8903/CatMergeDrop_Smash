using System;
using System.Collections.Generic;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Drop.Ecs.Systems;
using Unity.Core;
using Unity.Entities;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    public readonly struct StageCandidateMetrics
    {
        public readonly int Runs;
        public readonly int Wins;
        public readonly int Frames;
        public readonly int NormalGold;
        public readonly int GoldTickets;
        public readonly int ItemTickets;
        public readonly int DistinctWinningLanes;
        public readonly StageScenarioMetrics[] Scenarios;

        public StageCandidateMetrics(int runs, int wins, int frames, int normalGold,
            int goldTickets, int itemTickets, int distinctWinningLanes, StageScenarioMetrics[] scenarios)
        {
            Runs = runs;
            Wins = wins;
            Frames = frames;
            NormalGold = normalGold;
            GoldTickets = goldTickets;
            ItemTickets = itemTickets;
            DistinctWinningLanes = distinctWinningLanes;
            Scenarios = scenarios;
        }

        public float WinRate => Runs == 0 ? 0f : (float)Wins / Runs;
    }

    public readonly struct StageScenarioMetrics
    {
        public readonly string Name;
        public readonly int Runs;
        public readonly int Wins;

        public StageScenarioMetrics(string name, int runs, int wins)
        {
            Name = name;
            Runs = runs;
            Wins = wins;
        }
    }

    public static class StageCandidateSimulator
    {
        public static StageCandidateMetrics Evaluate(StageCandidate candidate, int stageIndex, int expectedCatLevel,
            int maximumSteps, float fixedDeltaTime, BrickConfig brickConfig, CatConfig catConfig,
            StageBalanceConfig balanceConfig, int laneFilter = -1, bool expectedStackedOnly = false,
            int maximumDrops = 1)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (brickConfig == null) throw new ArgumentNullException(nameof(brickConfig));
            if (catConfig == null) throw new ArgumentNullException(nameof(catConfig));
            if (balanceConfig == null) throw new ArgumentNullException(nameof(balanceConfig));

            int stepsLimit = Mathf.Clamp(maximumSteps, 1, 10000);
            float deltaTime = Mathf.Clamp(fixedDeltaTime, .005f, 1f);
            int safeStageIndex = Mathf.Max(1, stageIndex);
            int safeLevel = Mathf.Max(1, expectedCatLevel);
            int dropsLimit = Mathf.Clamp(maximumDrops, 1, 100);
            using var world = new World("StageCandidateSimulator");
            EntityManager manager = world.EntityManager;
            SystemHandle fall = world.CreateSystem<FallSystem>();
            SystemHandle collisions = world.CreateSystem<GateCollisionSystem>();
            SystemHandle damage = world.CreateSystem<DamageAndBreakSystem>();
            SystemHandle rowAdvance = world.CreateSystem<DropRowAdvanceSystem>();
            SystemHandle result = world.CreateSystem<DropRunResultSystem>();
            SystemHandle cleanup = world.CreateSystem<BreakEventCleanupSystem>();
            StageDefinition definition = ScriptableObject.CreateInstance<StageDefinition>();
            candidate.Configure(definition, safeStageIndex);

            int wins = 0;
            int frames = 0;
            int normalGold = 0;
            int goldTickets = 0;
            int itemTickets = 0;
            int winningLanes = 0;
            var winningLaneSet = new bool[candidate.BoardColumns];
            var scenarioMetrics = new List<StageScenarioMetrics>();
            try
            {
                for (int levelOffset = -1; levelOffset <= 1; levelOffset++)
                for (int policy = 0; policy < 2; policy++)
                {
                    if (expectedStackedOnly && (levelOffset != 0 || policy != 1)) continue;
                    int scenarioRuns = 0;
                    int scenarioWins = 0;
                    int scenarioLevel = Mathf.Max(1, safeLevel + levelOffset);
                    for (int lane = 0; lane < candidate.BoardColumns; lane++)
                    {
                        if (laneFilter >= 0 && lane != laneFilter) continue;
                        Entity stage = DropStageFactory.Create(manager, brickConfig, balanceConfig,
                            safeStageIndex, definition);
                        BoardCatSnapshot[] cats = policy == 0
                            ? SoloCats(lane, scenarioLevel)
                            : MultiCats(lane, scenarioLevel);
                        scenarioRuns++;
                        for (int drop = 0; drop < dropsLimit; drop++)
                        {
                            if (!DropStageFactory.RequestDrop(manager, stage, catConfig, cats)) break;
                            int runFrames = Run(manager, world, stage, frames, stepsLimit, deltaTime,
                                fall, collisions, damage, rowAdvance, result, cleanup);
                            frames += runFrames;
                            Stage completed = manager.GetComponentData<Stage>(stage);
                            normalGold += completed.GoldEarned;
                            goldTickets += completed.GoldTicketsEarned;
                            itemTickets += completed.ItemsEarned;
                            if (completed.Phase != Phase.Won) continue;
                            wins++;
                            scenarioWins++;
                            MarkWinningLanes(manager, stage, completed.GoalProgress, winningLaneSet);
                            break;
                        }
                        DropStageFactory.Destroy(manager, stage);
                    }
                    scenarioMetrics.Add(new StageScenarioMetrics(
                        $"{(policy == 0 ? "Solo" : "Multi")} Lv{scenarioLevel}", scenarioRuns, scenarioWins));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(definition); }

            for (int i = 0; i < winningLaneSet.Length; i++) if (winningLaneSet[i]) winningLanes++;
            int runs = 0;
            foreach (StageScenarioMetrics scenario in scenarioMetrics) runs += scenario.Runs;
            return new StageCandidateMetrics(runs, wins, frames, normalGold,
                goldTickets, itemTickets, winningLanes, scenarioMetrics.ToArray());
        }

        private static int Run(EntityManager manager, World world, Entity stage, int frames,
            int stepsLimit, float deltaTime, SystemHandle fall, SystemHandle collisions,
            SystemHandle damage, SystemHandle rowAdvance, SystemHandle result, SystemHandle cleanup)
        {
            for (int runFrames = 0; runFrames < stepsLimit; runFrames++)
            {
                world.SetTime(new TimeData((frames + runFrames) * deltaTime, deltaTime));
                cleanup.Update(world.Unmanaged);
                fall.Update(world.Unmanaged);
                collisions.Update(world.Unmanaged);
                damage.Update(world.Unmanaged);
                rowAdvance.Update(world.Unmanaged);
                result.Update(world.Unmanaged);
                Phase phase = manager.GetComponentData<Stage>(stage).Phase;
                if (phase == Phase.Won || phase == Phase.Lost) return runFrames + 1;
            }
            return stepsLimit;
        }

        private static BoardCatSnapshot[] SoloCats(int lane, int level)
            => new[] { new BoardCatSnapshot(lane, lane, 0, level) };

        private static BoardCatSnapshot[] MultiCats(int lane, int level)
        {
            // A player can stack the board's three rows in one lane to wear down a route.
            var cats = new BoardCatSnapshot[3];
            for (int i = 0; i < cats.Length; i++)
                cats[i] = new BoardCatSnapshot(lane * 3 + i, lane, i, level);
            return cats;
        }

        private static void MarkWinningLanes(EntityManager manager, Entity stage, float goalProgress,
            bool[] winningLanes)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            using var cats = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < cats.Length; i++)
            {
                Cat cat = manager.GetComponentData<Cat>(cats[i]);
                if (cat.Stage == stage && cat.Y >= goalProgress)
                    winningLanes[cat.Column] = true;
            }
        }
    }
}
