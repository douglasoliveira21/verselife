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
                PlayModeWindow.SetCustomRenderingResolution(1280, 720, "LifeVerse checks");   // so the photos are always the same size
            }
            EditorApplication.EnterPlaymode();
        }
    }
}
