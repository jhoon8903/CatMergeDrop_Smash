using System;
using Framework.Poolable.Components;
using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable
{
    /// <summary>
    /// 풀 싱글톤을 값으로 꺼내 쓰고, 스코프가 끝나면 다시 써넣는 헬퍼.
    ///
    /// 풀 내부 상태는 전부 포인터(Unsafe 컨테이너) 뒤에 있으므로 복사본으로 조작해도 동일한 메모리를 다룬다.
    /// 다만 컨테이너 헤더(길이/용량/재할당된 포인터)는 복사본에만 반영되므로 반드시 write-back이 필요하다.
    /// RefRW를 들고 구조 변경을 하면 청크가 이동해 포인터가 무효화되므로 이 방식이 안전하다.
    ///
    /// 사용:
    /// using (var access = EntityPoolAccess.Open(state.EntityManager, poolEntity))
    /// {
    ///     Entity bullet = access.Pool.Rent(state.EntityManager, id, LocalTransform.FromPosition(0, 1, 0));
    /// }
    /// </summary>
    public struct EntityPoolAccess : IDisposable
    {
        private EntityManager _manager;
        private Entity _entity;
        public EntityPool Pool { get; private set; }

        public Entity Rent(EntityManager manager, PoolId id, in LocalTransform transform, Entity session)
        {
            EntityPool pool = Pool;
            Entity result = pool.Get(manager, id, transform, session);
            Pool = pool;
            return result;
        }

        public void Return(EntityManager manager, Entity instance)
        {
            EntityPool pool = Pool;
            pool.Return(manager, instance);
            Pool = pool;
        }

        public static EntityPoolAccess Open(EntityManager manager, Entity poolEntity)
        {
            return new EntityPoolAccess
            {
                _manager = manager,
                _entity = poolEntity,
                Pool = manager.GetComponentData<EntityPool>(poolEntity)
            };
        }
 
        /// <summary>풀 엔티티를 매번 질의한다. 핫 루프에서는 엔티티를 캐싱한 오버로드를 쓸 것.</summary>
        public static EntityPoolAccess Open(EntityManager manager)
        {
            EntityQuery query = manager.CreateEntityQuery(ComponentType.ReadWrite<EntityPool>());
            Entity poolEntity = query.GetSingletonEntity();
            query.Dispose();
            return Open(manager, poolEntity);
        }
 
        public void Dispose()
        {
            if (_entity == Entity.Null) return;
            _manager.SetComponentData(_entity, Pool);
        }
    }
}
