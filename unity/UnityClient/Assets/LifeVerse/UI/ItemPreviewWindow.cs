using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Audio;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;
using Numerics = System.Numerics;

namespace LifeVerse.Unity.UI
{
    public enum ItemPreviewKind { None, Model, Texture, Animation, Audio, Package }

    /// <summary>
    /// "Pré-visualizar": what an inventory item really is, before it is used. A model turns on a small stage with its size and triangles, a picture can be brought closer, an animation plays on a bare skeleton
    /// (choose the clip, pause, drag the time), a sound plays with a position bar, and a saved construction lists its parts and the things it is made of, with which of them the person has. Nothing is placed in the world.
    /// </summary>
    public sealed class ItemPreviewWindow : FloatingWindow
    {
        public const long MaxBytes = 50L * 1024 * 1024;

        private readonly ApiContext _c; private readonly Label _info, _problem; private readonly VisualElement _stage;
        private ModelPreview _model; private Texture2D _picture; private GameObject _speaker; private AudioClip _clip; private IVisualElementScheduledItem _tick; private bool _closed;

        public ItemPreviewKind Kind { get; }
        /// <summary>True once what the item is has been drawn (or listed, or can be played).</summary>
        public bool Loaded { get; private set; }
        public string Problem => _problem.text;
        public string Info => _info.text;
        /// <summary>The lines of a construction: its parts, then the things it is made of (for the checks).</summary>
        public readonly List<string> Lines = new List<string>();

        public static ItemPreviewKind KindOf(string category, string mime)
        {
            category = (category ?? "").ToLowerInvariant(); mime = (mime ?? "").ToLowerInvariant();
            if (category == "packages") return ItemPreviewKind.Package;
            if (category == "animations") return ItemPreviewKind.Animation;
            if (category == "models" || mime.StartsWith("model/", StringComparison.Ordinal)) return ItemPreviewKind.Model;
            if (category == "textures" || mime.StartsWith("image/", StringComparison.Ordinal)) return ItemPreviewKind.Texture;
            if (category == "sounds" || mime.StartsWith("audio/", StringComparison.Ordinal)) return ItemPreviewKind.Audio;
            return ItemPreviewKind.None;
        }

        public ItemPreviewWindow(ApiContext context, Guid item, Guid asset, string name, string category, string mime, long size)
            : base(context.Ui, "item-preview", name.Length > 0 ? name : "Pré-visualizar", 460f, 520f, new Vector2(470, 80))
        {
            _c = context; Kind = KindOf(category, mime); SetIcon("eye");
            _info = UiKit.Text(InventoryManagerWindow.CategoryWords(category), "lv-small", "lv-muted"); _info.style.whiteSpace = WhiteSpace.Normal; Body.Add(_info);
            _problem = UiKit.Text("", "lv-small", "lv-bad"); _problem.style.whiteSpace = WhiteSpace.Normal; Body.Add(_problem);
            _stage = new VisualElement(); _stage.style.flexGrow = 1; _stage.style.flexShrink = 1; _stage.style.minHeight = 0; _stage.style.overflow = Overflow.Hidden; Body.Add(_stage);
            Closed += Release;
            if (Kind == ItemPreviewKind.None) { _problem.text = "Esse tipo de item não tem pré-visualização."; return; }
            if (Kind != ItemPreviewKind.Package && size > MaxBytes) { _problem.text = "Grande demais para pré-visualizar (o limite é " + (MaxBytes / 1024 / 1024) + " MB)."; return; }
            var waiting = UiKit.Text("Carregando…", "lv-muted"); _stage.Add(waiting);
            if (Kind == ItemPreviewKind.Package)
            {
                // the answers are watched on the window's body, which stays while the window is open
                this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items/" + item + "/contents"), r =>
                {
                    if (_closed) return; _stage.Clear();
                    if (r.Status < 200 || r.Status >= 300) { _problem.text = r.Status == 404 ? "Essa construção não está mais no seu inventário." : r.Status == 400 ? "Esse item não é uma construção." : "Não foi possível ler o conteúdo."; return; }
                    ShowPackage(r.Body);
                });
                return;
            }
            this.Then(_c.Api.GetBytesAsync("/api/v1/assets/" + asset + "/download"), r =>
            {
                if (_closed) return; _stage.Clear();
                if (r.Status != 200 || r.Bytes == null || r.Bytes.Length == 0) { _problem.text = "Não foi possível carregar o arquivo (sem conexão ou sem permissão)."; return; }
                if (Kind == ItemPreviewKind.Model) ShowModel(r.Bytes); else if (Kind == ItemPreviewKind.Texture) ShowTexture(r.Bytes); else if (Kind == ItemPreviewKind.Animation) ShowAnimation(r.Bytes); else ShowSound(r.Bytes);
            });
        }

