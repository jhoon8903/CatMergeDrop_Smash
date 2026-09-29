using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Data;
using Framework.Data.Entity;
using NUnit.Framework;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageAttemptRewardTests
    {
        private StageBalanceConfig _balance;
        private PlayerSaveDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
            _definition = ScriptableObject.CreateInstance<PlayerSaveDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_balance);
            UnityEngine.Object.DestroyImmediate(_definition);
        }

        [Test]
        public async Task EnsureAttemptStarted_EmptyFirstStageFundsThreeSummonsAndCreatesFourDrops()
        {
            SaveSystem saves = await CreateLoadedSaveAsync();
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.SetGold(0);
                DropRewardPersistence.EnsureAttemptStarted(data, _balance);
            });

            PlayerSaveData data = saves.GetSnapshot<PlayerSaveData>();
            int summonCost = _balance.SummonCostFor(data.MaxMergedCatLevel);
            Assert.That(data.Gold, Is.EqualTo(summonCost * 3));
            Assert.That(data.HasStageAttempt, Is.True);
            Assert.That(data.AttemptDropBudget, Is.EqualTo(4));
            Assert.That(data.AttemptDropsRemaining, Is.EqualTo(4));
        }

        [Test]
        public async Task FailedDrops_GrantOrdinaryAndSpecialRewardsOnceAcrossFourDropBudget()
        {
            SaveSystem saves = await CreateAttemptSaveAsync();
            for (int drop = 0; drop < 4; drop++)
            {
                await ReserveAsync(saves);
                await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, false,
                    5, 1, 2, new[] { 4 - drop, 0 }, CancellationToken.None);
            }

            PlayerSaveData settled = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(settled.Gold, Is.EqualTo(30 + 20));
            Assert.That(settled.AttemptDropsRemaining, Is.Zero);
            Assert.That(BrickRewardTickets.Count(settled, BrickRewardTickets.GoldId), Is.EqualTo(4));
            Assert.That(BrickRewardTickets.Count(settled, BrickRewardTickets.ItemId), Is.EqualTo(8));
            Assert.That(settled.GetAttemptBrickHealth(), Is.EqualTo(new[] { 1, 0 }));

            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, false,
                5, 1, 2, new[] { 1, 0 }, CancellationToken.None);
            PlayerSaveData repeated = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(repeated.Gold, Is.EqualTo(settled.Gold));
            Assert.That(BrickRewardTickets.Count(repeated, BrickRewardTickets.GoldId), Is.EqualTo(4));
            Assert.That(BrickRewardTickets.Count(repeated, BrickRewardTickets.ItemId), Is.EqualTo(8));
        }

        [Test]
        public async Task RestartAfterExhaustion_RemovesAttemptRewardsAndRestoresMergedCats()
        {
            SaveSystem saves = await CreateAttemptSaveAsync(data =>
            {
                data.SetGold(70);
                data.AddOwnedCat(2, 0);
                data.AddOwnedCat(2, 1);
                data.MergeFormationCats(1, 0);
            });
            PlayerSaveData entry = saves.GetSnapshot<PlayerSaveData>();
            int entryCatId = entry.GetFormationCatId(0);

            for (int drop = 0; drop < 4; drop++)
            {
                await ReserveAsync(saves);
                await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, false,
                    3, 1, 1, new[] { 3, 0 }, CancellationToken.None);
            }
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.AddOwnedCat(8);
                data.SetGold(999);
                data.RestartStageAttempt();
            });

            PlayerSaveData restarted = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(restarted.Gold, Is.EqualTo(70));
            Assert.That(restarted.GetFormationCatId(0), Is.EqualTo(entryCatId));
            Assert.That(restarted.GetOwnedCatLevel(entryCatId), Is.EqualTo(3));
            Assert.That(restarted.MaxMergedCatLevel, Is.EqualTo(3));
            Assert.That(restarted.ActiveOwnedCatCount, Is.EqualTo(1));
            Assert.That(BrickRewardTickets.Count(restarted, BrickRewardTickets.GoldId), Is.Zero);
            Assert.That(BrickRewardTickets.Count(restarted, BrickRewardTickets.ItemId), Is.Zero);
            Assert.That(restarted.AttemptDropsRemaining, Is.EqualTo(4));
        }

        [Test]
        public async Task ClearedDrop_RemovesBoardReserveAndRewardCatsBeforeNewCheckpoint()
        {
            SaveSystem saves = await CreateAttemptSaveAsync(data =>
            {
                data.AddOwnedCat(3, 0);
                data.AddOwnedCat(4);
            });
            PlayerSaveData entry = saves.GetSnapshot<PlayerSaveData>();
            int entryGold = entry.Gold;
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.AddOwnedCat(3, 1);
                data.MergeFormationCats(1, 0);
                BrickRewardTickets.Bank(data, 0, 2);
                BrickRewardTickets.OpenAll(data, _balance, _ => 1);
                BrickRewardTickets.Bank(data, 0, 3);
            });
            entry = saves.GetSnapshot<PlayerSaveData>();

            await ReserveAsync(saves);
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, true,
                7, 1, 1, new[] { 0, 0 }, CancellationToken.None);

            PlayerSaveData won = saves.GetSnapshot<PlayerSaveData>();
            int expectedGold = entryGold + 7 + _balance.ClearBonusFor(entry.MaxMergedCatLevel);
            Assert.That(won.StageIndex, Is.EqualTo(2));
            Assert.That(won.ActiveOwnedCatCount, Is.Zero);
            Assert.That(won.GetUnformedCatIds(), Is.Empty);
            for (int slot = 0; slot < PlayerSaveData.BoardSlotCount; slot++)
                Assert.That(won.GetCatLevel(slot), Is.Zero);
            Assert.That(BrickRewardTickets.Count(won, BrickRewardTickets.ItemId), Is.Zero);
            Assert.That(won.Gold, Is.EqualTo(expectedGold));
            Assert.That(won.HasStageAttempt, Is.True);
            Assert.That(won.AttemptDropsRemaining, Is.EqualTo(4));

            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.SetGold(1);
                data.AddOwnedCat(9, 0);
                data.RestartStageAttempt();
            });
            PlayerSaveData restarted = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(restarted.StageIndex, Is.EqualTo(2));
            Assert.That(restarted.Gold, Is.EqualTo(expectedGold));
            Assert.That(restarted.ActiveOwnedCatCount, Is.Zero);
            Assert.That(restarted.GetFormationCatId(0), Is.Zero);
            PlayerSaveData reloaded = JsonUtility.FromJson<PlayerSaveData>(JsonUtility.ToJson(restarted));
            reloaded.Validate();
            reloaded.RestartStageAttempt();
            Assert.That(reloaded.ActiveOwnedCatCount, Is.Zero);
            await saves.UpdateAsync<PlayerSaveData>(data => data.AddOwnedCat(2, 0));
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, true,
                7, 1, 1, new[] { 0, 0 }, CancellationToken.None);
            Assert.That(saves.GetSnapshot<PlayerSaveData>().ActiveOwnedCatCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FailedSettlementWrite_LeavesReservationAndRetryPaysOnlyOnce()
        {
            var repository = new MemoryRepository();
            var saves = new SaveSystem(repository, _definition);
            await saves.LoadAsync(CancellationToken.None);
            await saves.UpdateAsync<PlayerSaveData>(data => DropRewardPersistence.EnsureAttemptStarted(data, _balance));
            await ReserveAsync(saves);
            repository.FailNextSave = true;

            Assert.ThrowsAsync<IOException>(async () => await DropRewardPersistence.SaveAttemptDropAsync(
                saves, _balance, 1, false, 9, 2, 1, new[] { 1, 0 }, CancellationToken.None));

            PlayerSaveData afterFailure = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(afterFailure.AttemptDropInFlight, Is.True);
            Assert.That(afterFailure.AttemptDropsRemaining, Is.EqualTo(3));
            Assert.That(afterFailure.Gold, Is.EqualTo(30));
            Assert.That(BrickRewardTickets.Count(afterFailure, BrickRewardTickets.GoldId), Is.Zero);

            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, false,
                9, 2, 1, new[] { 1, 0 }, CancellationToken.None);
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, false,
                9, 2, 1, new[] { 1, 0 }, CancellationToken.None);

            PlayerSaveData retried = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(retried.Gold, Is.EqualTo(39));
            Assert.That(retried.AttemptDropInFlight, Is.False);
            Assert.That(BrickRewardTickets.Count(retried, BrickRewardTickets.GoldId), Is.EqualTo(2));
            Assert.That(BrickRewardTickets.Count(retried, BrickRewardTickets.ItemId), Is.EqualTo(1));
        }

        [Test]
        public async Task DepletedEconomyWithFourDrops_CanRestartAndRecoversBaseline()
        {
            SaveSystem saves = await CreateAttemptSaveAsync(data =>
            {
                data.SetGold(0);
                data.AddOwnedCat(3, 0);
            });
            PlayerSaveData entry = saves.GetSnapshot<PlayerSaveData>();
            int entryCatId = entry.GetFormationCatId(0);

            await saves.UpdateAsync<PlayerSaveData>(data => data.SellOwnedCat(entryCatId, 0));
            PlayerSaveData depleted = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(depleted.AttemptDropsRemaining, Is.EqualTo(4));
            Assert.That(depleted.ActiveOwnedCatCount, Is.Zero);
            Assert.That(depleted.Gold, Is.LessThan(_balance.SummonCostFor(depleted.MaxMergedCatLevel)));
            Assert.That(DropRewardPersistence.CanRestartAttempt(depleted, _balance), Is.True);

            await saves.UpdateAsync<PlayerSaveData>(data => data.RestartStageAttempt());
            PlayerSaveData restarted = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(restarted.Gold, Is.Zero);
            Assert.That(restarted.GetFormationCatId(0), Is.EqualTo(entryCatId));
            Assert.That(restarted.GetOwnedCatLevel(entryCatId), Is.EqualTo(3));
            Assert.That(restarted.AttemptDropsRemaining, Is.EqualTo(4));
        }

        [Test]
        public async Task AuthoredFinalStageClear_ExpiresCatTicketsAndLeavesGoldOpenable()
        {
            StageBalanceConfig authoredBalance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            Assert.That(authoredBalance, Is.Not.Null);
            Assert.That(authoredBalance.LastDefinedStage, Is.EqualTo(1000));

            SaveSystem saves = await CreateLoadedSaveAsync();
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.SetStageIndex(authoredBalance.LastDefinedStage);
                data.ClearStageCats(); data.AddOwnedCat(2, 0); data.SetGold(100);
                DropRewardPersistence.EnsureAttemptStarted(data, authoredBalance);
                data.ReserveAttemptDrop();
            });
            await DropRewardPersistence.SaveAttemptDropAsync(saves, authoredBalance,
                authoredBalance.LastDefinedStage, true, 0, 2, 1, new[] { 0 }, CancellationToken.None, new[] { 0 });

            PlayerSaveData completed = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(completed.StageIndex, Is.EqualTo(1001));
            Assert.That(completed.Gold, Is.EqualTo(100 + authoredBalance.ClearBonusFor(2) + 3));
            Assert.That(completed.HasStageAttempt, Is.False);
            Assert.That(BrickRewardTickets.Count(completed, BrickRewardTickets.GoldId), Is.EqualTo(2));
            Assert.That(BrickRewardTickets.Count(completed, BrickRewardTickets.ItemId), Is.Zero);

            BrickRewardTickets.OpenResult opened = BrickRewardTickets.OpenAll(completed, authoredBalance, _ => 1);
            Assert.That(opened.GoldOpened, Is.EqualTo(2));
            Assert.That(opened.ItemOpened, Is.Zero);
            Assert.That(completed.ActiveOwnedCatCount, Is.Zero);
            Assert.That(BrickRewardTickets.Count(completed, BrickRewardTickets.GoldId), Is.Zero);
            Assert.That(BrickRewardTickets.Count(completed, BrickRewardTickets.ItemId), Is.Zero);
        }

        [Test]
        public async Task EmptyLaterStage_FundsSummonsBeforeCapturingCheckpoint()
        {
            SaveSystem saves = await CreateLoadedSaveAsync();
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.SetStageIndex(43);
                data.SetGold(0);
                DropRewardPersistence.EnsureAttemptStarted(data, _balance);
            });
            PlayerSaveData entry = saves.GetSnapshot<PlayerSaveData>();
            Assert.That(entry.Gold, Is.EqualTo(3 * _balance.SummonCostFor(entry.MaxMergedCatLevel)));
            entry.SetGold(0);
            entry.RestartStageAttempt();
            Assert.That(entry.Gold, Is.GreaterThan(0));
            Assert.That(entry.ActiveOwnedCatCount, Is.Zero);
        }

        [Test]
        public void SurvivorGold_CountsDistinctLivingSlotsOnlyWithConfiguredLevelPrices()
        {
            var data = new PlayerSaveData();
            data.ClearStageCats();
            data.AddOwnedCat(1, 0);
            data.AddOwnedCat(3, 1);
            data.AddOwnedCat(8, 2); // Deployed but spent: absent from survivor slots.
            data.AddOwnedCat(8); // Legacy unformed record is never eligible.
            Assert.That(DropRewardPersistence.SurvivorGoldFor(data, new[] { 0, 1, 1, 3 },
                new[] { 2, 3, 7, 10 }), Is.EqualTo(9));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task SurvivorGold_ClearOnlyIsAtomicAndIncludedInNextCheckpoint(bool cleared)
        {
            SaveSystem saves = await CreateAttemptSaveAsync(data =>
            {
                data.ClearStageCats();
                data.AddOwnedCat(1, 0);
                data.AddOwnedCat(3, 1);
                data.SetGold(0);
            });
            var before = saves.GetSnapshot<PlayerSaveData>();
            await ReserveAsync(saves);
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, cleared,
                0, 0, 0, new[] { 0 }, CancellationToken.None, new[] { 0, 1, 1 });
            var settled = saves.GetSnapshot<PlayerSaveData>();
            int expected = cleared ? System.Math.Max(_balance.ClearBonusFor(before.MaxMergedCatLevel),
                3 * _balance.SummonCostFor(before.MaxMergedCatLevel)) + 6 : 0;
            Assert.That(settled.Gold, Is.EqualTo(expected));
            Assert.That(settled.ActiveOwnedCatCount, Is.EqualTo(cleared ? 0 : 2));
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, cleared,
                0, 0, 0, new[] { 0 }, CancellationToken.None, new[] { 0, 1 });
            Assert.That(saves.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(expected));
            if (cleared)
            {
                await saves.UpdateAsync<PlayerSaveData>(data => { data.SetGold(0); data.RestartStageAttempt(); });
                Assert.That(saves.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(expected));
                Assert.That(saves.GetSnapshot<PlayerSaveData>().ActiveOwnedCatCount, Is.Zero);
            }
        }

        [Test]
        public async Task SurvivorGold_SaveFailureRetriesWithoutDoublePayout()
        {
            var repository = new MemoryRepository();
            var saves = new SaveSystem(repository, _definition);
            await saves.LoadAsync(CancellationToken.None);
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                data.ClearStageCats(); data.AddOwnedCat(3, 0); data.SetGold(100);
                DropRewardPersistence.EnsureAttemptStarted(data, _balance);
                data.ReserveAttemptDrop();
            });
            repository.FailNextSave = true;
            Assert.ThrowsAsync<IOException>(async () => await DropRewardPersistence.SaveAttemptDropAsync(
                saves, _balance, 1, true, 0, 0, 0, new[] { 0 }, CancellationToken.None, new[] { 0 }));
            Assert.That(saves.GetSnapshot<PlayerSaveData>().StageIndex, Is.EqualTo(1));
            Assert.That(saves.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(100));
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, true,
                0, 0, 0, new[] { 0 }, CancellationToken.None, new[] { 0 });
            int settled = saves.GetSnapshot<PlayerSaveData>().Gold;
            Assert.That(settled, Is.EqualTo(100 + _balance.ClearBonusFor(3) + 4));
            await DropRewardPersistence.SaveAttemptDropAsync(saves, _balance, 1, true,
                0, 0, 0, new[] { 0 }, CancellationToken.None, new[] { 0 });
            Assert.That(saves.GetSnapshot<PlayerSaveData>().Gold, Is.EqualTo(settled));
        }

        private async Task<SaveSystem> CreateAttemptSaveAsync(Action<PlayerSaveData> prepare = null)
        {
            SaveSystem saves = await CreateLoadedSaveAsync();
            await saves.UpdateAsync<PlayerSaveData>(data =>
            {
                prepare?.Invoke(data);
                DropRewardPersistence.EnsureAttemptStarted(data, _balance);
            });
            return saves;
        }

        private async Task<SaveSystem> CreateLoadedSaveAsync()
        {
            var saves = new SaveSystem(new MemoryRepository(), _definition);
            await saves.LoadAsync(CancellationToken.None);
            return saves;
        }

        private static Task ReserveAsync(SaveSystem saves)
            => saves.UpdateAsync<PlayerSaveData>(data => data.ReserveAttemptDrop(), CancellationToken.None);

        private sealed class MemoryRepository : ISaveRepository
        {
            public SaveRecord Record { get; private set; }
            public bool FailNextSave { get; set; }

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<SaveRecord> LoadAsync(string saveKey, CancellationToken cancellationToken) => Task.FromResult(Record);
            public Task SaveAsync(string saveKey, SaveRecord record, CancellationToken cancellationToken)
            {
                if (FailNextSave)
                {
                    FailNextSave = false;
                    throw new IOException("Injected save failure.");
                }
                Record = record;
                return Task.CompletedTask;
            }
        }
    }
}
