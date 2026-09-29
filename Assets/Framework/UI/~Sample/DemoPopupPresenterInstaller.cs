using Framework.UI.Interfaces;
using Framework.UI.LifeTimeScope;
using UnityEngine;
using VContainer;

namespace Framework.UI._Sample
{
    /// <summary>DemoPopup이 속한 UILayerRoot에 Presenter를 등록한다.</summary>
    public sealed class DemoPopupPresenterInstaller : MonoBehaviour, IUILayerInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder.Register<DemoPopupPresenter>(Lifetime.Scoped).As<IUIViewPresenter>();
        }
    }
}
