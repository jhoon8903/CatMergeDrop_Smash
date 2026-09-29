using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Framework.Scene
{
    /// <summary>
    /// 앱의 저장 데이터·인증·원격 설정을 바탕으로 최초 진입 Scene을 결정한다.
    /// 프로젝트에는 하나만 두며 BootLifetimeScope가 직접 주입한다.
    /// </summary>
    public sealed class SceneEntryResolver : ScriptableObject
    {
        [SerializeField] private SceneId entryScene = SceneId.Game;

        public UniTask<SceneId> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(entryScene);
        }
    }
}
