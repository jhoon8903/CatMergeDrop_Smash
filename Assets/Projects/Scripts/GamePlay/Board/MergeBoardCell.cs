using UnityEngine;

namespace Projects.Scripts.GamePlay
{
    /// <summary>One world-space merge cell with sprite visuals and a 2D hit area.</summary>
    public sealed class MergeBoardCell : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer background;
        [SerializeField] private SpriteRenderer cat;
        [SerializeField] private BoxCollider2D hitArea;
        [SerializeField] private int slot;
        private Sprite _fallbackCatSprite;
        private DropCatView _dropCatView;

        public int Slot => slot;
        public Sprite CatSprite => cat != null ? cat.sprite : null;

        public bool IsConfigured => background != null && cat != null && hitArea != null;

        private void Awake()
        {
            _fallbackCatSprite = cat != null ? cat.sprite : null;
            _dropCatView = GetComponentInChildren<DropCatView>(true);
        }

        public void Render(int catLevel, bool selected, Sprite levelSprite, int columns)
        {
            if (!IsConfigured) return;
            bool alternate = ((slot / columns + slot % columns) & 1) != 0;
            background.color = selected ? new Color(1f, .85f, .53f)
                : alternate ? new Color(.89f, .87f, .83f) : new Color(.93f, .91f, .87f);
            Sprite sprite = levelSprite != null ? levelSprite : _fallbackCatSprite;
            if (cat.sprite != sprite && sprite != null)
            {
                if (_dropCatView != null) _dropCatView.SetLevelSprite(sprite);
                else
                {
                    cat.sprite = sprite;
                    float size = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
                    cat.transform.localScale = Vector3.one * (WorldPlayfieldLayout.CatVisualSize / size);
                }
            }
            if (_dropCatView != null) _dropCatView.SetBoardLevel(catLevel);
            else cat.enabled = catLevel > 0 && cat.sprite != null;
        }
    }
}
