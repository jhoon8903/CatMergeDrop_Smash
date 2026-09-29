using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.UI.Interfaces;
using Framework.UI.Navigation;

namespace Framework.UI.Components
{
    /// <summary>DI 서비스와 runtime args를 분리하는 타입 안전 MVP Presenter 베이스.</summary>
    public abstract class UIPresenter<TView, TArgs> : IUIViewPresenter
        where TView : UIView
        where TArgs : IUIViewArgs
    {
        protected readonly TView View;

        protected UIPresenter(TView view) => View = view;
        public abstract int ViewCode { get; }

        public UniTask PresentAsync(IUIViewArgs args, CancellationToken cancellationToken)
            => args is TArgs typed
                ? OnPresentAsync(typed, cancellationToken)
                : UniTask.FromException(new ArgumentException($"Expected {typeof(TArgs).Name}, got {args?.GetType().Name ?? "null"}."));

        public abstract UniTask DismissAsync(CancellationToken cancellationToken);
        protected abstract UniTask OnPresentAsync(TArgs args, CancellationToken cancellationToken);
    }
}
