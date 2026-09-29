using Framework.Poolable.Components;
using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable.Interfaces
{
    /// <summary>
    /// 엔티티의 "생성 / 초기화 / 리셋 / 파괴" 정책을 캡슐화하는 팩토리.
    /// 풀은 재사용만 책임지고, 무엇을 어떻게 만들지는 전부 여기로 위임한다.
    ///
    /// 구현체는 stateless한 readonly struct로 만든다. 풀이 팩토리를 값으로 전달하므로
    /// 팩토리 내부 필드를 수정해도 호출자에게 반영되지 않는다. 상태가 필요하면 컴포넌트로 들고 있을 것.
    /// </summary>
    public interface IEntityFactory
    {
        /// <summary>
        /// 새 인스턴스를 만든다(비활성 상태로 반환할 필요는 없다).
        /// </summary>
        Entity Create(EntityManager mg, in PoolDefinition definition);
 
        /// <summary>
        /// 대여 직전 초기화.
        /// 위치 세팅과 활성화를 담당한다.
        /// </summary>
        void Initialize(EntityManager mg, Entity instance, in PoolDefinition definition, in LocalTransform transform, Entity session);
 
        /// <summary>
        /// 재사용 전 프리팹 원본 상태로 되돌린다.
        /// </summary>
        void Reset(EntityManager mg, Entity instance, in PoolDefinition definition);
 
        /// <summary>
        /// 인스턴스를 완전히 파괴한다.
        /// </summary>
        void Destroy(EntityManager mg, Entity instance);
    }
}