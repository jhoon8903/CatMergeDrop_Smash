using Framework.Poolable.Components;
using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable.Interfaces
{
    /// <summary>
    /// 엔티티 풀 계약.
    /// 구현체는 struct이며 제네릭 제약(where T : unmanaged, IEntityPool)으로 사용해야 박싱 없이 Burst에서 동작한다.
    /// 모든 구조 변경은 메인 스레드 EntityManager로 처리한다(ECB의 임시 엔티티는 풀에 보관할 수 없기 때문).
    /// </summary>
    public interface IEntityPool
    {
        /// <summary>
        /// 풀 정의를 등록한다.
        /// 프리팹 엔티티에 PooledEntity를 부착하는 것까지 수행.
        /// </summary>
        void Register(EntityManager mg, in PoolDefinition definition);
 
        /// <summary>
        /// 미리 인스턴스를 만들어 비활성 상태로 적재한다.
        /// count가 음수면 InitialCapacity를 사용.
        /// </summary>
        int Prewarm(EntityManager mg, PoolId id, int count = -1);
 
        /// <summary>
        /// 대기 중인 인스턴스를 꺼내거나,
        /// 없으면 새로 만들어 활성화한다.
        /// </summary>
        Entity Get(EntityManager mg, PoolId id, in LocalTransform transform, Entity session = default);
 
        /// <summary>
        /// 인스턴스를 비활성화하고 풀로 되돌린다.
        /// 상한을 넘으면 파괴한다.
        /// </summary>
        void Return(EntityManager mg, Entity instance);
 
        /// <summary>
        /// 해당 세션으로 빌려간 모든 인스턴스를 한 번에 반납한다.
        /// </summary>
        void ReturnSession(EntityManager mg, Entity session);
 
        /// <summary>
        /// 세션 추적만 끊는다(인스턴스는 반납하지 않음).
        /// </summary>
        void ForgetSession(Entity session);
 
        /// <summary>
        /// 인스턴스를 프리팹 원본 상태로 되돌린다.
        /// </summary>
        void Restore(EntityManager mg, Entity instance);
 
        /// <summary>
        /// 대기 중인 인스턴스를 모두 파괴해 메모리를 회수한다(활성 인스턴스는 유지).
        /// </summary>
        void Release(EntityManager mg, PoolId id);
 
        /// <summary>
        /// 모든 풀을 비운다.
        /// </summary>
        void Clear(EntityManager mg);
    }
}
