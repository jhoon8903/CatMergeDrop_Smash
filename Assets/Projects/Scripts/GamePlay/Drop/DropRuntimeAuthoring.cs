using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Board;
using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Drop
{
    /// <summary>SubScene-baked gameplay contract. Presentation stays in GameScene.</summary>
    public struct DropRuntimeBaked : IComponentData
    {
        public int BrickCapacity;
        public int CatCapacity;
    }

    public sealed class DropRuntimeAuthoring : MonoBehaviour
    {
        private sealed class DropRuntimeBaker : Baker<DropRuntimeAuthoring>
        {
            public override void Bake(DropRuntimeAuthoring authoring)
            {
                Entity entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new DropRuntimeBaked
                {
                    BrickCapacity = WorldPlayfieldLayout.BrickCount,
                    CatCapacity = MergeBoardModel.SlotCount
                });
            }
        }
    }
}
