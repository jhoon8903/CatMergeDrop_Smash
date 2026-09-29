using Framework.Poolable.Components;
using Framework.Poolable.Interfaces;
using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable
{
    /// <summary>
    /// 베이킹된 엔티티 프리팹을 복제하는 기본 팩토리.
    /// 리셋 대상 컴포넌트를 늘리고 싶으면 Reset()에 추가하거나, 이 인터페이스를 구현한 struct를 새로 만들어
    /// EntityPool&lt;TFactory&gt;의 제네릭 인자만 바꾸면 된다.
    /// </summary>
    public readonly struct PrefabEntityFactory : IEntityFactory
    {
        public Entity Create(EntityManager mg, in PoolDefinition definition)
        {
            // PooledEntity는 프리팹 엔티티에 미리 붙여두므로 복제 시 함께 따라온다.
            return mg.Instantiate(definition.Prefab);
        }
 
        public void Initialize(EntityManager mg, Entity instance, in PoolDefinition definition, in LocalTransform transform, Entity session)
        {
            if (mg.HasComponent<LocalTransform>(instance)) mg.SetComponentData(instance, transform);
            PooledEntity pooled = new PooledEntity { Id = definition.Id, Session = session };
            if (mg.HasComponent<PooledEntity>(instance)) mg.SetComponentData(instance, pooled);
            else mg.AddComponentData(instance, pooled); // 프리팹 등록 경로를 거치지 않은 예외 상황 방어
            // SetEnabled는 LinkedEntityGroup을 따라 자식까지 처리한다. (ECB의 RemoveComponent<Disabled>는 루트만 처리)
            mg.SetEnabled(instance, true);
        }
 
        public void Reset(EntityManager mg, Entity instance, in PoolDefinition definition)
        {
            if (mg.HasComponent<LocalTransform>(instance) && mg.HasComponent<LocalTransform>(definition.Prefab)) mg.SetComponentData(instance, mg.GetComponentData<LocalTransform>(definition.Prefab));
            // 필요한 런타임 상태를 여기서 초기화한다. 예)
            // if (mg.HasComponent<Health>(instance))
            //     mg.SetComponentData(instance, mg.GetComponentData<Health>(definition.Prefab));
            // if (mg.HasBuffer<DamageEvent>(instance))
            //     mg.GetBuffer<DamageEvent>(instance).Clear();
        }
 
        public void Destroy(EntityManager mg, Entity instance) => mg.DestroyEntity(instance);
    }
}