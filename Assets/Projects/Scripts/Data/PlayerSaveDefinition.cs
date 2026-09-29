using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Framework.Data;
using Framework.Data.Entity;
using UnityEngine;

namespace Projects.Scripts.Data
{
    [CreateAssetMenu(menuName = "Project/Save/Player Save Definition")]
    public sealed class PlayerSaveDefinition : SaveDataDefinition, IFirestoreSaveCodec
    {
        public override string SaveKey => "player";
        public override Type DataType => typeof(PlayerSaveData);
        public override ISaveData CreateDefault() => new PlayerSaveData();

        public Dictionary<string, object> Encode(SaveRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            PlayerSaveData data = JsonUtility.FromJson<PlayerSaveData>(record.Payload);
            if (data == null || record.SchemaVersion != PlayerSaveData.CurrentSchemaVersion) throw new InvalidDataException("Player save schema is invalid.");
            data.Validate();
            List<object> board = new List<object>(PlayerSaveData.BoardSlotCount);
            for (int i = 0; i < PlayerSaveData.BoardSlotCount; i++) board.Add((long)data.GetCatLevel(i));
            List<object> ownedCats = new List<object>(data.OwnedCatCount);
            for (int id = 1; id <= data.OwnedCatCount; id++)
            {
                try { ownedCats.Add((long)data.GetOwnedCatLevel(id)); }
                catch (ArgumentOutOfRangeException) { ownedCats.Add(0L); }
            }
            List<object> formation = new List<object>(PlayerSaveData.BoardSlotCount);
            for (int i = 0; i < PlayerSaveData.BoardSlotCount; i++) formation.Add((long)data.GetFormationCatId(i));
            List<object> inventory = new List<object>();
            ItemStackSaveData[] items = data.GetInventory();
            for (int i = 0; i < items.Length; i++)
            {
                ItemStackSaveData item = items[i];
                inventory.Add(new Dictionary<string, object>
                {
                    ["itemId"] = item.itemId,
                    ["quantity"] = (long)item.quantity
                });
            }
            var fields = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)record.SchemaVersion,
                ["savedAtUnixMilliseconds"] = record.SavedAtUnixMilliseconds,
                ["progress"] = (long)data.StageIndex,
                ["gold"] = (long)data.Gold,
                ["reserveCapacity"] = (long)data.ReserveCapacity,
                ["maxMergedCatLevel"] = (long)data.MaxMergedCatLevel,
                ["board"] = board,
                ["ownedCats"] = ownedCats,
                ["formation"] = formation,
                ["inventory"] = inventory
            };
            string stageAttempt = data.GetStageAttemptPayload();
            if (stageAttempt != null) fields["stageAttempt"] = stageAttempt;
            return fields;
        }

        public SaveRecord Decode(IDictionary<string, object> fields)
        {
            if (fields == null) throw new ArgumentNullException(nameof(fields));
            int schemaVersion = checked((int)GetInteger(fields, "schemaVersion"));
            if (schemaVersion != PlayerSaveData.CurrentSchemaVersion) throw new InvalidDataException($"Unsupported player save schema version: {schemaVersion}.");

            // Previously published saves used one JSON payload field. The next write
            // converts such a document to the visible Firestore field layout.
            if (fields.TryGetValue("payload", out object legacyPayload))
            {
                if (legacyPayload is not string json || string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Legacy player save payload is invalid.");
                PlayerSaveData legacyData = JsonUtility.FromJson<PlayerSaveData>(json);
                if (legacyData == null) throw new InvalidDataException("Legacy player save payload is invalid.");
                legacyData.Validate();
                return new SaveRecord(schemaVersion, GetInteger(fields, "savedAtUnixMilliseconds"), JsonUtility.ToJson(legacyData));
            }

            PlayerSaveData data = new PlayerSaveData();
            data.SetStageIndex(checked((int)GetInteger(fields, "progress")));
            data.SetGold(checked((int)GetInteger(fields, "gold")));
            if (fields.TryGetValue("reserveCapacity", out object reserveCapacity))
                data.SetReserveCapacity(checked((int)AsInteger(reserveCapacity, "reserveCapacity")));
            bool hasMaxMergedCatLevel = fields.TryGetValue("maxMergedCatLevel", out object maxMergedCatLevel);
            if (hasMaxMergedCatLevel)
                data.SetMaxMergedCatLevelFromSave(checked((int)AsInteger(maxMergedCatLevel, "maxMergedCatLevel")));
            if (!fields.TryGetValue("board", out object boardValue) || boardValue is not IList board
                || !IsSupportedBoardSlotCount(board.Count))
                throw new InvalidDataException($"Firestore board must contain {PlayerSaveData.LegacyBoardSlotCount} or {PlayerSaveData.BoardSlotCount} slots.");
            for (int i = 0; i < board.Count; i++) data.SetCatLevel(i, checked((int)AsInteger(board[i], $"board[{i}]")));

            // The visible board remains in the document for old clients. New clients
            // prefer ownership plus formation when both fields are present.
            bool hasOwnedCats = fields.TryGetValue("ownedCats", out object ownedValue);
            bool hasFormation = fields.TryGetValue("formation", out object formationValue);
            if (hasOwnedCats != hasFormation)
                throw new InvalidDataException("Firestore owned cat data requires both ownedCats and formation.");
            if (hasOwnedCats)
            {
                if (ownedValue is not IList owned || formationValue is not IList formation
                    || !IsSupportedBoardSlotCount(formation.Count))
                    throw new InvalidDataException("Firestore owned cat data is invalid.");
                int[] ownedLevels = new int[owned.Count];
                for (int i = 0; i < owned.Count; i++) ownedLevels[i] = checked((int)AsInteger(owned[i], $"ownedCats[{i}]"));
                int[] formationIds = new int[PlayerSaveData.BoardSlotCount];
                for (int slot = 0; slot < formation.Count; slot++)
                {
                    int originalId = checked((int)AsInteger(formation[slot], $"formation[{slot}]"));
                    if (originalId == 0) continue;
                    if (originalId < 1 || originalId > ownedLevels.Length || ownedLevels[originalId - 1] < 1)
                        throw new InvalidDataException($"Firestore formation[{slot}] is invalid.");
                    formationIds[slot] = originalId;
                }
                data.SetOwnedCatsAndFormation(ownedLevels, formationIds);
            }

            if (fields.TryGetValue("inventory", out object inventoryValue))
            {
                if (inventoryValue is not IList entries) throw new InvalidDataException("Firestore inventory must be an array.");
                ItemStackSaveData[] items = new ItemStackSaveData[entries.Count];
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] is not IDictionary<string, object> item 
                        || !item.TryGetValue("itemId", out object id) 
                        || id is not string itemId) throw new InvalidDataException($"Firestore inventory[{i}] is invalid.");
                    items[i] = new ItemStackSaveData
                    {
                        itemId = itemId,
                        quantity = checked((int)GetInteger(item, "quantity"))
                    };
                }
                data.SetInventory(items);
            }

            if (fields.TryGetValue("stageAttempt", out object stageAttemptValue))
            {
                if (stageAttemptValue is not string stageAttempt || string.IsNullOrWhiteSpace(stageAttempt))
                    throw new InvalidDataException("Firestore stage attempt is invalid.");
                try { data.SetStageAttemptPayloadFromSave(stageAttempt); }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    throw new InvalidDataException("Firestore stage attempt is invalid.", exception);
                }
            }

            if (!hasMaxMergedCatLevel) data.MarkMaxMergedCatLevelForMigration();
            data.Validate();
            return new SaveRecord(schemaVersion, GetInteger(fields, "savedAtUnixMilliseconds"), JsonUtility.ToJson(data));
        }

        private static long GetInteger(IDictionary<string, object> fields, string name) 
            => !fields.TryGetValue(name, out object value) 
                ? throw new InvalidDataException($"Firestore save is missing '{name}'.") 
                : AsInteger(value, name);

        private static long AsInteger(object value, string name)
        {
            if (value is long longValue) return longValue;
            if (value is int intValue) return intValue;
            throw new InvalidDataException($"Firestore field '{name}' must be an integer.");
        }

        private static bool IsSupportedBoardSlotCount(int count)
            => count == PlayerSaveData.LegacyBoardSlotCount || count == PlayerSaveData.BoardSlotCount;
    }
}
