using System;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    public enum CatSpritePose
    {
        Idle,
        Jump,
        Drop
    }

    [Serializable]
    public struct CatLevelCombatStats
    {
        [Min(1)] public int impactDamage;
        [Min(0)] public int bounceCount;
    }

    [CreateAssetMenu(fileName = "CatConfig", menuName = "Cat Merge Drop/Cat Config")]
    public sealed class CatConfig : ScriptableObject
    {
        private const float DefaultBounceDuration = .60f;

        [Header("Drop Motion")]
        [SerializeField, Min(0.01f)]
        [Tooltip("Maximum fall speed in normalized drop progress per second. 0.4 is 7.2 world units/s with the current drop travel.")]
        private float fallSpeed = .4f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Fall acceleration in normalized drop progress per second squared.")]
        private float fallAcceleration = .65f;

        [Header("Rebound")]
        [SerializeField, Min(0.01f)]
        [Tooltip("World-space rebound height when Bounce Strength is 1.")]
        private float baseBounceHeight = .45f;

        [SerializeField, Min(0.01f)]
        [Tooltip("1 = standard rebound height; 2 = twice the rebound height.")]
        private float bounceStrength = 1.4f;

        [SerializeField, Min(0.01f)]
        [Tooltip("Minimum up-and-down rebound time in seconds. Stronger rebounds can take longer so their landing speed stays below Fall Speed.")]
        private float bounceDuration = DefaultBounceDuration;

        [Header("Level Combat Stats")]
        [Tooltip("One entry per cat level. Missing higher levels use the legacy damage and bounce curves.")]
        [SerializeField] private CatLevelCombatStats[] combatStatsByLevel;

        [Header("Impact Pose")]
        [SerializeField, Min(0.01f)] private float impactCompressionDuration = .07f;
        [SerializeField, Min(0.01f)] private float impactStretchDuration = .15f;
        [SerializeField, Min(0.01f)] private float impactRecoveryDuration = .20f;
        [SerializeField] private Vector2 impactCompressionScale = new Vector2(1.12f, .88f);
        [SerializeField] private Vector2 impactStretchScale = new Vector2(.96f, 1.05f);

        [Header("Sprites")]
        [SerializeField]
        [Tooltip("Level-ordered cat sprites. Levels above the authored range use the final sprite.")]
        private Sprite[] levelSprites;

        [SerializeField]
        [Tooltip("Level-ordered jump sprites. Missing entries fall back to the idle sprite.")]
        private Sprite[] jumpSprites;

        [SerializeField]
        [Tooltip("Level-ordered drop sprites. Missing entries fall back to the idle sprite.")]
        private Sprite[] dropSprites;

        public float FallSpeed => Mathf.Max(0.01f, fallSpeed);
        public float FallAcceleration => Mathf.Max(0.01f, fallAcceleration);
        public float BaseBounceHeight => Mathf.Max(0.01f, baseBounceHeight);
        public float BounceStrength => Mathf.Max(0.01f, bounceStrength);
        public float BounceHeight => BaseBounceHeight * BounceStrength;
        public float BounceDuration => Mathf.Max(0.01f, bounceDuration);
        public float EffectiveBounceDuration => Mathf.Max(BounceDuration,
            4f * BounceHeight / (WorldPlayfieldLayout.DropTravel * FallSpeed));
        public int ImpactDamageFor(int level)
        {
            int safeLevel = Mathf.Max(1, level);
            return combatStatsByLevel != null && safeLevel <= combatStatsByLevel.Length
                ? Mathf.Max(1, combatStatsByLevel[safeLevel - 1].impactDamage)
                : safeLevel;
        }

        public int BounceCountFor(int level)
        {
            int safeLevel = Mathf.Max(1, level);
            return combatStatsByLevel != null && safeLevel <= combatStatsByLevel.Length
                ? Mathf.Max(0, combatStatsByLevel[safeLevel - 1].bounceCount)
                : safeLevel > int.MaxValue / 2 ? int.MaxValue : safeLevel * 2;
        }
        public float ImpactCompressionDuration => Mathf.Max(0.01f, impactCompressionDuration);
        public float ImpactStretchDuration => Mathf.Max(0.01f, impactStretchDuration);
        public float ImpactRecoveryDuration => Mathf.Max(0.01f, impactRecoveryDuration);
        public Vector2 ImpactCompressionScale => PositiveScale(impactCompressionScale);
        public Vector2 ImpactStretchScale => PositiveScale(impactStretchScale);

        public Sprite SpriteFor(int level) => SpriteFor(level, CatSpritePose.Idle);

        public Sprite SpriteFor(int level, CatSpritePose pose)
        {
            Sprite idle = SpriteAtLevel(levelSprites, level);
            if (pose == CatSpritePose.Idle) return idle;

            Sprite[] poseSprites = pose == CatSpritePose.Jump ? jumpSprites : dropSprites;
            return SpriteAtLevel(poseSprites, level) ?? idle;
        }

        private static Sprite SpriteAtLevel(Sprite[] sprites, int level)
        {
            if (sprites == null || sprites.Length == 0) return null;
            int index = Mathf.Clamp(level - 1, 0, sprites.Length - 1);
            return sprites[index];
        }

        private static Vector2 PositiveScale(Vector2 scale) => new Vector2(
            Mathf.Max(0.01f, scale.x), Mathf.Max(0.01f, scale.y));

        private void OnValidate()
        {
            fallSpeed = Mathf.Max(0.01f, fallSpeed);
            fallAcceleration = Mathf.Max(0.01f, fallAcceleration);
            baseBounceHeight = Mathf.Max(0.01f, baseBounceHeight);
            bounceStrength = Mathf.Max(0.01f, bounceStrength);
            bounceDuration = Mathf.Max(0.01f, bounceDuration);
            impactCompressionDuration = Mathf.Max(0.01f, impactCompressionDuration);
            impactStretchDuration = Mathf.Max(0.01f, impactStretchDuration);
            impactRecoveryDuration = Mathf.Max(0.01f, impactRecoveryDuration);
            impactCompressionScale = PositiveScale(impactCompressionScale);
            impactStretchScale = PositiveScale(impactStretchScale);
            if (combatStatsByLevel == null) return;
            for (int i = 0; i < combatStatsByLevel.Length; i++)
            {
                CatLevelCombatStats stats = combatStatsByLevel[i];
                stats.impactDamage = Mathf.Max(1, stats.impactDamage);
                stats.bounceCount = Mathf.Max(0, stats.bounceCount);
                combatStatsByLevel[i] = stats;
            }
        }
    }
}
