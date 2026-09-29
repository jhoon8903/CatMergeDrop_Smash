using System;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Configs;

namespace Projects.Scripts.GamePlay.Board
{
    public readonly struct AutoSummonResult
    {
        public int Count { get; }
        public long GoldSpent { get; }

        public AutoSummonResult(int count, long goldSpent)
        {
            Count = count;
            GoldSpent = goldSpent;
        }
    }

    /// <summary>Batch board actions used by the optional convenience buttons.</summary>
    public static class BoardAutoActions
    {
        public static bool HasMergePair(int[] levels, int activeSlotCount)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (activeSlotCount < 0 || activeSlotCount > levels.Length)
                throw new ArgumentOutOfRangeException(nameof(activeSlotCount));
            for (int target = 0; target < activeSlotCount; target++)
            {
                int level = levels[target];
                if (level < 1 || level == int.MaxValue) continue;
                for (int source = target + 1; source < activeSlotCount; source++)
                    if (levels[source] == level) return true;
            }
            return false;
        }

        public static int MergeAll(PlayerSaveData data, int activeSlotCount)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ValidateSlotCount(activeSlotCount);
            // Do not pull unformed legacy cats onto the board during gameplay.
            int merged = 0;
            while (TryFindMergePair(data, activeSlotCount, out int source, out int target))
            {
                data.MergeFormationCats(source, target);
                merged++;
            }
            return merged;
        }

        public static AutoSummonResult SummonIntoEmpty(PlayerSaveData data, int activeSlotCount,
            StageBalanceConfig balance, Func<int, int> rollLevel)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (rollLevel == null) throw new ArgumentNullException(nameof(rollLevel));
            ValidateSlotCount(activeSlotCount);
            // Summons always target currently vacant board slots.
            int count = 0;
            long spent = 0;
            for (int slot = 0; slot < activeSlotCount; slot++)
            {
                if (data.GetFormationCatId(slot) != 0) continue;
                int cost = balance.SummonCostFor(data.MaxMergedCatLevel);
                if (data.Gold < cost) break;
                int level = rollLevel(data.MaxMergedCatLevel);
                balance.GetSummonLevelRange(data.MaxMergedCatLevel, out int minimum, out int maximum);
                if (level < minimum || level > maximum)
                    throw new InvalidOperationException("Rolled cat level is outside the configured summon range.");
                data.SetGold(data.Gold - cost);
                data.AddOwnedCat(level, slot);
                count++;
                spent += cost;
            }
            return new AutoSummonResult(count, spent);
        }

        private static bool TryFindMergePair(PlayerSaveData data, int activeSlotCount,
            out int source, out int target)
        {
            for (target = 0; target < activeSlotCount; target++)
            {
                int level = data.GetCatLevel(target);
                if (level < 1 || level == int.MaxValue) continue;
                for (source = target + 1; source < activeSlotCount; source++)
                    if (data.GetCatLevel(source) == level) return true;
            }
            source = target = -1;
            return false;
        }

        private static void ValidateSlotCount(int activeSlotCount)
        {
            if (activeSlotCount < 0 || activeSlotCount > PlayerSaveData.BoardSlotCount)
                throw new ArgumentOutOfRangeException(nameof(activeSlotCount));
        }
    }
}
