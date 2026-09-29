using System;
using Framework.Data;
using Framework.Data.Entity;
using UnityEngine;

namespace Projects.Scripts.Data
{
    /// <summary>
    /// CatMergeDrop progression fields;
    /// shared save infrastructure lives in Framework.Data.
    /// </summary>
    [Serializable]
    public sealed class PlayerSaveData : ISaveData
    {
        public const int CurrentSchemaVersion = 1;
        public const int LegacyBoardSlotCount = 12;
        public const int BoardSlotCount = 18;
        public const int DefaultReserveCapacity = 12;
        public const int MaxReserveCapacity = 33;
        // Four is the current stage budget; retain a bounded format for future difficulty scaling.
        public const int MaxStageAttemptDropBudget = 100;
        public const int MaxAttemptBrickHealthCount = 60;

        [SerializeField] private int schemaVersionValue = CurrentSchemaVersion;
        // The first three stage-one summons are funded so a new save can start playing.
        [SerializeField] private int gold = 30;
        [SerializeField] private int stageIndex = 1;
        [SerializeField] private int reserveCapacity = DefaultReserveCapacity;
        [SerializeField] private int maxMergedCatLevel = 1;
        [SerializeField] private bool maxMergedCatLevelInitialized;
        /// <summary>
        /// Six columns by three rows, flattened row-major.
        /// Zero means empty.
        /// </summary>
        [SerializeField] private int[] catLevelsByBoardSlot = new int[BoardSlotCount];
        // Ownership is independent from the currently formed board. Cat ids are one-based
        // indexes into ownedCatLevels so zero can represent an empty formation slot.
        [SerializeField] private bool ownershipInitialized;
        [SerializeField] private int nextOwnedCatId = 1;
        [SerializeField] private int[] ownedCatLevels = Array.Empty<int>();
        [SerializeField] private int[] formationCatIds = new int[BoardSlotCount];
        [SerializeField] private ItemStackSaveData[] inventory = Array.Empty<ItemStackSaveData>();
        // This is deliberately a small, non-recursive snapshot of the player state before
        // stage preparation changes it. It lets a failed attempt reset economy and cats.
        [SerializeField] private bool stageAttemptActive;
        [SerializeField] private StageAttemptSaveData stageAttempt;

        int ISaveData.SchemaVersion => schemaVersionValue;
        public int Gold => gold;
        public int StageIndex => stageIndex;
        public int ReserveCapacity { get { EnsureReserveCapacityMigrated(); return reserveCapacity; } }
        public int MaxMergedCatLevel
        {
            get
            {
                EnsureOwnershipMigrated();
                EnsureMaxMergedCatLevelMigrated();
                return maxMergedCatLevel;
            }
        }

        public bool HasStageAttempt => stageAttemptActive;
        public int AttemptDropBudget => !stageAttemptActive ? 0 : GetActiveStageAttempt().dropBudget;
        public int AttemptDropsRemaining => !stageAttemptActive ? 0 : GetActiveStageAttempt().dropsRemaining;
        public bool AttemptDropInFlight => stageAttemptActive && GetActiveStageAttempt().dropInFlight;
        public int AttemptCatalogRevision => !stageAttemptActive ? 0 : GetActiveStageAttempt().catalogRevision;

        /// <summary>Captures the stage-entry state before any attempt preparation mutates it.</summary>
        public void BeginStageAttempt(int dropBudget, int catalogRevision = 0)
        {
            if (HasStageAttempt) throw new InvalidOperationException("A stage attempt is already active.");
            if (dropBudget < 1 || dropBudget > MaxStageAttemptDropBudget)
                throw new ArgumentOutOfRangeException(nameof(dropBudget));
            if (catalogRevision < 0) throw new ArgumentOutOfRangeException(nameof(catalogRevision));

            EnsureOwnershipMigrated();
            EnsureMaxMergedCatLevelMigrated();
            EnsureReserveCapacityMigrated();
            stageAttempt = StageAttemptSaveData.From(this, dropBudget, catalogRevision);
            stageAttemptActive = true;
        }

