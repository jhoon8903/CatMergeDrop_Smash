using NUnit.Framework;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageBrickPatternTests
    {
        private StageBalanceConfig _stageBalanceConfig;

        [SetUp]
        public void SetUp()
        {
            _stageBalanceConfig = ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stageBalanceConfig);

        [Test]
        public void FallbackStage_FillsEveryBrickCell()
        {
            StageLayout layout = _stageBalanceConfig.LayoutForStage(2);
            for (int row = 0; row < layout.BrickRows; row++)
            for (int column = 0; column < layout.BrickColumns; column++)
                Assert.That(_stageBalanceConfig.HasBrick(2, row, column), Is.True);
        }

        [Test]
        public void Preview_ExposesRewardKinds()
        {
            Assert.That(StageBrickPattern.GetKind(1, 1), Is.EqualTo(BrickKind.Gold));
            Assert.That(StageBrickPattern.GetKind(2, 2), Is.EqualTo(BrickKind.Item));
        }

        [Test]
        public void EditedBrickConfig_ChangesBrickGold()
        {
            BrickConfig edited = ScriptableObject.CreateInstance<BrickConfig>();
            try
            {
                var serialized = new SerializedObject(edited);
                serialized.FindProperty("levelConfigs").GetArrayElementAtIndex(1)
                    .FindPropertyRelative("goldReward").intValue = 27;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(edited.GoldForLevel(1), Is.EqualTo(10));
                Assert.That(edited.GoldForLevel(2), Is.EqualTo(27));
            }
            finally { Object.DestroyImmediate(edited); }
        }
    }
}
