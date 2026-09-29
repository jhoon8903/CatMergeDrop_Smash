using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using VContainer;

namespace Framework.Scene
{
    /// <summary>
    /// BootScene에 상주하며 Scene 선택·로드·언로드와 전환 UI를 전담한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneSwitcher : MonoBehaviour, ISceneSwitcher
    {
        private readonly SemaphoreSlim _switchGate = new(1, 1);
        private ISceneCatalog _sceneCatalog;
        private SceneEntryResolver _entryResolver;
        private ITransitionView _transitionView;
        private AsyncOperationHandle<SceneInstance> _currentHandle;
        private bool _hasCurrentScene;
        private bool _initialized;

        public SceneId? CurrentScene { get; private set; }
        public event Action<float> LoadProgressChanged;

        [Inject]
        public void Construct(
            ISceneCatalog sceneCatalog,
            SceneEntryResolver entryResolver,
            ITransitionView transitionView)
        {
            _sceneCatalog = sceneCatalog;
            _entryResolver = entryResolver;
            _transitionView = transitionView;
        }

        private void OnDestroy()
        {
            _switchGate.Dispose();
        }

        /// <summary>
        ///   Bootstrap 완료 시 한 번 호출한다.
        /// 최초 목적지는 SceneSwitcher가 결정한다.
        /// </summary>
        public async UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (_initialized) return;
            if (_sceneCatalog == null || _entryResolver == null || _transitionView == null)
                throw new InvalidOperationException("SceneSwitcher 의존성이 주입되지 않았습니다. BootLifetimeScope 구성을 확인하세요.");

            SceneId destination = await _entryResolver.ResolveAsync(cancellationToken);
            await SwitchAsync(destination, cancellationToken);
            _initialized = true;
        }

        public async UniTask SwitchAsync(SceneId destination, CancellationToken cancellationToken = default)
        {
            if (CurrentScene == destination) return;
            await _switchGate.WaitAsync(cancellationToken);
            try
            {
                if (CurrentScene == destination) return;
                if (!_sceneCatalog.TryGet(destination, out AssetReference reference) || reference == null || !reference.RuntimeKeyIsValid())
                    throw new InvalidOperationException($"{destination} Scene Addressable이 SceneCatalog에 등록되지 않았습니다.");

                await _transitionView.ShowAsync(cancellationToken);
                await LoadAndActivateAsync(destination, reference, cancellationToken);
            }
            finally
            {
                if (_transitionView != null) await _transitionView.HideAsync(CancellationToken.None);
                _switchGate.Release();
            }
        }

        private async UniTask LoadAndActivateAsync(SceneId destination, AssetReference reference, CancellationToken cancellationToken)
        {
            AsyncOperationHandle<SceneInstance> nextHandle = Addressables.LoadSceneAsync(reference, LoadSceneMode.Additive, true);
            try
            {
                while (!nextHandle.IsDone)
                {
                    LoadProgressChanged?.Invoke(nextHandle.PercentComplete);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (nextHandle.Status != AsyncOperationStatus.Succeeded)
                    throw nextHandle.OperationException ?? new InvalidOperationException($"{destination} Scene 로드에 실패했습니다.");

                SceneManager.SetActiveScene(nextHandle.Result.Scene);
                LoadProgressChanged?.Invoke(1f);

                if (_hasCurrentScene && _currentHandle.IsValid())
                    await Addressables.UnloadSceneAsync(_currentHandle, autoReleaseHandle: true).ToUniTask(cancellationToken: cancellationToken);

                _currentHandle = nextHandle;
                _hasCurrentScene = true;
                CurrentScene = destination;
            }
            catch
            {
                if (nextHandle.IsValid()) Addressables.Release(nextHandle);
                throw;
            }
        }
    }
}
