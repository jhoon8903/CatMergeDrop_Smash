using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.UI.Navigation;

namespace Framework.UI.Interfaces
{
    /// <summary>씬에 등록된 View의 표시 수명과 화면 stack을 관리한다.</summary>
    public interface IUINavigator
    {
        UniTask<UIHandle> PushAsync(int viewCode, IUIViewArgs args, CancellationToken cancellationToken = default);
        UniTask<bool> CloseAsync(UIHandle handle, CancellationToken cancellationToken = default);
        UniTask<bool> CloseTopAsync(UIViewLayer layer, CancellationToken cancellationToken = default);
    }
}
