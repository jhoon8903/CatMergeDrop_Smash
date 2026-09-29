using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.Scene;
using UnityEngine;
using VContainer;

namespace Framework.BootStrap
{
    /// <summary>
    /// BootScene에 하나만 배치하는 앱 시작점.
    /// Inspector 목록 순서대로 BootstrapStep을 초기화하고, 종료 시 역순으로 정리한다.
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        private const float BootstrapProgressWeight = 0.7f;
        [SerializeField] private BootstrapStep[] steps = Array.Empty<BootstrapStep>();
        private readonly List<BootstrapStep> _initializedSteps = new();
        private ISceneSwitcher _sceneSwitcher;
        private ILoadingView _loadingView;
        private IObjectResolver _resolver;
        private CancellationTokenSource _cancellation;
        private bool _isRunning;
        private bool _isComplete;

        public bool IsComplete => _isComplete;

        public bool ContainsStep(BootstrapStep target)
        {
            if (target == null) return false;
            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == target) return true;
            }
            return false;
        }

        [Inject]
        public void Construct(ISceneSwitcher sceneSwitcher, ILoadingView loadingView, IObjectResolver resolver)
        {
            _sceneSwitcher = sceneSwitcher;
            _loadingView = loadingView;
            _resolver = resolver;
        }

        private void Start()
        {
            _cancellation = new CancellationTokenSource();
            RunOnStartAsync().Forget();
        }

        private async UniTaskVoid RunOnStartAsync()
        {
            try
            {
                await RunAsync(_cancellation.Token);
            }
            catch (OperationCanceledException) when (_cancellation is { IsCancellationRequested: true })
            {
                // 앱 종료 중 취소는 정상 흐름이다.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        public async UniTask RunAsync(CancellationToken cancellationToken)
        {
            if (_isComplete) return;
            if (_isRunning) throw new InvalidOperationException("Bootstrap is already running.");

            _isRunning = true;
            if (_sceneSwitcher == null || _loadingView == null)
                throw new InvalidOperationException("Bootstrapper 의존성이 주입되지 않았습니다. BootLifetimeScope 구성을 확인하세요.");

            _loadingView.SetProgress(0f);
            await _loadingView.ShowAsync(cancellationToken);

            void UpdateLoadingProgress(float progress)
                => _loadingView.SetProgress(BootstrapProgressWeight + (1f - BootstrapProgressWeight) * Mathf.Clamp01(progress));
            _sceneSwitcher.LoadProgressChanged += UpdateLoadingProgress;
            try
            {
                BootstrapContext context = new BootstrapContext(_resolver);
                for (int i = 0; i < steps.Length; i++)
                {
                    BootstrapStep step = steps[i];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (step == null) continue;
                    await step.InitializeAsync(context, cancellationToken);
                    _initializedSteps.Add(step);
                    _loadingView.SetProgress(BootstrapProgressWeight * (i + 1) / Mathf.Max(1, steps.Length));
                }

                await _sceneSwitcher.InitializeAsync(cancellationToken);
                _isComplete = true;
            }
            finally
            {
                _sceneSwitcher.LoadProgressChanged -= UpdateLoadingProgress;
                await _loadingView.HideAsync(CancellationToken.None);
                _isRunning = false;
            }
        }

        private void OnDestroy()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;

            for (int i = _initializedSteps.Count - 1; i >= 0; i--)
            {
                try
                {
                    _initializedSteps[i].Shutdown();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            _initializedSteps.Clear();
        }
    }
}
