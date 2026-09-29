using System;
using System.Collections.Generic;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Configs
{
    [Serializable]
    public struct BrickLevelConfig
    {
        [Min(1)] public int maxHealth;
        [Min(0)] public int goldReward;
    }

    [CreateAssetMenu(fileName = "BrickConfig", menuName = "Cat Merge Drop/Brick Config")]
    public sealed class BrickConfig : ScriptableObject
    {
        [Header("Level Stats")]
        [SerializeField] private BrickLevelConfig[] levelConfigs =
        {
            new BrickLevelConfig { maxHealth = 1, goldReward = 10 }, new BrickLevelConfig { maxHealth = 2, goldReward = 15 },
            new BrickLevelConfig { maxHealth = 4, goldReward = 20 }, new BrickLevelConfig { maxHealth = 6, goldReward = 25 },
            new BrickLevelConfig { maxHealth = 8, goldReward = 30 }, new BrickLevelConfig { maxHealth = 10, goldReward = 35 },
            new BrickLevelConfig { maxHealth = 12, goldReward = 40 }, new BrickLevelConfig { maxHealth = 14, goldReward = 45 },
            new BrickLevelConfig { maxHealth = 16, goldReward = 50 }, new BrickLevelConfig { maxHealth = 18, goldReward = 55 },
            new BrickLevelConfig { maxHealth = 20, goldReward = 60 }, new BrickLevelConfig { maxHealth = 24, goldReward = 65 },
            new BrickLevelConfig { maxHealth = 28, goldReward = 70 }, new BrickLevelConfig { maxHealth = 32, goldReward = 75 },
            new BrickLevelConfig { maxHealth = 36, goldReward = 80 }, new BrickLevelConfig { maxHealth = 42, goldReward = 85 },
            new BrickLevelConfig { maxHealth = 48, goldReward = 90 }, new BrickLevelConfig { maxHealth = 54, goldReward = 95 },
            new BrickLevelConfig { maxHealth = 56, goldReward = 100 }, new BrickLevelConfig { maxHealth = 64, goldReward = 105 },
            new BrickLevelConfig { maxHealth = 72, goldReward = 110 }, new BrickLevelConfig { maxHealth = 80, goldReward = 115 },
            new BrickLevelConfig { maxHealth = 90, goldReward = 120 }, new BrickLevelConfig { maxHealth = 96, goldReward = 125 },
            new BrickLevelConfig { maxHealth = 108, goldReward = 130 }, new BrickLevelConfig { maxHealth = 128, goldReward = 135 },
            new BrickLevelConfig { maxHealth = 144, goldReward = 140 }, new BrickLevelConfig { maxHealth = 182, goldReward = 145 },
            new BrickLevelConfig { maxHealth = 224, goldReward = 150 }, new BrickLevelConfig { maxHealth = 270, goldReward = 155 }
        };
        [SerializeField] private Sprite[] levelSprites = new Sprite[30];
        [Header("Special Bricks")]
        [SerializeField] private Sprite goldBrickSprite;
        [SerializeField] private Sprite itemBrickSprite;
        [SerializeField] private Sprite[] crackSprites = new Sprite[3];
        [SerializeField] private Sprite[] debrisSprites = new Sprite[3];
        [SerializeField, Range(4, 16)] private int minimumDebris = 8;
        [SerializeField, Range(4, 16)] private int maximumDebris = 12;
        [Header("Impact VFX")]
        [SerializeField, Range(0.08f, 0.3f)] private float impactDuration = .16f;
        [SerializeField, Range(0f, 1f)] private float impactFlashAlpha = .7f;
        [SerializeField] private Color impactFlashColor = Color.white;
        [SerializeField, Range(0.1f, 1f)] private float impactFlashDurationMultiplier = .72f;
        [SerializeField, Range(0.1f, 0.9f)] private float impactSquashPhase = .42f;
        [SerializeField, Range(0.5f, 1.5f)] private float impactSquashX = 1.15f;
        [SerializeField, Range(0.5f, 1.5f)] private float impactSquashY = .82f;
        [SerializeField, Range(0.5f, 1.5f)] private float impactReboundX = .95f;
        [SerializeField, Range(0.5f, 1.5f)] private float impactReboundY = 1.08f;

        [Header("Debris VFX")]
        [SerializeField, Range(0f, 1f)] private float debrisSpawnSpreadX = .42f;
        [SerializeField, Range(0f, 1f)] private float debrisSpawnSpreadY = .25f;
        [SerializeField, Min(0.01f)] private float debrisSpeed = 2.1f;
        [SerializeField, Min(0.01f)] private float finalBurstDebrisSpeed = 3.8f;
        [SerializeField, Min(0.01f)] private float debrisLifetime = .52f;
        [SerializeField, Min(0.01f)] private float finalBurstDebrisLifetime = .85f;
        [SerializeField, Range(0.01f, 1f)] private float debrisMinimumSize = .1f;
        [SerializeField, Range(0.01f, 1f)] private float debrisMaximumSize = .16f;
        [SerializeField, Range(0f, 2f)] private float debrisVerticalSpeedMinimum = .35f;
        [SerializeField, Range(0f, 2f)] private float debrisVerticalSpeedMaximum = 1.35f;
        [SerializeField, Min(0f)] private float debrisGravity = 5f;
        [SerializeField, Min(0f)] private float debrisSpinSpeed = 540f;

        [Header("Final Hit VFX")]
        [SerializeField, Range(0f, 1f)] private float finalBurstFlashAlpha = .7f;
        [SerializeField] private Color finalBurstFlashColor = new Color(1f, .96f, .7f, 1f);
        [SerializeField, Min(0.01f)] private float finalBurstFlashDuration = .23f;
        [SerializeField, Range(0.5f, 2f)] private float finalBurstFlashStartScale = 1.16f;
        [SerializeField, Range(1f, 3f)] private float finalBurstFlashEndScale = 1.42f;
        [SerializeField, Min(0.01f)] private float shockwaveDuration = .32f;
        [SerializeField, Range(0f, 1f)] private float shockwaveAlpha = .65f;
        [SerializeField] private Color shockwaveColor = new Color(1f, .92f, .55f, 1f);
        [SerializeField, Min(0.001f)] private float shockwaveStartRadius = .12f;
        [SerializeField, Min(0.001f)] private float shockwaveEndRadius = 1.05f;
        [SerializeField, Min(0.001f)] private float shockwaveStartWidth = .055f;
        [SerializeField, Min(0.001f)] private float shockwaveEndWidth = .01f;

        public const int DebrisGridSize = 8;
        public const int DebrisPieceCount = DebrisGridSize * DebrisGridSize;
        [NonSerialized] private Dictionary<Sprite, Sprite[]> _bakedDebris;

        public int LevelCount => levelConfigs?.Length ?? 0;

        public int HealthForLevel(int level)
        {
            if (levelConfigs == null || level < 1 || level > levelConfigs.Length)
                throw new ArgumentOutOfRangeException(nameof(level), $"Brick level {level} has no health config.");
            int health = levelConfigs[level - 1].maxHealth;
            if (health < 1)
                throw new InvalidOperationException($"Brick level {level} must have positive health.");
            return health;
        }

        public int HealthFor(int level, BrickKind kind)
        {
            // Special bricks are one-hit rewards regardless of their authored level.
            int normalHealth = HealthForLevel(level);
            return kind == BrickKind.Gold || kind == BrickKind.Item
                ? 1 : normalHealth;
        }

        public int GoldForLevel(int level)
        {
            if (levelConfigs == null || level < 1 || level > levelConfigs.Length)
                throw new ArgumentOutOfRangeException(nameof(level), $"Brick level {level} has no gold config.");
            return Mathf.Max(0, levelConfigs[level - 1].goldReward);
        }

        public int LevelForRouteCost(int damage, int desiredCost)
            => LevelForRouteCost(damage, desiredCost, LevelCount);

        public int LevelForRouteCost(int damage, int desiredCost, int maximumLevel)
        {
            int safeDamage = Mathf.Max(1, damage);
            int targetCost = Mathf.Max(0, desiredCost);
            long targetHealth = (long)safeDamage * (targetCost + 1);
            int bestLevel = 0;
            long bestScore = long.MaxValue;
            for (int level = 1; level <= Mathf.Min(LevelCount, maximumLevel); level++)
            {
                int health = HealthForLevel(level);
                int cost = (health - 1) / safeDamage;
                long score = (long)Math.Abs(cost - targetCost) * 1_000_000L
                    + Math.Abs((long)health - targetHealth);
                if (score >= bestScore) continue;
                bestScore = score;
                bestLevel = level;
            }
            if (bestLevel == 0) throw new InvalidOperationException("Brick level health table is empty.");
            return bestLevel;
        }

        public int LevelClosestToHealth(int targetHealth)
        {
            int bestLevel = 0;
            long bestDistance = long.MaxValue;
            for (int level = 1; level <= LevelCount; level++)
            {
                long distance = Math.Abs((long)HealthForLevel(level) - Mathf.Max(1, targetHealth));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestLevel = level;
            }
            if (bestLevel == 0) throw new InvalidOperationException("Brick level health table is empty.");
            return bestLevel;
        }

        public int MinimumDebris => Mathf.Clamp(minimumDebris, 4, 16);
        public int MaximumDebris => Mathf.Clamp(maximumDebris, MinimumDebris, 16);
        public float ImpactDuration => Mathf.Clamp(impactDuration, .08f, .3f);
        public float ImpactFlashAlpha => Mathf.Clamp01(impactFlashAlpha);
        public Color ImpactFlashColor => impactFlashColor;
        public float ImpactFlashDurationMultiplier => Mathf.Clamp(impactFlashDurationMultiplier, .1f, 1f);
        public float ImpactSquashPhase => Mathf.Clamp(impactSquashPhase, .1f, .9f);
        public Vector2 ImpactSquashScale => new Vector2(Mathf.Clamp(impactSquashX, .5f, 1.5f), Mathf.Clamp(impactSquashY, .5f, 1.5f));
        public Vector2 ImpactReboundScale => new Vector2(Mathf.Clamp(impactReboundX, .5f, 1.5f), Mathf.Clamp(impactReboundY, .5f, 1.5f));
        public Vector2 DebrisSpawnSpread => new Vector2(Mathf.Clamp(debrisSpawnSpreadX, 0f, 1f), Mathf.Clamp(debrisSpawnSpreadY, 0f, 1f));
        public float DebrisSpeed => Mathf.Max(.01f, debrisSpeed);
        public float FinalBurstDebrisSpeed => Mathf.Max(.01f, finalBurstDebrisSpeed);
        public float DebrisLifetime => Mathf.Max(.01f, debrisLifetime);
        public float FinalBurstDebrisLifetime => Mathf.Max(.01f, finalBurstDebrisLifetime);
        public Vector2 DebrisSizeRange => OrderedRange(debrisMinimumSize, debrisMaximumSize);
        public Vector2 DebrisVerticalSpeedRange => OrderedRange(debrisVerticalSpeedMinimum, debrisVerticalSpeedMaximum, 0f);
        public float DebrisGravity => Mathf.Max(0f, debrisGravity);
        public float DebrisSpinSpeed => Mathf.Max(0f, debrisSpinSpeed);
        public int FinalBurstDebris => DebrisPieceCount;
        public float FinalBurstFlashAlpha => Mathf.Clamp01(finalBurstFlashAlpha);
        public Color FinalBurstFlashColor => finalBurstFlashColor;
        public float FinalBurstFlashDuration => Mathf.Max(.01f, finalBurstFlashDuration);
        public float FinalBurstFlashStartScale => Mathf.Clamp(finalBurstFlashStartScale, .5f, 2f);
        public float FinalBurstFlashEndScale => Mathf.Clamp(finalBurstFlashEndScale, 1f, 3f);
        public float ShockwaveDuration => Mathf.Max(.01f, shockwaveDuration);
        public float ShockwaveAlpha => Mathf.Clamp01(shockwaveAlpha);
        public Color ShockwaveColor => shockwaveColor;
        public float ShockwaveStartRadius => Mathf.Max(.001f, shockwaveStartRadius);
        public float ShockwaveEndRadius => Mathf.Max(ShockwaveStartRadius, shockwaveEndRadius);
        public float ShockwaveStartWidth => Mathf.Max(.001f, shockwaveStartWidth);
        public float ShockwaveEndWidth => Mathf.Max(.001f, shockwaveEndWidth);

        private static Vector2 OrderedRange(float minimum, float maximum, float lowerBound = .01f)
        {
            minimum = Mathf.Max(lowerBound, minimum);
            return new Vector2(minimum, Mathf.Max(minimum, maximum));
        }

        public Sprite SpriteFor(int level)
        {
            int index = level - 1;
            return levelSprites != null && index >= 0 && index < levelSprites.Length
                ? levelSprites[index]
                : null;
        }

        public Sprite SpriteFor(int level, BrickKind kind)
        {
            return kind switch
            {
                BrickKind.Gold when goldBrickSprite != null => goldBrickSprite,
                BrickKind.Item when itemBrickSprite != null => itemBrickSprite,
                _ => SpriteFor(level)
            };
        }

        public Sprite CrackSpriteFor(int stage)
        {
            int index = stage - 1;
            return crackSprites != null && index >= 0 && index < crackSprites.Length
                ? crackSprites[index]
                : null;
        }

        public Sprite DebrisSpriteFor(Sprite brickSprite, int index)
        {
            int pieceIndex = ((index % DebrisPieceCount) + DebrisPieceCount) % DebrisPieceCount;
            if (brickSprite == null || brickSprite.texture == null)
                return debrisSprites != null && debrisSprites.Length > 0
                    ? debrisSprites[pieceIndex % debrisSprites.Length] : null;
            _bakedDebris ??= new Dictionary<Sprite, Sprite[]>();
            if (!_bakedDebris.TryGetValue(brickSprite, out Sprite[] pieces))
            {
                Rect source = brickSprite.textureRect;
                float width = source.width / DebrisGridSize;
                float height = source.height / DebrisGridSize;
                pieces = new Sprite[DebrisPieceCount];
                for (int i = 0; i < pieces.Length; i++)
                {
                    Rect crop = new Rect(source.x + (i % DebrisGridSize) * width,
                        source.y + (i / DebrisGridSize) * height, width, height);
                    pieces[i] = Sprite.Create(brickSprite.texture, crop, new Vector2(.5f, .5f),
                        brickSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                    pieces[i].name = $"{brickSprite.name}_debris_{i + 1:00}";
                    pieces[i].hideFlags = HideFlags.DontSave;
                }
                _bakedDebris.Add(brickSprite, pieces);
            }
            return pieces[index % pieces.Length];
        }

        private void OnDisable()
        {
            if (_bakedDebris == null) return;
            foreach (Sprite[] pieces in _bakedDebris.Values)
            foreach (Sprite piece in pieces)
            {
                if (piece == null) continue;
                if (Application.isPlaying) Destroy(piece);
                else DestroyImmediate(piece);
            }
            _bakedDebris.Clear();
        }
    }
}
