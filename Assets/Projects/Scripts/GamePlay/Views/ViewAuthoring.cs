using Unity.Entities;
using UnityEngine;

namespace Projects.Scripts.GamePlay.Views
{
    /// <summary>Shared lifecycle for authored world-space views backed by ECS state.</summary>
    public abstract class ViewAuthoring : MonoBehaviour
    {
        public abstract bool IsConfigured { get; }

        public virtual void Hide() => SetVisible(false);

        protected abstract void SetVisible(bool visible);
    }

    /// <summary>Typed rendering contract keeps each view tied to its ECS data shape.</summary>
    public abstract class ViewAuthoring<TState> : ViewAuthoring
        where TState : unmanaged, IComponentData
    {
        public void Render(in TState state, bool emphasized = false)
        {
            if (!IsConfigured) return;
            RenderConfigured(state, emphasized);
        }

        protected abstract void RenderConfigured(in TState state, bool emphasized);
    }

    public abstract class ViewBaker<T> : Baker<T> where T : ViewAuthoring
    {
        protected Entity CreateEntity(T authoring, TransformUsageFlags transformUsage = TransformUsageFlags.Dynamic)
        {
            if (authoring == null) return Entity.Null;
            Entity entity = GetEntity(authoring.gameObject, transformUsage);
            // Register the authored view as a companion component on its baked entity.
            AddComponentObject(entity, authoring);
            return entity;
        }
    }
}
