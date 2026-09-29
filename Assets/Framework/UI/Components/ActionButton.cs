using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Framework.UI.Components
{
    public enum ActionButtonTweenType : byte
    {
        Press,
        PunchScale
    }

    /// <summary>
    /// 입력 연출, 전역 중복 입력 방지, 활성 상태를 제공하는 Button 래퍼.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    public sealed class ActionButton : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Button button;
        [SerializeField] private RectTransform animationTarget;
        [SerializeField] private ActionButtonTweenType tweenType = ActionButtonTweenType.Press;
        [SerializeField, Min(0f)] private float pressOffsetY = 12f;
        [SerializeField, Min(0.01f)] private float pressDuration = 0.08f;
        [SerializeField, Min(1f)] private float punchScale = 1.08f;
        [SerializeField, Min(0.01f)] private float punchDuration = 0.18f;
        [SerializeField] private bool useGlobalLock = true;
        [SerializeField] private string lockKey = "UI.Action";
        [SerializeField, Min(0f)] private float lockDuration = 0.15f;
        [SerializeField] private UnityEvent invoked;

        private Vector3 _normalScale;
        private Vector2 _normalAnchoredPosition;
        private CancellationTokenSource _tweenCancellation;

        public bool IsInteractable => button != null && button.interactable;
        public UnityEvent Invoked => invoked;

        private void OnEnable()
        {
            if (button == null) button = GetComponent<Button>();
            if (animationTarget == null) animationTarget = transform as RectTransform;
            if (animationTarget == null) return;
            _normalScale = animationTarget.localScale;
            _normalAnchoredPosition = animationTarget.anchoredPosition;
        }

        private void OnDisable()
        {
            CancelTween();
        }

        public void SetInteractable(bool value)
        {
            if (button == null) return;
            button.interactable = value;
            if (!value) RestorePressPositionAsync().Forget();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable) return;
            if (tweenType == ActionButtonTweenType.Press) PlayPressClickAsync().Forget();
            Invoke();
        }

        private void Invoke()
        {
            if (!IsInteractable) return;
            string key = string.IsNullOrWhiteSpace(lockKey) ? "UI.Action" : lockKey;
            if (useGlobalLock && !UIInputLock.TryAcquire(key)) return;

            try
            {
                if (tweenType == ActionButtonTweenType.PunchScale) PlayPunchScaleAsync().Forget();
                invoked.Invoke();
            }
            finally
            {
                if (useGlobalLock) ReleaseLockAfter(key).Forget();
            }
        }

        private async UniTaskVoid RestorePressPositionAsync()
        {
            if (animationTarget == null) return;
            CancellationToken token = RenewTween();
            try
            {
                await TweenAnchoredPositionAsync(_normalAnchoredPosition, pressDuration, token);
            }
            catch (OperationCanceledException) { }
        }

        private async UniTaskVoid PlayPressClickAsync()
        {
            if (animationTarget == null) return;
            CancellationToken token = RenewTween();
            try
            {
                await TweenAnchoredPositionAsync(_normalAnchoredPosition + Vector2.down * pressOffsetY, pressDuration, token);
                await TweenAnchoredPositionAsync(_normalAnchoredPosition, pressDuration, token);
            }
            catch (OperationCanceledException) { }
        }

        private async UniTaskVoid PlayPunchScaleAsync()
        {
            if (animationTarget == null) return;
            CancellationToken token = RenewTween();
            try
            {
                float halfDuration = punchDuration * 0.5f;
                await TweenScaleAsync(_normalScale * punchScale, halfDuration, token);
                await TweenScaleAsync(_normalScale, halfDuration, token);
            }
            catch (OperationCanceledException) { }
        }

        private async UniTask TweenScaleAsync(Vector3 target, float duration, CancellationToken cancellationToken)
        {
            Vector3 start = animationTarget.localScale;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                animationTarget.localScale = Vector3.LerpUnclamped(start, target, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            animationTarget.localScale = target;
        }

        private async UniTask TweenAnchoredPositionAsync(Vector2 target, float duration, CancellationToken cancellationToken)
        {
            Vector2 start = animationTarget.anchoredPosition;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                animationTarget.anchoredPosition = Vector2.LerpUnclamped(start, target, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
            animationTarget.anchoredPosition = target;
        }

        private CancellationToken RenewTween()
        {
            CancelTween();
            _tweenCancellation = new CancellationTokenSource();
            return _tweenCancellation.Token;
        }

        private void CancelTween()
        {
            if (_tweenCancellation == null) return;
            _tweenCancellation.Cancel();
            _tweenCancellation.Dispose();
            _tweenCancellation = null;
        }

        private async UniTask ReleaseLockAfter(string key)
        {
            try
            {
                if (lockDuration > 0f) await UniTask.Delay(TimeSpan.FromSeconds(lockDuration), ignoreTimeScale: true);
            }
            finally
            {
                UIInputLock.Release(key);
            }
        }
    }
}
