using Unity.Entities;

namespace Framework.Notification
{
    /// <summary>
    /// ECB에서 알림을 요청하는 편의 API.
    /// </summary>
    public static class NotificationRequestExtensions
    {
        public static void Notify(this EntityCommandBuffer ecb, int messageCode, NotificationKind kind = NotificationKind.Info,
            NotificationPresentation presentation = NotificationPresentation.Toast, float duration = 0f,
            int intArgument = 0, float floatArgument = 0f, Entity source = default)
        {
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new NotificationRequest
            {
                MessageCode = messageCode,
                Kind = kind,
                Presentation = presentation,
                Duration = duration,
                IntArgument = intArgument,
                FloatArgument = floatArgument,
                Source = source
            });
        }

        public static void Notify(this EntityCommandBuffer.ParallelWriter ecb, int sortKey, int messageCode,
            NotificationKind kind = NotificationKind.Info, NotificationPresentation presentation = NotificationPresentation.Toast,
            float duration = 0f, int intArgument = 0, float floatArgument = 0f, Entity source = default)
        {
            Entity request = ecb.CreateEntity(sortKey);
            ecb.AddComponent(sortKey, request, new NotificationRequest
            {
                MessageCode = messageCode,
                Kind = kind,
                Presentation = presentation,
                Duration = duration,
                IntArgument = intArgument,
                FloatArgument = floatArgument,
                Source = source
            });
        }
    }
}
