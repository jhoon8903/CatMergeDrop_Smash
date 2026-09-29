using System;
using System.Collections.Generic;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Special bricks bank one opening per destroyed brick; cat openings expire with the stage.</summary>
    public static class BrickRewardTickets
    {
        public const string GoldId = "gold_brick_opening";
        public const string ItemId = "item_brick_opening";

        public readonly struct OpenResult
        {
            public readonly int GoldOpened;
            public readonly int ItemOpened;
            public readonly int GoldGranted;
            public readonly int[] CatLevels;

            public OpenResult(int goldOpened, int itemOpened, int goldGranted, int[] catLevels)
            {
                GoldOpened = goldOpened;
                ItemOpened = itemOpened;
                GoldGranted = goldGranted;
                CatLevels = catLevels;
            }
        }

        public static int Count(PlayerSaveData data, string ticketId)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ItemStackSaveData[] items = data.GetInventory();
            for (int i = 0; i < items.Length; i++)
                if (items[i].itemId == ticketId) return items[i].quantity;
            return 0;
        }

        public static void Bank(PlayerSaveData data, int goldCount, int itemCount)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (goldCount < 0 || itemCount < 0) throw new ArgumentOutOfRangeException();
            if (goldCount == 0 && itemCount == 0) return;
            List<ItemStackSaveData> items = new List<ItemStackSaveData>(data.GetInventory());
            Add(items, GoldId, goldCount);
            Add(items, ItemId, itemCount);
            data.SetInventory(items.ToArray());
        }

        /// <summary>Unopened cat rewards cannot create last stage's cats in the next stage.</summary>
        public static void ExpireStageCatRewards(PlayerSaveData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var items = new List<ItemStackSaveData>(data.GetInventory());
            items.RemoveAll(item => item.itemId == ItemId);
            data.SetInventory(items.ToArray());
        }

        public static OpenResult OpenAll(PlayerSaveData data, StageBalanceConfig balance,
            Func<int, int> rollLevel = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            return OpenAll(data, balance, ActiveBoardSlotCount(data, balance), rollLevel);
        }

        /// <summary>
        /// Opens all gold tickets and as many cat tickets as the active board can hold.
        /// Cat tickets without a vacant board slot remain unopened; rewards never create
        /// unformed cats for a reserve or later automatic placement.
        /// </summary>
        public static OpenResult OpenAll(PlayerSaveData data, StageBalanceConfig balance,
            int activeBoardSlotCount, Func<int, int> rollLevel = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (balance == null) throw new ArgumentNullException(nameof(balance));
            if (activeBoardSlotCount < 0 || activeBoardSlotCount > PlayerSaveData.BoardSlotCount)
                throw new ArgumentOutOfRangeException(nameof(activeBoardSlotCount));
            rollLevel ??= maxLevel => UnityEngine.Random.Range(0, maxLevel) + 1;
            List<ItemStackSaveData> items = new List<ItemStackSaveData>(data.GetInventory());
            int goldCount = Remove(items, GoldId);
            int itemCount = Remove(items, ItemId);
            if (goldCount == 0 && itemCount == 0) return new OpenResult(0, 0, 0, Array.Empty<int>());

            balance.GetSummonLevelRange(data.MaxMergedCatLevel, out _, out int maximum);
            long payout = (long)goldCount * 3L * balance.SummonCostFor(data.MaxMergedCatLevel);
            int granted = (int)Math.Min(payout, int.MaxValue - (long)data.Gold);
            data.SetGold(data.Gold + granted);
            int[] vacantSlots = VacantSlots(data, activeBoardSlotCount);
            int itemOpened = Math.Min(itemCount, vacantSlots.Length);
            int[] levels = new int[itemOpened];
            for (int i = 0; i < levels.Length; i++)
            {
                int level = rollLevel(maximum);
                if (level < 1 || level > maximum)
                    throw new InvalidOperationException("Item Brick cat level is outside 1..Spawn Max Level.");
                levels[i] = level;
                data.AddOwnedCat(level, vacantSlots[i]);
            }
            Add(items, ItemId, itemCount - itemOpened);
            data.SetInventory(items.ToArray());
            return new OpenResult(goldCount, itemOpened, granted, levels);
        }

        private static int ActiveBoardSlotCount(PlayerSaveData data, StageBalanceConfig balance)
        {
            StageDefinition definition = balance.GetStageDefinition(data.StageIndex);
            StageLayout layout = definition != null && data.HasStageAttempt
                ? StageBalanceConfig.LayoutFor(definition, data.AttemptCatalogRevision)
                : balance.LayoutForStage(data.StageIndex);
            return layout.BoardSlotCount;
        }

        private static int[] VacantSlots(PlayerSaveData data, int activeBoardSlotCount)
        {
            var slots = new List<int>();
            for (int slot = 0; slot < activeBoardSlotCount; slot++)
                if (data.GetFormationCatId(slot) == 0) slots.Add(slot);
            return slots.ToArray();
        }

        private static void Add(List<ItemStackSaveData> items, string id, int count)
        {
            if (count == 0) return;
            int index = items.FindIndex(item => item.itemId == id);
            if (index < 0) items.Add(new ItemStackSaveData { itemId = id, quantity = count });
            else items[index].quantity = (int)Math.Min(int.MaxValue, (long)items[index].quantity + count);
        }

        private static int Remove(List<ItemStackSaveData> items, string id)
        {
            int index = items.FindIndex(item => item.itemId == id);
            if (index < 0) return 0;
            int count = items[index].quantity;
            items.RemoveAt(index);
            return count;
        }
    }
}
