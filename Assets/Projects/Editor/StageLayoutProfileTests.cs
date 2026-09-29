using System;
using NUnit.Framework;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class StageLayoutProfileTests
    {
        private const string BalancePath = "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset";

        [Test]
        public void ProductionBalance_UsesDefinitionsForEveryStage()
        {
            StageBalanceConfig balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(BalancePath);
            Assert.That(balance, Is.Not.Null);
            int previousColumns = 4;
            for (int stage = 1; stage <= 1000; stage++)
            {
                StageDefinition definition = balance.GetStageDefinition(stage);
                Assert.That(definition, Is.Not.Null, $"Stage {stage} is missing.");
                Assert.That(balance.LayoutForStage(stage).BoardColumns, Is.EqualTo(definition.BoardColumns));
                Assert.That(Math.Abs(definition.BoardColumns - previousColumns), Is.LessThanOrEqualTo(1));
                previousColumns = definition.BoardColumns;
            }
            Assert.Throws<InvalidOperationException>(() => balance.LayoutForStage(1001));
        }

        [Test]
        public void EmptyBalance_UsesOneNeutralLayoutForIsolatedTests()
        {
            StageBalanceConfig balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
            try
            {
                Assert.That(balance.LayoutForStage(1).BoardColumns, Is.EqualTo(4));
                Assert.That(balance.LayoutForStage(27).BoardColumns, Is.EqualTo(4));
                Assert.That(balance.HasBrick(27, 0, 0), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(balance); }
        }
    }
}
