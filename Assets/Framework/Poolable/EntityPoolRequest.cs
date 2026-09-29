using Framework.Poolable.Components;
using Unity.Entities;
using Unity.Transforms;

namespace Framework.Poolable
{
    /// <summary>
    /// Job / IJobEntity 안에서도 안전하게 쓸 수 있는 요청 API.
    /// ECB가 만든 임시 엔티티를 풀에 직접 넣지 않고, 요청만 남겨 EntityPoolSystem이 처리하게 한다.
    /// </summary>
    public static class EntityPoolRequest
    {
        public static void Spawn(this EntityCommandBuffer ecb, PoolId id, in LocalTransform transform, Entity session = default)
        {
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new SpawnRequest
            {
                Id = id, 
                Transform = transform, 
                Session = session
            });
        }
 
        public static void Spawn(this EntityCommandBuffer.ParallelWriter ecb, int sortKey, PoolId id, in LocalTransform transform, Entity session = default)
        {
            Entity request = ecb.CreateEntity(sortKey);
            ecb.AddComponent(sortKey, request, new SpawnRequest
            {
                Id = id, 
                Transform = transform, 
                Session = session
            });
        }
 
        public static void Despawn(this EntityCommandBuffer ecb, Entity instance)
        {
            if (instance == Entity.Null) return;
            ecb.AddComponent<DespawnRequest>(instance);
        }
 
        public static void Despawn(this EntityCommandBuffer.ParallelWriter ecb, int sortKey, Entity instance)
        {
            if (instance == Entity.Null) return;
            ecb.AddComponent<DespawnRequest>(sortKey, instance);
        }
    }
}