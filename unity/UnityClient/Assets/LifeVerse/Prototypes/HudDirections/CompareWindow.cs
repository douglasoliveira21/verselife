using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.Prototypes.HudDirections
{
    /// <summary>
    /// LifeVerse › Protótipos de HUD: the three directions side by side in one editor window, live (the mouse and the keyboard focus work as drawn). Keys 1, 2 and 3 change the
    /// direction. It reads the backdrop pictures from artifacts/hud-directions/fundos (made by the capture) and touches nothing of the game.
    /// </summary>
    public sealed class CompareWindow : EditorWindow
    {
        private int _direction, _state, _backdrop; private VisualElement _frame, _bar; private Texture2D _picture;
        private static readonly (string Label, string File)[] Backdrops = { ("Dia", "fundo-dia-16x9"), ("Entardecer", "fundo-entardecer-16x9"), ("Noite", "fundo-noite-16x9") };
        private static string Folder => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../artifacts/hud-directions/fundos"));

        [MenuItem("LifeVerse/Protótipos de HUD (comparar)")]
        public static void Open() { var window = GetWindow<CompareWindow>(); window.titleContent = new GUIContent("Protótipos de HUD"); window.minSize = new Vector2(960, 600); window.Show(); }

        /// <summary>-executeMethod …CompareWindow.SelfTest: opens the window, shows every choice once and closes the editor (1 on a problem).</summary>
        public static void SelfTest()
        {
            var frames = 0;
            void Step()
            {
                if (++frames < 30) return; EditorApplication.update -= Step; var failures = 0; var shown = 0;
                try
                {
                    var window = GetWindow<CompareWindow>(); if (window._frame == null) window.CreateGUI();
                    for (var d = 0; d < 3; d++) for (var s = 0; s < 3; s++) for (var b = 0; b < Backdrops.Length; b++)
                    {
                        window._direction = d; window._state = s; window._backdrop = b; window.Draw();
                        var hud = window._frame.Q<VisualElement>("hud"); if (hud == null || hud.childCount == 0) failures++; else shown++;
                    }
                    window.Close();
                }
                catch (System.Exception exception) { failures++; Debug.LogError(exception); }
                Debug.Log($"event=hud_window shown={shown} failures={failures}"); EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
            EditorApplication.update += Step;
        }

        private void CreateGUI()
        {
            rootVisualElement.style.backgroundColor = new Color(.08f, .09f, .1f);
            _bar = new VisualElement(); _bar.style.flexDirection = FlexDirection.Row; _bar.style.flexWrap = Wrap.Wrap; _bar.style.paddingTop = 6; _bar.style.paddingBottom = 6; _bar.style.paddingLeft = 6; rootVisualElement.Add(_bar);
            _frame = new VisualElement(); _frame.style.flexGrow = 1; _frame.style.overflow = Overflow.Hidden; rootVisualElement.Add(_frame);
            _frame.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            rootVisualElement.focusable = true;
            rootVisualElement.RegisterCallback<KeyDownEvent>(e => { var n = e.keyCode == KeyCode.Alpha1 ? 0 : e.keyCode == KeyCode.Alpha2 ? 1 : e.keyCode == KeyCode.Alpha3 ? 2 : -1; if (n >= 0) { _direction = n; Draw(); } });
            Draw();
        }

        private void Choices(string[] labels, int chosen, System.Action<int> pick)
        {
            for (var i = 0; i < labels.Length; i++) { var n = i; var button = new Button(() => { pick(n); Draw(); }) { text = labels[i] }; if (i == chosen) button.style.backgroundColor = new Color(.13f, .45f, .43f); _bar.Add(button); }
            var gap = new VisualElement(); gap.style.width = 24; _bar.Add(gap);
        }

        private void Draw()
        {
            var all = HudDirection.All; _bar.Clear(); _frame.Clear();
            Choices(new[] { "1 · " + all[0].Title, "2 · " + all[1].Title, "3 · " + all[2].Title }, _direction, n => _direction = n);
            Choices(new[] { "Explorando", "Conversando", "Menu" }, _state, n => _state = n);
            Choices(new[] { Backdrops[0].Label, Backdrops[1].Label, Backdrops[2].Label }, _backdrop, n => _backdrop = n);
            var path = Path.Combine(Folder, Backdrops[_backdrop].File + ".png");
            if (!File.Exists(path)) { _frame.Add(new Label("Faltam as imagens de fundo em " + Folder + ". Rode a captura (ver docs/ui-directions/README.md).")); return; }
            if (_picture != null) DestroyImmediate(_picture); _picture = new Texture2D(2, 2, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp }; _picture.LoadImage(File.ReadAllBytes(path));
            var people = JsonUtility.FromJson<WorldBackdrop.People>(File.ReadAllText(Path.ChangeExtension(path, ".json")));
            var canvas = new VisualElement { name = "canvas" }; canvas.style.width = 1920; canvas.style.height = 1080; canvas.style.position = Position.Absolute; canvas.style.transformOrigin = new TransformOrigin(0, 0);
            canvas.Add(all[_direction].Stage((HudState)_state, _picture, people)); _frame.Add(canvas); Fit();
        }

        /// <summary>The 1920×1080 picture, whole, in whatever room the window has.</summary>
        private void Fit()
        {
            var canvas = _frame?.Q<VisualElement>("canvas"); if (canvas == null) return; var w = _frame.layout.width; var h = _frame.layout.height; if (!(w > 1f) || !(h > 1f)) return;
            var k = Mathf.Min(w / 1920f, h / 1080f); canvas.style.scale = new Scale(new Vector3(k, k, 1f)); canvas.style.left = (w - 1920f * k) / 2f; canvas.style.top = (h - 1080f * k) / 2f;
        }

        private void OnDestroy() { if (_picture != null) DestroyImmediate(_picture); }
    }
}
