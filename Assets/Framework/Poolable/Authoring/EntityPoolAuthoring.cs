using Framework.Poolable.Components;
using Framework.Poolable.Scriptable;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Framework.Poolable.Authoring
{
    /// <summary>
    /// 서브씬에 하나 배치한다. EntityPoolSO의 GameObject 프리팹들을 엔티티 프리팹으로 베이킹해
    /// PoolPrefabElement 버퍼 싱글톤으로 만든다.
    /// </summary>
    public class EntityPoolAuthoring : MonoBehaviour
    {
        [SerializeField] private EntityPoolContainer poolSet;
 
        public EntityPoolContainer PoolSet => poolSet;
 
        private class EntityPoolBaker : Baker<EntityPoolAuthoring>
        {
            public override void Bake(EntityPoolAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent<EntityPoolConfig>(entity);
                DynamicBuffer<PoolPrefabElement> buffer = AddBuffer<PoolPrefabElement>(entity);
                EntityPoolContainer poolSet = authoring.PoolSet;
                if (poolSet == null) return;
                // SO를 수정하면 다시 베이킹되도록 의존성을 등록한다.
                DependsOn(poolSet);

                for (int i = 0; i < poolSet.Entries.Count; i++)
                {
                    EntityEntry entry = poolSet.Entries[i];
                    if (entry is not { IsValid: true }) continue;
                    buffer.Add(new PoolPrefabElement
                    {
                        Id = PoolId.FromName(entry.Id),
                        Prefab = GetEntity(entry.Prefab, TransformUsageFlags.Dynamic),
                        InitialCapacity = entry.InitialCapacity,
                        MaxCapacity = entry.MaxCapacity,
                        Name = new FixedString64Bytes(entry.Id)
                    });
                }
            }
        }
    }
}