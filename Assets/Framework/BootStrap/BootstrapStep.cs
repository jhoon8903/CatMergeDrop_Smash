using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.BootStrap.Interfaces;
using UnityEngine;

namespace Framework.BootStrap
{
    /// <summary>
    /// 부트스트랩 목록에서 순서대로 실행되는 선택형 초기화 모듈.
    /// SDK별 초기화, 로컬 저장소 준비, 원격 설정 등을 이 클래스로 분리한다.
    /// </summary>
    public abstract class BootstrapStep : ScriptableObject, IAsyncInitializable, IShutdownable
    {
        public abstract UniTask InitializeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 기존 Step은 InitializeAsync(CancellationToken)만 구현하면 된다.
        /// </summary>
        public virtual UniTask InitializeAsync(BootstrapContext context, CancellationToken cancellationToken)
            => InitializeAsync(cancellationToken);

        public abstract void Shutdown();
    }
}
