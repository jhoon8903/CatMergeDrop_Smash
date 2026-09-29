using System;
using Framework.Poolable.Interfaces;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 프리팹별로 분리된 엔티티 풀.
    ///
    /// [unmanaged 보장 규칙]
    /// - 제네릭 타입 파라미터를 필드로 갖지 않는다. 구성된 제네릭 struct는 컴파일러/Burst 버전에 따라
    ///   managed로 판정되는 경우가 있어, ISystem 필드나 IComponentData로 쓸 수 없게 된다.
    /// - 필드에는 Native* 대신 Unsafe* 컨테이너만 쓴다. Unsafe 컨테이너는 AtomicSafetyHandle /
    ///   DisposeSentinel 같은 버전별 부가 필드가 없어 어떤 Collections 버전에서도 확실히 unmanaged다.
    /// - 팩토리는 필드가 아니라 제네릭 메서드 인자로 받는다. unmanaged 제약이라 박싱도 가상 호출도 없다.
    ///
    /// 싱글톤 IComponentData로 보관하지만, 구조 변경(Instantiate/SetEnabled)은 청크 메모리를 이동시키므로
    /// RefRW를 들고 있으면 안 된다. 값으로 꺼내 쓰고 끝나면 다시 써넣는다(EntityPoolAccess 참고).
    /// </summary>
    public struct EntityPool : IComponentData, IEntityPool, IDisposable
    {
        private struct Bucket
        {
            public PoolDefinition Def;
            public UnsafeList<Entity> Free; // 대기 중인 인스턴스
            public int ActiveCount;
            public int TotalCount; // 활성 + 대기
        }

        private UnsafeHashMap<PoolId, Bucket> _buckets;
        private UnsafeHashMap<Entity, UnsafeList<Entity>> _sessions;
        private readonly AllocatorManager.AllocatorHandle _allocator;

        public bool IsCreated => _buckets.IsCreated;
        public int PoolCount => _buckets.IsCreated ? _buckets.Count : 0;

        public EntityPool(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
            _allocator = allocator;
            _buckets = new UnsafeHashMap<PoolId, Bucket>(math.max(1, capacity), allocator);
            _sessions = new UnsafeHashMap<Entity, UnsafeList<Entity>>(math.max(1, capacity), allocator);
        }

        #region Register, Prewarm

        public void Register(EntityManager mg, in PoolDefinition definition)
        {
            if (definition.Prefab == Entity.Null || !mg.Exists(definition.Prefab)) return;
            if (_buckets.ContainsKey(definition.Id)) return; // 중복 등록 무시
 
            // 프리팹에 PooledEntity를 달아두면 모든 복제본이 자동으로 소속 풀을 갖는다.
            PooledEntity tag = new PooledEntity
            {
                Id = definition.Id, 
                Session = Entity.Null
            };
            if (mg.HasComponent<PooledEntity>(definition.Prefab)) mg.SetComponentData(definition.Prefab, tag);
            else mg.AddComponentData(definition.Prefab, tag);
 
            _buckets.Add(definition.Id, new Bucket
            {
                Def = definition,
                Free = new UnsafeList<Entity>(math.max(1, definition.InitialCapacity), _allocator),
                ActiveCount = 0,
                TotalCount = 0
            });
        }

        public int Prewarm(EntityManager mg, PoolId id, int count = -1)
        {
            if (!_buckets.TryGetValue(id, out Bucket bucket)) return 0;
            if (count < 0) count = bucket.Def.InitialCapacity;
            if (bucket.Def.MaxCapacity > 0) count = math.min(count, bucket.Def.MaxCapacity - bucket.TotalCount);
            if (count <= 0) return 0;
 
            NativeArray<Entity> instances = new NativeArray<Entity>(count, Allocator.Temp);
            mg.Instantiate(bucket.Def.Prefab, instances); // 배치 생성
 
            for (int i = 0; i < count; i++)
            {
                mg.SetEnabled(instances[i], false); // 자식(LinkedEntityGroup)까지 비활성화
                bucket.Free.Add(instances[i]);
            }
 
            bucket.TotalCount += count;
            _buckets[id] = bucket; // UnsafeList는 재할당될 수 있으므로 반드시 다시 써준다
            instances.Dispose();
            return count;
        }

        #endregion

        #region Get, Return

        public Entity Get<TFactory>(EntityManager mg, TFactory factory, PoolId id, in LocalTransform transform, Entity session) where TFactory : unmanaged, IEntityFactory
        {
            if (!_buckets.TryGetValue(id, out Bucket bucket)) return Entity.Null; // 등록되지 않은 풀
 
            Entity instance = Entity.Null;
 
            // 외부에서 파괴된 엔티티가 섞여 있을 수 있으므로 유효한 것이 나올 때까지 꺼낸다.
            while (bucket.Free.Length > 0)
            {
                int last = bucket.Free.Length - 1;
                Entity candidate = bucket.Free[last];
                bucket.Free.RemoveAt(last);
 
                if (mg.Exists(candidate))
                {
                    instance = candidate;
                    break;
                }
 
                bucket.TotalCount = math.max(0, bucket.TotalCount - 1);
            }
 
            if (instance == Entity.Null)
            {
                if (bucket.Def.MaxCapacity > 0 && bucket.TotalCount >= bucket.Def.MaxCapacity)
                {
                    _buckets[id] = bucket;
                    return Entity.Null; // 상한 도달: 호출부에서 실패를 처리하게 한다
                }
 
                instance = factory.Create(mg, bucket.Def);
                bucket.TotalCount++;
            }
            else
            {
                factory.Reset(mg, instance, bucket.Def); // 재사용 전 원본 상태 복구
            }
 
            factory.Initialize(mg, instance, bucket.Def, transform, session);
 
            bucket.ActiveCount++;
            _buckets[id] = bucket;
 
            TrackSession(session, instance);
            return instance;
        }

        public void Return<TFactory>(EntityManager mg, TFactory factory, Entity instance)
            where TFactory : unmanaged, IEntityFactory
        {
            if (instance == Entity.Null || !mg.Exists(instance)) return;
            if (!mg.HasComponent<PooledEntity>(instance))
            {
                mg.DestroyEntity(instance); // 풀 소속이 아닌 엔티티
                return;
            }
 
            PooledEntity pooled = mg.GetComponentData<PooledEntity>(instance);
            UntrackSession(pooled.Session, instance);
 
            if (!_buckets.TryGetValue(pooled.Id, out Bucket bucket))
            {
                mg.DestroyEntity(instance); // 정의가 사라진 풀
                return;
            }
 
            bucket.ActiveCount = math.max(0, bucket.ActiveCount - 1);
 
            bool overflow = bucket.Def.MaxCapacity > 0 && bucket.Free.Length >= bucket.Def.MaxCapacity;
            if (overflow)
            {
                factory.Destroy(mg, instance);
                bucket.TotalCount = math.max(0, bucket.TotalCount - 1);
            }
            else
            {
                mg.SetComponentData(instance, new PooledEntity { Id = pooled.Id, Session = Entity.Null });
                mg.SetEnabled(instance, false);
                bucket.Free.Add(instance);
            }
 
            _buckets[pooled.Id] = bucket;
        }
        public void Restore<TFactory>(EntityManager mg, TFactory factory, Entity instance) where TFactory : unmanaged, IEntityFactory
        {
            if (instance == Entity.Null || !mg.Exists(instance)) return;
            if (!mg.HasComponent<PooledEntity>(instance)) return;
            PoolId id = mg.GetComponentData<PooledEntity>(instance).Id;
            if (_buckets.TryGetValue(id, out Bucket bucket)) factory.Reset(mg, instance, bucket.Def);
        }

        #endregion

        #region Session

        public void ReturnSession<TFactory>(EntityManager mg, TFactory factory, Entity session) where TFactory : unmanaged, IEntityFactory
        {
            if (session == Entity.Null || !_sessions.TryGetValue(session, out UnsafeList<Entity> list)) return;
            _sessions.Remove(session); // Return()이 다시 건드리지 않도록 먼저 끊는다
            for (int i = 0; i < list.Length; i++) Return(mg, factory, list[i]);
            list.Dispose();
        }

        public void ForgetSession(Entity session)
        {
            if (session == Entity.Null) return;
            if (!_sessions.TryGetValue(session, out UnsafeList<Entity> list)) return;
            _sessions.Remove(session);
            list.Dispose();
        }
        
        private void TrackSession(Entity session, Entity instance)
        {
            if (session == Entity.Null) return;
            if (!_sessions.TryGetValue(session, out UnsafeList<Entity> list)) list = new UnsafeList<Entity>(8, _allocator);
            list.Add(instance);
            _sessions[session] = list; // 재할당된 포인터를 다시 기록
        }
        
        private void UntrackSession(Entity session, Entity instance)
        {
            if (session == Entity.Null) return;
            if (!_sessions.TryGetValue(session, out UnsafeList<Entity> list)) return;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != instance) continue;
                list.RemoveAtSwapBack(i);
                break;
            }
            _sessions[session] = list;
        }

        #endregion

        #region Release

        public void Release<TFactory>(EntityManager mg, TFactory factory, PoolId id) where TFactory : unmanaged, IEntityFactory
        {
            if (!_buckets.TryGetValue(id, out Bucket bucket)) return;
            for (int i = 0; i < bucket.Free.Length; i++)
            {
                Entity instance = bucket.Free[i];
                if (mg.Exists(instance)) factory.Destroy(mg, instance);
            }
            bucket.TotalCount = math.max(0, bucket.TotalCount - bucket.Free.Length);
            bucket.Free.Clear();
            _buckets[id] = bucket;
        }

        public void Clear<TFactory>(EntityManager mg, TFactory factory) where TFactory : unmanaged, IEntityFactory
        {
            if (!_buckets.IsCreated) return;
            // 순회 중 수정하면 안 되므로 키를 먼저 모은다.
            NativeList<PoolId> ids = new NativeList<PoolId>(_buckets.Count, Allocator.Temp);
            foreach (var pair in _buckets) ids.Add(pair.Key);
            for (int i = 0; i < ids.Length; i++) Release(mg, factory, ids[i]);
            ids.Dispose();
            DisposeSessions();
        }
        
        private void DisposeSessions()
        {
            if (!_sessions.IsCreated) return;
            foreach (KVPair<Entity, UnsafeList<Entity>> pair in _sessions)
            {
                UnsafeList<Entity> list = pair.Value;
                if (list.IsCreated) list.Dispose();
            }
            _sessions.Clear();
        }

        public void Dispose()
        {
            if (_buckets.IsCreated)
            {
                foreach (KVPair<PoolId, Bucket> pair in _buckets)
                {
                    UnsafeList<Entity> free = pair.Value.Free;
                    if (free.IsCreated) free.Dispose();
                }
 
                _buckets.Dispose();
            }
            if (!_sessions.IsCreated) return;
            DisposeSessions();
            _sessions.Dispose();
        }
            
        #endregion

        #region Override

        public Entity Get(EntityManager mg, PoolId id, in LocalTransform transform, Entity session = default) => Get(mg, default(PrefabEntityFactory), id, transform, session);
 
        public void Return(EntityManager mg, Entity instance) => Return(mg, default(PrefabEntityFactory), instance);
 
        public void ReturnSession(EntityManager mg, Entity session) => ReturnSession(mg, default(PrefabEntityFactory), session);
 
        public void Restore(EntityManager mg, Entity instance) => Restore(mg, default(PrefabEntityFactory), instance);
 
        public void Release(EntityManager mg, PoolId id) => Release(mg, default(PrefabEntityFactory), id);
 
        public void Clear(EntityManager mg) => Clear(mg, default(PrefabEntityFactory));

        #endregion

        #region Diagnosis

        public bool TryGetStats(PoolId id, out int free, out int active, out int total)
        {
            free = active = total = 0;
            if (!_buckets.TryGetValue(id, out Bucket bucket)) return false;
            free = bucket.Free.Length;
            active = bucket.ActiveCount;
            total = bucket.TotalCount;
            return true;
        }

        #endregion
    }
}