using System.Threading;
using Cysharp.Threading.Tasks;

namespace Framework.UI.Animation
{
    /// <summary>UIView의 open/close 연출 계약.</summary>
    public interface IUIAnimation
    {
        void ApplyImmediate(bool visible);
        UniTask PlayAsync(bool opening, CancellationToken cancellationToken);
        void SetInputEnabled(bool enabled);
    }
}
