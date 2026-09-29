using Unity.Entities;

namespace Framework.Notification
{
    /// <summary>
    /// 알림 표시를 요청하는 단발성 데이터.
    /// MessageCode는 로컬라이즈된 문구가 아닌 프로젝트별 카탈로그 식별자이며, 0은 무시된다.
    /// </summary>
    public struct NotificationRequest : IComponentData
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
