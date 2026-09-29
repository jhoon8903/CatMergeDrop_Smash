using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.UI.Animation;
using UnityEngine;

namespace Framework.UI.Components
{
    /// <summary>
    /// uGUI 기반 화면·팝업·오버레이의 공통 베이스.
    /// ViewCode는 프로젝트별로 정의하며 0은 미할당 값으로 예약한다.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private int viewCode;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UIAnimationPlayer animationPlayer;
        
        private bool _initialized;
        private bool _visualStateApplied;
        public int ViewCode => viewCode;
        public bool IsVisible { get; private set; }
        protected bool IsScreen { get; private set; }

        internal void Initialize(UIViewLayer layer)
        {
            if (_initialized) return;
            IsScreen = layer == UIViewLayer.Screen;
            _initialized = true;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            ApplyImmediate(false);
            OnInitialized();
        }

        protected abstract void OnInitialized();

        internal async UniTask SetVisibleAsync(bool visible, bool immediate, CancellationToken cancellationToken)
        {
            if (_visualStateApplied && IsVisible == visible) return;

            _visualStateApplied = true;
            if (visible)
            {
                gameObject.SetActive(true);
                SetInputEnabled(false);
                if (immediate) animationPlayer?.ApplyImmediate(true);
                else if (animationPlayer != null) await animationPlayer.PlayAsync(true, cancellationToken);
                SetInputEnabled(true);
                IsVisible = true;
                OnShown(immediate);
            }
            else
            {
                SetInputEnabled(false);
                if (immediate) animationPlayer?.ApplyImmediate(false);
                else if (animationPlayer != null) await animationPlayer.PlayAsync(false, cancellationToken);
                IsVisible = false;
                if (!IsScreen) gameObject.SetActive(false);
                OnHidden(immediate);
            }
        }

        protected abstract void OnShown(bool immediate);

        private void ApplyImmediate(bool visible)
        {
            _visualStateApplied = true;
            IsVisible = visible;
            if (animationPlayer == null) animationPlayer = GetComponent<UIAnimationPlayer>();
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            animationPlayer?.ApplyImmediate(visible);
            SetInputEnabled(visible);
            if (!visible && !IsScreen) gameObject.SetActive(false);
        }

        private void SetInputEnabled(bool enable)
        {
            if (animationPlayer != null) animationPlayer.SetInputEnabled(enable);
            if (canvasGroup == null) return;
            canvasGroup.interactable = enable;
            canvasGroup.blocksRaycasts = enable;
        }

        protected abstract void OnHidden(bool immediate);
    }
}
