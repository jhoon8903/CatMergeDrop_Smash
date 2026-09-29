using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Persists rewards from a completed drop run.</summary>
    public static class DropRewardPersistence
    {
        public static bool CanRestartAttempt(PlayerSaveData data, StageBalanceConfig balance)
            => data.HasStageAttempt && !data.AttemptDropInFlight &&
               (data.AttemptDropsRemaining == 0 ||
                (data.ActiveOwnedCatCount == 0 && data.Gold < balance.SummonCostFor(data.MaxMergedCatLevel)));

        public static void EnsureAttemptStarted(PlayerSaveData data, StageBalanceConfig balance)
        {
            if (data.HasStageAttempt || balance.IsBeyondFinalStage(data.StageIndex)) return;
            // Each stage starts a fresh roster; fund preparation before capturing its checkpoint.
            if (data.ActiveOwnedCatCount == 0)
                data.SetGold(Math.Max(data.Gold, (int)Math.Min(int.MaxValue,
                    3L * balance.SummonCostFor(data.MaxMergedCatLevel))));
            int slots = balance.LayoutForStage(data.StageIndex).BoardSlotCount;
            data.FitFormationToSlotCount(slots);
            data.FillFormationFromUnformed(slots);
            data.BeginStageAttempt(balance.DropBudgetFor(data.StageIndex),
                balance.GetStageDefinition(data.StageIndex)?.CatalogRevision ?? 0);
        }

        /// <summary>Only living cats deployed on the final Drop are liquidated; reserve/tickets never count.</summary>
        public static int SurvivorGoldFor(PlayerSaveData data, int[] survivingSlots, int[] prices = null)
        {
            if (survivingSlots == null) return 0;
            long gold = 0;
            var seen = new HashSet<int>();
            foreach (int slot in survivingSlots)
            {
                int id = data.GetFormationCatId(slot);
                if (id <= 0 || !seen.Add(id)) continue;
                gold += CatSaleRules.PriceForLevel(data.GetOwnedCatLevel(id), prices);
            }
            return (int)Math.Min(int.MaxValue, gold);
        }

        public static Task SaveAttemptDropAsync(SaveSystem saves, StageBalanceConfig balance,
            int expectedStage, bool cleared, int gold, int goldTickets, int items,
            int[] brickHealth, CancellationToken token, int[] survivingCatSlots = null, int[] settlementPrices = null)
        {
            return saves.UpdateAsync<PlayerSaveData>(data =>
            {
                // Reservation and settlement are persisted independently. Repeating a
                // settled result must never mint rewards or consume another Drop.
                if (data.StageIndex != expectedStage || !data.AttemptDropInFlight) return;
                data.CommitAttemptDrop(brickHealth);
                data.SetGold((int)Math.Min(int.MaxValue, (long)data.Gold + gold +
                    (cleared ? balance.ClearBonusFor(data.MaxMergedCatLevel) : 0)));
                BrickRewardTickets.Bank(data, goldTickets, items);
                if (!cleared) return;
                int survivorGold = SurvivorGoldFor(data, survivingCatSlots, settlementPrices);
                data.ClearStageCats();
                BrickRewardTickets.ExpireStageCatRewards(data);
                data.CompleteStageAttempt();
                data.SetStageIndex(checked(data.StageIndex + 1));
                data.FitFormationToSlotCount(balance.LayoutForStage(data.StageIndex).BoardSlotCount);
                // Preserve the existing preparation floor, then carry survivor proceeds on top.
                // Otherwise the floor silently absorbs the reward for low-gold players.
                if (!balance.IsBeyondFinalStage(data.StageIndex))
                    data.SetGold(Math.Max(data.Gold, (int)Math.Min(int.MaxValue,
                        3L * balance.SummonCostFor(data.MaxMergedCatLevel))));
                data.SetGold((int)Math.Min(int.MaxValue, (long)data.Gold + survivorGold));
                EnsureAttemptStarted(data, balance);
            }, token);
        }

        public static async Task SaveAsync(SaveSystem saveSystem, StageBalanceConfig stageBalanceConfig,
            int earnedGold, int earnedItems,
            bool stageCleared, int expectedStageIndex, CancellationToken cancellationToken,
            int[] consumedCatIds = null, int goldBrickTickets = 0)
        {
            if (stageBalanceConfig == null) throw new ArgumentNullException(nameof(stageBalanceConfig));
            if (earnedGold <= 0 && !stageCleared) return;

            await saveSystem.UpdateAsync<PlayerSaveData>(data =>
            {
                int clearBonus = 0;
                // A completed run can retry after its save succeeded. Once the stage has
                // advanced, skip all of its success effects so rewards and cats are not
                // applied a second time. Failed runs keep their existing reward behavior.
                if (stageCleared)
                {
                    if (data.StageIndex != expectedStageIndex) return;
                    if (data.StageIndex < int.MaxValue) data.SetStageIndex(data.StageIndex + 1);
                    if (consumedCatIds != null)
                        for (int i = 0; i < consumedCatIds.Length; i++) data.ConsumeOwnedCat(consumedCatIds[i]);
                    clearBonus = stageBalanceConfig.ClearBonusFor(data.MaxMergedCatLevel);
                }
                data.SetGold((int)Math.Min((long)data.Gold + earnedGold, int.MaxValue));
                if (clearBonus > 0)
                    data.SetGold((int)Math.Min((long)data.Gold + clearBonus, int.MaxValue));
                if (stageCleared)
                    BrickRewardTickets.Bank(data, goldBrickTickets, earnedItems);
            }, cancellationToken);
        }
    }
}
