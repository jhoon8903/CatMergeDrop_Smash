using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Framework.Notification
{
    /// <summary>
    /// 요청을 검증해 메시지 이벤트로 변환한다.
    /// MessageCode가 0인 요청은 무시하며, Duration이 0 이하면 UI 기본 표시 시간을 사용한다.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct NotificationSystem : ISystem
    {
        private EntityQuery _requests;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _requests = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NotificationRequest>()
                .Build(ref state);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (_requests.IsEmpty) return;

            EntityManager manager = state.EntityManager;
            NativeArray<Entity> entities = _requests.ToEntityArray(Allocator.Temp);
            NativeArray<NotificationRequest> requests = _requests.ToComponentDataArray<NotificationRequest>(Allocator.Temp);

            for (int i = 0; i < requests.Length; i++)
            {
                NotificationRequest request = requests[i];
                if (request.MessageCode == 0) continue;

                Entity message = manager.CreateEntity();
                manager.AddComponentData(message, new NotificationMessage
                {
                    MessageCode = request.MessageCode,
                    Kind = request.Kind,
                    Presentation = request.Presentation,
                    Duration = request.Duration,
                    IntArgument = request.IntArgument,
                    FloatArgument = request.FloatArgument,
                    Source = request.Source
                });
            }

            manager.DestroyEntity(entities);
            entities.Dispose();
            requests.Dispose();
        }
    }
}
