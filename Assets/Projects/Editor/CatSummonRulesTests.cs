using NUnit.Framework;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class CatSummonRulesTests
    {
        private StageBalanceConfig _stageBalanceConfig;

        [SetUp]
        public void SetUp()
        {
            _stageBalanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stageBalanceConfig);

        [TestCase(1, 10)]
        [TestCase(4, 20)]
        [TestCase(5, 30)]
        [TestCase(10, 80)]
        public void SummonCost_RisesWithHighestUnlockedSpawnLevel(
            int highestMergedLevel, int expectedCost)
        {
            Assert.That(_stageBalanceConfig.SummonCostFor(highestMergedLevel),
                Is.EqualTo(expectedCost));
        }

        [TestCase(1, 1, 1)]
        [TestCase(5, 1, 3)]
        [TestCase(6, 1, 4)]
        [TestCase(10, 5, 8)]
        [TestCase(30, 25, 28)]
        public void SummonRange_FollowsHighestMergedLevelEvenAfterBoardClears(
            int boardMaximum, int expectedMinimum, int expectedMaximum)
        {
            _stageBalanceConfig.GetSummonLevelRange(boardMaximum, out int minimum, out int maximum);
            Assert.That(minimum, Is.EqualTo(expectedMinimum));
            Assert.That(maximum, Is.EqualTo(expectedMaximum));
        }

        [Test]
        public void EditedStageBalance_ChangesSummonRangeCostAndClearReward()
        {
            StageBalanceConfig edited = Object.Instantiate(_stageBalanceConfig);
            try
            {
                var serialized = new SerializedObject(edited);
                serialized.FindProperty("minimumSummonGap").intValue = 6;
                serialized.FindProperty("maximumSummonGap").intValue = 3;
                serialized.FindProperty("summonGoldPerMaximumSpawnLevel").intValue = 12;
                serialized.FindProperty("clearSummonCount").intValue = 7;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                edited.GetSummonLevelRange(10, out int minimum, out int maximum);
                Assert.That((minimum, maximum), Is.EqualTo((4, 7)));
                Assert.That(edited.SummonCostFor(10), Is.EqualTo(84));
                Assert.That(edited.ClearBonusFor(10), Is.EqualTo(588));
            }
            finally { Object.DestroyImmediate(edited); }
        }

        [Test]
        public void SpawnWeights_FavorLowerLevelsAndNormalizeToActiveRange()
        {
            StageBalanceConfig edited = Object.Instantiate(_stageBalanceConfig);
            try
            {
                var serialized = new SerializedObject(edited);
                serialized.FindProperty("minimumSummonGap").intValue = 8;
                serialized.FindProperty("maximumSummonGap").intValue = 4;
                SerializedProperty weights = serialized.FindProperty("spawnWeightsFromLowestLevel");
                int[] expected = { 40, 25, 18, 11, 6 };
                weights.arraySize = expected.Length;
                for (int i = 0; i < expected.Length; i++)
                    weights.GetArrayElementAtIndex(i).intValue = expected[i];
                serialized.ApplyModifiedPropertiesWithoutUndo();

                edited.GetSummonLevelRange(10, out int minimum, out int maximum);
                Assert.That((minimum, maximum), Is.EqualTo((2, 6)));
                int[] counts = new int[expected.Length];
                for (int ticket = 0; ticket < 100; ticket++)
                    counts[edited.SummonLevelForTicket(10, ticket) - minimum]++;
                Assert.That(counts, Is.EqualTo(expected));

                edited.GetSummonLevelRange(6, out minimum, out maximum);
                Assert.That((minimum, maximum), Is.EqualTo((1, 2)));
                Assert.That(edited.SummonLevelForTicket(6, 39), Is.EqualTo(1));
                Assert.That(edited.SummonLevelForTicket(6, 40), Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(edited); }
        }

        [Test]
        public void HighestBoardLevel_IgnoresEmptyCells()
        {
            Assert.That(CatSummonRules.HighestBoardLevel(new[] { 0, 2, 0, 9, 3 }),
                Is.EqualTo(9));
        }

    }
}
