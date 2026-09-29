using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    /// <summary>Imports the seamless board-cell backing as an opaque point sprite.</summary>
    public sealed class BoardSpriteImporter : AssetPostprocessor
    {
        private const string BoardFolder = "Assets/Projects/Art/Build/Board/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BoardFolder, System.StringComparison.Ordinal)) return;
            if (assetImporter is not TextureImporter importer) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.spritePixelsPerUnit = 256f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
