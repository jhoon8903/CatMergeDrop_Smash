using System.Threading;
using Cysharp.Threading.Tasks;

namespace Framework.BootStrap.Interfaces
{
    /// <summary>
    /// 앱 시작 시 한 번 초기화해야 하는 서비스의 계약.
    /// 구현체는 취소를 존중하고, 재시도 정책은 호출자에게 맡긴다.
    /// </summary>
    public interface IAsyncInitializable
    {
        UniTask InitializeAsync(CancellationToken cancellationToken);
    }
}
