using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace Framework.Scene
{
    /// <summary>Addressable Scene 목록을 제공한다.</summary>
    public interface ISceneCatalog
    {
        bool TryGet(SceneId sceneId, out AssetReference sceneReference);
    }

    /// <summary>Scene 전환 중 입력을 막고 화면을 가리는 UI의 계약이다.</summary>
    public interface ITransitionView
    {
        UniTask ShowAsync(CancellationToken cancellationToken);
        UniTask HideAsync(CancellationToken cancellationToken);
    }

    /// <summary>Boot 및 Scene 로딩 진행도를 표현하는 UI의 계약이다.</summary>
    public interface ILoadingView
    {
        UniTask ShowAsync(CancellationToken cancellationToken);
        void SetProgress(float progress);
        UniTask HideAsync(CancellationToken cancellationToken);
    }

    /// <summary>앱 전역 Scene 전환 진입점이다.</summary>
    public interface ISceneSwitcher
    {
        SceneId? CurrentScene { get; }
        event Action<float> LoadProgressChanged;
        UniTask InitializeAsync(CancellationToken cancellationToken);
        UniTask SwitchAsync(SceneId destination, CancellationToken cancellationToken = default);
    }
}
