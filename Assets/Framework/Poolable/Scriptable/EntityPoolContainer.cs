using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Framework.BootStrap;
using Framework.Poolable.Components;
using UnityEngine;

namespace Framework.Poolable.Scriptable
{
    /// <summary>
    /// 프로젝트의 모든 엔티티 풀 정의를 한곳에서 관리하는 에셋.
    /// 이 에셋은 "정의"만 들고 있고, 실제 엔티티 프리팹 변환은 EntityPoolAuthoring의 Baker가 담당한다.
    /// </summary>
    public class EntityPoolContainer : BootstrapStep
    {
        [SerializeField] private List<EntityEntry> entries = new();
 
        private Dictionary<string, EntityEntry> _lookup;
 
        public IReadOnlyList<EntityEntry> Entries => entries;
 
        public override UniTask InitializeAsync(CancellationToken cancellationToken)
        {
            BuildLookup();
            return UniTask.CompletedTask;
        }
        
        private void BuildLookup()
        {
            if (_lookup != null && _lookup.Count == entries.Count) return;
            _lookup = new Dictionary<string, EntityEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                EntityEntry entry = entries[i];
                if (entry is { IsValid: true }) _lookup[entry.Id] = entry;
            }
        }

        public override void Shutdown()
        {
            _lookup?.Clear();
            
        }

        public bool TryGet(string id, out EntityEntry entry) => _lookup.TryGetValue(id, out entry);
 
        /// <summary>
        /// 게임플레이 코드에서 문자열 대신 쓸 런타임 키를 얻는다.
        /// </summary>
        public PoolId GetPoolId(string id) => PoolId.FromName(id);
 
        private void OnValidate()
        {
            _lookup = null;
 
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < entries.Count; i++)
            {
                EntityEntry entry = entries[i];
                if (entry == null) continue;
                entry.Sanitize();
                if (!entry.IsValid) continue;
                if (!seen.Add(entry.Id)) Debug.LogError($"[EntityPoolSO] 중복된 풀 ID: '{entry.Id}' ({name})", this);
            }
        }
    }
}