using System;
using Unity.Collections;

namespace Framework.Poolable.Components
{
    /// <summary>
    /// 풀 식별자.
    /// 문자열을 해시해 int로 보관하므로 네이티브 컨테이너의 키로 쓸 수 있고 Burst에서도 안전하다.
    /// </summary>
    public readonly struct PoolId : IEquatable<PoolId>
    {
        public readonly int Value;
 
        public PoolId(int value) => Value = value;
 
        public bool IsValid => Value != 0;
 
        /// <summary>
        /// 런타임(Burst 가능) 변환.
        /// </summary>
        public static PoolId FromName(in FixedString64Bytes name) => new(name.GetHashCode());
 
        /// <summary>
        /// 에디터/베이킹용 변환.
        /// 이름은 UTF8 기준 61바이트를 넘기면 안 된다.
        /// </summary>
        public static PoolId FromName(string name) => string.IsNullOrEmpty(name) ? default : FromName(new FixedString64Bytes(name));
 
        public bool Equals(PoolId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PoolId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(PoolId a, PoolId b) => a.Value == b.Value;
        public static bool operator !=(PoolId a, PoolId b) => a.Value != b.Value;
        public override string ToString() => $"PoolId({Value})";
    }
}