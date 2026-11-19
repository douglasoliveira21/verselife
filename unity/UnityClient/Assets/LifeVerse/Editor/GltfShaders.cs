using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// glTFast makes its materials at run time from shader graphs that nothing in the scenes refers to, so a player build leaves them out and an imported model draws magenta.
    /// Before every build the shaders are put in the "Always Included Shaders" list of the Graphics settings (the list is a file in ProjectSettings, so it stays).
    /// </summary>
    public sealed class GltfShaders : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        private static readonly string[] Names = { "Shader Graphs/glTF-pbrMetallicRoughness", "Shader Graphs/glTF-unlit", "Shader Graphs/glTF-pbrSpecularGlossiness" };

        public void OnPreprocessBuild(BuildReport report) => Ensure();

        [MenuItem("LifeVerse/Incluir shaders do glTF no build")]
        public static void Ensure()
        {
            var settings = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var serialized = new SerializedObject(settings); var list = serialized.FindProperty("m_AlwaysIncludedShaders"); var changed = false;
            foreach (var name in Names)
            {
                var shader = Shader.Find(name); if (shader == null) { Debug.LogWarning("[LifeVerse] shader do glTF não encontrado: " + name); continue; }
                if (Enumerable.Range(0, list.arraySize).Any(i => list.GetArrayElementAtIndex(i).objectReferenceValue == shader)) continue;
                list.InsertArrayElementAtIndex(list.arraySize); list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader; changed = true;
            }
            if (changed) { serialized.ApplyModifiedProperties(); AssetDatabase.SaveAssets(); Debug.Log("[LifeVerse] shaders do glTF incluídos no build."); }
        }
    }
}
