using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// 이전 Simulation 프레임에서 발행된 세션 이벤트를 제거한다.
    /// 구독 시스템은 GameSessionSystem 뒤의 Simulation 또는 Presentation 단계에서 읽어야 한다.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct SessionTransitionEventCleanupSystem : ISystem
    {
        private EntityQuery _events;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _events = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SessionTransitioned>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!_events.IsEmpty) state.EntityManager.DestroyEntity(_events);
        }
    }
}
