using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor.StageGeneration
{
    /// <summary>Preserves a meaningful normal-brick obstacle after reward bricks become one-hit.</summary>
    public static class StageSpecialBrickHealthMigration
    {
        [MenuItem("Tools/Cat Merge Drop/Balance One-Hit Special Brick Stages")]
        public static void Apply()
        {
            var balance = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(
                "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset");
            var bricks = AssetDatabase.LoadAssetAtPath<BrickConfig>(
                "Assets/Projects/Assets/Configuration/BrickConfig.asset");
            var cats = AssetDatabase.LoadAssetAtPath<CatConfig>(
                "Assets/Projects/Assets/Configuration/CatConfig.asset");
            if (balance == null || bricks == null || cats == null)
                throw new System.InvalidOperationException("Stage, Brick and Cat configs are required.");

            int changed = 0;
            for (int stage = 1; stage <= 1000; stage++)
            {
                StageDefinition definition = balance.GetStageDefinition(stage);
                if (definition == null) throw new System.InvalidOperationException($"Stage {stage} is missing.");
                int columns = definition.BoardColumns;
                int rows = definition.BrickRows;
                int lane = StageBulkGenerator.RewardLaneForStage(stage, columns);
                int damage = cats.ImpactDamageFor(StageBulkGenerator.SoloRouteCatLevel(stage));
                int cost = 0;
                var cells = new StageBrickCell[columns * rows];
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    StageBrickCell cell = definition.GetCell(row, column);
                    cells[index] = cell;
                    if (column == lane && cell.Present)
                        cost += Mathf.Max(0, (bricks.HealthFor(cell.Level, cell.Kind) - 1) / damage);
                }
                if (cost != 0) continue;
                if (rows < 3) throw new System.InvalidOperationException($"Stage {stage} needs a third brick row.");
                int obstacle = 2 * columns + lane;
                cells[obstacle].Kind = BrickKind.Normal;
                cells[obstacle].Level = bricks.LevelForRouteCost(damage, 1);
                definition.Configure(stage, columns, rows, cells);
                EditorUtility.SetDirty(definition);
                changed++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"One-hit special Brick balance applied: {changed} stage reward lanes gained a normal Brick obstacle.");
        }
    }
}
