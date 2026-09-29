using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Framework.Scene
{
    /// <summary>게임별 구현이 가능한 Scene Loading 표시 추상 View.</summary>
    public abstract class LoadingView : MonoBehaviour, ILoadingView
    {
        public abstract UniTask ShowAsync(CancellationToken cancellationToken);
        public abstract void SetProgress(float progress);
        public abstract UniTask HideAsync(CancellationToken cancellationToken);
    }
}
