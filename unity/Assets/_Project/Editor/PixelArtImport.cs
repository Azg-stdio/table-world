using UnityEditor;
using UnityEngine;

namespace TableWorld.Editor
{
    /// <summary>
    /// Import settings for third-party art that needs them (M12): the pixel-art UI frames stay crisp (point filtering,
    /// no compression or mipmaps) and the tavern's retro textures tile (repeat) with mipmaps.
    /// </summary>
    public sealed class PixelArtImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            var path = assetPath.Replace("\\", "/");
            if (path.Contains("/Art/ThirdParty/MegaPackFree/"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
            }
            else if (path.Contains("/Art/ThirdParty/Grimoire/"))
            {
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
            }
            else if (path.Contains("/Art/ThirdParty/AquilariusRetroTextures/"))
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 4;
            }
        }
    }
}
