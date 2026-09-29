using System.Collections.Generic;

namespace Framework.UI.Components
{
    /// <summary>
    /// 같은 프레임에 발생하는 중복 UI 입력을 막는 전역 잠금.
    /// </summary>
    public static class UIInputLock
    {
        private static readonly HashSet<string> Keys = new();

        public static bool TryAcquire(string key) => !string.IsNullOrWhiteSpace(key) && Keys.Add(key);
        public static void Release(string key) => Keys.Remove(key);
        public static void Clear() => Keys.Clear();
    }
}
