using Framework.UI.Interfaces;
using Framework.UI.Navigation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Framework.UI.LifeTimeScope
{
    public sealed class UIRootLifetimeScope : LifetimeScope
    {
        [SerializeField] private UILayerRoot screenRoot;
        [SerializeField] private UILayerRoot popupRoot;
        [SerializeField] private UILayerRoot overlayRoot;

        protected override void Configure(IContainerBuilder builder)
        {
            if (screenRoot == null || popupRoot == null || overlayRoot == null) throw new System.InvalidOperationException("UIRootLifetimeScope의 LayerRoot 참조가 없습니다.");
            builder.RegisterInstance(new UILayerRootRegistry(screenRoot, popupRoot, overlayRoot));
            builder.Register<UINavigator>(Lifetime.Singleton).As<IUINavigator>();
        }
    }
}
