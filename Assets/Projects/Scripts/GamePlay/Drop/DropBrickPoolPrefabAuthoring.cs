using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>Minimal baked prefab for pooled drop bricks.</summary>
    public sealed class DropBrickPoolPrefabAuthoring : MonoBehaviour
    {
        private sealed class BrickBaker : Baker<DropBrickPoolPrefabAuthoring>
        {
            public override void Bake(DropBrickPoolPrefabAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<Brick>(entity);
            }
        }
    }
}
