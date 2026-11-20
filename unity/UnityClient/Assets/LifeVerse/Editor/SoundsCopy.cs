using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>Before a build, copies the recordings that come with the game (footsteps, keyboard, login theme: client/Assets/audio, about 5 MB) to Assets/StreamingAssets/audio, where the game reads them. The copies are not kept in git: the originals are the only ones.</summary>
    public sealed class SoundsCopy : IPreprocessBuildWithReport
    {
        public int callbackOrder => 11;
        public void OnPreprocessBuild(BuildReport report) => Copy();

        [MenuItem("LifeVerse/Copiar os sons do jogo para o build")]
        public static void Copy()
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "client", "Assets", "audio")); var target = Path.Combine(Application.dataPath, "StreamingAssets", "audio");
            if (!Directory.Exists(source)) { Debug.LogWarning("[LifeVerse] sons do jogo não encontrados: " + source); return; }
            var copied = 0; Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source, "*.mp3")) { var to = Path.Combine(target, Path.GetFileName(file)); if (File.Exists(to) && new FileInfo(to).Length == new FileInfo(file).Length) continue; File.Copy(file, to, true); copied++; }
            if (copied > 0) { AssetDatabase.Refresh(); Debug.Log("[LifeVerse] " + copied + " som(ns) do jogo copiado(s) para o build."); }
        }
    }
}
