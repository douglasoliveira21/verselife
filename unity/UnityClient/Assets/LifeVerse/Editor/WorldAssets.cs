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
            BuildTerrain(shader); BuildOcean(shader); BuildRain(); BuildEffects(); BuildObjects(shader); BuildSun(); BuildFoam();
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

        /// <summary>The two looks every object starts from (opaque, and see-through for objects with transparency); WorldObjects copies one and sets colour, glow, roughness and metal.</summary>
        private static void BuildObjects(Shader shader)
        {
            BuildObject(shader, "ObjectMaterial", false, false); BuildObject(shader, "ObjectMaterialBlend", true, false);
            BuildObject(shader, "ObjectMaterialDouble", false, true); BuildObject(shader, "ObjectMaterialBlendDouble", true, true);
        }

        /// <summary>
        /// HDRP only draws a map when the material has its keyword on, and the keywords are set here, in the editor. So every prototype already carries neutral maps (white colour,
        /// flat normal, a mask of ones, black-times-white glow): a picture put in at run time then just replaces the neutral one, and the numbers (colour, remap range) keep working.
        /// </summary>
        private static void BuildObject(Shader shader, string name, bool blended, bool doubleSided)
        {
            var material = Fresh(name, shader);
            if (blended) { material.SetFloat("_SurfaceType", 1f); material.SetFloat("_BlendMode", 0f); material.SetFloat("_ZWrite", 0f); material.SetFloat("_TransparentZWrite", 0f); }
            if (doubleSided) { material.SetFloat("_DoubleSidedEnable", 1f); material.SetFloat("_DoubleSidedNormalMode", 1f); }
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", .3f); material.SetColor("_EmissiveColor", Color.black);
            material.SetTexture("_BaseColorMap", Attach(material, Flat(new Color32(255, 255, 255, 255), false), "NeutralBase"));
            material.SetTexture("_NormalMap", Attach(material, Flat(new Color32(128, 128, 255, 255), true), "NeutralNormal"));
            material.SetTexture("_MaskMap", Attach(material, Flat(new Color32(255, 255, 255, 255), true), "NeutralMask"));
            material.SetTexture("_EmissiveColorMap", Attach(material, Flat(new Color32(255, 255, 255, 255), false), "NeutralGlow"));
            material.SetFloat("_MetallicRemapMin", 0f); material.SetFloat("_MetallicRemapMax", 0f); material.SetFloat("_SmoothnessRemapMin", .7f); material.SetFloat("_SmoothnessRemapMax", .7f);
            HDShaderUtils.ResetMaterialKeywords(material); EditorUtility.SetDirty(material);
        }

        private static Texture2D Flat(Color32 colour, bool linear)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false, linear) { wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[16]; for (var i = 0; i < pixels.Length; i++) pixels[i] = colour; texture.SetPixels32(pixels); texture.Apply(); return texture;
        }

        private static void BuildFoam()
        {
            var shader = Shader.Find("LifeVerse/ShoreFoam") ?? throw new InvalidOperationException("O shader LifeVerse/ShoreFoam não foi encontrado (erro de compilação do shader?).");
            var material = Fresh("FoamMaterial", shader); EditorUtility.SetDirty(material);
        }

        private static void BuildSun()
        {
            var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("O shader HDRP/Unlit não foi encontrado.");
            var material = Fresh("SunMaterial", shader);
            material.SetColor("_UnlitColor", Color.black); material.SetColor("_EmissiveColor", new Color(1f, .96f, .82f) * 80000f); material.SetFloat("_UseEmissiveIntensity", 0f);
            HDShaderUtils.ResetMaterialKeywords(material); EditorUtility.SetDirty(material);
        }

        /// <summary>The two materials of the particles scripts put on objects: soft puffs that blend (smoke, dust, rain) and that add light (fire, sparks, explosions).</summary>
        private static void BuildEffects()
        {
            var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("O shader HDRP/Unlit não foi encontrado.");
            foreach (var (name, blend) in new[] { ("EffectMaterial", 0f), ("EffectMaterialAdd", 1f) })
            {
                var material = Fresh(name, shader); material.SetColor("_UnlitColor", Color.white); material.SetFloat("_SurfaceType", 1f); material.SetFloat("_BlendMode", blend); material.SetFloat("_ZWrite", 0f); material.SetFloat("_DoubleSidedEnable", 1f);
                HDShaderUtils.ResetMaterialKeywords(material); EditorUtility.SetDirty(material);
            }
        }

        private static void BuildRain()
        {
            var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("O shader HDRP/Unlit não foi encontrado.");
            var material = Fresh("RainMaterial", shader);
            material.SetColor("_UnlitColor", new Color(.78f, .85f, .96f, .45f)); material.SetFloat("_SurfaceType", 1f); material.SetFloat("_BlendMode", 0f); material.SetFloat("_ZWrite", 0f);
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
