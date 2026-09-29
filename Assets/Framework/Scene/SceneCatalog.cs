using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Framework.Scene
{
    [Serializable]
    public sealed class SceneReference
    {
        [SerializeField] private SceneId sceneId;
        [SerializeField] private AssetReference scene;

        public SceneId SceneId => sceneId;
        public AssetReference Asset => scene;
    }

    /// <summary>
    /// 프로젝트가 사용할 Addressable Scene 목록이다.
    /// SceneSwitcher는 이 에셋만 알고, 개별 Scene Asset을 직접 보유하지 않는다.
    /// </summary>
    public sealed class SceneCatalog : ScriptableObject, ISceneCatalog
    {
        [SerializeField] private SceneReference[] scenes = Array.Empty<SceneReference>();

        private readonly Dictionary<SceneId, AssetReference> _sceneMap = new();

        private void OnEnable() => RebuildMap();

        public bool TryGet(SceneId sceneId, out AssetReference sceneReference)
        {
            if (_sceneMap.Count == 0) RebuildMap();
            return _sceneMap.TryGetValue(sceneId, out sceneReference);
        }

        private void RebuildMap()
        {
            _sceneMap.Clear();
            if (scenes == null) return;
            for (int i = 0; i < scenes.Length; i++)
            {
                SceneReference entry = scenes[i];
                if (entry?.Asset == null) continue;

                if (!_sceneMap.TryAdd(entry.SceneId, entry.Asset))
                    Debug.LogError($"[SceneCatalog] 중복 SceneId: {entry.SceneId}", this);
            }
        }
    }
}
