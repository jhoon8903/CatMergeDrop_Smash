using NUnit.Framework;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class BrickRewardTicketsTests
    {
        private StageBalanceConfig _balance;

        [SetUp]
        public void SetUp() => _balance = ScriptableObject.CreateInstance<StageBalanceConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_balance);

        [Test]
        public void MultipleOpenings_GrantThreeSummonCostsAndUniformRangeCatsOnce()
        {
            var save = new PlayerSaveData();
            save.AddOwnedCat(5, 0);
            save.AddOwnedCat(5, 1);
            save.MergeFormationCats(1, 0); // Maximum merged level 6, summon maximum 4.
            BrickRewardTickets.Bank(save, 2, 2);
            int nextRoll = 0;

            BrickRewardTickets.OpenResult opened = BrickRewardTickets.OpenAll(save, _balance,
                max => ++nextRoll == 1 ? 1 : max);

            Assert.That(opened.GoldOpened, Is.EqualTo(2));
            Assert.That(opened.ItemOpened, Is.EqualTo(2));
            Assert.That(opened.GoldGranted,
                Is.EqualTo(6 * _balance.SummonCostFor(save.MaxMergedCatLevel)));
            Assert.That(opened.CatLevels, Is.EqualTo(new[] { 1, 4 }));
            Assert.That(save.ActiveOwnedCatCount, Is.EqualTo(3));
            Assert.That(BrickRewardTickets.Count(save, BrickRewardTickets.GoldId), Is.Zero);
            Assert.That(BrickRewardTickets.Count(save, BrickRewardTickets.ItemId), Is.Zero);
            Assert.That(BrickRewardTickets.OpenAll(save, _balance).ItemOpened, Is.Zero);
            save.Validate();
        }

        [Test]
        public void OpenAll_PlacesCatsOnlyInVacantActiveBoardSlots()
        {
            var save = new PlayerSaveData();
            save.AddOwnedCat(1, 0);
            save.AddOwnedCat(2, 1);
            save.AddOwnedCat(3, 2);
            BrickRewardTickets.Bank(save, 0, 2);

            BrickRewardTickets.OpenResult opened = BrickRewardTickets.OpenAll(save, _balance, 4, _ => 1);

            Assert.That(opened.ItemOpened, Is.EqualTo(1));
            Assert.That(opened.CatLevels, Is.EqualTo(new[] { 1 }));
            Assert.That(save.GetCatLevel(3), Is.EqualTo(1));
            Assert.That(save.GetUnformedCatIds(), Is.Empty);
            Assert.That(BrickRewardTickets.Count(save, BrickRewardTickets.ItemId), Is.EqualTo(1));
            save.Validate();
        }

        [Test]
        public void OpenAll_WhenActiveBoardIsFull_LeavesCatTicketsUnopened()
        {
            var save = new PlayerSaveData();
            for (int slot = 0; slot < 4; slot++) save.AddOwnedCat(slot + 1, slot);
            BrickRewardTickets.Bank(save, 1, 2);

            BrickRewardTickets.OpenResult opened = BrickRewardTickets.OpenAll(save, _balance, 4, _ => 1);

            Assert.That(opened.GoldOpened, Is.EqualTo(1));
            Assert.That(opened.ItemOpened, Is.Zero);
            Assert.That(opened.CatLevels, Is.Empty);
            Assert.That(save.ActiveOwnedCatCount, Is.EqualTo(4));
            Assert.That(save.GetUnformedCatIds(), Is.Empty);
            Assert.That(BrickRewardTickets.Count(save, BrickRewardTickets.GoldId), Is.Zero);
            Assert.That(BrickRewardTickets.Count(save, BrickRewardTickets.ItemId), Is.EqualTo(2));
            save.Validate();
        }
    }
}