        /// <summary>Persists a reservation before a drop starts so a terminated app cannot grant a free drop.</summary>
        public void ReserveAttemptDrop()
        {
            RequireStageAttempt();
            StageAttemptSaveData attempt = GetActiveStageAttempt();
            if (attempt.dropInFlight) throw new InvalidOperationException("A stage attempt drop is already in flight.");
            if (attempt.dropsRemaining < 1) throw new InvalidOperationException("No stage attempt drops remain.");
            attempt.dropsRemaining--;
            attempt.dropInFlight = true;
        }

        /// <summary>Commits row-major brick health after a completed drop. Zero represents a destroyed brick.</summary>
        public void CommitAttemptDrop(int[] brickHealth)
        {
            RequireStageAttempt();
            StageAttemptSaveData attempt = GetActiveStageAttempt();
            if (!attempt.dropInFlight) throw new InvalidOperationException("No stage attempt drop is in flight.");
            attempt.SetBrickHealth(brickHealth);
            attempt.dropInFlight = false;
        }

        public int[] GetAttemptBrickHealth()
            => !stageAttemptActive || GetActiveStageAttempt().brickHealth == null
                ? Array.Empty<int>() : (int[])stageAttempt.brickHealth.Clone();

        /// <summary>Refunds only an interrupted reservation; the last committed brick state remains intact.</summary>
        public bool RecoverInterruptedAttemptDrop()
        {
            if (!stageAttemptActive) return false;
            StageAttemptSaveData attempt = GetActiveStageAttempt();
            if (!attempt.dropInFlight) return false;
            if (attempt.dropsRemaining >= attempt.dropBudget)
                throw new InvalidOperationException("Stage attempt drop reservation is invalid.");
            attempt.dropsRemaining++;
            attempt.dropInFlight = false;
            return true;
        }

        /// <summary>Restores all stage-entry state and starts the same drop budget again without charging the player.</summary>
        public void RestartStageAttempt(int? catalogRevision = null)
        {
            RequireStageAttempt();
            StageAttemptSaveData checkpoint = GetActiveStageAttempt();
            int revision = catalogRevision ?? checkpoint.catalogRevision;
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(catalogRevision));
            checkpoint.RestoreInto(this);
            stageAttempt = StageAttemptSaveData.From(this, checkpoint.dropBudget, revision);
            stageAttemptActive = true;
        }

        public void CompleteStageAttempt()
        {
            stageAttemptActive = false;
            stageAttempt = null;
        }

        /// <summary>Firestore uses this compact nested JSON field to avoid recursive save documents.</summary>
        public string GetStageAttemptPayload()
            => !stageAttemptActive ? null : JsonUtility.ToJson(GetActiveStageAttempt());

        /// <summary>Codec-only import of the optional Firestore stage-attempt checkpoint.</summary>
        public void SetStageAttemptPayloadFromSave(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Stage attempt payload is empty.", nameof(payload));
            StageAttemptSaveData parsed = JsonUtility.FromJson<StageAttemptSaveData>(payload);
            if (parsed == null) throw new ArgumentException("Stage attempt payload is invalid.", nameof(payload));
            parsed.Validate();
            stageAttempt = parsed;
            stageAttemptActive = true;
        }

