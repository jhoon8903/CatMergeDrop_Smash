using System;
using UnityEngine;

namespace Framework.Poolable
{
    [Serializable]
    public class EntityEntry
    {
        [Tooltip("풀 식별자. 비워두면 프리팹 이름을 사용한다. (UTF8 기준 61바이트 이하)")]
        [SerializeField] private string id;
 
        [SerializeField] private GameObject prefab;
 
        [Tooltip("시작 시 미리 생성해 둘 개수")]
        [SerializeField, Min(0)] private int initialCapacity = 10;
 
        [Tooltip("보유 가능한 최대 인스턴스 수(활성 + 대기). 0이면 무제한")]
        [SerializeField, Min(0)] private int maxCapacity = 100;
 
        public string Id => string.IsNullOrWhiteSpace(id) ? (prefab != null ? prefab.name : string.Empty) : id;
        public GameObject Prefab => prefab;
        public int InitialCapacity => initialCapacity;
        public int MaxCapacity => maxCapacity;
 
        public bool IsValid => prefab != null && !string.IsNullOrEmpty(Id);
 
        internal void Sanitize()
        {
            if (maxCapacity > 0 && initialCapacity > maxCapacity) initialCapacity = maxCapacity;
        }
    }
}