namespace Framework.BootStrap.Interfaces
{
    /// <summary>
    /// 초기화된 리소스를 해제하는 계약.
    /// Shutdown은 여러 번 호출되어도 안전하게 구현한다.
    /// </summary>
    public interface IShutdownable
    {
        void Shutdown();
    }
}