        public int GetCatLevel(int slot)
        {
            if (slot < 0 || slot >= BoardSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            EnsureOwnershipMigrated();
            int catId = formationCatIds[slot];
            return catId == 0 ? 0 : GetOwnedCatLevel(catId);
        }

        public int OwnedCatCount { get { EnsureOwnershipMigrated(); return ownedCatLevels.Length; } }
        public int ActiveOwnedCatCount
        {
            get
            {
                EnsureOwnershipMigrated();
                int count = 0;
                for (int i = 0; i < ownedCatLevels.Length; i++) if (ownedCatLevels[i] > 0) count++;
                return count;
            }
        }
        public int GetFormationCatId(int slot)
        {
            if (slot < 0 || slot >= BoardSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            EnsureOwnershipMigrated();
            return formationCatIds[slot];
        }

        public int GetOwnedCatLevel(int catId)
        {
            EnsureOwnershipMigrated();
            if (catId < 1 || catId > ownedCatLevels.Length || ownedCatLevels[catId - 1] < 1)
                throw new ArgumentOutOfRangeException(nameof(catId));
            return ownedCatLevels[catId - 1];
        }

        public int[] GetUnformedCatIds()
        {
            EnsureOwnershipMigrated();
            bool[] formed = new bool[ownedCatLevels.Length + 1];
            for (int i = 0; i < formationCatIds.Length; i++)
                if (formationCatIds[i] > 0 && formationCatIds[i] < formed.Length) formed[formationCatIds[i]] = true;
            var ids = new System.Collections.Generic.List<int>();
            for (int id = 1; id <= ownedCatLevels.Length; id++) if (ownedCatLevels[id - 1] > 0 && !formed[id]) ids.Add(id);
            return ids.ToArray();
        }

        public void SetGold(int value) => gold = value;
        public void SetStageIndex(int value) => stageIndex = value;
        public void SetReserveCapacity(int value)
        {
            if (value < DefaultReserveCapacity || value > MaxReserveCapacity)
                throw new ArgumentOutOfRangeException(nameof(value));
            reserveCapacity = value;
        }

        public void SetCatLevel(int slot, int level)
        {
            if (slot < 0 || slot >= BoardSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));
            EnsureOwnershipMigrated();
            int catId = formationCatIds[slot];
            if (level == 0)
            {
                formationCatIds[slot] = 0;
                return;
            }
            if (catId == 0)
            {
                AddOwnedCat(level, slot);
                return;
            }
            ownedCatLevels[catId - 1] = level;
        }

        public int AddOwnedCat(int level, int formationSlot = -1)
        {
            EnsureOwnershipMigrated();
            EnsureMaxMergedCatLevelMigrated();
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (formationSlot < -1 || formationSlot >= BoardSlotCount) throw new ArgumentOutOfRangeException(nameof(formationSlot));
            int id = nextOwnedCatId++;
            if (id != ownedCatLevels.Length + 1) throw new InvalidOperationException("Owned cat ids must be contiguous.");
            Array.Resize(ref ownedCatLevels, id);
            ownedCatLevels[id - 1] = level;
            if (formationSlot >= 0) formationCatIds[formationSlot] = id;
            return id;
        }

        public void MoveFormationCat(int sourceSlot, int targetSlot)
        {
            EnsureOwnershipMigrated();
            ValidateSlot(sourceSlot); ValidateSlot(targetSlot);
            if (formationCatIds[sourceSlot] == 0) throw new InvalidOperationException("Source formation slot is empty.");
            (formationCatIds[sourceSlot], formationCatIds[targetSlot]) = (formationCatIds[targetSlot], formationCatIds[sourceSlot]);
        }

        public void MergeFormationCats(int sourceSlot, int targetSlot)
        {
            EnsureOwnershipMigrated();
            EnsureMaxMergedCatLevelMigrated();
            ValidateSlot(sourceSlot); ValidateSlot(targetSlot);
            int sourceId = formationCatIds[sourceSlot];
            int targetId = formationCatIds[targetSlot];
            if (sourceId == 0 || targetId == 0 || GetOwnedCatLevel(sourceId) != GetOwnedCatLevel(targetId)) throw new InvalidOperationException("Only equal-level formed cats can merge.");
            if (ownedCatLevels[targetId - 1] == int.MaxValue) throw new InvalidOperationException("Cat level cannot increase further.");
            int mergedLevel = ++ownedCatLevels[targetId - 1];
            if (mergedLevel > maxMergedCatLevel) maxMergedCatLevel = mergedLevel;
            maxMergedCatLevelInitialized = true;
            ownedCatLevels[sourceId - 1] = 0;
            formationCatIds[sourceSlot] = 0;
        }

        public void AssignOwnedCatToFormation(int catId, int slot)
        {
            EnsureOwnershipMigrated();
            ValidateSlot(slot);
            GetOwnedCatLevel(catId);
            if (formationCatIds[slot] != 0) throw new InvalidOperationException("Formation slot is occupied.");
            for (int i = 0; i < formationCatIds.Length; i++) if (formationCatIds[i] == catId)
                throw new InvalidOperationException("Cat is already formed.");
            formationCatIds[slot] = catId;
        }

        public void UnassignFormationSlot(int slot)
        {
            EnsureOwnershipMigrated();
            ValidateSlot(slot);
            formationCatIds[slot] = 0;
        }

        /// <summary>
        /// Fits the current formation into the active board area. Cats outside that area
        /// fill empty active slots in row-major order; cats that cannot fit return to reserve.
        /// </summary>
        public void FitFormationToSlotCount(int activeSlotCount)
        {
            EnsureOwnershipMigrated();
            if (activeSlotCount < 0 || activeSlotCount > BoardSlotCount)
                throw new ArgumentOutOfRangeException(nameof(activeSlotCount));

            for (int sourceSlot = activeSlotCount; sourceSlot < BoardSlotCount; sourceSlot++)
            {
                int catId = formationCatIds[sourceSlot];
                if (catId == 0) continue;

                int targetSlot = Array.FindIndex(formationCatIds, 0, activeSlotCount, catIdBySlot => catIdBySlot == 0);
                if (targetSlot >= 0) formationCatIds[targetSlot] = catId;
                formationCatIds[sourceSlot] = 0;
            }
        }

        /// <summary>Forms valid unformed cats into empty active slots in ascending id order.</summary>
        public void FillFormationFromUnformed(int activeSlotCount)
        {
            EnsureOwnershipMigrated();
            if (activeSlotCount < 0 || activeSlotCount > BoardSlotCount)
                throw new ArgumentOutOfRangeException(nameof(activeSlotCount));

            bool[] formed = new bool[ownedCatLevels.Length + 1];
            for (int slot = 0; slot < formationCatIds.Length; slot++)
                if (formationCatIds[slot] > 0) formed[formationCatIds[slot]] = true;

            int nextId = 1;
            for (int slot = 0; slot < activeSlotCount; slot++)
            {
                if (formationCatIds[slot] != 0) continue;
                while (nextId <= ownedCatLevels.Length && (ownedCatLevels[nextId - 1] < 1 || formed[nextId])) nextId++;
                if (nextId > ownedCatLevels.Length) return;
                formationCatIds[slot] = nextId;
                formed[nextId] = true;
                nextId++;
            }
        }

        public void ReturnFormationCatToReserve(int slot)
        {
            EnsureOwnershipMigrated();
            ValidateSlot(slot);
            if (formationCatIds[slot] == 0) throw new InvalidOperationException("Formation slot is empty.");
            formationCatIds[slot] = 0;
        }

        /// <summary>Forms an unformed cat in an occupied slot and returns its former cat to reserve.</summary>
        public void ReplaceFormationCatWithOwnedCat(int catId, int slot)
        {
            EnsureOwnershipMigrated();
            ValidateSlot(slot);
            GetOwnedCatLevel(catId);
            if (formationCatIds[slot] == 0) throw new InvalidOperationException("Formation slot is empty.");
            for (int i = 0; i < formationCatIds.Length; i++)
                if (formationCatIds[i] == catId) throw new InvalidOperationException("Cat is already formed.");
            formationCatIds[slot] = catId;
        }

        public int SellOwnedCat(int catId, int payout)
        {
            EnsureOwnershipMigrated();
            if (payout < 0) throw new ArgumentOutOfRangeException(nameof(payout));
            GetOwnedCatLevel(catId);
            for (int i = 0; i < formationCatIds.Length; i++) if (formationCatIds[i] == catId) formationCatIds[i] = 0;
            ownedCatLevels[catId - 1] = 0;
            gold = (int)Math.Min(int.MaxValue, (long)gold + payout);
            return payout;
        }

        /// <summary>
        /// Permanently consumes an owned cat without granting a payout. Repeating a
        /// completed consumption is safe and returns false.
        /// </summary>
        public bool ConsumeOwnedCat(int catId)
        {
            EnsureOwnershipMigrated();
            if (catId < 1 || catId > ownedCatLevels.Length)
                throw new ArgumentOutOfRangeException(nameof(catId));
            if (ownedCatLevels[catId - 1] < 1) return false;

            for (int i = 0; i < formationCatIds.Length; i++)
                if (formationCatIds[i] == catId) formationCatIds[i] = 0;
            ownedCatLevels[catId - 1] = 0;
            return true;
        }

        /// <summary>Ends the stage's roster, including reserve cats and merged descendants.</summary>
        public void ClearStageCats()
        {
            EnsureOwnershipMigrated();
            EnsureMaxMergedCatLevelMigrated();
            ownedCatLevels = Array.Empty<int>();
            formationCatIds = new int[BoardSlotCount];
            catLevelsByBoardSlot = new int[BoardSlotCount];
            nextOwnedCatId = 1;
        }

        /// <summary>Codec-only replacement that preserves stable one-based ids, including sold-id gaps.</summary>
        public void SetOwnedCatsAndFormation(int[] levels, int[] catIdsBySlot)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (catIdsBySlot == null || catIdsBySlot.Length != BoardSlotCount) throw new ArgumentException("Formation must contain every board slot.", nameof(catIdsBySlot));
            ownedCatLevels = (int[])levels.Clone();
            formationCatIds = (int[])catIdsBySlot.Clone();
            nextOwnedCatId = ownedCatLevels.Length + 1;
            ownershipInitialized = true;
        }

