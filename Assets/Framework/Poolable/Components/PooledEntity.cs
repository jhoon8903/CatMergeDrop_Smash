using Unity.Entities;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 풀이 관리하는 모든 인스턴스에 붙는 컴포넌트.
    /// 프리팹 엔티티에 미리 달아두므로 Instantiate 시 자동 복사되고,
    /// 대여/반납 때 구조 변경이 발생하지 않는다.
    /// </summary>
    public struct PooledEntity : IComponentData
    {
        public PoolId Id;
 
        /// <summary>
        /// 이 인스턴스를 빌린 세션(웨이브, 스테이지 등).
        /// 없으면 Entity.Null.
        /// </summary>
        public Entity Session;
    }
}