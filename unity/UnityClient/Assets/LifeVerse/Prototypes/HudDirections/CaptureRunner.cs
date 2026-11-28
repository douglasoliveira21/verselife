using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// Photographs the three HUD directions on a real GPU, inside the editor (without -batchmode):
    ///   Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run OUT.log -lifeverse-hud-directions C:\out
    /// With -lifeverse-hud-skip-world the backdrop pictures already in the folder are reused. The game itself does not start with a -lifeverse argument.
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-hud-directions"); if (index < 0 || index + 1 >= args.Length) return;
            var runner = new GameObject("HUD directions").AddComponent<CaptureRunner>(); runner.Folder = args[index + 1]; runner.SkipWorld = Array.IndexOf(args, "-lifeverse-hud-skip-world") >= 0;
        }

        public string Folder; public bool SkipWorld;

        private IEnumerator Start()
        {
            string failure = null; var steps = Run();
            while (true)
            {
                object current;
                try { if (!steps.MoveNext()) break; current = steps.Current; } catch (Exception exception) { failure = exception.ToString(); break; }
                yield return current;
            }
            Console.WriteLine(failure == null ? "event=hud_directions result=passed" : "event=hud_directions result=failed error=" + failure.Replace('\n', ' '));
            CheckRun.Quit(failure == null ? 0 : 1);
        }

        private IEnumerator Run()
        {
            Application.targetFrameRate = 30;
            var backdrops = Path.Combine(Folder, "fundos");
            if (!SkipWorld) { var world = WorldBackdrop.Render(backdrops); while (world.MoveNext()) yield return world.Current; }

            var report = new StringBuilder(); var failures = 0;
            void Note(string line) { report.AppendLine(line); Console.WriteLine("event=hud_check " + line); }
            foreach (var line in Checks.AgainstTheGame()) { Note(line); failures++; }

            foreach (var direction in HudDirection.All)
            {
                var covered = new HashSet<string>();
                foreach (var shot in Shots)
                {
                    var picture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp }; picture.LoadImage(File.ReadAllBytes(Path.Combine(backdrops, shot.Backdrop + ".png")));
                    var people = JsonUtility.FromJson<WorldBackdrop.People>(File.ReadAllText(Path.Combine(backdrops, shot.Backdrop + ".json")));
                    var target = new RenderTexture(shot.Width, shot.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); target.Create();
                    var panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>(); panel.scaleMode = PanelScaleMode.ConstantPixelSize; panel.scale = Scale(shot.Width, shot.Height); panel.targetTexture = target; panel.clearColor = true;
                    var host = new GameObject("panel"); var document = host.AddComponent<UIDocument>(); document.panelSettings = panel;
                    document.rootVisualElement.style.flexGrow = 1;   // the absolute stage gives the root no height of its own
                    var stage = direction.Stage(shot.State, picture, people); document.rootVisualElement.Add(stage);
                    for (var i = 0; i < 45; i++) yield return null;   // layout, then the transitions
                    var hud = stage.Q<VisualElement>("hud");
                    var name = $"{direction.Id}-{shot.State.ToString().ToLowerInvariant()}-{shot.Label}";
                    Save(target, Path.Combine(Folder, name + ".png"));
                    covered.UnionWith(Checks.Functions(hud));
                    var problems = Checks.Layout(hud, true); failures += problems.Count;
                    foreach (var problem in problems) Note(name + " " + problem);
                    Console.WriteLine($"event=hud_shot name={name} scale={panel.scale:0.###} problems={problems.Count}");
                    Destroy(host); Destroy(panel.themeStyleSheet); Destroy(panel); target.Release(); Destroy(target); Destroy(picture);
                    yield return null;
                }
                var missing = Model.Required().Where(id => !covered.Contains(id)).ToList(); failures += missing.Count;
                Note($"{direction.Id} funções={Model.Required().Count()} sem lugar={missing.Count}" + (missing.Count > 0 ? ": " + string.Join(", ", missing) : ""));
            }
            foreach (var font in Kit.MissingFonts) { Note("fonte não carregada: " + font); failures++; }
            Note("problemas=" + failures);
            File.WriteAllText(Path.Combine(Folder, "relatorio.txt"), report.ToString());
        }

        /// <summary>The scale proposed in the diagnosis: follows the smaller side from 1920×1080, never under .85 nor over 2.</summary>
        public static float Scale(float width, float height) => Mathf.Clamp(Mathf.Min(width / 1920f, height / 1080f), .85f, 2f);

        private static readonly (string Label, string Backdrop, int Width, int Height, HudState State)[] Shots =
        {
            ("1920x1080", "fundo-dia-16x9", 1920, 1080, HudState.Explorando), ("1920x1080", "fundo-dia-16x9", 1920, 1080, HudState.Conversando), ("1920x1080", "fundo-dia-16x9", 1920, 1080, HudState.Menu),
            ("1280x720", "fundo-dia-16x9", 1280, 720, HudState.Explorando), ("1280x720", "fundo-dia-16x9", 1280, 720, HudState.Conversando), ("1280x720", "fundo-dia-16x9", 1280, 720, HudState.Menu),
            ("2560x1080", "fundo-dia-21x9", 2560, 1080, HudState.Explorando),
            ("entardecer-1920x1080", "fundo-entardecer-16x9", 1920, 1080, HudState.Explorando), ("noite-1920x1080", "fundo-noite-16x9", 1920, 1080, HudState.Conversando),
        };

        public static void Save(RenderTexture source, string path)
        {
            var before = RenderTexture.active; RenderTexture.active = source;
            var picture = new Texture2D(source.width, source.height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); picture.Apply();
            RenderTexture.active = before; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllBytes(path, picture.EncodeToPNG()); Destroy(picture);
        }
    }
}
