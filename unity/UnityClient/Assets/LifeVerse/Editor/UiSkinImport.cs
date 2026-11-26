using UnityEditor;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// The textures of the interface (drawn by unity/tools/ui-skin/generate.py) are imported as interface pictures: no mip maps, no compression, no resizing and no repeating,
    /// so the bevels and the 9-slice borders stay sharp. Applies to everything under Resources/LifeVerse/UI/Skin, on every machine.
    /// </summary>
    public sealed class UiSkinImport : AssetPostprocessor
    {
        public const string Folder = "/Resources/LifeVerse/UI/Skin/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.GUI;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
