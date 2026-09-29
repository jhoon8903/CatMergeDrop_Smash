using System;
using Projects.Scripts.GamePlay.Configs;
using UnityEngine;

namespace Projects.Scripts.GamePlay
{
    /// <summary>Follows the launched cats, then the bricks after their first contact.</summary>
    [DisallowMultipleComponent]
    public sealed class DropCameraController : MonoBehaviour
    {
        [SerializeField] private DropCameraConfig config;

        private static DropCameraConfig _defaultConfig;

        private Camera _camera;
        private Vector3 _originalPosition;
        private float _originalOrthographicSize;
        private float _boardCenterY;
        private float _boardHalfWidth;
        private float _dropHalfWidth;
        private float _dropFocusY;
        private int _stageIndex = 1;
        private float _verticalVelocity;
        private float _sizeVelocity;
        private float _targetSwitchDampingRemaining;
        private bool _isDropping;
        private bool _hasHitBrick;
        private int _trackedCatSlot = -1;
        private bool _targetSwitchedThisFrame;
        private bool _isInitialized;
        private float _shakeTime;
        private float _shakeDuration;
        private float _shakeMagnitude;
        private Vector2 _lastShakeOffset;

        private DropCameraConfig Config
        {
            get
            {
                if (config != null) return config;
                if (_defaultConfig == null) _defaultConfig = ScriptableObject.CreateInstance<DropCameraConfig>();
                return _defaultConfig;
            }
        }

        public int TrackedCatSlot => _trackedCatSlot;

        public void Initialize(Camera cam, float boardCenterY, float boardHalfWidth)
        {
            _camera = cam;
            _boardCenterY = boardCenterY;
            _boardHalfWidth = Mathf.Max(0f, boardHalfWidth);
            _dropHalfWidth = _boardHalfWidth;
            _dropFocusY = boardCenterY;
            _isDropping = false;
            _trackedCatSlot = -1;
            _targetSwitchedThisFrame = false;
            _verticalVelocity = 0f;
            _sizeVelocity = 0f;
            _targetSwitchDampingRemaining = 0f;
            _isInitialized = _camera != null;

            if (_camera == null) return;
            _originalPosition = _camera.transform.position;
            _originalOrthographicSize = _camera.orthographicSize;
            Vector3 position = _camera.transform.position;
            position.y = _boardCenterY;
            _camera.transform.position = position;
            _camera.orthographicSize = GetMergeSize();
        }

        /// <summary>Updates the framing profile for the active stage.</summary>
        public void SetStageIndex(int stageIndex)
        {
            _stageIndex = Mathf.Max(1, stageIndex);
        }

        public void SetStageLayout(StageLayout layout)
        {
            _boardHalfWidth = layout.WallInnerX;
            _dropHalfWidth = layout.WallInnerX;
        }

        public void BeginDrop(Action onLaunch)
        {
            _isDropping = true;
            _hasHitBrick = false;
            _trackedCatSlot = -1;
            _targetSwitchedThisFrame = false;
            _dropFocusY = _boardCenterY;
            _verticalVelocity = 0f;
            _sizeVelocity = 0f;
            _targetSwitchDampingRemaining = 0f;
            // Launch while the board is still on screen; camera motion follows the cats.
            onLaunch?.Invoke();
        }

        public void TrackFallingCat(int slot, float catWorldY)
        {
            if (!_isInitialized || !_isDropping) return;
            float targetY = Mathf.Min(_boardCenterY, catWorldY + Config.FallingLead);
            if (slot != _trackedCatSlot)
            {
                bool switchingCats = _trackedCatSlot >= 0;
                _targetSwitchedThisFrame = switchingCats;
                _targetSwitchDampingRemaining = switchingCats
                    ? Config.TargetSwitchDampingDuration : 0f;
                _trackedCatSlot = slot;
                _dropFocusY = targetY;
                _verticalVelocity = 0f;
                return;
            }
            // After first impact, keep the camera moving toward the goal even when
            // the remaining part of this cat's column contains no more bricks.
            _dropFocusY = _hasHitBrick ? Mathf.Min(_dropFocusY, targetY) : targetY;
        }

