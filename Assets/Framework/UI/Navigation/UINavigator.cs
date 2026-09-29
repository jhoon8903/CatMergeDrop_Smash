using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.UI.Interfaces;

namespace Framework.UI.Navigation
{
    /// <summary>
    /// ViewCode로 대상 UILayerRoot를 찾아 요청을 위임하는 UI 진입점.
    /// runtime args는 DI 등록 대신 Presenter.PresentAsync로 전달한다.
    /// </summary>
    public sealed class UINavigator : IUINavigator, IDisposable
    {
        private readonly UILayerRootRegistry _roots;

        public UINavigator(UILayerRootRegistry roots)
        {
            _roots = roots;
        }

        public async UniTask<UIHandle> PushAsync(int viewCode, IUIViewArgs args, CancellationToken cancellationToken = default)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (!_roots.TryGetHandle(viewCode, out UIHandle handle)) throw new InvalidOperationException($"Scene에 등록되지 않은 ViewCode: {viewCode}");
            return await handle.Owner.OpenAsync(handle, args, cancellationToken);
        }

        public async UniTask<bool> CloseAsync(UIHandle handle, CancellationToken cancellationToken = default)
        {
            if (handle == null) return false;
            return await handle.Owner.CloseAsync(handle, cancellationToken);
        }

        public async UniTask<bool> CloseTopAsync(UIViewLayer layer, CancellationToken cancellationToken = default)
        {
            UILayerRoot root = _roots.Get(layer);
            return root != null && await root.CloseTopAsync(cancellationToken);
        }

        public void Dispose()
        {
            // View scope의 수명은 Scene이 소유한다.
        }
    }
}
