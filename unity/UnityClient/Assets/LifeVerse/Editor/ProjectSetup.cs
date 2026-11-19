using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// Configura o projeto do cliente de forma repetível: pipeline HDRP, espaço de cor, APIs gráficas do Windows,
    /// Input System, perfil de API .NET e a cena Bootstrap. Roda pelo menu ou em linha de comando:
    /// Unity -batchmode -nographics -quit -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.ProjectSetup.ConfigureBatch
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/LifeVerse/Settings";
        private const string PipelineAssetPath = SettingsFolder + "/LifeVerse-HDRP.asset";
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        [MenuItem("LifeVerse/Configurar projeto (HDRP, Player e cena)")]
        public static void Configure()
        {
            ConfigurePlayer();
            var pipeline = EnsurePipelineAsset();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var current = QualitySettings.GetQualityLevel();
            for (var level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            EnsureBootstrapScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[LifeVerse] Projeto configurado: HDRP ativo, espaço de cor linear, Direct3D12/11, Input System, cena Bootstrap.");
        }

        public static void ConfigureBatch()
        {
            try { Configure(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "LifeVerse";
            PlayerSettings.productName = "LifeVerse";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            // HDRP precisa de DirectX 11/12 ou Vulkan; o Windows tenta Direct3D12 primeiro
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
            // o HDRP só aceita Vulkan no Linux (a lista padrão inclui OpenGL e o build é recusado); vale também para o servidor dedicado
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.Vulkan });
            // .NET Standard 2.1 casa com os plugins pré-compilados (shared e scripting)
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            SetActiveInputHandler(1); // 1 = somente o Input System (novo); vale na próxima abertura do editor
        }

        private static void SetActiveInputHandler(int value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0) return;
            var settings = new SerializedObject(assets[0]);
            var property = settings.FindProperty("activeInputHandler");
            if (property == null || property.intValue == value) return;
            property.intValue = value;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static HDRenderPipelineAsset EnsurePipelineAsset()
        {
            Directory.CreateDirectory(SettingsFolder);
            var existing = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelineAssetPath);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(asset, PipelineAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void EnsureBootstrapScene()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