        private async void ShowModel(byte[] bytes)
        {
            var report = LifeVerse.Unity.Models.ModelInspector.Inspect(bytes);
            _info.text += " · " + LifeVerse.Unity.Models.ModelInspector.SizeText(report.Size) + " · " + report.Triangles.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " triângulos";
            _model = new ModelPreview(420, 340, 200f); _model.Image.style.alignSelf = Align.Center; _stage.Add(_model.Image);
            var shown = _model; await shown.Show(bytes); if (_closed || shown != _model) return;
            Loaded = shown.Loaded; if (!shown.Loaded) _problem.text = shown.Problem;
        }

        private void ShowTexture(byte[] bytes)
        {
            _picture = new Texture2D(2, 2);
            if (!Pictures.Load(_picture, bytes)) { _problem.text = "Não foi possível ler a imagem."; return; }
            _info.text += " · " + _picture.width + " × " + _picture.height + " pontos";
            var view = new ScrollView(ScrollViewMode.VerticalAndHorizontal); view.style.flexGrow = 1; _stage.Add(view);
            var image = new Image { image = _picture, scaleMode = ScaleMode.ScaleToFit }; image.style.flexShrink = 0; view.Add(image);
            var fit = Mathf.Min(1f, 420f / Mathf.Max(1, _picture.width), 340f / Mathf.Max(1, _picture.height));
            void Zoom(float factor) { image.style.width = _picture.width * fit * factor; image.style.height = _picture.height * fit * factor; }
            var slider = new Slider("Aproximar", 1f, 8f) { value = 1f }; slider.RegisterValueChangedCallback(e => Zoom(e.newValue)); _stage.Add(slider); Zoom(1f); Loaded = true;
        }

        // ---- animations: the clips of the file on a bare skeleton ----
        private SkeletonClip _playing; private float _time; private bool _paused; private StickView _figure; private Slider _scrub;
        public IReadOnlyList<string> Clips { get; private set; } = new string[0];

        private void ShowAnimation(byte[] bytes)
        {
            var report = AnimationValidator.Validate(bytes); var names = SkeletonClip.Names(bytes);
            if (names == null || names.Count == 0 || report.Clips.Count == 0) { _problem.text = "Esse arquivo não tem uma animação que o jogo reconheça." + (report.Errors.FirstOrDefault() is { } first ? " (" + first.Message + ")" : ""); return; }
            Clips = names.ToList();
            _figure = new StickView(); _figure.style.flexGrow = 1; _stage.Add(_figure);
            var about = UiKit.Text("", "lv-small", "lv-muted");
            void Choose(string clipName)
            {
                _playing = SkeletonClip.Read(bytes, clipName); _time = 0f;
                about.text = _playing == null ? "Não foi possível ler esta animação." : "Dura " + _playing.Duration.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " s";
                Draw();
            }
            var pick = new DropdownField("Animação", names.ToList(), 0); pick.RegisterValueChangedCallback(e => Choose(e.newValue)); _stage.Add(pick); _stage.Add(about);
            _scrub = new Slider(0f, 1f); _scrub.RegisterValueChangedCallback(e => { if (_playing != null) { _paused = true; _time = e.newValue * _playing.Duration; Draw(); } }); _stage.Add(_scrub);
            Button play = null; play = UiKit.Button("Pausar", () => { _paused = !_paused; play.text = _paused ? "Tocar" : "Pausar"; }); _stage.Add(play);
            Choose(names[0]);
            _tick = _stage.schedule.Execute(() => { if (_paused || _playing == null) return; _time = Mathf.Repeat(_time + .033f, Mathf.Max(.001f, _playing.Duration)); _scrub.SetValueWithoutNotify(_time / Mathf.Max(.001f, _playing.Duration)); Draw(); }).Every(33);
            Loaded = _playing != null;
        }

        private void Draw() { if (_figure == null || _playing == null) return; _figure.Pose = StickFigure.Pose(_playing, _time); _figure.MarkDirtyRepaint(); }

        /// <summary>The bare skeleton seen from the front.</summary>
        private sealed class StickView : VisualElement
        {
            public Dictionary<string, Numerics.Vector3> Pose;
            public StickView() { style.minHeight = 240; generateVisualContent += Paint; }

            private void Paint(MeshGenerationContext context)
            {
                if (Pose == null) return; var size = contentRect.size; if (size.x < 10f || size.y < 10f) return;
                var scale = Mathf.Min(size.x, size.y) / 2.3f; var painter = context.painter2D;
                Vector2 At(Numerics.Vector3 p) => new Vector2(size.x / 2f + p.X * scale, size.y - 14f - p.Y * scale);
                painter.strokeColor = new Color(.08f, .45f, .41f); painter.lineWidth = 4f; painter.lineCap = LineCap.Round;
                foreach (var (from, to) in StickFigure.Lines()) { painter.BeginPath(); painter.MoveTo(At(Pose[from])); painter.LineTo(At(Pose[to])); painter.Stroke(); }
                painter.fillColor = new Color(.71f, .28f, .18f); painter.BeginPath(); painter.Arc(At(Pose["head"]), .11f * scale, 0f, 360f); painter.Fill();
            }
        }

