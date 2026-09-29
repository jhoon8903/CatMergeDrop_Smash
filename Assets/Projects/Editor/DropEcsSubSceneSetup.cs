using Framework.Poolable.Authoring;
using Framework.Poolable.Components;
using Framework.Poolable.Scriptable;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Board;
using Projects.Scripts.GamePlay.Drop;
using Projects.Scripts.GamePlay.Drop.Ecs.Components;
using Unity.Scenes;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Projects.Editor
{
    /// <summary>Builds the SubScene that bakes Drop ECS metadata and pool prefabs.</summary>
    public static class DropEcsSubSceneSetup
    {
        private const string GameScenePath = "Assets/Projects/Scenes/BootScene/GameScene.unity";
        private const string SubScenePath = "Assets/Projects/Scenes/BootScene/DropEcsSubScene.unity";
        private const string PoolPath = "Assets/Projects/Assets/Configuration/PoolContainer.asset";
        private const string BrickPrefabPath = "Assets/Projects/Prefabs/GamePlay/PooledDropBrick.prefab";
        private const string CatPrefabPath = "Assets/Projects/Prefabs/GamePlay/PooledDropCat.prefab";

        [MenuItem("Tools/Cat Merge Drop/Build Drop ECS SubScene and Pools")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Stop Play mode before rebuilding the ECS SubScene.");

            GameObject brickPrefab = EnsurePrefab<DropBrickPoolPrefabAuthoring>(BrickPrefabPath);
            GameObject catPrefab = EnsurePrefab<DropCatPoolPrefabAuthoring>(CatPrefabPath);
            EntityPoolContainer pool = AssetDatabase.LoadAssetAtPath<EntityPoolContainer>(PoolPath);
            if (pool == null) throw new System.InvalidOperationException("PoolContainer.asset is missing.");
            ConfigurePool(pool, brickPrefab, catPrefab);

            Scene subScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath) == null
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(SubScenePath, OpenSceneMode.Single);
            GameObject root = null;
            foreach (GameObject candidate in subScene.GetRootGameObjects())
                if (candidate.name == "DropEcsRuntime") { root = candidate; break; }
            if (root == null) root = new GameObject("DropEcsRuntime");
            if (root.GetComponent<DropRuntimeAuthoring>() == null)
                root.AddComponent<DropRuntimeAuthoring>();
            EntityPoolAuthoring authoring = root.GetComponent<EntityPoolAuthoring>();
            if (authoring == null) authoring = root.AddComponent<EntityPoolAuthoring>();
            var serializedAuthoring = new SerializedObject(authoring);
            serializedAuthoring.FindProperty("poolSet").objectReferenceValue = pool;
            serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(subScene, SubScenePath);

            Scene gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            GameObject sceneRoot = GameObject.Find("DropEcsSubScene");
            if (sceneRoot == null) sceneRoot = new GameObject("DropEcsSubScene");
            SubScene reference = sceneRoot.GetComponent<SubScene>();
            if (reference == null) reference = sceneRoot.AddComponent<SubScene>();
            reference.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
            reference.AutoLoadScene = true;
            EditorSceneManager.MarkSceneDirty(gameScene);
            EditorSceneManager.SaveScene(gameScene);
            AssetDatabase.SaveAssets();
            Debug.Log("Drop ECS SubScene and Brick/Cat pools configured.");
        }

        [MenuItem("Tools/Cat Merge Drop/Log Drop ECS Runtime")]
        public static void LogRuntimeStatus()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogWarning("Drop ECS runtime: default world is unavailable.");
                return;
            }
            EntityManager manager = world.EntityManager;
            using EntityQuery baked = manager.CreateEntityQuery(ComponentType.ReadOnly<DropRuntimeBaked>());
            using EntityQuery config = manager.CreateEntityQuery(
                ComponentType.ReadOnly<EntityPoolConfig>(), ComponentType.ReadOnly<PoolPrefabElement>());
            using EntityQuery poolQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<EntityPool>());
            using EntityQuery stages = manager.CreateEntityQuery(
                ComponentType.ReadOnly<Projects.Scripts.GamePlay.Drop.Ecs.Components.Stage>());
            using EntityQuery bricks = manager.CreateEntityQuery(ComponentType.ReadOnly<Brick>());
            using EntityQuery cats = manager.CreateEntityQuery(ComponentType.ReadOnly<Cat>());
            using EntityQuery scenes = manager.CreateEntityQuery(ComponentType.ReadOnly<SceneReference>());
            int registrations = poolQuery.CalculateEntityCount() == 1
                ? poolQuery.GetSingleton<EntityPool>().PoolCount : 0;
            SubScene reference = Object.FindFirstObjectByType<SubScene>();
            string sceneStatus = reference == null ? "missing" :
                $"guid={reference.SceneGUID}, active={reference.isActiveAndEnabled}";
            string streaming = "none";
            using (var sceneEntities = scenes.ToEntityArray(Unity.Collections.Allocator.Temp))
                if (sceneEntities.Length > 0)
                    streaming = SceneSystem.GetSceneStreamingState(world.Unmanaged, sceneEntities[0]).ToString();
            Debug.Log($"Drop ECS runtime: baked={baked.CalculateEntityCount()}, " +
                $"poolConfigs={config.CalculateEntityCount()}, pools={registrations}, " +
                $"stages={stages.CalculateEntityCount()}, bricks={bricks.CalculateEntityCount()}, " +
                $"cats={cats.CalculateEntityCount()}, subScene={sceneStatus}, " +
                $"sceneEntities={scenes.CalculateEntityCount()}, streaming={streaming}");
        }

        private static GameObject EnsurePrefab<T>(string path) where T : MonoBehaviour
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<T>() != null) return existing;
            GameObject temporary = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            temporary.AddComponent<T>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, path);
            Object.DestroyImmediate(temporary);
            return prefab;
        }

        private static void ConfigurePool(EntityPoolContainer pool, GameObject brick, GameObject cat)
        {
            var serialized = new SerializedObject(pool);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = 2;
            SetEntry(entries.GetArrayElementAtIndex(0), "drop-brick", brick,
                WorldPlayfieldLayout.BrickCount, WorldPlayfieldLayout.BrickCount);
            SetEntry(entries.GetArrayElementAtIndex(1), "drop-cat", cat,
                MergeBoardModel.SlotCount, MergeBoardModel.SlotCount);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pool);
        }

        private static void SetEntry(SerializedProperty property, string id, GameObject prefab,
            int initial, int maximum)
        {
            property.FindPropertyRelative("id").stringValue = id;
            property.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            property.FindPropertyRelative("initialCapacity").intValue = initial;
            property.FindPropertyRelative("maxCapacity").intValue = maximum;
        }
    }
}
