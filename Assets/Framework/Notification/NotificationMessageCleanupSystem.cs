using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Framework.Notification
{
    /// <summary>이전 프레임의 메시지 이벤트를 다음 프레임 시작에 제거한다.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct NotificationMessageCleanupSystem : ISystem
    {
        private EntityQuery _messages;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _messages = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NotificationMessage>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (!_messages.IsEmpty) state.EntityManager.DestroyEntity(_messages);
        }
    }
}
