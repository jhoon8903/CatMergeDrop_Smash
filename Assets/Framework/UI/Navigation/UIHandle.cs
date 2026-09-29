using System.Threading;
using Framework.UI.Components;
using Framework.UI.Interfaces;

namespace Framework.UI.Navigation
{
    /// <summary>캐시된 View 인스턴스의 열림 상태와 취소 토큰을 소유한다.</summary>
    public sealed class UIHandle
    {
        private CancellationTokenSource _presentationLifetime;

        internal UIHandle(int viewCode, UIView view, IUIViewPresenter presenter, UILayerRoot owner)
        {
            ViewCode = viewCode;
            View = view;
            Presenter = presenter;
            Owner = owner;
        }

        public int ViewCode { get; }
        public UIView View { get; }
        public IUIViewPresenter Presenter { get; }
        public UILayerRoot Owner { get; }
        public bool IsOpen { get; internal set; }
        internal CancellationToken PresentationToken => _presentationLifetime?.Token ?? CancellationToken.None;

        internal void BeginPresentation()
        {
            _presentationLifetime?.Dispose();
            _presentationLifetime = new CancellationTokenSource();
            IsOpen = true;
        }

        internal void EndPresentation()
        {
            IsOpen = false;
            _presentationLifetime?.Cancel();
            _presentationLifetime?.Dispose();
            _presentationLifetime = null;
        }

    }
}
