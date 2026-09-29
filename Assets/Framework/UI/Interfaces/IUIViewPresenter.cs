using System.Threading;
using Cysharp.Threading.Tasks;

namespace Framework.UI.Interfaces
{
    public interface IUIViewPresenter
    {
        int ViewCode { get; }
        UniTask PresentAsync(IUIViewArgs args, CancellationToken cancellationToken);
        UniTask DismissAsync(CancellationToken cancellationToken);
    }
}
