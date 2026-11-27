using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>Before a build, copies the body that comes with the game (assets/avatars/lifeverse_man.bin, about 50 MB) to Assets/StreamingAssets/avatars, where the game reads it. The copy is not kept in git: the original is the only one.</summary>
    public sealed class BuiltInBodiesCopy : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10;
        public void OnPreprocessBuild(BuildReport report) => Copy();

        [MenuItem("LifeVerse/Copiar o corpo do jogo para o build")]
        public static void Copy()
        {
            var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "assets", "avatars", "lifeverse_man.bin"));
            var target = Path.Combine(Application.dataPath, "StreamingAssets", "avatars", "lifeverse_man.bin");
            if (!File.Exists(source)) { Debug.LogWarning("[LifeVerse] corpo do jogo não encontrado: " + source); return; }
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(source).Length && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target, true); AssetDatabase.Refresh(); Debug.Log("[LifeVerse] corpo do jogo copiado para o build.");
        }
    }
}
