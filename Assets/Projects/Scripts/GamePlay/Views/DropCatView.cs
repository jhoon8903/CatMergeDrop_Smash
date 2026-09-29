using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Projects.Scripts.GamePlay.Configs;
using Projects.Scripts.GamePlay.Views;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Stable authored board slot for a baked cat view entity.</summary>
    public struct CatViewData : IComponentData
    {
        public int Slot;
    }

    /// <summary>The board cat visual itself travels during Drop, then returns to its cell.</summary>
    public sealed class DropCatView : ViewAuthoring<Cat>
    {
        [SerializeField] private SpriteRenderer cat;
        [SerializeField] private int slot;
        private Vector3 _boardLocalPosition;
        private Vector3 _baseCatScale;
        private TextMesh _levelBadge;
        private CatConfig _config;
        private Vector2 _impactScale = Vector2.one;
        private float _impactAge = -1f;
        private int _lastImpactSerial;
        private int _level;
        private bool _inDrop;

        public int Slot => slot;
        public override bool IsConfigured => cat != null;

        public void Configure(CatConfig config) => _config = config;

        private void Awake()
        {
            _boardLocalPosition = transform.localPosition;
            if (cat != null && cat.sprite != null)
            {
                float size = Mathf.Max(cat.sprite.bounds.size.x, cat.sprite.bounds.size.y);
                _baseCatScale = Vector3.one * (WorldPlayfieldLayout.CatVisualSize / size);
                ApplyImpactScale();
            }
        }

        public void SetLevelSprite(Sprite sprite)
        {
            if (cat == null || sprite == null || cat.sprite == sprite) return;
            cat.sprite = sprite;
            float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            _baseCatScale = Vector3.one * (WorldPlayfieldLayout.CatVisualSize / size);
            ApplyImpactScale();
        }

        /// <summary>Updates the persistent board label; the same view uses it while dropping.</summary>
        public void SetBoardLevel(int level)
        {
            _level = Mathf.Max(0, level);
            EnsureLevelBadge();
            RefreshLevelBadge();
            SetVisible(_level > 0);
            ResetImpactScale();
        }

        protected override void RenderConfigured(in Cat state, bool emphasized)
        {
            _inDrop = true;
            _level = state.Level;
            EnsureLevelBadge();
            RefreshLevelBadge();
            SetVisible(true);
            transform.position = new Vector3(
                state.X,
                WorldPlayfieldLayout.DropY(state.Y), 0f);
            UpdateImpactScale(state.ImpactSerial);
        }

        public override void Hide()
        {
            if (!_inDrop) return;
            base.Hide();
        }

        public void ReturnToBoard()
        {
            if (!_inDrop) return;
            transform.localPosition = _boardLocalPosition;
            SetVisible(true);
            _inDrop = false;
            ResetImpactScale();
        }

        protected override void SetVisible(bool visible)
        {
            if (cat != null) cat.enabled = visible;
            if (_levelBadge != null) _levelBadge.gameObject.SetActive(visible && _level > 0);
        }

        private void EnsureLevelBadge()
        {
            if (_levelBadge != null) return;

            GameObject badge = new GameObject("LevelBadge");
            badge.transform.SetParent(transform, false);
            badge.transform.localPosition = new Vector3(.36f, -.35f, 0f);
            _levelBadge = badge.AddComponent<TextMesh>();
            _levelBadge.anchor = TextAnchor.MiddleCenter;
            _levelBadge.alignment = TextAlignment.Center;
            _levelBadge.characterSize = .025f;
            _levelBadge.fontSize = 48;
            _levelBadge.color = new Color(.19f, .12f, .12f);
            badge.GetComponent<MeshRenderer>().sortingOrder = 4;
        }

        private void RefreshLevelBadge()
        {
            if (_levelBadge != null) _levelBadge.text = $"LV. {_level}";
        }

        private void UpdateImpactScale(int impactSerial)
        {
            if (_config == null) return;
            if (impactSerial != _lastImpactSerial)
            {
                _lastImpactSerial = impactSerial;
                _impactAge = 0f;
            }

            if (_impactAge < 0f) return;
            _impactAge += Time.deltaTime;
            float compressionEnd = _config.ImpactCompressionDuration;
            float stretchEnd = compressionEnd + _config.ImpactStretchDuration;
            float recoveryEnd = stretchEnd + _config.ImpactRecoveryDuration;
            if (_impactAge < compressionEnd)
                _impactScale = Vector2.Lerp(Vector2.one, _config.ImpactCompressionScale,
                    Mathf.SmoothStep(0f, 1f, _impactAge / compressionEnd));
            else if (_impactAge < stretchEnd)
                _impactScale = Vector2.Lerp(_config.ImpactCompressionScale, _config.ImpactStretchScale,
                    Mathf.SmoothStep(0f, 1f, (_impactAge - compressionEnd) / _config.ImpactStretchDuration));
            else if (_impactAge < recoveryEnd)
                _impactScale = Vector2.Lerp(_config.ImpactStretchScale, Vector2.one,
                    Mathf.SmoothStep(0f, 1f, (_impactAge - stretchEnd) / _config.ImpactRecoveryDuration));
            else
            {
                _impactAge = -1f;
                _impactScale = Vector2.one;
            }
            ApplyImpactScale();
        }

        private void ResetImpactScale()
        {
            _impactAge = -1f;
            _lastImpactSerial = 0;
            _impactScale = Vector2.one;
            ApplyImpactScale();
        }

        private void ApplyImpactScale()
        {
            if (cat == null) return;
            cat.transform.localScale = new Vector3(
                _baseCatScale.x * _impactScale.x,
                _baseCatScale.y * _impactScale.y,
                _baseCatScale.z);
        }
    }

    /// <summary>Bakes the board cat view as a dynamic entity with its GameObject companion.</summary>
    public sealed class DropCatViewBaker : ViewBaker<DropCatView>
    {
        public override void Bake(DropCatView authoring)
        {
            Entity entity = CreateEntity(authoring, TransformUsageFlags.Dynamic);
            if (entity == Entity.Null) return;

            AddComponent(entity, new CatViewData { Slot = authoring.Slot });
        }
    }
}
