using System;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// How a check of the game ends, in the player and in the editor. The player just quits with the code. The editor, when it was started to run a check
    /// (<c>-lifeverse-editor-run OUTPUT</c>, see <c>EditorChecks.Play</c>), writes what the check prints to that file and closes itself with the same code; a check that never ends is closed after
    /// twenty minutes with the code 3. An editor opened by a person is never closed.
    /// </summary>
    public static class CheckRun
    {
        public const string Flag = "-lifeverse-editor-run";
        public const float TimeoutSeconds = 1200f;

        /// <summary>The file the editor writes to, or null when this is not a run of a check in the editor.</summary>
        public static string OutputPath(string[] args)
        {
            var at = Array.IndexOf(args ?? Array.Empty<string>(), Flag); return at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("-", StringComparison.Ordinal) ? args[at + 1] : null;
        }

        public static void Quit(int code)
        {
#if UNITY_EDITOR
            if (OutputPath(Environment.GetCommandLineArgs()) != null) { Console.WriteLine("event=editor_run exit=" + code); Console.Out.Flush(); UnityEditor.EditorApplication.Exit(code); return; }
#endif
            Application.Quit(code);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Begin()
        {
            var path = OutputPath(Environment.GetCommandLineArgs()); if (path == null) return;
            Console.SetOut(new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            Application.runInBackground = true;
            var watchdog = new GameObject("Editor run watchdog"); UnityEngine.Object.DontDestroyOnLoad(watchdog); var watch = watchdog.AddComponent<Watchdog>(); var told = new System.Collections.Generic.HashSet<string>();
            Application.logMessageReceived += (message, stack, type) =>
            {
                if (type is not (LogType.Error or LogType.Exception or LogType.Assert) || !told.Add(message)) return;   // each error once: a renderer warning can repeat every frame
                Console.WriteLine("event=unity_error type=" + type + " message=" + message.Replace('\n', ' ') + " at=" + (stack ?? "").Split('\n')[0]);
                if (type == LogType.Exception && Breaks(stack)) watch.Broken = true;
            };
        }

        /// <summary>An exception that came out of a check kills its coroutine, so it will never say it ended.</summary>
        public static bool Breaks(string stack) => stack != null && (stack.Contains("LifeVerse.Unity.Server.WorldSyncSelfCheck") || stack.Contains("LifeVerse.Unity.Server.EndToEndCheck"));

        private sealed class Watchdog : MonoBehaviour
        {
            public bool Broken;
            private void Update()
            {
                if (Broken) { Broken = false; Console.WriteLine("event=editor_run result=broken"); Quit(2); }
                else if (Time.realtimeSinceStartup > TimeoutSeconds) { Console.WriteLine("event=editor_run result=timeout"); Quit(3); }
            }
        }
#endif
    }
}
