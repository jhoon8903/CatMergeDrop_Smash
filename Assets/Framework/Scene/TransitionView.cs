using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Framework.Scene
{
    /// <summary>SceneSwitcher가 전환 중 입력을 가리고 화면을 덮는 범용 추상 View.</summary>
    public abstract class TransitionView : MonoBehaviour, ITransitionView
    {
        public abstract UniTask ShowAsync(CancellationToken cancellationToken);
        public abstract UniTask HideAsync(CancellationToken cancellationToken);
    }
}
