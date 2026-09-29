using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data;
using Framework.Data.Entity;
using Framework.Data.Local;
using NUnit.Framework;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class PlayerSaveDefinitionTests
    {
        private const string LegacyTwelveSlotPayload = "{\"schemaVersionValue\":1,\"gold\":42,\"stageIndex\":3,\"catLevelsByBoardSlot\":[2,0,0,0,0,0,0,0,0,0,0,5],\"ownershipInitialized\":true,\"nextOwnedCatId\":4,\"ownedCatLevels\":[2,0,5],\"formationCatIds\":[1,0,0,0,0,0,0,0,0,0,0,3],\"inventory\":[{\"itemId\":\"hint\",\"quantity\":2}]}";
        private PlayerSaveDefinition _definition;
        private StageBalanceConfig _stageBalanceConfig;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<PlayerSaveDefinition>();
            _stageBalanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_definition);
            UnityEngine.Object.DestroyImmediate(_stageBalanceConfig);
        }

        [Test]
        public void FirestoreFields_RoundTripPlayerSave()
        {
            var original = new PlayerSaveData();
            original.SetStageIndex(3);
            original.SetGold(42);
            original.SetCatLevel(0, 2);
            original.SetCatLevel(11, 5);
            original.SetInventory(new[] { new ItemStackSaveData { itemId = "hint", quantity = 2 } });
            var record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion, 12345, JsonUtility.ToJson(original));

            Dictionary<string, object> fields = _definition.Encode(record);
            Assert.That(fields["progress"], Is.EqualTo(3L));
            Assert.That(fields["gold"], Is.EqualTo(42L));
            Assert.That(fields["reserveCapacity"], Is.EqualTo((long)PlayerSaveData.DefaultReserveCapacity));
            Assert.That(fields["maxMergedCatLevel"], Is.EqualTo(1L));
            Assert.That(((IList)fields["board"]).Count, Is.EqualTo(PlayerSaveData.BoardSlotCount));
            Assert.That(fields.ContainsKey("payload"), Is.False);

            SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);
            Assert.That(restored.StageIndex, Is.EqualTo(3));
            Assert.That(restored.Gold, Is.EqualTo(42));
            Assert.That(restored.GetCatLevel(0), Is.EqualTo(2));
            Assert.That(restored.GetCatLevel(11), Is.EqualTo(5));
            Assert.That(restored.GetInventory()[0].itemId, Is.EqualTo("hint"));
        }

        [Test]
        public void FirestoreFields_RejectWrongBoardSize()
        {
            var fields = _definition.Encode(new SaveRecord(1, 12345, JsonUtility.ToJson(new PlayerSaveData())));
            fields["board"] = new List<object> { 0L };
            Assert.Throws<System.IO.InvalidDataException>(() => _definition.Decode(fields));
        }

        [Test]
        public async Task LocalTwelveSlotSave_MigratesToEighteenSlotsWithoutLosingOwnership()
        {
            var repository = new MemoryRepository(new SaveRecord(1, 12345, LegacyTwelveSlotPayload));
            var saveSystem = new SaveSystem(repository, _definition);

            await saveSystem.LoadAsync(CancellationToken.None);
            PlayerSaveData migrated = saveSystem.GetSnapshot<PlayerSaveData>();

            Assert.That(migrated.OwnedCatCount, Is.EqualTo(3));
            Assert.That(migrated.ActiveOwnedCatCount, Is.EqualTo(2));
            Assert.That(migrated.ReserveCapacity, Is.EqualTo(PlayerSaveData.DefaultReserveCapacity));
            Assert.That(migrated.MaxMergedCatLevel, Is.EqualTo(5));
            Assert.That(migrated.GetOwnedCatLevel(1), Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => migrated.GetOwnedCatLevel(2));
            Assert.That(migrated.GetFormationCatId(11), Is.EqualTo(3));
            Assert.That(migrated.GetCatLevel(11), Is.EqualTo(5));
            Assert.That(migrated.GetCatLevel(12), Is.Zero);
            Assert.That(migrated.GetCatLevel(17), Is.Zero);
            Assert.That(migrated.GetInventory()[0].quantity, Is.EqualTo(2));

            await saveSystem.UpdateAsync<PlayerSaveData>(_ => { }, CancellationToken.None);
            Dictionary<string, object> fields = _definition.Encode(repository.Record);
            Assert.That(((IList)fields["board"]).Count, Is.EqualTo(18));
            Assert.That(((IList)fields["formation"]).Count, Is.EqualTo(18));
        }

        [Test]
        public void FirestoreTwelveSlotFields_MigrateToEighteenSlotsAndPreserveFormation()
        {
            var fields = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L,
                ["savedAtUnixMilliseconds"] = 12345L,
                ["progress"] = 3L,
                ["gold"] = 42L,
                ["board"] = new List<object> { 2L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 5L },
                ["ownedCats"] = new List<object> { 2L, 0L, 5L },
                ["formation"] = new List<object> { 1L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 0L, 3L },
                ["inventory"] = new List<object> { new Dictionary<string, object> { ["itemId"] = "hint", ["quantity"] = 2L } }
            };

            SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData migrated = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);
            migrated.Validate();

            Assert.That(migrated.OwnedCatCount, Is.EqualTo(3));
            Assert.That(migrated.ActiveOwnedCatCount, Is.EqualTo(2));
            Assert.That(migrated.ReserveCapacity, Is.EqualTo(PlayerSaveData.DefaultReserveCapacity));
            Assert.That(migrated.MaxMergedCatLevel, Is.EqualTo(5));
            Assert.That(migrated.GetFormationCatId(11), Is.EqualTo(3));
            Assert.That(migrated.GetCatLevel(11), Is.EqualTo(5));
            Assert.That(migrated.GetCatLevel(12), Is.Zero);
            Assert.That(migrated.GetCatLevel(17), Is.Zero);
            Assert.That(migrated.GetInventory()[0].itemId, Is.EqualTo("hint"));

            Dictionary<string, object> normalized = _definition.Encode(decoded);
            Assert.That(((IList)normalized["board"]).Count, Is.EqualTo(18));
            Assert.That(((IList)normalized["formation"]).Count, Is.EqualTo(18));
        }

        [Test]
        public void FirestoreLegacyPayload_MigratesToEighteenSlots()
        {
            var fields = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1L,
                ["savedAtUnixMilliseconds"] = 12345L,
                ["payload"] = LegacyTwelveSlotPayload
            };

            SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData migrated = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);

            Assert.That(migrated.GetFormationCatId(11), Is.EqualTo(3));
            Assert.That(migrated.GetCatLevel(17), Is.Zero);
            Assert.That(((IList)_definition.Encode(decoded)["board"]).Count, Is.EqualTo(18));
        }

        [Test]
        public void FirestoreFields_RoundTripUpgradedReserveCapacity()
        {
            var original = new PlayerSaveData();
            original.SetReserveCapacity(24);
            var record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion, 12345, JsonUtility.ToJson(original));

            Dictionary<string, object> fields = _definition.Encode(record);
            SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);

            Assert.That(fields["reserveCapacity"], Is.EqualTo(24L));
            Assert.That(restored.ReserveCapacity, Is.EqualTo(24));
        }

        [Test]
        public async Task FirstLoad_WritesDefaultSave()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);

            await saveSystem.LoadAsync(CancellationToken.None);

            Assert.That(repository.Record, Is.Not.Null);
            Assert.That(saveSystem.GetSnapshot<PlayerSaveData>().StageIndex, Is.EqualTo(1));
            Assert.That(saveSystem.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(30));
        }

        [Test]
        public async Task BrokenBrickGold_IsSavedEvenWhenTheDropFails()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);
            await saveSystem.LoadAsync(CancellationToken.None);

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 1, false, 1,
                CancellationToken.None, null, 1);

            Assert.That(saveSystem.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(35));
            Assert.That(saveSystem.GetSnapshot<PlayerSaveData>().StageIndex, Is.EqualTo(1));
            Assert.That(BrickRewardTickets.Count(saveSystem.GetSnapshot<PlayerSaveData>(),
                BrickRewardTickets.GoldId), Is.Zero);
            Assert.That(BrickRewardTickets.Count(saveSystem.GetSnapshot<PlayerSaveData>(),
                BrickRewardTickets.ItemId), Is.Zero);
            Assert.That(JsonUtility.FromJson<PlayerSaveData>(repository.Record.Payload).Gold, Is.EqualTo(35));
        }

        [Test]
        public async Task SuccessfulDrop_AtomicallyConsumesParticipatingCatsAndDoesNotApplyTwice()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);
            await saveSystem.LoadAsync(CancellationToken.None);
            int first = 0;
            int second = 0;
            int reserve = 0;
            await saveSystem.UpdateAsync<PlayerSaveData>(data =>
            {
                first = data.AddOwnedCat(2, 0);
                second = data.AddOwnedCat(3, 1);
                reserve = data.AddOwnedCat(4);
            }, CancellationToken.None);

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 1, true, 1,
                CancellationToken.None, new[] { first, second }, 2);

            PlayerSaveData saved = saveSystem.GetSnapshot<PlayerSaveData>();
            Assert.That(saved.StageIndex, Is.EqualTo(2));
            Assert.That(saved.Gold, Is.EqualTo(85));
            Assert.That(saved.GetFormationCatId(0), Is.Zero);
            Assert.That(saved.GetFormationCatId(1), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => saved.GetOwnedCatLevel(first));
            Assert.Throws<ArgumentOutOfRangeException>(() => saved.GetOwnedCatLevel(second));
            Assert.That(saved.GetUnformedCatIds(), Is.EqualTo(new[] { reserve }));
            Assert.That(BrickRewardTickets.Count(saved, BrickRewardTickets.ItemId), Is.EqualTo(1));
            Assert.That(BrickRewardTickets.Count(saved, BrickRewardTickets.GoldId), Is.EqualTo(2));

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 1, true, 1,
                CancellationToken.None, new[] { first, second }, 2);
            PlayerSaveData retried = saveSystem.GetSnapshot<PlayerSaveData>();
            Assert.That(retried.Gold, Is.EqualTo(85));
            Assert.That(BrickRewardTickets.Count(retried, BrickRewardTickets.ItemId), Is.EqualTo(1));
            Assert.That(BrickRewardTickets.Count(retried, BrickRewardTickets.GoldId), Is.EqualTo(2));
        }

        [Test]
        public async Task FailedDrop_RetainsParticipatingCats()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);
            await saveSystem.LoadAsync(CancellationToken.None);
            await saveSystem.UpdateAsync<PlayerSaveData>(data =>
            {
                data.AddOwnedCat(2, 0);
                data.AddOwnedCat(3, 1);
            }, CancellationToken.None);
            PlayerSaveData before = saveSystem.GetSnapshot<PlayerSaveData>();
            int first = before.GetFormationCatId(0);
            int second = before.GetFormationCatId(1);

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 0, false, 1,
                CancellationToken.None, new[] { first, second });

            PlayerSaveData saved = saveSystem.GetSnapshot<PlayerSaveData>();
            Assert.That(saved.StageIndex, Is.EqualTo(1));
            Assert.That(saved.GetFormationCatId(0), Is.EqualTo(first));
            Assert.That(saved.GetFormationCatId(1), Is.EqualTo(second));
            Assert.That(saved.GetOwnedCatLevel(first), Is.EqualTo(2));
            Assert.That(saved.GetOwnedCatLevel(second), Is.EqualTo(3));
        }

        [Test]
        public async Task SuccessfulDrop_AddsFiveSummonsOnlyOnce()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);
            await saveSystem.LoadAsync(CancellationToken.None);
            int participant = 0;
            await saveSystem.UpdateAsync<PlayerSaveData>(data =>
            {
                data.SetGold(0);
                participant = data.AddOwnedCat(4, 0);
                data.AddOwnedCat(4, 1);
                data.MergeFormationCats(1, 0);
            }, CancellationToken.None);

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 0, true, 1,
                CancellationToken.None, new[] { participant });

            PlayerSaveData saved = saveSystem.GetSnapshot<PlayerSaveData>();
            int nextSummonCost = _stageBalanceConfig.SummonCostFor(saved.MaxMergedCatLevel);
            Assert.That(saved.StageIndex, Is.EqualTo(2));
            Assert.That(saved.ActiveOwnedCatCount, Is.Zero);
            Assert.That(saved.MaxMergedCatLevel, Is.EqualTo(5));
            Assert.That(nextSummonCost, Is.EqualTo(30));
            Assert.That(saved.Gold, Is.EqualTo(5 + nextSummonCost * 5));

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 0, true, 1,
                CancellationToken.None, new[] { participant });
            Assert.That(saveSystem.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(5 + nextSummonCost * 5));
        }

        [Test]
        public async Task SuccessfulDrop_AddsFiveSummonsEvenWithExistingGold()
        {
            var repository = new MemoryRepository();
            var saveSystem = new SaveSystem(repository, _definition);
            await saveSystem.LoadAsync(CancellationToken.None);
            await saveSystem.UpdateAsync<PlayerSaveData>(data => data.SetGold(500), CancellationToken.None);

            await DropRewardPersistence.SaveAsync(saveSystem, _stageBalanceConfig, 5, 0, true, 1,
                CancellationToken.None);

            PlayerSaveData saved = saveSystem.GetSnapshot<PlayerSaveData>();
            Assert.That(saved.StageIndex, Is.EqualTo(2));
            Assert.That(saved.Gold, Is.EqualTo(555));
        }

        [Test]
        public async Task LocalRepository_RoundTripsSaveRecord()
        {
            string key = "save_test_" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.persistentDataPath, "saves", key + ".json");
            var repository = new LocalJsonSaveRepository();
            var expected = new SaveRecord(1, 12345, JsonUtility.ToJson(new PlayerSaveData()));
            try
            {
                await repository.InitializeAsync(CancellationToken.None);
                await repository.SaveAsync(key, expected, CancellationToken.None);
                SaveRecord loaded = await repository.LoadAsync(key, CancellationToken.None);
                Assert.That(loaded.SchemaVersion, Is.EqualTo(expected.SchemaVersion));
                Assert.That(loaded.Payload, Is.EqualTo(expected.Payload));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public async Task LocalSaveEditor_ChangesStageAndGoldWithoutLosingCatsOrInventory()
        {
            string key = "save_editor_test_" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.persistentDataPath, "saves", key + ".json");
            var repository = new LocalJsonSaveRepository();
            try
            {
                (SaveRecord missing, PlayerSaveData initial) =
                    await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                Assert.That(missing, Is.Null);
                initial.AddOwnedCat(5, 0);
                initial.SetInventory(new[] { new ItemStackSaveData { itemId = "hint", quantity = 2 } });
                SaveRecord first = await PlayerLocalSaveEditorStorage.SaveAsync(
                    repository, key, null, initial, false);

                (SaveRecord loaded, PlayerSaveData draft) =
                    await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                Assert.That(loaded.Payload, Is.EqualTo(first.Payload));
                draft.SetStageIndex(12);
                draft.SetGold(999);
                await PlayerLocalSaveEditorStorage.SaveAsync(repository, key, loaded, draft, true);

                var (_, restored) = await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                Assert.That(restored.StageIndex, Is.EqualTo(12));
                Assert.That(restored.Gold, Is.EqualTo(999));
                Assert.That(restored.GetCatLevel(0), Is.EqualTo(5));
                Assert.That(restored.GetInventory()[0].quantity, Is.EqualTo(2));
                Assert.That(File.Exists(path + ".editor-backup"), Is.True);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".editor-backup")) File.Delete(path + ".editor-backup");
            }
        }

        [Test]
        public async Task LocalSaveEditor_ResetRestoresAllDefaultsAndBacksUpPreviousSave()
        {
            string key = "save_editor_reset_" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.persistentDataPath, "saves", key + ".json");
            var repository = new LocalJsonSaveRepository();
            try
            {
                var previous = new PlayerSaveData();
                previous.SetStageIndex(12);
                previous.SetGold(999);
                previous.AddOwnedCat(2, 0);
                previous.AddOwnedCat(2, 1);
                previous.MergeFormationCats(0, 1);
                previous.SetInventory(new[] { new ItemStackSaveData { itemId = "hint", quantity = 2 } });
                await PlayerLocalSaveEditorStorage.SaveAsync(repository, key, null, previous, false);
                string originalFile = File.ReadAllText(path);

                await PlayerLocalSaveEditorStorage.ResetAsync(repository, key);

                var (saved, restored) = await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                var defaults = new PlayerSaveData();
                Assert.That(restored.StageIndex, Is.EqualTo(defaults.StageIndex));
                Assert.That(restored.Gold, Is.EqualTo(defaults.Gold));
                Assert.That(restored.ActiveOwnedCatCount, Is.Zero);
                Assert.That(restored.MaxMergedCatLevel, Is.EqualTo(defaults.MaxMergedCatLevel));
                Assert.That(restored.GetInventory(), Is.Empty);
                Assert.That(saved.SchemaVersion, Is.EqualTo(PlayerSaveData.CurrentSchemaVersion));
                Assert.That(File.Exists(path + ".editor-backup"), Is.True);
                Assert.That(File.ReadAllText(path + ".editor-backup"), Is.EqualTo(originalFile));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".editor-backup")) File.Delete(path + ".editor-backup");
            }
        }

        [Test]
        public async Task LocalSaveEditor_ResetRecoversUnreadableSave()
        {
            string key = "save_editor_broken_" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.persistentDataPath, "saves", key + ".json");
            var repository = new LocalJsonSaveRepository();
            try
            {
                await repository.InitializeAsync(CancellationToken.None);
                File.WriteAllText(path, "invalid save");

                await PlayerLocalSaveEditorStorage.ResetAsync(repository, key);

                var (_, restored) = await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                Assert.That(restored.StageIndex, Is.EqualTo(1));
                Assert.That(File.ReadAllText(path + ".editor-backup"), Is.EqualTo("invalid save"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".editor-backup")) File.Delete(path + ".editor-backup");
            }
        }

        [Test]
        public async Task LocalSaveEditor_RejectsAFileChangedAfterLoading()
        {
            string key = "save_editor_conflict_" + Guid.NewGuid().ToString("N");
            string path = Path.Combine(Application.persistentDataPath, "saves", key + ".json");
            var repository = new LocalJsonSaveRepository();
            try
            {
                SaveRecord first = await PlayerLocalSaveEditorStorage.SaveAsync(
                    repository, key, null, new PlayerSaveData(), false);
                var outsideData = new PlayerSaveData();
                outsideData.SetGold(77);
                await repository.SaveAsync(key, new SaveRecord(first.SchemaVersion,
                    first.SavedAtUnixMilliseconds + 1, JsonUtility.ToJson(outsideData)),
                    CancellationToken.None);

                var edited = new PlayerSaveData();
                edited.SetGold(500);
                bool rejected = false;
                try
                {
                    await PlayerLocalSaveEditorStorage.SaveAsync(repository, key, first, edited, true);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                Assert.That(rejected, Is.True);
                var (current, _) = await PlayerLocalSaveEditorStorage.LoadAsync(repository, key);
                Assert.That(JsonUtility.FromJson<PlayerSaveData>(current.Payload).Gold, Is.EqualTo(77));
                Assert.That(File.Exists(path + ".editor-backup"), Is.False);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".editor-backup")) File.Delete(path + ".editor-backup");
            }
        }

        [Test]
        public void LegacyBoard_MigratesIntoOwnedCatsAndFormation()
        {
            var legacy = new PlayerSaveData();
            legacy.SetCatLevel(0, 2);
            legacy.SetCatLevel(4, 5);

            Assert.That(legacy.GetFormationCatId(0), Is.EqualTo(1));
            Assert.That(legacy.GetFormationCatId(4), Is.EqualTo(2));
            Assert.That(legacy.GetOwnedCatLevel(1), Is.EqualTo(2));
            Assert.That(legacy.GetOwnedCatLevel(2), Is.EqualTo(5));

            legacy.UnassignFormationSlot(4);
            Assert.That(legacy.GetUnformedCatIds(), Is.EqualTo(new[] { 2 }));
            Assert.That(legacy.SellOwnedCat(2, 4), Is.EqualTo(4));
            Assert.That(legacy.Gold, Is.EqualTo(34));
        }

        [Test]
        public void FirestoreFields_RoundTripReserveAndFormation()
        {
            var original = new PlayerSaveData();
            int formedId = original.AddOwnedCat(3, 0);
            int reserveId = original.AddOwnedCat(5);
            original.SellOwnedCat(formedId, 0);
            original.AssignOwnedCatToFormation(reserveId, 2);
            var record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion, 12345, JsonUtility.ToJson(original));

            SaveRecord decoded = _definition.Decode(_definition.Encode(record));
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);
            Assert.That(restored.GetFormationCatId(2), Is.EqualTo(reserveId));
            Assert.That(restored.GetOwnedCatLevel(reserveId), Is.EqualTo(5));
        }

        [Test]
        public void FirestoreFields_RejectPartialOwnedCatData()
        {
            var fields = _definition.Encode(new SaveRecord(1, 12345, JsonUtility.ToJson(new PlayerSaveData())));
            fields.Remove("formation");

            Assert.Throws<InvalidDataException>(() => _definition.Decode(fields));
        }

        [Test]
        public void FormationCats_CanReturnToReserveAndBeReplacedWithoutChangingOwnership()
        {
            var data = new PlayerSaveData();
            int first = data.AddOwnedCat(2, 0);
            int second = data.AddOwnedCat(4, 1);
            int reserve = data.AddOwnedCat(6);

            data.ReturnFormationCatToReserve(1);
            data.ReplaceFormationCatWithOwnedCat(reserve, 0);

            Assert.That(data.GetFormationCatId(0), Is.EqualTo(reserve));
            Assert.That(data.GetUnformedCatIds(), Is.EquivalentTo(new[] { first, second }));
            Assert.That(data.GetOwnedCatLevel(first), Is.EqualTo(2));
            Assert.That(data.GetOwnedCatLevel(second), Is.EqualTo(4));
        }

        [Test]
        public void FitFormationToSlotCount_ReflowsOutOfRangeCatsBeforeReturningThemToReserve()
        {
            var data = new PlayerSaveData();
            int first = data.AddOwnedCat(2, 0);
            int second = data.AddOwnedCat(3, 10);
            int third = data.AddOwnedCat(4, 14);
            int fourth = data.AddOwnedCat(5, 17);

            data.FitFormationToSlotCount(3);

            Assert.That(data.GetFormationCatId(0), Is.EqualTo(first));
            Assert.That(data.GetFormationCatId(1), Is.EqualTo(second));
            Assert.That(data.GetFormationCatId(2), Is.EqualTo(third));
            for (int slot = 3; slot < PlayerSaveData.BoardSlotCount; slot++)
                Assert.That(data.GetFormationCatId(slot), Is.Zero);
            Assert.That(data.GetUnformedCatIds(), Is.EqualTo(new[] { fourth }));
        }

        [Test]
        public void FillFormationFromUnformed_FillsActiveSlotsInOwnedIdOrder()
        {
            var data = new PlayerSaveData();
            int first = data.AddOwnedCat(2, 0);
            int second = data.AddOwnedCat(3);
            int third = data.AddOwnedCat(4);

            data.FillFormationFromUnformed(3);

            Assert.That(data.GetFormationCatId(0), Is.EqualTo(first));
            Assert.That(data.GetFormationCatId(1), Is.EqualTo(second));
            Assert.That(data.GetFormationCatId(2), Is.EqualTo(third));
        }

        [Test]
        public void MergeFormationCats_TracksAndPersistsHighestMergedLevel()
        {
            var data = new PlayerSaveData();
            data.AddOwnedCat(2, 0);
            data.AddOwnedCat(2, 1);
            data.MergeFormationCats(0, 1);
            var record = new SaveRecord(PlayerSaveData.CurrentSchemaVersion, 12345, JsonUtility.ToJson(data));

            Dictionary<string, object> fields = _definition.Encode(record);
            SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);

            Assert.That(data.MaxMergedCatLevel, Is.EqualTo(3));
            Assert.That(fields["maxMergedCatLevel"], Is.EqualTo(3L));
            Assert.That(restored.MaxMergedCatLevel, Is.EqualTo(3));
        }

        private sealed class MemoryRepository : ISaveRepository
        {
            public SaveRecord Record { get; private set; }
            public MemoryRepository(SaveRecord record = null) => Record = record;
            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<SaveRecord> LoadAsync(string saveKey, CancellationToken cancellationToken)
                => Task.FromResult(Record);
            public Task SaveAsync(string saveKey, SaveRecord record, CancellationToken cancellationToken)
            {
                Record = record;
                return Task.CompletedTask;
            }
        }
    }
}
