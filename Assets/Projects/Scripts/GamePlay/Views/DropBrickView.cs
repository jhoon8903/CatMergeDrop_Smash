using System.Collections.Generic;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Views;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Drop.Ecs.Enums;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Stable authored coordinates for a tile view entity.</summary>
    public struct BrickViewData : IComponentData
    {
        public int Row;
        public int Column;
    }

    /// <summary>Scene-authored tile view backed by SpriteRenderer and world TextMesh.</summary>
    [DisallowMultipleComponent]
    public sealed class DropBrickView : ViewAuthoring<Drop.Ecs.Components.Brick>
    {
        [SerializeField] private SpriteRenderer block;
        [SerializeField] private int row;
        [SerializeField] private int column;
        private Sprite _fallbackSprite;
        private Vector3 _fallbackScale;
        private Vector3 _blockBaseScale;
        private SpriteRenderer _crack;
        private SpriteRenderer _impactFlash;
        private BrickConfig _brickConfig;
        private readonly List<DebrisParticle> _debris = new List<DebrisParticle>();
        private readonly List<GoldPopup> _goldPopups = new List<GoldPopup>();
        private readonly List<Shockwave> _shockwaves = new List<Shockwave>();
        private readonly List<BurstFlash> _burstFlashes = new List<BurstFlash>();
        private float _impactElapsed = -1f;
        private float _impactDuration;
        private float _flashElapsed = -1f;
        private float _flashDuration;
        private float _flashAlpha;

        private struct DebrisParticle
        {
            public SpriteRenderer Renderer;
            public Vector2 Velocity;
            public float Life;
            public float Duration;
            public float AngularVelocity;
        }

        private struct GoldPopup
        {
            public TextMesh Text;
            public float Life;
            public float Duration;
        }

        private struct Shockwave
        {
            public LineRenderer Renderer;
            public float Life;
            public float Duration;
            public float StartRadius;
            public float EndRadius;
        }

        private struct BurstFlash
        {
            public SpriteRenderer Renderer;
            public Vector3 StartScale;
            public float Life;
            public float Duration;
            public float Alpha;
        }

        public int Row => row;
        public int Column => column;
        public override bool IsConfigured => block != null;

        private void Awake()
        {
            if (block != null)
            {
                _fallbackSprite = block.sprite;
                _fallbackScale = block.transform.localScale;
                _blockBaseScale = _fallbackScale;
                GameObject crackObject = new GameObject("Cracks");
                crackObject.transform.SetParent(block.transform, false);
                _crack = crackObject.AddComponent<SpriteRenderer>();
                _crack.sortingLayerID = block.sortingLayerID;
                _crack.sortingOrder = block.sortingOrder + 1;
                _crack.enabled = false;

                GameObject flashObject = new GameObject("ImpactFlash");
                flashObject.transform.SetParent(block.transform, false);
                _impactFlash = flashObject.AddComponent<SpriteRenderer>();
                _impactFlash.sortingLayerID = block.sortingLayerID;
                _impactFlash.sortingOrder = block.sortingOrder + 3;
                _impactFlash.enabled = false;
            }
        }

        public void SetStagePosition(int activeRow, int activeColumn, int brickColumns)
        {
            Vector3 position = transform.position;
            position.x = WorldPlayfieldLayout.ColumnX(activeColumn, brickColumns);
            position.y = WorldPlayfieldLayout.DropY(WorldPlayfieldLayout.TileProgress(activeRow));
            transform.position = position;
        }

        private void Update()
        {
            float delta = Time.deltaTime;
            UpdateImpactScale(delta);
            UpdateImpactFlash(delta);
            for (int i = _debris.Count - 1; i >= 0; i--)
            {
                DebrisParticle particle = _debris[i];
                if (particle.Renderer == null)
                {
                    _debris.RemoveAt(i);
                    continue;
                }
                particle.Life -= delta;
                if (particle.Life <= 0f)
                {
                    Destroy(particle.Renderer.gameObject);
                    _debris.RemoveAt(i);
                    continue;
                }
                particle.Velocity += Vector2.down * (_brickConfig.DebrisGravity * delta);
                particle.Renderer.transform.position += (Vector3)(particle.Velocity * delta);
                particle.Renderer.transform.Rotate(0f, 0f, particle.AngularVelocity * delta);
                Color color = particle.Renderer.color;
                color.a = Mathf.Clamp01(particle.Life / particle.Duration);
                particle.Renderer.color = color;
                _debris[i] = particle;
            }

            for (int i = _goldPopups.Count - 1; i >= 0; i--)
            {
                GoldPopup popup = _goldPopups[i];
                if (popup.Text == null) { _goldPopups.RemoveAt(i); continue; }
                popup.Life -= delta;
                if (popup.Life <= 0f)
                {
                    Destroy(popup.Text.gameObject);
                    _goldPopups.RemoveAt(i);
                    continue;
                }
                popup.Text.transform.position += Vector3.up * (.65f * delta);
                Color color = popup.Text.color;
                color.a = Mathf.Clamp01(popup.Life / popup.Duration);
                popup.Text.color = color;
                _goldPopups[i] = popup;
            }

            for (int i = _shockwaves.Count - 1; i >= 0; i--)
            {
                Shockwave shockwave = _shockwaves[i];
                if (shockwave.Renderer == null)
                {
                    _shockwaves.RemoveAt(i);
                    continue;
                }

                shockwave.Life -= delta;
                if (shockwave.Life <= 0f)
                {
                    Destroy(shockwave.Renderer.material);
                    Destroy(shockwave.Renderer.gameObject);
                    _shockwaves.RemoveAt(i);
                    continue;
                }

                float progress = 1f - shockwave.Life / shockwave.Duration;
                float radius = Mathf.Lerp(shockwave.StartRadius, shockwave.EndRadius, progress);
                SetShockwavePoints(shockwave.Renderer, radius);
                Color color = shockwave.Renderer.startColor;
                color.a = Mathf.Lerp(_brickConfig.ShockwaveAlpha, 0f, progress);
                shockwave.Renderer.startColor = color;
                shockwave.Renderer.endColor = color;
                shockwave.Renderer.widthMultiplier = Mathf.Lerp(_brickConfig.ShockwaveStartWidth, _brickConfig.ShockwaveEndWidth, progress);
                _shockwaves[i] = shockwave;
            }

            for (int i = _burstFlashes.Count - 1; i >= 0; i--)
            {
                BurstFlash flash = _burstFlashes[i];
                if (flash.Renderer == null)
                {
                    _burstFlashes.RemoveAt(i);
                    continue;
                }

                flash.Life -= delta;
                if (flash.Life <= 0f)
                {
                    Destroy(flash.Renderer.gameObject);
                    _burstFlashes.RemoveAt(i);
                    continue;
                }

                float progress = 1f - flash.Life / flash.Duration;
                flash.Renderer.transform.localScale = Vector3.Lerp(flash.StartScale, flash.StartScale * _brickConfig.FinalBurstFlashEndScale, progress);
                Color color = flash.Renderer.color;
                color.a = Mathf.Lerp(flash.Alpha, 0f, progress);
                flash.Renderer.color = color;
                _burstFlashes[i] = flash;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _shockwaves.Count; i++)
            {
                if (_shockwaves[i].Renderer != null)
                    Destroy(_shockwaves[i].Renderer.material);
            }
        }

        public void SetLevelSprite(Sprite sprite)
        {
            if (block == null) return;
            _fallbackSprite ??= block.sprite;
            Sprite selected = sprite != null ? sprite : _fallbackSprite;
            if (block.sprite == selected) return;
            block.sprite = selected;
            if (_impactFlash != null) _impactFlash.sprite = selected;
            if (sprite == null) _blockBaseScale = _fallbackScale;
            else
            {
                _blockBaseScale = new Vector3(
                    WorldPlayfieldLayout.TileWidth / sprite.bounds.size.x,
                    WorldPlayfieldLayout.TileHeight / sprite.bounds.size.y, 1f);
            }
            ApplyBlockScale(Vector3.one);
        }

        public void PlayImpact(int remainingHealth, int maxHealth, BrickConfig config,
            int goldReward = 0, BrickKind kind = BrickKind.Normal)
        {
            if (block == null || config == null) return;
            _brickConfig = config;
            SetCrack(remainingHealth, maxHealth);
            bool finalHit = remainingHealth <= 0;
            if (!finalHit) PlayHitFeedback(config);
            if (finalHit)
            {
                SpawnFinalFlash();
                SpawnDebris(config.FinalBurstDebris, true);
                SpawnShockwave();
                if (kind == BrickKind.Gold)
                    SpawnRewardPopup("GOLD BOX +1", new Color(1f, .85f, .3f, 1f));
                else if (kind == BrickKind.Item)
                    SpawnRewardPopup("CAT BOX +1", new Color(.65f, .92f, 1f, 1f));
                else if (goldReward > 0)
                    SpawnRewardPopup($"+{goldReward}G", new Color(1f, .85f, .3f, 1f));
            }
            else SpawnDebris(Random.Range(config.MinimumDebris, config.MaximumDebris + 1), false);
        }

        private void PlayHitFeedback(BrickConfig config)
        {
            _impactDuration = config.ImpactDuration;
            _impactElapsed = 0f;
            _flashDuration = _impactDuration * config.ImpactFlashDurationMultiplier;
            _flashElapsed = 0f;
            _flashAlpha = config.ImpactFlashAlpha;
            if (_impactFlash == null) return;
            _impactFlash.sprite = block.sprite;
            Color flashColor = config.ImpactFlashColor;
            flashColor.a = _flashAlpha;
            _impactFlash.color = flashColor;
            _impactFlash.enabled = _impactFlash.sprite != null;
        }

        private void UpdateImpactScale(float delta)
        {
            if (_impactElapsed < 0f || block == null) return;
            _impactElapsed += delta;
            float progress = Mathf.Clamp01(_impactElapsed / _impactDuration);
            Vector3 multiplier;
            if (progress < _brickConfig.ImpactSquashPhase)
                multiplier = Vector3.Lerp((Vector3)_brickConfig.ImpactSquashScale, (Vector3)_brickConfig.ImpactReboundScale, progress / _brickConfig.ImpactSquashPhase);
            else
                multiplier = Vector3.Lerp((Vector3)_brickConfig.ImpactReboundScale, Vector3.one, (progress - _brickConfig.ImpactSquashPhase) / (1f - _brickConfig.ImpactSquashPhase));
            ApplyBlockScale(multiplier);
            if (progress >= 1f) _impactElapsed = -1f;
        }

        private void UpdateImpactFlash(float delta)
        {
            if (_flashElapsed < 0f || _impactFlash == null) return;
            _flashElapsed += delta;
            float progress = Mathf.Clamp01(_flashElapsed / _flashDuration);
            Color color = _impactFlash.color;
            color.a = Mathf.Lerp(_flashAlpha, 0f, progress);
            _impactFlash.color = color;
            if (progress >= 1f)
            {
                _impactFlash.enabled = false;
                _flashElapsed = -1f;
            }
        }

        private void ApplyBlockScale(Vector3 multiplier)
        {
            block.transform.localScale = Vector3.Scale(_blockBaseScale, multiplier);
        }

        private void SpawnDebris(int count, bool finalBurst)
        {
            float speed = finalBurst ? _brickConfig.FinalBurstDebrisSpeed : _brickConfig.DebrisSpeed;
            float life = finalBurst ? _brickConfig.FinalBurstDebrisLifetime : _brickConfig.DebrisLifetime;
            Vector2 sizeRange = _brickConfig.DebrisSizeRange;
            Vector2 spread = _brickConfig.DebrisSpawnSpread;
            Vector2 verticalSpeedRange = _brickConfig.DebrisVerticalSpeedRange;
            for (int i = 0; i < count; i++)
            {
                int pieceIndex = finalBurst ? i : Random.Range(0, BrickConfig.DebrisPieceCount);
                Sprite sprite = _brickConfig.DebrisSpriteFor(block.sprite, pieceIndex);
                if (sprite == null) sprite = block.sprite;
                if (sprite == null) continue;
                GameObject fragment = new GameObject(finalBurst ? "BrickBurstFragment" : "BrickDebris");
                fragment.transform.SetParent(transform, false);
                Vector2 offset = finalBurst
                    ? new Vector2(
                        ((pieceIndex % BrickConfig.DebrisGridSize + .5f) / BrickConfig.DebrisGridSize - .5f) * WorldPlayfieldLayout.TileWidth,
                        ((pieceIndex / BrickConfig.DebrisGridSize + .5f) / BrickConfig.DebrisGridSize - .5f) * WorldPlayfieldLayout.TileHeight)
                    : new Vector2(Random.Range(-spread.x, spread.x), Random.Range(-spread.y, spread.y));
                fragment.transform.position = block.transform.position + new Vector3(offset.x, offset.y, -.03f);
                if (finalBurst)
                {
                    float variation = Random.Range(.9f, 1.08f);
                    fragment.transform.localScale = new Vector3(
                        WorldPlayfieldLayout.TileWidth / (BrickConfig.DebrisGridSize * sprite.bounds.size.x),
                        WorldPlayfieldLayout.TileHeight / (BrickConfig.DebrisGridSize * sprite.bounds.size.y), 1f) * variation;
                }
                else
                {
                    float fragmentSize = Random.Range(sizeRange.x, sizeRange.y);
                    fragment.transform.localScale = Vector3.one * (fragmentSize /
                        Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));
                }
                SpriteRenderer renderer = fragment.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingLayerID = block.sortingLayerID;
                renderer.sortingOrder = block.sortingOrder + (finalBurst ? 4 : 2);
                renderer.color = block.color;
                _debris.Add(new DebrisParticle
                {
                    Renderer = renderer,
                    Velocity = new Vector2(Random.Range(-speed, speed), Random.Range(speed * verticalSpeedRange.x, speed * verticalSpeedRange.y)),
                    Life = life,
                    Duration = life,
                    AngularVelocity = Random.Range(-_brickConfig.DebrisSpinSpeed, _brickConfig.DebrisSpinSpeed)
                });
            }
        }

        private void SpawnRewardPopup(string label, Color color)
        {
            GameObject popupObject = new GameObject("BrickRewardPopup");
            popupObject.transform.SetParent(transform, true);
            popupObject.transform.position = block.transform.position + new Vector3(0f, .22f, -.1f);
            TextMesh reward = popupObject.AddComponent<TextMesh>();
            reward.text = label;
            reward.anchor = TextAnchor.MiddleCenter;
            reward.alignment = TextAlignment.Center;
            reward.fontSize = 48;
            reward.characterSize = .025f;
            reward.color = color;
            MeshRenderer mesh = popupObject.GetComponent<MeshRenderer>();
            mesh.sortingLayerID = block.sortingLayerID;
            mesh.sortingOrder = block.sortingOrder + 8;
            _goldPopups.Add(new GoldPopup { Text = reward, Life = .8f, Duration = .8f });
        }

        private void SpawnFinalFlash()
        {
            if (block.sprite == null || block.transform.parent == null) return;
            GameObject flashObject = new GameObject("BrickBurstFlash");
            flashObject.transform.SetParent(block.transform.parent, false);
            flashObject.transform.localPosition = block.transform.localPosition + Vector3.back * .05f;
            flashObject.transform.localRotation = block.transform.localRotation;
            flashObject.transform.localScale = block.transform.localScale * _brickConfig.FinalBurstFlashStartScale;
            SpriteRenderer renderer = flashObject.AddComponent<SpriteRenderer>();
            renderer.sprite = block.sprite;
            renderer.sortingLayerID = block.sortingLayerID;
            renderer.sortingOrder = block.sortingOrder + 5;
            Color flashColor = _brickConfig.FinalBurstFlashColor;
            flashColor.a = _brickConfig.FinalBurstFlashAlpha;
            renderer.color = flashColor;
            _burstFlashes.Add(new BurstFlash
            {
                Renderer = renderer,
                StartScale = flashObject.transform.localScale,
                Life = _brickConfig.FinalBurstFlashDuration,
                Duration = _brickConfig.FinalBurstFlashDuration,
                Alpha = _brickConfig.FinalBurstFlashAlpha
            });
        }

        private void SpawnShockwave()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            GameObject waveObject = new GameObject("BrickShockwave");
            waveObject.transform.SetParent(transform, true);
            waveObject.transform.position = block.transform.position + Vector3.back * .04f;
            LineRenderer wave = waveObject.AddComponent<LineRenderer>();
            wave.useWorldSpace = false;
            wave.loop = true;
            wave.positionCount = 18;
            wave.material = new Material(shader);
            wave.sortingLayerID = block.sortingLayerID;
            wave.sortingOrder = block.sortingOrder + 3;
            Color shockwaveColor = _brickConfig.ShockwaveColor;
            shockwaveColor.a = _brickConfig.ShockwaveAlpha;
            wave.startColor = shockwaveColor;
            wave.endColor = wave.startColor;
            wave.widthMultiplier = _brickConfig.ShockwaveStartWidth;
            SetShockwavePoints(wave, _brickConfig.ShockwaveStartRadius);
            _shockwaves.Add(new Shockwave
            {
                Renderer = wave,
                Life = _brickConfig.ShockwaveDuration,
                Duration = _brickConfig.ShockwaveDuration,
                StartRadius = _brickConfig.ShockwaveStartRadius,
                EndRadius = _brickConfig.ShockwaveEndRadius
            });
        }

        private static void SetShockwavePoints(LineRenderer wave, float radius)
        {
            int count = wave.positionCount;
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * i / count;
                wave.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        private void SetCrack(int remainingHealth, int maxHealth)
        {
            if (_crack == null) return;
            if (remainingHealth <= 0 || remainingHealth >= maxHealth || _brickConfig == null)
            {
                _crack.enabled = false;
                return;
            }
            int damage = maxHealth - remainingHealth;
            int stage = Mathf.Clamp(Mathf.CeilToInt(3f * damage / maxHealth), 1, 3);
            Sprite sprite = _brickConfig.CrackSpriteFor(stage);
            _crack.sprite = sprite;
            _crack.enabled = sprite != null;
            if (sprite != null && block.sprite != null)
                _crack.transform.localScale = new Vector3(
                    block.sprite.bounds.size.x / sprite.bounds.size.x,
                    block.sprite.bounds.size.y / sprite.bounds.size.y, 1f);
        }

        protected override void RenderConfigured(in Brick brick, bool emphasized)
        {
            SetVisible(brick.Health > 0);
            block.color = Color.white;
            SetCrack(brick.Health, brick.MaxHealth);
            if (_crack != null) _crack.color = Color.white;
        }

        protected override void SetVisible(bool visible)
        {
            if (block != null) block.enabled = visible;
            if (!visible && _crack != null) _crack.enabled = false;
            if (!visible && _impactFlash != null) _impactFlash.enabled = false;
        }
    }

    /// <summary>Bakes each authored brick view into a dynamic entity with a companion view object.</summary>
    public sealed class DropBrickViewBaker : ViewBaker<DropBrickView>
    {
        public override void Bake(DropBrickView authoring)
        {
            Entity entity = CreateEntity(authoring, TransformUsageFlags.Dynamic);
            if (entity == Entity.Null) return;

            AddComponent(entity, new BrickViewData
            {
                Row = authoring.Row,
                Column = authoring.Column
            });
        }
    }
}
