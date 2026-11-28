using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LifeVerse.Unity.UI;
using LifeVerse.Unity.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.BuildEditor
{
    /// <summary>
    /// Photographs the prototype of the build tool on a real GPU, inside the editor (without -batchmode):
    ///   Unity.exe -projectPath unity/UnityClient -executeMethod LifeVerse.Unity.EditorTools.EditorChecks.Play -lifeverse-editor-run OUT.log -lifeverse-build-prototypes C:\out
    /// It makes the twenty states at 1920×1080, three of them at other resolutions, the side-by-side comparisons and a report of what does not fit. The game itself is not started.
    /// </summary>
    public sealed class Runner : MonoBehaviour
    {
        private const string Sheet = "Assets/LifeVerse/Prototypes/BuildEditor/BuildEditorProto.uss";
        private static readonly (int Width, int Height)[] Others = { (1280, 720), (1366, 768), (2560, 1440), (3840, 2160), (2560, 1080), (5120, 1440) };
        private static readonly int[] AtOthers = { 3, 9, 12 };
        public const float WindowX = 50f, WindowY = 70f;
        /// <summary>The bottom bar of the game, measured on its 1920×1080 photo: how wide it is, in panel units, and how much room it takes at the bottom.</summary>
        private const float BarWidth = 864f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-lifeverse-build-prototypes"); if (index < 0 || index + 1 >= args.Length) return;
            new GameObject("Build editor prototypes").AddComponent<Runner>()._folder = args[index + 1];
        }

        private string _folder; private Dictionary<string, MapWindow> _maps; private readonly StringBuilder _report = new StringBuilder(); private int _problems;

        private IEnumerator Start()
        {
            string failure = null; var steps = new Stack<IEnumerator>(); steps.Push(Run());
            while (steps.Count > 0)
            {
                object current;
                try { if (!steps.Peek().MoveNext()) { steps.Pop(); continue; } current = steps.Peek().Current; } catch (Exception exception) { failure = exception.ToString(); break; }
                if (current is IEnumerator inner) { steps.Push(inner); continue; }
                yield return current;
            }
            Console.WriteLine(failure == null ? "event=build_prototypes result=passed problems=" + _problems : "event=build_prototypes result=failed error=" + failure.Replace('\n', ' '));
            CheckRun.Quit(failure == null ? 0 : 1);
        }

        private void Note(string line) { _report.AppendLine(line); Console.WriteLine("event=build_prototype_note " + line); }

        private static string Title(MapWindow map) => map.Key == "grid" ? "Opções da grade" : map.Key == "texture-picker" ? "Escolher textura" : map.Key == "color-picker" ? "Cor" : map.Key == "media" ? "Mídia na face" : "Construir";

        private static (Vector3 Look, Vector3 From) Framing(Scenario s)
        {
            if (s.Marks == Marks.Terrain) return (new Vector3(3.2f, .5f, -2f), new Vector3(0f, 5f, -9.5f));
            if (s.Marks == Marks.Ghost) return (new Vector3(.2f, .8f, -1f), new Vector3(0f, 2.8f, -8f));
            if (s.Extra == "media" || s.Extra == "color-picker") return (new Vector3(3.2f, .9f, 0f), new Vector3(.2f, 2.8f, -8.4f));
            if (s.Extra == "texture-picker") return (new Vector3(3f, .9f, 0f), new Vector3(.2f, 2.8f, -8.4f));
            switch (s.Selected)
            {
                case Subject.Ring: return (new Vector3(-1.5f, .7f, -.5f), new Vector3(.2f, 2.8f, -6.8f));
                case Subject.Several: return (new Vector3(1.2f, 1f, .8f), new Vector3(.2f, 3.2f, -9.6f));
                case Subject.Table: return (new Vector3(-3.4f, .8f, 1f), new Vector3(.4f, 2.4f, -6.2f));
                default: return (new Vector3(.9f, .85f, 0f), new Vector3(.3f, 2.6f, -7.2f));
            }
        }

        private IEnumerator Run()
        {
            Application.targetFrameRate = 30;
            _maps = Map.Load(); Directory.CreateDirectory(_folder);
            var backdrop = new Backdrop(); yield return backdrop.Build();
            foreach (var s in Scenario.All) yield return Shot(backdrop, s, 1920, 1080, Path.Combine(_folder, $"{s.Number:00}-{s.Id}.jpg"));
            foreach (var (width, height) in Others)
                foreach (var number in AtOthers) { var s = Scenario.All[number - 1]; yield return Shot(backdrop, s, width, height, Path.Combine(_folder, "resolucoes", $"{s.Number:00}-{s.Id}-{width}x{height}.jpg")); }
            foreach (var s in Scenario.All)
                yield return Picture(1920, 1080, 1f, Comparison.Build(_maps, s, Title), null, Path.Combine(_folder, "comparativos", $"{s.Number:00}-{s.Id}.png"), false);
            // every component of the build window appears in at least one state, or the report says which does not
            var shown = new HashSet<string>(from s in Scenario.All from w in _maps["tools"].Widgets where s.Visible(w) select w.Id);
            var never = _maps["tools"].Widgets.Where(w => !shown.Contains(w.Id) && w.Type != "panel" && w.Type != "radio_group").Select(w => w.Name).Distinct().ToList();
            Note($"componentes da janela de construção que não aparecem em nenhum dos 20 estados: {never.Count}: {string.Join(", ", never)}");
            var unnamed = (from s in Scenario.All from w in _maps["tools"].Widgets where s.Visible(w) && (w.Type == "check_box" || w.Type == "radio_item" || w.Type == "text") && Texts.Of(w).Length == 0 && w.Name != "text status" && w.Name != "media_info" && w.Name != "current_url" && !w.Name.EndsWith(" Name", StringComparison.Ordinal) && !w.Name.StartsWith("selection_", StringComparison.Ordinal) select w.Name).Distinct().ToList();
            if (unnamed.Count > 0) { _problems += unnamed.Count; Note("sem texto em português: " + string.Join(", ", unnamed)); }
            Note("problemas=" + _problems);
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), _report.ToString());
        }

        private IEnumerator Shot(Backdrop backdrop, Scenario s, int width, int height, string path)
        {
            // the same scale rule as the game
            var scale = new UserSettings().EffectiveScale(height); var (look, from) = Framing(s);
            Texture2D picture = null; Projector projector = null; yield return backdrop.Shoot(look, from, width, height, scale, (t, p) => { picture = t; projector = p; });
            var panel = new Vector2(width, height) / scale;
            var stage = new VisualElement { name = "stage" }; stage.style.position = Position.Absolute; stage.style.left = stage.style.top = stage.style.right = stage.style.bottom = 0; stage.style.backgroundImage = new StyleBackground(picture);
            stage.Add(new Gizmos(s, projector));
            // on a narrow screen the centred bar reaches under the window: the window then ends above it
            var tools = _maps["tools"]; var underBar = (panel.x - BarWidth) / 2f < WindowX + tools.Size.x; var room = panel.y - 8f - (underBar ? FloatingWindow.BottomInset : 8f); var tall = Mathf.Min(tools.Size.y, room); var top = tools.Size.y + WindowY + 8f <= panel.y && !underBar ? WindowY : 8f;
            var window = Controls.Window(tools, s, Title(tools), tall); window.style.left = WindowX; window.style.top = top; stage.Add(window);
            if (s.Extra != null) { var map = _maps[s.Extra]; var extra = Controls.Window(map, s, Title(map), map.Size.y); extra.style.left = WindowX + tools.Size.x + 14f; extra.style.top = top; stage.Add(extra); }
            if (underBar) Note($"{Path.GetFileNameWithoutExtension(path)} a barra de baixo chega sob a janela: a janela termina acima dela ({tall:0} de {tools.Size.y:0})");
            var note = new Label("Protótipo para aprovação · os controles ainda não funcionam"); note.AddToClassList("bp-note"); note.AddToClassList("lv-small"); note.style.right = 14; note.style.bottom = 12; stage.Add(note);
            yield return Picture(width, height, scale, stage, picture, path, true);
        }

        private IEnumerator Picture(int width, int height, float scale, VisualElement stage, Texture2D background, string path, bool check)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); target.Create();
            var panel = ScriptableObject.CreateInstance<PanelSettings>(); panel.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>(); panel.scaleMode = PanelScaleMode.ConstantPixelSize; panel.scale = scale; panel.targetTexture = target; panel.clearColor = true;
            var host = new GameObject("panel"); var document = host.AddComponent<UIDocument>(); document.panelSettings = panel;
            var root = document.rootVisualElement; root.style.flexGrow = 1; root.AddToClassList("lv-root");
            var theme = Resources.Load<StyleSheet>("LifeVerse/UI/Theme"); var own = AssetDatabase.LoadAssetAtPath<StyleSheet>(Sheet);
            if (theme == null || own == null) throw new InvalidOperationException("stylesheet missing");
            root.styleSheets.Add(theme); root.styleSheets.Add(own); root.Add(stage);
            for (var i = 0; i < 20; i++) yield return null;
            if (check) foreach (var problem in Check(stage, new Vector2(width, height) / scale)) { _problems++; Note($"{Path.GetFileNameWithoutExtension(path)} {problem}"); }
            var before = RenderTexture.active; RenderTexture.active = target; var shot = new Texture2D(width, height, TextureFormat.RGB24, false); shot.ReadPixels(new Rect(0, 0, width, height), 0, 0); shot.Apply(); RenderTexture.active = before;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))); File.WriteAllBytes(path, path.EndsWith(".png", StringComparison.Ordinal) ? shot.EncodeToPNG() : shot.EncodeToJPG(90));
            Console.WriteLine("event=build_prototype_shot name=" + Path.GetFileName(path));
            Destroy(shot); Destroy(host); Destroy(panel.themeStyleSheet); Destroy(panel); target.Release(); Destroy(target); if (background != null) Destroy(background);
            yield return null;
        }

        /// <summary>What does not fit: components outside their window, components over each other and words that are cut.</summary>
        private static IEnumerable<string> Check(VisualElement stage, Vector2 panel)
        {
            var found = new List<string>();
            var windows = stage.Children().Where(c => c.ClassListContains("lv-window")).ToList();
            foreach (var window in windows)
            {
                var box = window.worldBound;
                if (box.xMin < -1f || box.yMin < -1f || box.xMax > panel.x + 1f || box.yMax > panel.y + 1f) found.Add($"janela {window.name} sai da tela: {box}");
                var scroll = window.Q<ScrollView>("tab-scroll");
                var controls = window.Query(className: "bp-control").ToList();
                bool Scrolled(VisualElement e) => scroll != null && scroll.Contains(e);
                if (scroll != null)
                    foreach (var part in new VisualElement[] { scroll.contentViewport, scroll.verticalScroller })
                        if (part.worldBound.yMax > box.yMax + 1f || part.worldBound.xMax > box.xMax + 1f) found.Add($"a rolagem sai da janela: {part.name} {part.worldBound} em {box}");
                foreach (var c in controls)
                {
                    var r = c.worldBound; if (r.width < 1f || r.height < 1f) { found.Add("sem tamanho: " + c.name); continue; }
                    if (Scrolled(c) && r.xMax > scroll.contentViewport.worldBound.xMax + .5f) found.Add($"cortado pela barra de rolagem: {c.name} {r}");
                    if (r.xMin < box.xMin - 1f || r.xMax > box.xMax + 1f || !Scrolled(c) && (r.yMin < box.yMin - 1f || r.yMax > box.yMax + 1f)) found.Add($"fora da janela: {c.name} {r}");
                }
                for (var i = 0; i < controls.Count; i++)
                    for (var j = i + 1; j < controls.Count; j++)
                    {
                        var a = controls[i].worldBound; var b = controls[j].worldBound; var w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin); var h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                        // text boxes of the map are as wide as the original ones, which overlap their neighbours: only the words themselves count
                        if (controls[i] is Label left) w = Mathf.Min(a.xMin + Measure(left), b.xMax) - Mathf.Max(a.xMin, b.xMin);
                        if (controls[j] is Label right) w = Mathf.Min(w, Mathf.Min(b.xMin + Measure(right), a.xMax) - Mathf.Max(a.xMin, b.xMin));
                        if (w > 3f && h > 3f && Scrolled(controls[i]) == Scrolled(controls[j])) found.Add($"sobrepostos ({w:0}×{h:0}): {Short(controls[i].name)} e {Short(controls[j].name)}");
                    }
                foreach (var label in window.Query<Label>(className: "bp-text").ToList())
                {
                    if (string.IsNullOrEmpty(label.text) || label.ClassListContains("bp-text-wrap") || label.resolvedStyle.textOverflow == TextOverflow.Ellipsis) continue;
                    var need = Measure(label); var has = label.contentRect.width;
                    if (need > has + 1.5f) found.Add($"texto cortado: «{label.text}» precisa de {need:0}, tem {has:0}");
                }
            }
            return found;
        }

        private static float Measure(Label label) => label.MeasureTextSize(label.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
        private static string Short(string id) { var parts = (id ?? "").Split('/'); return parts[parts.Length - 1]; }
    }
}
