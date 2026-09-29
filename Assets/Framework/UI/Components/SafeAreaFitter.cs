using UnityEngine;

namespace Framework.UI.Components
{
    public enum SafeAreaMode : byte { All, Top, Bottom, Horizontal }

    /// <summary>기기 Safe Area를 RectTransform의 anchor에 반영한다.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        [SerializeField] private RectTransform target;
        [SerializeField] private SafeAreaMode mode = SafeAreaMode.All;
        private Rect _lastSafeArea;
        private Vector2Int _lastSize;
        private bool _applied;

        private void OnEnable() => Apply();
        private void OnDisable() => _applied = false;
        private void OnRectTransformDimensionsChange() => Apply();

        [ContextMenu("Apply Safe Area")]
        public void Apply()
        {
            if (_applied && _lastSafeArea == Screen.safeArea && _lastSize.x == Screen.width && _lastSize.y == Screen.height) return;
            if (target == null) target = transform as RectTransform;
            if (target == null || Screen.width <= 0 || Screen.height <= 0) return;

            Rect safe = Screen.safeArea;
            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;

            if (mode == SafeAreaMode.Top) min.y = 0f;
            else if (mode == SafeAreaMode.Bottom) max.y = 1f;
            else if (mode == SafeAreaMode.Horizontal) { min.y = 0f; max.y = 1f; }

            target.anchorMin = min;
            target.anchorMax = max;
            target.offsetMin = Vector2.zero;
            target.offsetMax = Vector2.zero;
            _lastSafeArea = safe;
            _lastSize = new Vector2Int(Screen.width, Screen.height);
            _applied = true;
        }
    }
}
