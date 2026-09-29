using Unity.Entities;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 반납할 인스턴스에 붙이는 태그.
    /// EntityPoolSystem이 수거해 처리한다.
    /// </summary>
    public struct DespawnRequest : IComponentData
    {
        
    }
}