using System.Collections.Generic;
using Projects.Scripts.GamePlay;
using Projects.Scripts.GamePlay.Configs;
using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    /// <summary>Binds the approved v3 cat sprite set after a complete preflight.</summary>
    public static class CatV3AssetBinder
    {
        private const int CatLevelCount = 24;
        private const string CatConfigPath = "Assets/Projects/Assets/Configuration/CatConfig.asset";
        private const string CatBaseFolder = "Assets/Projects/Art/Build/CatBase";
        private const string CatLevelsFolder = "Assets/Projects/Art/Build/CatLevels";

        [MenuItem("Tools/Cat Merge Drop/Bind V3 Cat Sprites")]
        public static void Bind()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Cat V3 binding is unavailable while entering or running Play mode.");
                return;
            }

            string[] idlePaths = BuildPaths("idle");
            string[] jumpPaths = BuildPaths("jump");
            string[] dropPaths = BuildPaths("drop");
            string[] allPaths = Combine(idlePaths, jumpPaths, dropPaths);
            List<string> missingPaths = FindMissingTextures(allPaths);
            if (missingPaths.Count > 0)
            {
                Debug.LogError($"Cat V3 binding aborted: {missingPaths.Count} required texture asset(s) are missing or invalid. " +
                    "CatConfig was not changed.\n" + string.Join("\n", missingPaths));
                return;
            }

            CatConfig config = AssetDatabase.LoadAssetAtPath<CatConfig>(CatConfigPath);
            if (config == null)
            {
                Debug.LogError($"Cat V3 binding aborted: CatConfig is missing at {CatConfigPath}.");
                return;
            }

            SerializedObject serializedConfig = new SerializedObject(config);
            SerializedProperty levelSprites = serializedConfig.FindProperty("levelSprites");
            SerializedProperty jumpSprites = serializedConfig.FindProperty("jumpSprites");
            SerializedProperty dropSprites = serializedConfig.FindProperty("dropSprites");
            if (levelSprites == null || jumpSprites == null || dropSprites == null)
            {
                Debug.LogError("Cat V3 binding aborted: CatConfig sprite arrays could not be found.");
                return;
            }

            foreach (string path in allPaths) EnsureSpriteImportSettings(path);

            Sprite[] idleSprites = LoadSprites(idlePaths);
            Sprite[] jumpSpriteAssets = LoadSprites(jumpPaths);
            Sprite[] dropSpriteAssets = LoadSprites(dropPaths);
            if (HasMissingSprite(idleSprites) || HasMissingSprite(jumpSpriteAssets) || HasMissingSprite(dropSpriteAssets))
            {
                Debug.LogError("Cat V3 binding aborted: a required Sprite could not be loaded after import. " +
                    "CatConfig was not changed.");
                return;
            }

            SetSprites(levelSprites, idleSprites);
            SetSprites(jumpSprites, jumpSpriteAssets);
            SetSprites(dropSprites, dropSpriteAssets);
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log($"Cat V3 binding complete: {CatLevelCount} Idle, {CatLevelCount} Jump, and " +
                $"{CatLevelCount} Drop sprites assigned to {CatConfigPath}.", config);
        }

        private static string[] BuildPaths(string pose)
        {
            string[] paths = new string[CatLevelCount];
            paths[0] = $"{CatBaseFolder}/cat_base_{pose}_v3.png";
            for (int level = 2; level <= CatLevelCount; level++)
                paths[level - 1] = $"{CatLevelsFolder}/cat_level_{level:00}_{pose}_v3.png";
            return paths;
        }

        private static string[] Combine(params string[][] pathGroups)
        {
            string[] paths = new string[CatLevelCount * pathGroups.Length];
            int index = 0;
            foreach (string[] group in pathGroups)
                foreach (string path in group)
                    paths[index++] = path;
            return paths;
        }

        private static List<string> FindMissingTextures(string[] paths)
        {
            var missingPaths = new List<string>();
            foreach (string path in paths)
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null ||
                    AssetImporter.GetAtPath(path) is not TextureImporter)
                    missingPaths.Add(path);
            return missingPaths;
        }

        private static void EnsureSpriteImportSettings(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"Expected a texture importer at {path}.");

            bool needsUpdate = importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single ||
                importer.spritePixelsPerUnit != 100f ||
                importer.spritePivot != new Vector2(.5f, .5f) ||
                importer.filterMode != FilterMode.Bilinear ||
                importer.mipmapEnabled ||
                !importer.alphaIsTransparency;
            if (!needsUpdate) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        private static Sprite[] LoadSprites(string[] paths)
        {
            var sprites = new Sprite[paths.Length];
            for (int i = 0; i < paths.Length; i++)
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(paths[i]);
            return sprites;
        }

        private static bool HasMissingSprite(Sprite[] sprites)
        {
            foreach (Sprite sprite in sprites)
                if (sprite == null) return true;
            return false;
        }

        private static void SetSprites(SerializedProperty property, Sprite[] sprites)
        {
            property.arraySize = sprites.Length;
            for (int i = 0; i < sprites.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
    }
}
