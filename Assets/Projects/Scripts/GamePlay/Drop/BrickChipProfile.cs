using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>The same four predetermined corner shapes drive sprite clipping and collision.</summary>
    public static class BrickChipProfile
    {
        public static int StageForHealth(int health, int maxHealth)
        {
            if (health <= 0 || maxHealth <= 0) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(3f * (maxHealth - health) / maxHealth), 0, 3);
        }

        public static float DepthWorld(int chipStage)
        {
            switch (chipStage)
            {
                case 1: return .15f;
                case 2: return .30f;
                case 3: return .45f;
                default: return 0f;
            }
        }

        public static float ContactProgress(in Brick brick, float catX, int brickColumns)
        {
            float center = WorldPlayfieldLayout.ColumnX(brick.Column, brickColumns);
            float relativeX = Mathf.Clamp01((catX - center) /
                WorldPlayfieldLayout.BoardColumnPitch + .5f);
            float chippedFraction = brick.ChipSide > 0 ? relativeX : 1f - relativeX;
            return WorldPlayfieldLayout.TileContactProgress(brick.Row) +
                DepthWorld(brick.ChipStage) * chippedFraction / WorldPlayfieldLayout.DropTravel;
        }

        public static float DeflectionSpeed(int chipStage) =>
            chipStage <= 0 ? 0f : .85f + chipStage * .35f;
    }
}
