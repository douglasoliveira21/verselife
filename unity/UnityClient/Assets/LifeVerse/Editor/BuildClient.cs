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
        /// <summary>The licenses of what the game ships (they have to travel with every copy of it).</summary>
        public const string Notices = "Assets/LifeVerse/Legal/THIRD-PARTY-NOTICES.txt";

        public static void CopyNotices(string folder) => File.Copy(Notices, Path.Combine(folder, Path.GetFileName(Notices)), true);

        public static void BuildWindows()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) { Debug.LogError("[LifeVerse] Nenhuma cena habilitada nas Build Settings."); EditorApplication.Exit(2); return; }
            PlayerSettings.enableFrameTimingStats = true;   // lets the benchmark tell CPU time from GPU time
            Directory.CreateDirectory("Builds/Windows");
            var options = new BuildPlayerOptions { scenes = scenes, locationPathName = "Builds/Windows/LifeVerse.exe", target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.None }; // explícito: o último build de servidor deixa o subtarget em Server
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[LifeVerse] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB em {summary.totalTime:g}, erros {summary.totalErrors}, avisos {summary.totalWarnings}");
            if (summary.result == BuildResult.Succeeded) CopyNotices("Builds/Windows");   // before the list of files, so the check covers it
            if (summary.result == BuildResult.Succeeded) Debug.Log($"[LifeVerse] Lista de arquivos para a verificação do jogo: {LifeVerse.Unity.World.GameFiles.Write("Builds/Windows")} arquivos");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// Servidor dedicado Linux (subtarget Server: sem gráficos, sem áudio). Precisa do módulo "Linux Dedicated Server Build Support".
        /// Unity -batchmode -nographics -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.BuildClient.BuildLinuxServer
        /// </summary>
        /// <summary>Where the Unity install keeps the IL2CPP player of Windows; absent when the module "Windows Build Support (IL2CPP)" is not installed.</summary>
        public static string Il2cppPlayerFolder() => Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations", "win64_player_nondevelopment_il2cpp");

        /// <summary>Whether this computer can build an IL2CPP player: the Unity module and the C++ compiler (Visual Studio "Desktop development with C++" or its Build Tools, with a Windows SDK).</summary>
        public static string Il2cppProblem()
        {
            if (!Directory.Exists(Il2cppPlayerFolder())) return "falta o módulo \"Windows Build Support (IL2CPP)\" da Unity (instale pelo Unity Hub)";
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) };
            var found = roots.Any(root => Directory.Exists(Path.Combine(root, "Microsoft Visual Studio")) && Directory.EnumerateFiles(Path.Combine(root, "Microsoft Visual Studio"), "cl.exe", SearchOption.AllDirectories).Any());
            return found ? null : "falta o compilador C++ (Visual Studio ou Build Tools com \"Desenvolvimento para desktop com C++\" e o Windows SDK)";
        }

        /// <summary>Builds the Windows player with IL2CPP (Builds/WindowsIl2cpp) and puts the scripting backend back as it was. Says what is missing instead of failing halfway.</summary>
        public static void BuildWindowsIl2cpp()
        {
            var problem = Il2cppProblem(); if (problem != null) { Debug.LogError("[LifeVerse] IL2CPP indisponível: " + problem); EditorApplication.Exit(3); return; }
            var target = UnityEditor.Build.NamedBuildTarget.Standalone; var before = PlayerSettings.GetScriptingBackend(target);
            try
            {
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP); PlayerSettings.enableFrameTimingStats = true;
                var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(); Directory.CreateDirectory("Builds/WindowsIl2cpp");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = "Builds/WindowsIl2cpp/LifeVerse.exe", target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.None });
                Debug.Log($"[LifeVerse] IL2CPP: {report.summary.result}, {report.summary.totalErrors} erros");
                if (report.summary.result == BuildResult.Succeeded) CopyNotices("Builds/WindowsIl2cpp");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            finally { PlayerSettings.SetScriptingBackend(target, before); }
        }
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
            if (summary.result == BuildResult.Succeeded) CopyNotices("Builds/LinuxServer");
            Debug.Log($"[LifeVerse] Servidor Linux {summary.result}: {summary.totalSize / (1024 * 1024)} MB em {summary.totalTime:g}, erros {summary.totalErrors}, avisos {summary.totalWarnings}");
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