        // ---- sounds ----
        public bool Playing => _speaker != null && _speaker.GetComponent<AudioSource>().isPlaying;
        public float Seconds => _clip != null ? _clip.length : 0f;

        private async void ShowSound(byte[] bytes)
        {
            var clip = await AudioDecode.DecodeAsync(bytes, "preview");
            if (_closed) { if (clip != null) UnityEngine.Object.Destroy(clip); return; }
            if (clip == null) { _problem.text = "Não foi possível ler o áudio (o jogo toca WAV, OGG e MP3 de até " + (AudioDecode.MaxBytes / 1024 / 1024) + " MB)."; return; }
            _clip = clip; _speaker = new GameObject("Item preview sound"); var source = BusSource.Make(_speaker, Bus.Effects, .8f, false).Source; source.clip = clip;
            var position = new ProgressBar { lowValue = 0f, highValue = Mathf.Max(.01f, clip.length), title = Clock(0f) + " / " + Clock(clip.length) }; _stage.Add(position);
            var loop = UiKit.Check("Repetir", false, on => source.loop = on);
            _stage.Add(UiKit.Row(UiKit.Button("Tocar", () => source.Play(), "lv-btn-primary"), UiKit.Button("Parar", () => source.Stop()), loop));
            _info.text += " · " + Clock(clip.length);
            _tick = _stage.schedule.Execute(() => { if (source == null) return; position.value = source.time; position.title = Clock(source.time) + " / " + Clock(clip.length); }).Every(100);
            Loaded = true;
        }

        private static string Clock(float seconds) => ((int)seconds / 60) + ":" + ((int)seconds % 60).ToString("00");

        // ---- constructions ----
        private void ShowPackage(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body); var root = document.RootElement;
                _info.text = "construção · " + SectionWindow.I(root, "objects") + " peça(s) · " + SectionWindow.I(root, "linksets") + " conjunto(s) · " + SectionWindow.I(root, "withScripts") + " com scripts · " + SectionWindow.I(root, "withAnimations") + " com animações";
                var list = new ScrollView(); list.style.flexGrow = 1; _stage.Add(list);
                void Line(string text, params string[] classes) { var label = UiKit.Text(text, classes); label.style.whiteSpace = WhiteSpace.Normal; list.Add(label); }
                var parts = root.GetProperty("parts").EnumerateArray().ToList();
                int ParentOf(JsonElement part) => part.TryGetProperty("parent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : -1;
                Line("Peças", "lv-bold", "lv-accent");
                foreach (var part in parts)
                {
                    var depth = 0; for (var at = ParentOf(part); at >= 0 && at < parts.Count && depth < 16; at = ParentOf(parts[at])) depth++;
                    var marks = new List<string>();
                    if (SectionWindow.I(part, "scripts") > 0) marks.Add(SectionWindow.I(part, "scripts") + " script(s)");
                    if (SectionWindow.I(part, "animations") > 0) marks.Add(SectionWindow.I(part, "animations") + " animação(ões)");
                    if (SectionWindow.I(part, "otherContent") > 0) marks.Add(SectionWindow.I(part, "otherContent") + " item(ns) dentro");
                    var text = new string(' ', depth * 4) + (depth > 0 ? "└ " : "") + SectionWindow.S(part, "name") + " (" + SectionWindow.S(part, "shape") + ")" + (marks.Count > 0 ? " · " + string.Join(", ", marks) : "");
                    Lines.Add(text); Line(text, "lv-small");
                }
                var assets = root.GetProperty("assets").EnumerateArray().ToList();
                if (assets.Count > 0)
                {
                    var missing = SectionWindow.I(root, "missing");
                    Line(missing == 0 ? "Você tem tudo de que ela é feita:" : "Faltam " + missing + " item(ns) que você ainda não tem (a construção pode não ficar completa):", "lv-bold", missing == 0 ? "lv-accent" : "lv-bad");
                    foreach (var asset in assets)
                    {
                        var held = asset.TryGetProperty("held", out var h) && h.ValueKind == JsonValueKind.True; var role = SectionWindow.S(asset, "role");
                        var text = (held ? "✓ " : "✗ ") + SectionWindow.S(asset, "name") + " · " + (role == "model" ? "modelo" : role == "texture" ? "textura" : "conteúdo") + (held ? "" : " (você não tem)");
                        Lines.Add(text); Line(text, "lv-small");
                    }
                }
                Loaded = true;
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException) { _problem.text = "O conteúdo recebido não pôde ser lido."; }
        }

        private void Release()
        {
            if (_closed) return; _closed = true; _tick?.Pause();
            _model?.Dispose(); _model = null;
            if (_picture != null) UnityEngine.Object.Destroy(_picture); _picture = null;
            if (_speaker != null) UnityEngine.Object.Destroy(_speaker); _speaker = null;
            if (_clip != null) UnityEngine.Object.Destroy(_clip); _clip = null;
        }
    }
}