        /// <summary>Switches from following the cats to the brick field at their first impact.</summary>
        public void NotifyBrickImpact(float tileWorldY)
        {
            if (!_isInitialized || !_isDropping || _hasHitBrick) return;
            _hasHitBrick = true;
            if (!_targetSwitchedThisFrame) _dropFocusY = tileWorldY;
            _verticalVelocity = 0f;
        }

        public void FocusBrokenTile(float tileWorldY)
        {
            if (!_isInitialized || !_isDropping || !_hasHitBrick || _targetSwitchedThisFrame) return;
            _dropFocusY = Mathf.Min(_dropFocusY, tileWorldY);
        }

        public void ReturnToMerge()
        {
            if (!_isInitialized) return;
            _isDropping = false;
            _hasHitBrick = false;
            _trackedCatSlot = -1;
            _targetSwitchedThisFrame = false;
            _dropFocusY = _boardCenterY;
            _verticalVelocity = 0f;
            _targetSwitchDampingRemaining = 0f;
            _shakeTime = 0f;
        }

        private void LateUpdate()
        {
            if (!_isInitialized || _camera == null) return;

            AdvanceCamera(Time.deltaTime);
        }

        private void AdvanceCamera(float deltaTime)
        {
            float switchBlend = _targetSwitchDampingRemaining > 0f
                ? _targetSwitchDampingRemaining / Config.TargetSwitchDampingDuration : 0f;
            float positionSmoothTime = Mathf.Lerp(Config.PositionSmoothTime,
                Config.TargetSwitchSmoothTime, switchBlend);
            _targetSwitchDampingRemaining = Mathf.Max(0f,
                _targetSwitchDampingRemaining - deltaTime);

            float mergeSize = GetMergeSize();
            float targetSize = _isDropping
                ? _hasHitBrick ? GetDropSize(mergeSize) : Mathf.Max(mergeSize, Config.FallingViewSize)
                : mergeSize;
            float targetY = _isDropping ? _dropFocusY : _boardCenterY;

            Transform cameraTransform = _camera.transform;
            Vector3 position = cameraTransform.position;
            position.x -= _lastShakeOffset.x;
            position.y -= _lastShakeOffset.y;
            position.y = Mathf.SmoothDamp(position.y, targetY, ref _verticalVelocity,
                positionSmoothTime, Config.MaximumCameraSpeed, deltaTime);
            _lastShakeOffset = Vector2.zero;
            if (_shakeTime > 0f)
            {
                _shakeTime = Mathf.Max(0f, _shakeTime - deltaTime);
                float strength = _shakeMagnitude * (_shakeTime / _shakeDuration);
                _lastShakeOffset = UnityEngine.Random.insideUnitCircle * strength;
                position.x += _lastShakeOffset.x;
                position.y += _lastShakeOffset.y;
            }
            cameraTransform.position = position;
            _camera.orthographicSize = Mathf.SmoothDamp(
                _camera.orthographicSize, targetSize, ref _sizeVelocity, Config.SizeSmoothTime,
                Mathf.Infinity, deltaTime);

            _targetSwitchedThisFrame = false;
        }

        private float GetMergeSize()
        {
            float aspect = Mathf.Max(_camera.aspect, 0.01f);
            return Mathf.Max(Config.MinimumMergeSize,
                _boardHalfWidth / aspect + Config.MergeFramingMargin);
        }

        private float GetDropSize(float mergeSize)
        {
            float aspect = Mathf.Max(_camera.aspect, .01f);
            float size = Mathf.Max(Config.MinimumDropSize, mergeSize * Config.DropZoomMultiplier,
                _dropHalfWidth / aspect + Config.MergeFramingMargin);
            return _stageIndex >= Config.ExtendedDropStage
                ? size + Config.ExtendedDropSizeAddition
                : size;
        }

        private void OnDestroy()
        {
            if (!_isInitialized || _camera == null) return;
            _camera.transform.position = _originalPosition;
            _camera.orthographicSize = _originalOrthographicSize;
        }
    }
}
