using System;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    [CreateAssetMenu(fileName = "StageBalanceConfig", menuName = "Cat Merge Drop/Stage Balance Config")]
    public sealed class StageBalanceConfig : ScriptableObject
    {
        private const int BoardRows = 3;
        [Header("Stage Attempt")]
        [SerializeField, Min(1)] private int dropBudget = 4;
        public int DropBudgetFor(int stageIndex)
        {
            StageDefinition definition = GetStageDefinition(stageIndex);
            int budget = definition != null && definition.DropBudgetOverride > 0
                ? definition.DropBudgetOverride : dropBudget;
            return Mathf.Clamp(budget, 1, 100);
        }
        public int LastDefinedStage
        {
            get
            {
                int last = 0;
                if (stageDefinitions != null)
                    foreach (StageDefinition definition in stageDefinitions)
                        if (definition != null) last = Mathf.Max(last, definition.StageIndex);
                return last;
            }
        }
        public bool IsBeyondFinalStage(int stageIndex) => LastDefinedStage > 0 && stageIndex > LastDefinedStage;
        [SerializeField] private StageDefinition[] stageDefinitions = Array.Empty<StageDefinition>();

        [Header("Brick Level Progression")]
        [SerializeField, Min(0)] private int brickLevelsAboveExpectedCat = 1;
        [SerializeField, Min(0)] private int brickLevelsBelowExpectedCat = 1;
        [SerializeField, Min(1)] private int stagesPerExtraBrickLevel = 40;

        [Header("Summons")]
        [SerializeField, Min(0)] private int summonGoldPerMaximumSpawnLevel = 10;
        [SerializeField, Min(0)] private int minimumSummonGap = 5;
        [SerializeField, Min(0)] private int maximumSummonGap = 2;
        [Header("Spawn Weights: Lowest to Highest")]
        [SerializeField]
        [Tooltip("Relative weights from the lowest to highest available summon level. Active weights are normalized automatically. Add entries when the summon range widens; the last weight is reused if entries run out.")]
        private int[] spawnWeightsFromLowestLevel = { 40, 25, 18, 11, 6 };
        [SerializeField, Min(0)] private int clearSummonCount = 5;

        public static int ExpectedCatLevelForStage(int stageIndex)
        {
            int level = 1;
            for (int index = Mathf.Max(1, stageIndex); index > 1; index >>= 1) level++;
            return level;
        }

        public int MinimumBrickLevelForStage(int stageIndex, int availableBrickLevels)
            => Mathf.Clamp(ExpectedCatLevelForStage(stageIndex) - Mathf.Max(0, brickLevelsBelowExpectedCat),
                1, MaximumBrickLevelForStage(stageIndex, availableBrickLevels));

        public int MaximumBrickLevelForStage(int stageIndex, int availableBrickLevels)
        {
            int maximum = ExpectedCatLevelForStage(stageIndex)
                + Mathf.Max(0, brickLevelsAboveExpectedCat)
                + Mathf.Max(0, stageIndex) / Mathf.Max(1, stagesPerExtraBrickLevel);
            return Mathf.Clamp(maximum, 1, Mathf.Max(1, availableBrickLevels));
        }

        public StageLayout LayoutForStage(int stageIndex)
        {
            StageDefinition definition = GetStageDefinition(IsBeyondFinalStage(stageIndex) ? LastDefinedStage : stageIndex);
            if (definition != null) return LayoutFor(definition);

            if (stageDefinitions != null && stageDefinitions.Length > 0)
                throw new InvalidOperationException($"Stage {stageIndex} has no StageDefinition.");

            // Neutral layout for isolated configurations used by editor tests.
            const int columns = 4;
            int rows = Mathf.Min(WorldPlayfieldLayout.BrickRows, WorldPlayfieldLayout.BrickCount / columns);
            return new StageLayout(columns, BoardRows, columns, rows);
        }

        public bool HasBrick(int stageIndex, int row, int column)
        {
            StageDefinition definition = GetStageDefinition(stageIndex);
            if (definition != null)
            {
                StageLayout definitionLayout = LayoutFor(definition);
                return row >= 0 && row < definitionLayout.BrickRows
                    && column >= 0 && column < definitionLayout.BrickColumns
                    && definition.GetCell(row, column).Present;
            }

            StageLayout layout = LayoutForStage(stageIndex);
            if (row < 0 || row >= layout.BrickRows || column < 0 || column >= layout.BrickColumns)
                return false;
            return true;
        }

        public int SummonCostFor(int highestMergedLevel)
        {
            GetSummonLevelRange(highestMergedLevel, out _, out int maximum);
            return SaturatedProduct(Mathf.Max(0, summonGoldPerMaximumSpawnLevel), maximum);
        }

        public void GetSummonLevelRange(int highestMergedLevel, out int minimum, out int maximum)
        {
            int highest = Mathf.Max(1, highestMergedLevel);
            minimum = Mathf.Max(1, SaturatedSubtract(highest, Mathf.Max(0, minimumSummonGap)));
            maximum = Mathf.Max(minimum, SaturatedSubtract(highest, Mathf.Max(0, maximumSummonGap)));
        }

        public int RollSummonLevel(int highestMergedLevel)
        {
            GetSummonLevelRange(highestMergedLevel, out int minimum, out int maximum);
            long totalWeight = TotalSpawnWeight(minimum, maximum);
            if (totalWeight <= 0) return minimum;
            long ticket = Math.Min(totalWeight - 1, (long)(UnityEngine.Random.value * totalWeight));
            return SummonLevelForTicket(highestMergedLevel, ticket);
        }

        public int SummonLevelForTicket(int highestMergedLevel, long ticket)
        {
            GetSummonLevelRange(highestMergedLevel, out int minimum, out int maximum);
            long totalWeight = TotalSpawnWeight(minimum, maximum);
            if (totalWeight <= 0) return minimum;
            long remaining = Math.Max(0, Math.Min(ticket, totalWeight - 1));
            for (int level = minimum; level <= maximum; level++)
            {
                remaining -= SpawnWeightForOffset(level - minimum);
                if (remaining < 0) return level;
            }
            return maximum;
        }

        public int ClearBonusFor(int highestMergedLevel)
            => SaturatedProduct(SummonCostFor(highestMergedLevel), Mathf.Max(0, clearSummonCount));

        public StageDefinition GetStageDefinition(int stageIndex)
        {
            if (stageDefinitions == null) return null;
            for (int i = 0; i < stageDefinitions.Length; i++)
            {
                StageDefinition definition = stageDefinitions[i];
                if (definition != null && definition.StageIndex == stageIndex) return definition;
            }
            return null;
        }

        internal static StageLayout LayoutFor(StageDefinition definition, int? revision = null)
        {
            int columns = Mathf.Clamp(definition.ColumnsForRevision(revision ?? definition.CatalogRevision), 1, MergeBoardModel.Columns);
            int maximumBrickRows = WorldPlayfieldLayout.BrickCount / columns;
            int rows = Mathf.Clamp(definition.RowsForRevision(revision ?? definition.CatalogRevision), 1, maximumBrickRows);
            return new StageLayout(columns, BoardRows, columns, rows);
        }

        private long TotalSpawnWeight(int minimum, int maximum)
        {
            long total = 0;
            for (int level = minimum; level <= maximum; level++)
                total += SpawnWeightForOffset(level - minimum);
            return total;
        }

        private int SpawnWeightForOffset(int offset)
        {
            if (spawnWeightsFromLowestLevel == null || spawnWeightsFromLowestLevel.Length == 0) return 1;
            return Mathf.Clamp(spawnWeightsFromLowestLevel[Mathf.Min(offset, spawnWeightsFromLowestLevel.Length - 1)], 0, 100);
        }

        private static int SaturatedProduct(int left, int right)
        {
            long product = (long)left * right;
            return (int)Math.Min(product, int.MaxValue);
        }

        private static int SaturatedSubtract(int value, int amount)
            => (int)Math.Max(1L, (long)value - amount);
    }
}
