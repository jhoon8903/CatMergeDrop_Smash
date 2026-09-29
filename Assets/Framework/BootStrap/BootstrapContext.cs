using VContainer;

namespace Framework.BootStrap
{
    /// <summary>BootstrapStep 간에 전달하는 런타임 부트 상태.</summary>
    public sealed class BootstrapContext
    {
        private readonly IObjectResolver _resolver;

        public BootstrapContext(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        public T Resolve<T>() => _resolver.Resolve<T>();
    }
}
