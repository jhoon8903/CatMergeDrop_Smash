using VContainer;

namespace Framework.UI.LifeTimeScope
{
    /// <summary>UILayerRoot 하위에서 레이어 전용 의존성을 등록한다.</summary>
    public interface IUILayerInstaller
    {
        void Install(IContainerBuilder builder);
    }
}
