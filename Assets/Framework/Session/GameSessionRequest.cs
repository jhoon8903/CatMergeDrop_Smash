using Unity.Entities;

namespace Framework.Session
{
    /// <summary>
    /// ECB에서 세션 전환을 요청하는 편의 API.
    /// </summary>
    public static class GameSessionRequest
    {
        public static void RequestTransition(this EntityCommandBuffer ecb, SessionTransition transition)
            => ecb.RequestTransition(transition, 0);

        public static void RequestTransition(this EntityCommandBuffer ecb, SessionTransition transition, byte reasonCode)
        {
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new SessionTransitionRequest
            {
                Transition = transition,
                ReasonCode = reasonCode
            });
        }

        public static void RequestTransition(this EntityCommandBuffer.ParallelWriter ecb, int sortKey, SessionTransition transition)
            => ecb.RequestTransition(sortKey, transition, 0);

        public static void RequestTransition(this EntityCommandBuffer.ParallelWriter ecb, int sortKey, SessionTransition transition, byte reasonCode)
        {
            Entity request = ecb.CreateEntity(sortKey);
            ecb.AddComponent(sortKey, request, new SessionTransitionRequest
            {
                Transition = transition,
                ReasonCode = reasonCode
            });
        }
    }
}
