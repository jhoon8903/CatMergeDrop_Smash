using Framework.Poolable.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Framework.Poolable
{
    /// <summary>
    /// 풀 싱글톤의 소유자. 베이킹된 EntityPoolSO 설정을 읽어 풀을 만들고, 스폰/디스폰 요청을 처리한다.
    /// 시스템 struct에는 EntityQuery와 bool만 두므로 unmanaged 조건을 확실히 만족한다.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct EntityPoolSystem : ISystem
    {
        private EntityQuery _configQuery;
        private EntityQuery _poolQuery;
        private EntityQuery _spawnQuery;
        private EntityQuery _despawnQuery;
        private bool _initialized;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _configQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<EntityPoolConfig, PoolPrefabElement>().Build(ref state);
            _poolQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<EntityPool>().Build(ref state);
            _spawnQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<SpawnRequest>().Build(ref state);
            _despawnQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<DespawnRequest, PooledEntity>().Build(ref state);
            // RequireAnyForUpdate(params EntityQuery[])는 매니지드 배열을 받으므로 Burst에서 쓸 수 없다.
            state.RequireForUpdate(_configQuery);
            state.EntityManager.CreateSingleton(new EntityPool(8, Allocator.Persistent));
        }

        /// <summary>
        /// 엔티티 자체는 월드가 정리하므로 컨테이너만 해제한다.
        /// </summary>
        /// <param name="state"></param>
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            if (_poolQuery.IsEmpty) return;
            // 엔티티 자체는 월드가 정리하므로 컨테이너만 해제한다.
            EntityPool pool = _poolQuery.GetSingleton<EntityPool>();
            pool.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityManager mg = state.EntityManager;
            Entity poolEntity = _poolQuery.GetSingletonEntity();
            // 구조 변경이 청크를 이동시키므로 RefRW가 아니라 값 복사 → write-back으로 다룬다.
            EntityPool pool = mg.GetComponentData<EntityPool>(poolEntity);
            if (!_initialized)
            {
                BuildPools(mg, ref pool);
                _initialized = true;
            }
            ProcessDespawn(mg, ref pool); // 먼저 반납해야 같은 프레임에 재사용된다
            ProcessSpawn(mg, ref pool);
            mg.SetComponentData(poolEntity, pool);
        }
        private void BuildPools(EntityManager mg, ref EntityPool pool)
        {
            Entity config = _configQuery.GetSingletonEntity();
            // Prewarm이 구조 변경을 일으켜 DynamicBuffer가 무효화되므로 먼저 복사해 둔다.
            NativeArray<PoolPrefabElement> elements = mg.GetBuffer<PoolPrefabElement>(config).ToNativeArray(Allocator.Temp);
            for (int i = 0; i < elements.Length; i++)
            {
                PoolPrefabElement element = elements[i];
                if (element.Prefab == Entity.Null || !element.Id.IsValid) continue;
                pool.Register(mg, new PoolDefinition
                {
                    Id = element.Id,
                    Prefab = element.Prefab,
                    InitialCapacity = element.InitialCapacity,
                    MaxCapacity = element.MaxCapacity,
                    Name = element.Name
                });
                pool.Prewarm(mg, element.Id);
            }
            elements.Dispose();
        }

        private void ProcessDespawn(EntityManager mg, ref EntityPool pool)
        {
            if (_despawnQuery.IsEmpty) return;
            NativeArray<Entity> entities = _despawnQuery.ToEntityArray(Allocator.Temp);
            mg.RemoveComponent<DespawnRequest>(entities); // 배치 구조 변경
            for (int i = 0; i < entities.Length; i++) pool.Return(mg, entities[i]);
            entities.Dispose();
        }

        private void ProcessSpawn(EntityManager mg, ref EntityPool pool)
        {
            if (_spawnQuery.IsEmpty) return;
            NativeArray<Entity> requestEntities = _spawnQuery.ToEntityArray(Allocator.Temp);
            NativeArray<SpawnRequest> requests = _spawnQuery.ToComponentDataArray<SpawnRequest>(Allocator.Temp);
            for (int i = 0; i < requests.Length; i++)
            {
                SpawnRequest request = requests[i];
                pool.Get(mg, request.Id, request.Transform, request.Session);
            }
            mg.DestroyEntity(requestEntities);
            requestEntities.Dispose();
            requests.Dispose();
        }
    }
}