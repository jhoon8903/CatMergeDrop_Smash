using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.Components
{
    /// <summary>화면 종횡비에 따라 CanvasScaler의 Match 값을 연속적으로 조절한다.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(CanvasScaler))]
    public sealed class CanvasAutoResolution : MonoBehaviour
    {
        [SerializeField] private Vector2 portraitReferenceResolution = new(720f, 1280f);
        [SerializeField] private Vector2 landscapeReferenceResolution = new(1280f, 720f);
        [SerializeField, Range(0.3f, 1f)] private float mobileAspect = 9f / 16f;
        [SerializeField, Range(0.3f, 1f)] private float tabletAspect = 3f / 4f;
        [SerializeField, Range(0f, 1f)] private float mobileMatch = 0f;
        [SerializeField, Range(0f, 1f)] private float tabletMatch = 1f;
        [SerializeField, Range(0f, 1f)] private float landscapeMatch = 1f;

        private Vector2Int _lastSize;
        private CanvasScaler _scaler;

        private void OnEnable() => Apply();
        private void OnDisable() => _lastSize = default;
        private void OnRectTransformDimensionsChange() => Apply();

        [ContextMenu("Apply Resolution")]
        public void Apply()
        {
            if (_lastSize.x == Screen.width && _lastSize.y == Screen.height) return;
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null || Screen.width <= 0 || Screen.height <= 0) return;

            float aspect = (float)Screen.width / Screen.height;
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            if (aspect > 1f)
            {
                _scaler.referenceResolution = landscapeReferenceResolution;
                _scaler.matchWidthOrHeight = landscapeMatch;
            }
            else
            {
                _scaler.referenceResolution = portraitReferenceResolution;
                float normalized = Mathf.InverseLerp(mobileAspect, Mathf.Max(mobileAspect + 0.001f, tabletAspect), aspect);
                _scaler.matchWidthOrHeight = Mathf.Lerp(mobileMatch, tabletMatch, normalized * normalized * (3f - 2f * normalized));
            }
            _lastSize = new Vector2Int(Screen.width, Screen.height);
        }
    }
}
