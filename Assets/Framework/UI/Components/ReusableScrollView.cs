using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Framework.UI.Components
{
    public interface IReusableScrollDataSource
    {
        int Count { get; }
        void Bind(IReusableScrollItem item, int index);
    }

    public interface IReusableScrollItem
    {
        RectTransform RectTransform { get; }
    }

    /// <summary>세로·고정 높이 목록을 위한 최소 셀 풀링 ScrollView.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ScrollRect))]
    public sealed class ReusableScrollView : MonoBehaviour
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [SerializeField] private MonoBehaviour itemPrefab;
        [SerializeField, Min(1f)] private float itemHeight = 100f;
        [SerializeField, Min(0)] private int extraItemCount = 2;

        private readonly List<IReusableScrollItem> _items = new();
        private IReusableScrollDataSource _dataSource;
        private int _firstIndex = -1;

        private void OnEnable()
        {
            if (scrollRect == null) scrollRect = GetComponent<ScrollRect>();
            if (viewport == null) viewport = scrollRect.viewport;
            if (content == null) content = scrollRect.content;
            scrollRect.onValueChanged.AddListener(OnScroll);
            Refresh();
        }

        private void OnDisable()
        {
            if (scrollRect != null)
            {
                scrollRect.onValueChanged.RemoveListener(OnScroll);
            }
        }

        public void SetDataSource(IReusableScrollDataSource dataSource, bool resetPosition = true)
        {
            _dataSource = dataSource;
            if (resetPosition) content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            Refresh();
        }

        public void Refresh()
        {
            int count = _dataSource?.Count ?? 0;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, count * itemHeight);
            EnsurePool();
            _firstIndex = -1;
            Rebind();
        }

        private void OnScroll(Vector2 _) => Rebind();

        private void EnsurePool()
        {
            if (itemPrefab is not IReusableScrollItem || viewport == null) return;
            int target = Mathf.CeilToInt(viewport.rect.height / itemHeight) + extraItemCount;
            while (_items.Count < target)
            {
                MonoBehaviour instance = Instantiate(itemPrefab, content);
                IReusableScrollItem item = instance as IReusableScrollItem;
                if (item == null)
                {
                    Destroy(instance.gameObject);
                    Debug.LogError("[ReusableScrollView] itemPrefab은 IReusableScrollItem을 구현해야 합니다.", this);
                    return;
                }
                _items.Add(item);
            }
        }

        private void Rebind()
        {
            if (_dataSource == null || content == null || viewport == null) return;
            int first = Mathf.Max(0, Mathf.FloorToInt(content.anchoredPosition.y / itemHeight));
            if (first == _firstIndex) return;
            _firstIndex = first;

            for (int i = 0; i < _items.Count; i++)
            {
                int index = first + i;
                IReusableScrollItem item = _items[i];
                bool active = index < _dataSource.Count;
                item.RectTransform.gameObject.SetActive(active);
                if (!active) continue;
                item.RectTransform.anchoredPosition = new Vector2(0f, -index * itemHeight);
                _dataSource.Bind(item, index);
            }
        }
    }
}
