using Framework.Scene;
using UnityEditor;
using UnityEngine;

namespace Framework.Editor.Scene
{
    /// <summary>Project 창의 우클릭 메뉴에서 Framework SO를 바로 생성한다.</summary>
    internal static class SceneAssetCreationMenu
    {
        [MenuItem("Assets/Framework/Scene/Scene Catalog", false, 0)]
        private static void CreateSceneCatalog()
        {
            CreateAsset<SceneCatalog>("SceneCatalog");
        }

        [MenuItem("Assets/Framework/Scene/Scene Entry Resolver", false, 1)]
        private static void CreateSceneEntryResolver()
        {
            CreateAsset<SceneEntryResolver>("SceneEntryResolver");
        }

        private static void CreateAsset<T>(string fileName) where T : ScriptableObject
        {
            ProjectWindowUtil.CreateAsset(ScriptableObject.CreateInstance<T>(), $"{fileName}.asset");
        }
    }
}
