using Unity.Entities;

namespace Framework.Notification
{
    /// <summary>
    /// 유효한 NotificationRequest에서 발행되는 한 프레임짜리 메시지 이벤트.
    /// UI·사운드·분석 어댑터는 이 이벤트를 읽고 각자 처리한다.
    /// </summary>
    public struct NotificationMessage : IComponentData
    {
        public int MessageCode;
        public NotificationKind Kind;
        public NotificationPresentation Presentation;
        public float Duration;
        public int IntArgument;
        public float FloatArgument;
        public Entity Source;
    }
}
