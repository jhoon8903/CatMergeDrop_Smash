using Cysharp.Threading.Tasks;
using Framework.UI.Components;
using Framework.UI.Interfaces;

namespace Framework.UI._Sample
{
    public sealed class DemoPopupPresenter : UIPresenter<DemoPopupView, DemoPopupArgs>
    {
        public override int ViewCode => 1001;
        private readonly IUINavigator _navigator;

        public DemoPopupPresenter(DemoPopupView view, IUINavigator navigator) : base(view) => _navigator = navigator;

        protected override UniTask OnPresentAsync(DemoPopupArgs args, System.Threading.CancellationToken cancellationToken)
        {
            View.CloseRequested += Close;
            return UniTask.CompletedTask;
        }

        public override UniTask DismissAsync(System.Threading.CancellationToken cancellationToken)
        {
            View.CloseRequested -= Close;
            return UniTask.CompletedTask;
        }

        private void Close()
        {
            _navigator.CloseTopAsync(UIViewLayer.Popup).Forget();
        }
    }
}