        public ItemStackSaveData[] GetInventory()
        {
            var copy = new ItemStackSaveData[inventory.Length];
            for (int i = 0; i < inventory.Length; i++)
                copy[i] = new ItemStackSaveData { itemId = inventory[i].itemId, quantity = inventory[i].quantity };
            return copy;
        }

        public void SetInventory(ItemStackSaveData[] items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            inventory = new ItemStackSaveData[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) throw new ArgumentException("Inventory contains a null item.", nameof(items));
                inventory[i] = new ItemStackSaveData { itemId = items[i].itemId, quantity = items[i].quantity };
            }
        }

        public void Validate()
        {
            if (schemaVersionValue != CurrentSchemaVersion)
                throw new InvalidOperationException($"Unsupported save schema version: {schemaVersionValue}.");
            if (gold < 0) throw new InvalidOperationException("Gold cannot be negative.");
            if (stageIndex < 1) throw new InvalidOperationException("StageIndex must be at least 1.");
            EnsureReserveCapacityMigrated();
            if (reserveCapacity < DefaultReserveCapacity || reserveCapacity > MaxReserveCapacity)
                throw new InvalidOperationException($"Reserve capacity must be between {DefaultReserveCapacity} and {MaxReserveCapacity}.");
            EnsureBoardSlotsMigrated();

            EnsureOwnershipMigrated();
            EnsureMaxMergedCatLevelMigrated();

            for (int i = 0; i < catLevelsByBoardSlot.Length; i++)
            {
                if (catLevelsByBoardSlot[i] < 0)
                    throw new InvalidOperationException($"Cat level at board slot {i} cannot be negative.");
            }

            if (formationCatIds.Length != BoardSlotCount || nextOwnedCatId != ownedCatLevels.Length + 1)
                throw new InvalidOperationException("Owned cat formation is invalid.");
            bool[] formed = new bool[ownedCatLevels.Length + 1];
            for (int i = 0; i < formationCatIds.Length; i++)
            {
                int catId = formationCatIds[i];
                if (catId < 0 || catId > ownedCatLevels.Length || (catId > 0 && ownedCatLevels[catId - 1] < 1))
                    throw new InvalidOperationException($"Formation cat at slot {i} is invalid.");
                if (catId > 0 && formed[catId]) throw new InvalidOperationException($"Cat {catId} appears in multiple formation slots.");
                if (catId > 0) formed[catId] = true;
            }
            for (int i = 0; i < ownedCatLevels.Length; i++) if (ownedCatLevels[i] < 0)
                throw new InvalidOperationException($"Owned cat level {i + 1} cannot be negative.");
            if (maxMergedCatLevel < 1)
                throw new InvalidOperationException("Max merged cat level must be at least one.");

            if (inventory == null) throw new InvalidOperationException("Inventory cannot be null.");
            for (int i = 0; i < inventory.Length; i++)
            {
                ItemStackSaveData item = inventory[i];
                if (item == null || string.IsNullOrWhiteSpace(item.itemId) || item.quantity < 1)
                    throw new InvalidOperationException($"Inventory entry {i} is invalid.");
            }
            if (stageAttemptActive)
            {
                if (stageAttempt == null) throw new InvalidOperationException("Stage attempt checkpoint is missing.");
                stageAttempt.Validate();
            }
        }

        private void EnsureOwnershipMigrated()
        {
            EnsureBoardSlotsMigrated();
            if (ownershipInitialized) return;
            ownedCatLevels = Array.Empty<int>();
            formationCatIds = new int[BoardSlotCount];
            nextOwnedCatId = 1;
            for (int slot = 0; slot < BoardSlotCount; slot++)
            {
                int level = catLevelsByBoardSlot[slot];
                if (level < 0) throw new InvalidOperationException($"Cat level at board slot {slot} cannot be negative.");
                if (level > 0) AddOwnedCatWithoutMigration(level, slot);
            }
            ownershipInitialized = true;
        }

        private void EnsureReserveCapacityMigrated()
        {
            // Saves written before reserve upgrades have no field and deserialize as zero.
            if (reserveCapacity == 0) reserveCapacity = DefaultReserveCapacity;
        }

        /// <summary>Seeds merge progression for saves written before it was tracked.</summary>
        private void EnsureMaxMergedCatLevelMigrated()
        {
            if (maxMergedCatLevelInitialized) return;
            maxMergedCatLevel = 1;
            for (int i = 0; i < ownedCatLevels.Length; i++)
                if (ownedCatLevels[i] > maxMergedCatLevel) maxMergedCatLevel = ownedCatLevels[i];
            maxMergedCatLevelInitialized = true;
        }

        /// <summary>Codec-only import of previously persisted merge progression.</summary>
        internal void SetMaxMergedCatLevelFromSave(int value)
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value));
            maxMergedCatLevel = value;
            maxMergedCatLevelInitialized = true;
        }

        /// <summary>Codec-only marker for saves written before merge progression existed.</summary>
        internal void MarkMaxMergedCatLevelForMigration() => maxMergedCatLevelInitialized = false;

        /// <summary>
        /// Expands the formerly published 4-by-3 board to the current 6-by-3 board.
        /// The first twelve row-major slots retain their ids and levels; new slots are empty.
        /// </summary>
        private void EnsureBoardSlotsMigrated()
        {
            catLevelsByBoardSlot = ExpandLegacyBoardSlots(catLevelsByBoardSlot, nameof(catLevelsByBoardSlot));
            formationCatIds = ExpandLegacyBoardSlots(formationCatIds, nameof(formationCatIds));
        }

        private static int[] ExpandLegacyBoardSlots(int[] slots, string fieldName)
        {
            if (slots == null)
                throw new InvalidOperationException($"The board save field '{fieldName}' cannot be null.");
            if (slots.Length == BoardSlotCount) return slots;
            if (slots.Length != LegacyBoardSlotCount)
                throw new InvalidOperationException($"The board save field '{fieldName}' must contain {LegacyBoardSlotCount} or {BoardSlotCount} slots.");

            int[] expanded = new int[BoardSlotCount];
            Array.Copy(slots, expanded, LegacyBoardSlotCount);
            return expanded;
        }

        private void AddOwnedCatWithoutMigration(int level, int slot)
        {
            int id = nextOwnedCatId++;
            Array.Resize(ref ownedCatLevels, id);
            ownedCatLevels[id - 1] = level;
            formationCatIds[slot] = id;
        }

        private static void ValidateSlot(int slot)
        {
            if (slot is < 0 or >= BoardSlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
        }

        private void RequireStageAttempt()
        {
            if (!stageAttemptActive) throw new InvalidOperationException("No stage attempt is active.");
        }

        private StageAttemptSaveData GetActiveStageAttempt()
            => stageAttempt ?? throw new InvalidOperationException("Stage attempt checkpoint is missing.");

        [Serializable]
        private sealed class StageAttemptSaveData
        {
            public int dropBudget;
            public int dropsRemaining;
            public bool dropInFlight;
            public int catalogRevision;
            public int[] brickHealth = Array.Empty<int>();
            public int entryGold;
            public int entryStageIndex;
            public int entryReserveCapacity;
            public int entryMaxMergedCatLevel;
            public bool entryMaxMergedCatLevelInitialized;
            public int[] entryCatLevelsByBoardSlot;
            public bool entryOwnershipInitialized;
            public int entryNextOwnedCatId;
            public int[] entryOwnedCatLevels;
            public int[] entryFormationCatIds;
            public ItemStackSaveData[] entryInventory;

            public static StageAttemptSaveData From(PlayerSaveData data, int budget, int revision)
            {
                return new StageAttemptSaveData
                {
                    dropBudget = budget,
                    dropsRemaining = budget,
                    catalogRevision = revision,
                    entryGold = data.gold,
                    entryStageIndex = data.stageIndex,
                    entryReserveCapacity = data.reserveCapacity,
                    entryMaxMergedCatLevel = data.maxMergedCatLevel,
                    entryMaxMergedCatLevelInitialized = data.maxMergedCatLevelInitialized,
                    entryCatLevelsByBoardSlot = (int[])data.catLevelsByBoardSlot.Clone(),
                    entryOwnershipInitialized = data.ownershipInitialized,
                    entryNextOwnedCatId = data.nextOwnedCatId,
                    entryOwnedCatLevels = (int[])data.ownedCatLevels.Clone(),
                    entryFormationCatIds = (int[])data.formationCatIds.Clone(),
                    entryInventory = CloneInventory(data.inventory)
                };
            }

            public void RestoreInto(PlayerSaveData data)
            {
                data.gold = entryGold;
                data.stageIndex = entryStageIndex;
                data.reserveCapacity = entryReserveCapacity;
                data.maxMergedCatLevel = entryMaxMergedCatLevel;
                data.maxMergedCatLevelInitialized = entryMaxMergedCatLevelInitialized;
                data.catLevelsByBoardSlot = (int[])entryCatLevelsByBoardSlot.Clone();
                data.ownershipInitialized = entryOwnershipInitialized;
                data.nextOwnedCatId = entryNextOwnedCatId;
                data.ownedCatLevels = (int[])entryOwnedCatLevels.Clone();
                data.formationCatIds = (int[])entryFormationCatIds.Clone();
                data.inventory = CloneInventory(entryInventory);
            }

            public void SetBrickHealth(int[] values)
            {
                if (values == null || values.Length < 1 || values.Length > MaxAttemptBrickHealthCount)
                    throw new ArgumentOutOfRangeException(nameof(values));
                for (int i = 0; i < values.Length; i++)
                    if (values[i] < 0) throw new ArgumentOutOfRangeException(nameof(values));
                brickHealth = (int[])values.Clone();
            }

            public void Validate()
            {
                if (dropBudget < 1 || dropBudget > MaxStageAttemptDropBudget || catalogRevision < 0 || dropsRemaining < 0 || dropsRemaining > dropBudget
                    || (dropInFlight && dropsRemaining >= dropBudget))
                    throw new InvalidOperationException("Stage attempt drop state is invalid.");
                if (brickHealth == null || brickHealth.Length > MaxAttemptBrickHealthCount)
                    throw new InvalidOperationException("Stage attempt brick health is invalid.");
                for (int i = 0; i < brickHealth.Length; i++) if (brickHealth[i] < 0)
                    throw new InvalidOperationException("Stage attempt brick health is invalid.");

                var entry = new PlayerSaveData
                {
                    gold = entryGold,
                    stageIndex = entryStageIndex,
                    reserveCapacity = entryReserveCapacity,
                    maxMergedCatLevel = entryMaxMergedCatLevel,
                    maxMergedCatLevelInitialized = entryMaxMergedCatLevelInitialized,
                    catLevelsByBoardSlot = entryCatLevelsByBoardSlot,
                    ownershipInitialized = entryOwnershipInitialized,
                    nextOwnedCatId = entryNextOwnedCatId,
                    ownedCatLevels = entryOwnedCatLevels,
                    formationCatIds = entryFormationCatIds,
                    inventory = entryInventory
                };
                entry.Validate();
            }

            private static ItemStackSaveData[] CloneInventory(ItemStackSaveData[] source)
            {
                if (source == null) return null;
                var copy = new ItemStackSaveData[source.Length];
                for (int i = 0; i < source.Length; i++)
                    copy[i] = source[i] == null ? null : new ItemStackSaveData { itemId = source[i].itemId, quantity = source[i].quantity };
                return copy;
            }
        }
    }

    [Serializable]
    public sealed class ItemStackSaveData
    {
        public string itemId;
        public int quantity;
    }
}
