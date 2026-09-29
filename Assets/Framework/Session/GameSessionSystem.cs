using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// 게임 규칙과 무관한 단일 세션의 수명주기를 관리한다.
    /// 유효하지 않은 상태 전환은 무시하며, 성공한 전환만 SessionTransitioned 이벤트로 발행한다.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [BurstCompile]
    public partial struct GameSessionSystem : ISystem
    {
        private EntityQuery _sessionQuery;
        private EntityQuery _requestQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _sessionQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<GameSession>()
                .WithAllRW<SessionStateChanged>()
                .Build(ref state);
            _requestQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SessionTransitionRequest>()
                .Build(ref state);

            Entity session = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(session, new GameSession(GamePhase.Idle, 0f));
            state.EntityManager.AddComponentData(session, new SessionStateChanged
            {
                Previous = GamePhase.Idle,
                Current = GamePhase.Idle,
                RunId = 0
            });
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            EntityManager manager = state.EntityManager;
            Entity sessionEntity = _sessionQuery.GetSingletonEntity();
            GameSession session = manager.GetComponentData<GameSession>(sessionEntity);
            bool wasRunning = session.Phase == GamePhase.Running;
            bool transitioned = false;
            GamePhase lastPreviousPhase = session.Phase;

            if (!_requestQuery.IsEmpty)
            {
                NativeArray<Entity> requestEntities = _requestQuery.ToEntityArray(Allocator.Temp);
                NativeArray<SessionTransitionRequest> requests = _requestQuery.ToComponentDataArray<SessionTransitionRequest>(Allocator.Temp);

                for (int i = 0; i < requests.Length; i++)
                {
                    GamePhase previousPhase = session.Phase;
                    if (!ApplyTransition(ref session, requests[i].Transition)) continue;

                    transitioned = true;
                    lastPreviousPhase = previousPhase;
                    EmitTransitionEvent(manager, previousPhase, session, requests[i].ReasonCode);
                }

                manager.DestroyEntity(requestEntities);
                requestEntities.Dispose();
                requests.Dispose();
            }

            // 전환이 발생한 프레임은 시간을 더하지 않는다.
            // 시작/재개 직후의 경과 시간은 항상 0부터 측정된다.
            if (wasRunning && session.Phase == GamePhase.Running) session.ElapsedTime += SystemAPI.Time.DeltaTime;

            if (transitioned)
            {
                manager.SetComponentData(sessionEntity, new SessionStateChanged
                {
                    Previous = lastPreviousPhase,
                    Current = session.Phase,
                    RunId = session.RunId
                });
            }

            manager.SetComponentData(sessionEntity, session);
        }

        private static bool ApplyTransition(ref GameSession session, SessionTransition transition)
        {
            switch (transition)
            {
                case SessionTransition.Start when session.Phase is GamePhase.Idle or GamePhase.Ended:
                    session.Phase = GamePhase.Running;
                    session.ElapsedTime = 0f;
                    session.RunId++;
                    return true;
                case SessionTransition.Pause when session.Phase == GamePhase.Running:
                    session.Phase = GamePhase.Paused;
                    return true;
                case SessionTransition.Resume when session.Phase == GamePhase.Paused:
                    session.Phase = GamePhase.Running;
                    return true;
                case SessionTransition.End when session.Phase is GamePhase.Running or GamePhase.Paused:
                    session.Phase = GamePhase.Ended;
                    return true;
                case SessionTransition.Reset when session.Phase == GamePhase.Ended:
                    session.Phase = GamePhase.Idle;
                    session.ElapsedTime = 0f;
                    return true;
                default:
                    return false;
            }
        }

        private static void EmitTransitionEvent(EntityManager manager, GamePhase previous, in GameSession session, byte reasonCode)
        {
            Entity entity = manager.CreateEntity();
            manager.AddComponentData(entity, new SessionTransitioned
            {
                Previous = previous,
                Current = session.Phase,
                RunId = session.RunId,
                ReasonCode = reasonCode
            });
        }
    }
}
