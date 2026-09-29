using Framework.BootStrap;
using UnityEditor;
using UnityEngine;

namespace Framework.Editor.Save
{
    internal static class SaveAssetCreationMenu
    {
        [MenuItem("Assets/Framework/Save/SaveStorage", false, 0)]
        private static void CreateSaveStorage()
        {
            CreateAsset<SaveSystemBootstrapStep>("SaveStorage");
        }

        private static void CreateAsset<T>(string fileName) where T : ScriptableObject
        {
            ProjectWindowUtil.CreateAsset(ScriptableObject.CreateInstance<T>(), $"{fileName}.asset");
        }
    }
}