using UnityEditor;
using UnityEditor.SceneManagement;

namespace LifeVerse.Unity.EditorTools
{
    /// <summary>
    /// Runs the checks of the game (the same ones the built player runs) inside the editor, in play mode, for a machine that cannot start the built player:
    ///   Unity.exe -batchmode -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run OUTPUT.log -lifeverse-live-api http://127.0.0.1:5099
    /// Without <c>-quit</c>. The editor closes itself with the code of the check. A check that draws or takes photos (the world self-check) needs the editor's window, so leave <c>-batchmode</c> out too:
    /// without a window the editor never reaches the end of a frame and the check waits for ever.
    /// </summary>
    public static class EditorChecks
    {
        public static void Play()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            if (!UnityEngine.Application.isBatchMode)
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");   // the frames are drawn into the Game view
                // so the photos are always the same size; "-lifeverse-editor-size 1920x1080" asks for another one
                var args = System.Environment.GetCommandLineArgs(); var at = System.Array.IndexOf(args, "-lifeverse-editor-size"); uint width = 1280, height = 720;
                if (at >= 0 && at + 1 < args.Length) { var parts = args[at + 1].Split('x'); if (parts.Length == 2 && uint.TryParse(parts[0], out var w) && uint.TryParse(parts[1], out var h)) { width = w; height = h; } }
                PlayModeWindow.SetCustomRenderingResolution(width, height, "LifeVerse checks");
            }
            EditorApplication.EnterPlaymode();
        }
    }
}
