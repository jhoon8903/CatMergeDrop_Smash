using UnityEditor;
using UnityEngine;

namespace Projects.Editor
{
    /// <summary>Applies the approved pixel-sprite import contract to Brick art.</summary>
    public sealed class BrickSpriteImporter : AssetPostprocessor
    {
        private const string BrickFolder = "Assets/Projects/Art/Build/Brick/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BrickFolder, System.StringComparison.Ordinal)) return;
            if (assetImporter is not TextureImporter importer) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePivot = new Vector2(.5f, .5f);
            importer.spritePixelsPerUnit = 256f;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
