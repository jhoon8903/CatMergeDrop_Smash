using Framework.Poolable.Components;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Framework.Poolable._Example
{
    /// <summary>
    /// 사용 예시 3가지.
    /// 실제 프로젝트에서는 패턴만 가져가고 이 파일은 지워도 된다.
    /// </summary>
    public struct BulletSpawner : IComponentData
    {
        public float Interval;
        public float Timer;
        public Entity Session; // 이 스포너가 만든 탄환들을 한 번에 회수할 때 사용
    }

    public struct Lifetime : IComponentData
    {
        public float Remaining;
    }

    /// <summary>
    /// [1] Job/ECB에서 요청만 남기는 방식 (권장, 병렬 가능).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct BulletSpawnSystem : ISystem
    {
        private PoolId _bulletId;

        private struct RefStruct
        {
            
        }

        public void OnCreate(ref SystemState state)
        {
            _bulletId = PoolId.FromName(new FixedString64Bytes("Bullet"));
            state.RequireForUpdate<BulletSpawner>();
            state.RequireForUpdate<EntityPoolConfig>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach ((RefRW<BulletSpawner> spawner, RefRO<LocalTransform> transform) in SystemAPI.Query<RefRW<BulletSpawner>, RefRO<LocalTransform>>())
            {
                spawner.ValueRW.Timer -= dt;
                if (spawner.ValueRO.Timer > 0f) continue;
                spawner.ValueRW.Timer = spawner.ValueRO.Interval;
                LocalTransform spawnAt = LocalTransform.FromPositionRotation(transform.ValueRO.Position + math.forward(transform.ValueRO.Rotation), transform.ValueRO.Rotation);
                ecb.Spawn(_bulletId, spawnAt, spawner.ValueRO.Session);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// [2] 수명이 끝난 인스턴스에 DespawnRequest를 붙여 반납.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct LifetimeSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach ((RefRW<Lifetime> lifetime, Entity entity)in SystemAPI.Query<RefRW<Lifetime>>().WithAll<PooledEntity>().WithEntityAccess())
            {
                lifetime.ValueRW.Remaining -= dt;
                if (lifetime.ValueRO.Remaining <= 0f) ecb.Despawn(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// [3] 메인 스레드에서 풀을 직접 다루는 방식 (즉시 Entity 핸들이 필요할 때).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EntityPoolSystem))]
    public partial struct DirectPoolAccessExample : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<EntityPool>();

        public void OnUpdate(ref SystemState state)
        {
            Entity poolEntity = SystemAPI.GetSingletonEntity<EntityPool>();
            PoolId id = PoolId.FromName(new FixedString64Bytes("Bullet"));
            // using 블록이 끝날 때 변경된 풀 상태가 자동으로 write-back 된다.
            using (var access = EntityPoolAccess.Open(state.EntityManager, poolEntity))
            {
                Entity instance = access.Pool.Get(state.EntityManager, id, LocalTransform.FromPosition(0, 1, 0));
                if (instance != Entity.Null) state.EntityManager.SetComponentData(instance, new Lifetime { Remaining = 3f });
                // 웨이브 종료 시 한 번에 회수: access.Pool.ReturnSession(state.EntityManager, sessionEntity);
                // 대기 인스턴스 메모리 회수: access.Pool.Release(state.EntityManager, id);
                // 커스텀 팩토리 사용:      access.Pool.Rent(state.EntityManager, new MyFactory(), id, tr, default);
            }
            state.Enabled = false; // 예시이므로 1회만 실행
        }
    }
}
