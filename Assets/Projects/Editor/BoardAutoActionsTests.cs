using NUnit.Framework;
using Projects.Scripts.Data;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class BoardAutoActionsTests
    {
        private StageBalanceConfig _balance;

        [SetUp]
        public void SetUp() => _balance = ScriptableObject.CreateInstance<StageBalanceConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_balance);

        [Test]
        public void AutoMerge_OnlyMergesCatsAlreadyOnTheBoard()
        {
            var data = new PlayerSaveData();
            int survivor = data.AddOwnedCat(1, 0);
            data.AddOwnedCat(1, 1);
            data.AddOwnedCat(2);

            int merged = BoardAutoActions.MergeAll(data, 3);

            Assert.That(merged, Is.EqualTo(1));
            Assert.That(data.ActiveOwnedCatCount, Is.EqualTo(2));
            Assert.That(data.GetFormationCatId(0), Is.EqualTo(survivor));
            Assert.That(data.GetCatLevel(0), Is.EqualTo(2));
            Assert.That(data.MaxMergedCatLevel, Is.EqualTo(2));
            Assert.That(data.GetUnformedCatIds(), Has.Length.EqualTo(1));
            data.Validate();
        }

        [Test]
        public void AutoSummon_OnlyCreatesCatsInVacantBoardSlots()
        {
            var data = new PlayerSaveData();
            data.SetGold(25);
            int existing = data.AddOwnedCat(2);

            AutoSummonResult result = BoardAutoActions.SummonIntoEmpty(data, 4,
                _balance, _ => 1);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.GoldSpent, Is.EqualTo(20));
            Assert.That(data.Gold, Is.EqualTo(5));
            Assert.That(data.GetFormationCatId(0), Is.Not.EqualTo(existing));
            Assert.That(data.GetCatLevel(0), Is.EqualTo(1));
            Assert.That(data.GetCatLevel(1), Is.EqualTo(1));
            Assert.That(data.GetCatLevel(2), Is.Zero);
            Assert.That(data.GetCatLevel(3), Is.Zero);
            Assert.That(data.GetUnformedCatIds(), Is.EqualTo(new[] { existing }));
            data.Validate();
        }

        [Test]
        public void AutoMerge_OnlyUsesActiveCells()
        {
            int[] levels = new int[PlayerSaveData.BoardSlotCount];
            levels[0] = 2;
            levels[4] = 2;

            Assert.That(BoardAutoActions.HasMergePair(levels, 4), Is.False);
            Assert.That(BoardAutoActions.HasMergePair(levels, 5), Is.True);
        }
    }
}
