using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Minimal baked prefab for pooled drop cats.</summary>
    public sealed class DropCatPoolPrefabAuthoring : MonoBehaviour
    {
        private sealed class CatBaker : Baker<DropCatPoolPrefabAuthoring>
        {
            public override void Bake(DropCatPoolPrefabAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<Cat>(entity);
            }
        }
    }
}
