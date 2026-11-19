using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// Build do cliente Windows por linha de comando:
    /// Unity -batchmode -nographics -quit -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildWindows
    /// O resultado fica em unity/UnityClient/Builds/Windows (ignorado pelo git).
    /// </summary>
    public static class BuildClient
    {
        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) { Debug.LogError("[LifeVerse] Nenhuma cena habilitada nas Build Settings."); EditorApplication.Exit(2); return; }
            Directory.CreateDirectory("Builds/Windows");
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = "Builds/Windows/LifeVerse.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.None };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[LifeVerse] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB em {summary.totalTime:g}, erros {summary.totalErrors}, avisos {summary.totalWarnings}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Servidor dedicado Linux (subtarget Server: sem gráficos, sem áudio). Precisa do módulo "Linux Dedicated Server Build Support".
        /// Unity -batchmode -nographics -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer
        /// </summary>
        public static void BuildLinuxServer()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) { Debug.LogError("[LifeVerse] Nenhuma cena habilitada nas Build Settings."); EditorApplication.Exit(2); return; }
            Directory.CreateDirectory("Builds/LinuxServer");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Builds/LinuxServer/LifeVerseServer.x86_64",
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[LifeVerse] Servidor Linux {summary.result}: {summary.totalSize / (1024 * 1024)} MB em {summary.totalTime:g}, erros {summary.totalErrors}, avisos {summary.totalWarnings}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
