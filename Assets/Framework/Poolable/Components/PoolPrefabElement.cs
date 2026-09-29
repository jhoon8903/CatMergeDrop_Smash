using Unity.Collections;
using Unity.Entities;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 베이킹된 프리팹 목록.
    /// EntityPoolSO의 각 엔트리 하나에 대응한다.
    /// </summary>
    [InternalBufferCapacity(8)]
    public struct PoolPrefabElement : IBufferElementData
    {
        public PoolId Id;
        public Entity Prefab;
        public int InitialCapacity;
        public int MaxCapacity;
        public FixedString64Bytes Name;
    }
}