using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// Job/ECB에서 생성 요청을 남길 때 쓰는 1회성 요청 엔티티.
    /// </summary>
    public struct SpawnRequest : IComponentData
    {
        public PoolId Id;
        public LocalTransform Transform;
        public Entity Session;
    }
}