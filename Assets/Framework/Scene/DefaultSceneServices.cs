using System.Threading;
using Cysharp.Threading.Tasks;

namespace Framework.Scene
{
    /// <summary>게임별 전환 UI가 연결되기 전에도 Scene 전환을 가능하게 하는 기본 구현이다.</summary>
    public sealed class NullTransitionView : ITransitionView
    {
        public UniTask ShowAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask HideAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
    }

    /// <summary>게임별 로딩 UI가 연결되기 전에도 Bootstrap을 가능하게 하는 기본 구현이다.</summary>
    public sealed class NullLoadingView : ILoadingView
    {
        public UniTask ShowAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
        public void SetProgress(float progress) { }
        public UniTask HideAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
    }
}
