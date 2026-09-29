using Unity.Collections;
using Unity.Entities;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 풀 하나의 정의.
    /// SO → Baker → 런타임으로 전달되는 값 덩어리.
    /// </summary>
    public struct PoolDefinition
    {
        public PoolId Id;
        public Entity Prefab;
 
        /// <summary>
        /// 미리 만들어 둘 개수.
        /// </summary>
        public int InitialCapacity;
 
        /// <summary>
        /// 풀이 보유할 수 있는 최대 인스턴스 수(활성 + 대기).
        /// 0이면 무제한.
        /// </summary>
        public int MaxCapacity;
 
        public FixedString64Bytes Name;
    }
}