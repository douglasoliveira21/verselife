using System;
using System.IO;
using LifeVerse.Unity.World;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// Creates the HDRP materials of the world (ground and ocean) under Assets/LifeVerse/World/Resources, where the runtime loads them. They are assets, not
    /// materials made in code, because HDRP turns features on with shader keywords that only the editor sets (HDShaderUtils.ResetMaterialKeywords).
    ///   Unity.exe -batchmode -nographics -quit -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.WorldAssets.BuildBatch
    /// </summary>
    public static class WorldAssets
    {
        private const string Folder = "Assets/LifeVerse/World/Resources/LifeVerse";

        [MenuItem("LifeVerse/Criar materiais do mundo")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("O shader HDRP/Lit não foi encontrado: o HDRP está instalado e ativo?");
            BuildTerrain(shader); BuildOcean(shader);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[LifeVerse] Materiais do mundo criados em " + Folder);
        }

        public static void BuildBatch()
        {
            try { Build(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static Material Fresh(string name, Shader shader)
        {
            var path = $"{Folder}/{name}.mat";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Texture2D Attach(Material material, Texture2D texture, string name) { texture.name = name; AssetDatabase.AddObjectToAsset(texture, material); return texture; }

        private static void BuildTerrain(Shader shader)
        {
            var material = Fresh("TerrainMaterial", shader);
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", .06f);
            material.SetTexture("_DetailMap", Attach(material, ProceduralTextures.DetailGrain(256, 41), "GroundDetail"));
            material.SetFloat("_DetailAlbedoScale", 1.1f); material.SetFloat("_DetailNormalScale", 0f); material.SetFloat("_DetailSmoothnessScale", 0f);
            HDShaderUtils.ResetMaterialKeywords(material); EditorUtility.SetDirty(material);
        }

        private static void BuildOcean(Shader shader)
        {
            var material = Fresh("OceanMaterial", shader);
            material.SetFloat("_SurfaceType", 1f); material.SetFloat("_BlendMode", 0f); material.SetFloat("_ZWrite", 0f); material.SetFloat("_TransparentZWrite", 0f);
            material.SetFloat("_DoubleSidedEnable", 1f); material.SetFloat("_DoubleSidedNormalMode", 1f);
            material.SetColor("_BaseColor", new Color(.03f, .30f, .42f, .82f)); material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", .96f);
            material.SetTexture("_NormalMap", Attach(material, ProceduralTextures.NormalMap(256, 4, 4, 3, 7f), "SwellNormal")); material.SetFloat("_NormalScale", 1.1f);
            material.SetTexture("_DetailMap", Attach(material, ProceduralTextures.DetailNormal(256, 8, 3, 9, 5f), "RippleDetail"));
            material.SetFloat("_DetailAlbedoScale", 0f); material.SetFloat("_DetailNormalScale", 1f); material.SetFloat("_DetailSmoothnessScale", 0f);
            HDShaderUtils.ResetMaterialKeywords(material); EditorUtility.SetDirty(material);
        }
    }
}
