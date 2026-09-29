using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Projects.Scripts.Data;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageAttemptSaveTests
    {
        private PlayerSaveDefinition _definition;

        [SetUp]
        public void SetUp() => _definition = ScriptableObject.CreateInstance<PlayerSaveDefinition>();

        [TearDown]
        public void TearDown() => UnityEngine.Object.DestroyImmediate(_definition);

        [Test]
        public void RestartStageAttempt_RestoresCompleteEntryStateAndBudget()
        {
            var data = CreatePopulatedSave();
            int entryGold = data.Gold;
            int entryStage = data.StageIndex;
            int entryCat = data.GetFormationCatId(0);
            data.BeginStageAttempt(4, 12);
            data.ReserveAttemptDrop();
            data.CommitAttemptDrop(new[] { 4, 0, 2 });

            data.SetGold(1);
            data.SetStageIndex(9);
            data.SetReserveCapacity(24);
            data.SetCatLevel(0, 9);
            data.SetInventory(new[] { new ItemStackSaveData { itemId = "changed", quantity = 1 } });

            data.RestartStageAttempt();

            Assert.That(data.Gold, Is.EqualTo(entryGold));
            Assert.That(data.StageIndex, Is.EqualTo(entryStage));
            Assert.That(data.ReserveCapacity, Is.EqualTo(PlayerSaveData.DefaultReserveCapacity));
            Assert.That(data.GetFormationCatId(0), Is.EqualTo(entryCat));
            Assert.That(data.GetOwnedCatLevel(entryCat), Is.EqualTo(4));
            Assert.That(data.MaxMergedCatLevel, Is.EqualTo(4));
            Assert.That(data.GetInventory()[0].itemId, Is.EqualTo("hint"));
            Assert.That(data.AttemptDropBudget, Is.EqualTo(4));
            Assert.That(data.AttemptCatalogRevision, Is.EqualTo(12));
            Assert.That(data.AttemptDropsRemaining, Is.EqualTo(4));
            Assert.That(data.AttemptDropInFlight, Is.False);
            Assert.That(data.GetAttemptBrickHealth(), Is.Empty);
        }

        [Test]
        public void LocalJsonRoundTrip_PreservesCommittedDamageAndAttemptState()
        {
            var original = CreatePopulatedSave();
            original.BeginStageAttempt(4, 23);
            original.ReserveAttemptDrop();
            original.CommitAttemptDrop(new[] { 6, 0, 1 });

            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(JsonUtility.ToJson(original));
            restored.Validate();

            Assert.That(restored.HasStageAttempt, Is.True);
            Assert.That(restored.AttemptDropBudget, Is.EqualTo(4));
            Assert.That(restored.AttemptCatalogRevision, Is.EqualTo(23));
            Assert.That(restored.AttemptDropsRemaining, Is.EqualTo(3));
            Assert.That(restored.AttemptDropInFlight, Is.False);
            Assert.That(restored.GetAttemptBrickHealth(), Is.EqualTo(new[] { 6, 0, 1 }));
        }

        [Test]
        public void FirestoreRoundTrip_PreservesAttemptCheckpoint()
        {
            var original = CreatePopulatedSave();
            original.BeginStageAttempt(4);
            original.ReserveAttemptDrop();
            original.CommitAttemptDrop(new[] { 6, 0, 1 });
            var record = new Framework.Data.Entity.SaveRecord(PlayerSaveData.CurrentSchemaVersion, 1234, JsonUtility.ToJson(original));

            Dictionary<string, object> fields = _definition.Encode(record);
            Framework.Data.Entity.SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);

            Assert.That(fields["stageAttempt"], Is.TypeOf<string>());
            Assert.That(restored.AttemptDropsRemaining, Is.EqualTo(3));
            Assert.That(restored.GetAttemptBrickHealth(), Is.EqualTo(new[] { 6, 0, 1 }));
            restored.RestartStageAttempt();
            Assert.That(restored.Gold, Is.EqualTo(77));
            Assert.That(restored.GetOwnedCatLevel(restored.GetFormationCatId(0)), Is.EqualTo(4));
            Assert.That(restored.MaxMergedCatLevel, Is.EqualTo(4));
        }

        [Test]
        public void DecodeOldFirestoreSaveWithoutAttempt_RemainsCompatible()
        {
            var original = CreatePopulatedSave();
            var record = new Framework.Data.Entity.SaveRecord(PlayerSaveData.CurrentSchemaVersion, 1234, JsonUtility.ToJson(original));
            Dictionary<string, object> fields = _definition.Encode(record);

            Framework.Data.Entity.SaveRecord decoded = _definition.Decode(fields);
            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(decoded.Payload);

            Assert.That(restored.HasStageAttempt, Is.False);
        }

        [Test]
        public void LegacyLocalJsonWithoutAttempt_RoundTripsAsInactive()
        {
            var legacy = new PlayerSaveData();
            legacy.SetGold(42);

            PlayerSaveData restored = JsonUtility.FromJson<PlayerSaveData>(JsonUtility.ToJson(legacy));
            restored.Validate();

            Assert.That(restored.HasStageAttempt, Is.False);
            Assert.That(restored.AttemptDropBudget, Is.Zero);
            Assert.That(restored.AttemptDropsRemaining, Is.Zero);
            Assert.That(restored.AttemptCatalogRevision, Is.Zero);
        }

        [Test]
        public void RecoverInterruptedAttemptDrop_RefundsOnlyTheReservedDrop()
        {
            var data = new PlayerSaveData();
            data.BeginStageAttempt(4);
            data.ReserveAttemptDrop();

            Assert.That(data.AttemptDropsRemaining, Is.EqualTo(3));
            Assert.That(data.RecoverInterruptedAttemptDrop(), Is.True);
            Assert.That(data.AttemptDropsRemaining, Is.EqualTo(4));
            Assert.That(data.AttemptDropInFlight, Is.False);
            Assert.That(data.RecoverInterruptedAttemptDrop(), Is.False);
        }

        [Test]
        public void RestartStageAttempt_UsesSuppliedCatalogRevisionForNewCheckpoint()
        {
            var data = new PlayerSaveData();
            data.BeginStageAttempt(4, 2);

            data.RestartStageAttempt(7);

            Assert.That(data.AttemptCatalogRevision, Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.RestartStageAttempt(-1));
        }

        [Test]
        public void LegacyAttemptPayloadWithoutCatalogRevision_UsesRevisionZero()
        {
            var original = CreatePopulatedSave();
            original.BeginStageAttempt(4, 23);
            string legacyPayload = original.GetStageAttemptPayload().Replace("\"catalogRevision\":23,", string.Empty);
            var restored = new PlayerSaveData();

            restored.SetStageAttemptPayloadFromSave(legacyPayload);

            Assert.That(restored.AttemptCatalogRevision, Is.Zero);
        }

        [Test]
        public void AttemptState_RejectsInvalidBudgetsAndBrickHealth()
        {
            var data = new PlayerSaveData();
            Assert.Throws<ArgumentOutOfRangeException>(() => data.BeginStageAttempt(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.BeginStageAttempt(PlayerSaveData.MaxStageAttemptDropBudget + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.BeginStageAttempt(4, -1));

            data.BeginStageAttempt(4);
            data.ReserveAttemptDrop();
            Assert.Throws<ArgumentOutOfRangeException>(() => data.CommitAttemptDrop(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.CommitAttemptDrop(Array.Empty<int>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.CommitAttemptDrop(new int[PlayerSaveData.MaxAttemptBrickHealthCount + 1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => data.CommitAttemptDrop(new[] { -1 }));
        }

        [Test]
        public void FirestoreDecode_RejectsInvalidAttemptPayload()
        {
            var fields = _definition.Encode(new Framework.Data.Entity.SaveRecord(
                PlayerSaveData.CurrentSchemaVersion, 1234, JsonUtility.ToJson(new PlayerSaveData())));
            fields["stageAttempt"] = "{\"dropBudget\":4,\"dropsRemaining\":5}";

            Assert.Throws<InvalidDataException>(() => _definition.Decode(fields));
        }

        private static PlayerSaveData CreatePopulatedSave()
        {
            var data = new PlayerSaveData();
            data.SetGold(77);
            data.SetStageIndex(3);
            int first = data.AddOwnedCat(3, 0);
            data.AddOwnedCat(3, 1);
            data.MergeFormationCats(1, 0);
            data.AddOwnedCat(4);
            data.SetInventory(new[] { new ItemStackSaveData { itemId = "hint", quantity = 2 } });
            Assert.That(first, Is.EqualTo(data.GetFormationCatId(0)));
            return data;
        }
    }
}
