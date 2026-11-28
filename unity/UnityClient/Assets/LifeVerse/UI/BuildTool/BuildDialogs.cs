using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>A window of the build tool laid out from a sheet of <see cref="BuildLayout"/>; on a short screen it scrolls.</summary>
    public abstract class BuildDialog : FloatingWindow
    {
        /// <summary>The height the sheet leaves for the title strip.</summary>
        protected const float TitleHeight = 36f;
        public BuildCanvas Widgets { get; }
        protected readonly VisualElement Page; private readonly ScrollView _scroll; private readonly float _full;

        protected BuildDialog(UiRoot ui, string id, string title, string sheet, Vector2 position) : base(ui, id, title, BuildLayout.Sheet(sheet).Size.x, -1f, position)
        {
            Widgets = new BuildCanvas(sheet); name = id; AddToClassList("bt-window");
            if (Resources.Load<StyleSheet>("LifeVerse/UI/BuildTool") is { } style_) styleSheets.Add(style_);
            Body.RemoveFromClassList("lv-window-body"); Body.AddToClassList("bt-dialog-body");
            _full = Widgets.Sheet.Size.y - TitleHeight;
            _scroll = new ScrollView(ScrollViewMode.Vertical); _scroll.AddToClassList("bt-scroll"); _scroll.style.position = Position.Relative; _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; Body.Add(_scroll);
            Page = new VisualElement { pickingMode = PickingMode.Ignore }; Page.style.height = _full; Page.style.width = Widgets.Sheet.Size.x - 2f; Page.style.flexShrink = 0; _scroll.Add(Page);
            RegisterCallback<GeometryChangedEvent>(_ => Fit()); Fit();
        }

        private void Fit()
        {
            var screen = Ui.WindowLayer.layout.height; if (screen < 200f) screen = 1080f; var tall = Mathf.Min(_full, screen - TitleHeight - 24f);
            if (_scroll.style.height.keyword != StyleKeyword.Undefined || !Mathf.Approximately(_scroll.style.height.value.value, tall)) _scroll.style.height = tall;
            _scroll.verticalScrollerVisibility = tall < _full - 1f ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
            var top = resolvedStyle.top; if (float.IsFinite(top) && top + tall + TitleHeight > screen - 8f && top > 8f) style.top = Mathf.Max(8f, screen - tall - TitleHeight - 8f);
        }

        /// <summary>Where a component of the sheet goes inside the page.</summary>
        protected Rect At(string slot) { var area = Widgets.AreaOf(slot); area.y -= TitleHeight; return area; }

        /// <summary>Makes a component of the sheet and puts it where the sheet says.</summary>
        protected VisualElement Place(string slot)
        {
            var entry = Widgets.Sheet.Of(slot); var element = Widgets.Make(entry); if (element != null) Widgets.Put(slot, element, At(slot), Page); return element;
        }

        protected Button Press(string slot, Action run, bool primary = false)
        {
            if (Place(slot) is not Button button) return null; button.clicked += () => { LifeVerse.Unity.Audio.UiSounds.Click(); run(); }; if (primary) button.AddToClassList("lv-btn-primary"); return button;
        }

        protected Label Words(string text, Rect area, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList("bt-text"); foreach (var c in classes) label.AddToClassList(c);
            label.style.position = Position.Absolute; label.style.left = area.x; label.style.top = area.y; label.style.width = area.width; label.style.height = area.height; Page.Add(label); return label;
        }
    }

    /// <summary>"Opções da grade": the step of the grid and the magnet. What the viewer offers beyond that waits for the 3D editor.</summary>
    public sealed class BuildGridWindow : BuildDialog
    {
        public BuildSpinner Step { get; }
        public Toggle Magnet { get; }

        public BuildGridWindow(UiRoot ui, BuildEditor editor, Action<string, string> remember) : base(ui, "build-grid", "Opções da grade", "grid", new Vector2(492, 120))
        {
            Step = Place("GridResolution") as BuildSpinner; Step.Min = .1f; Step.Max = 5f; Step.Step = .1f; Step.Format = "0.00"; Step.Show(editor.Grid, true);
            Step.Committed += v => { editor.Grid = v; remember?.Invoke("build.move_step", v.ToString("0.##", CultureInfo.InvariantCulture)); };
            Place("GridDrawSize"); Place("GridSubUnit"); Place("grid_opacity_label"); Place("GridOpacity");
            Widgets.Spinner("GridDrawSize")?.Blank();
            foreach (var name in new[] { "GridDrawSize", "GridSubUnit", "GridOpacity" }) Widgets.Unavailable(name, "a grade desenhada no mundo chega com o editor 3D.");
            Magnet = new Toggle { text = "Ímã: encostar nos vizinhos", value = editor.SnapMagnet, tooltip = "Ao arrastar uma seta, a seleção é puxada para os cantos e as faces dos objetos próximos" }; Magnet.AddToClassList("bt-check"); Widgets.Put("grid-magnet", Magnet, new Rect(14f, 177f, 300f, 22f), Page);
            Magnet.RegisterValueChangedCallback(e => editor.SnapMagnet = e.newValue);
        }
    }

    /// <summary>The colour picker: red, green and blue, hue, saturation and light, the code, a field to drag on and ready colours.</summary>
    public sealed class BuildColourWindow : BuildDialog
    {
        private readonly Action<string> _chosen; private readonly Color _original; private Color _colour; private float _hue, _saturation, _light; private bool _applied, _filling;
        private readonly VisualElement _field, _bar, _mark, _barMark, _current; private Texture2D _ramp;
        /// <summary>The colour now chosen, as #RRGGBB.</summary>
        public string Hex => "#" + ColorUtility.ToHtmlStringRGB(_colour);

        public BuildColourWindow(UiRoot ui, string hex, Action<string> chosen) : base(ui, "build-colour", "Cor", "color-picker", new Vector2(492, 90))
        {
            _chosen = chosen; _original = ColorUtility.TryParseHtmlString(hex ?? "", out var parsed) ? parsed : Color.white; _original.a = 1f; _colour = _original; (_hue, _saturation, _light) = ToHsl(_colour);
            foreach (var name in new[] { "r_val_text", "g_val_text", "b_val_text", "h_val_text", "s_val_text", "l_val_text", "hex_text", "hash_text", "Current color:", "(Drag below to save.)" })
            {
                if (Place(name) is not Label label) continue; label.style.width = name == "(Drag below to save.)" || name == "Current color:" ? 190f : name == "hash_text" ? 16f : 78f; if (name == "(Drag below to save.)") label.text = "Cores prontas:";
            }
            foreach (var (name, max) in new[] { ("rspin", 255f), ("gspin", 255f), ("bspin", 255f), ("hspin", 360f), ("sspin", 100f), ("lspin", 100f) })
            {
                var spinner = Place(name) as BuildSpinner; spinner.Min = 0f; spinner.Max = max; spinner.Step = 1f; spinner.Format = "0"; var captured = name;
                spinner.Committed += v => { if (_filling) return; if (captured[0] == 'r' || captured[0] == 'g' || captured[0] == 'b') { var c = _colour; if (captured[0] == 'r') c.r = v / 255f; else if (captured[0] == 'g') c.g = v / 255f; else c.b = v / 255f; Set(c, true); } else SetHsl(captured[0] == 'h' ? v / 360f : _hue, captured[0] == 's' ? v / 100f : _saturation, captured[0] == 'l' ? v / 100f : _light, true); };
            }
            var code = Place("hex_color") as TextField; code.maxLength = 7; code.RegisterValueChangedCallback(e => { if (_filling) return; var text = e.newValue.Trim().TrimStart('#'); if (text.Length == 6 && ColorUtility.TryParseHtmlString("#" + text, out var c)) Set(c, true); else Show(); });

            _field = Painted("colour-field", new Rect(200f, 12f, 296f, 208f)); var gradient = new Texture2D(96, 64, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (var x = 0; x < 96; x++) for (var y = 0; y < 64; y++) gradient.SetPixel(x, y, FromHsl(x / 95f, y / 63f, .5f));
            gradient.Apply(); _field.style.backgroundImage = new StyleBackground(gradient);
            _mark = Dot(_field); Drag(_field, at => SetHsl(at.x, 1f - at.y, _light < .05f || _light > .95f ? .5f : _light, false));
            _bar = Painted("colour-light", new Rect(508f, 12f, 22f, 208f)); _barMark = Dot(_bar); _barMark.style.left = 4f; Drag(_bar, at => SetHsl(_hue, _saturation, 1f - at.y, false));
            _current = Painted("colour-current", new Rect(14f, 282f, 96f, 62f));
            for (var i = 0; i < 32; i++)
            {
                var preset = i < 16 ? Color.HSVToRGB(i / 16f, .7f, .85f) : Color.HSVToRGB(0f, 0f, (i - 16) / 15f); var swatch = new Button(() => Set(preset, true)) { tooltip = "#" + ColorUtility.ToHtmlStringRGB(preset) }; swatch.AddToClassList("lv-btn"); swatch.AddToClassList("bt-picture");
                Widgets.Put("colour-preset-" + i, swatch, new Rect(14f + i % 16 * 38f, 396f + i / 16 * 36f, 32f, 30f), Page); swatch.style.backgroundColor = preset; swatch.style.minHeight = 0;
            }
            var now = Place("apply_immediate") as Toggle; now.value = true;
            var pipette = Press("color_pipette", () => { }); pipette?.Add(new Icon("pin", 15)); Widgets.Unavailable("color_pipette", "pegar a cor do mundo chega com o editor 3D.");
            if (Place("copy_color_combobox") is DropdownField copy)
            {
                copy.choices = new List<string> { "Copiar como…", "Hex", "RGB" }; copy.SetValueWithoutNotify("Copiar como…");
                copy.RegisterValueChangedCallback(e => { if (e.newValue == "Hex") GUIUtility.systemCopyBuffer = Hex; else if (e.newValue == "RGB") { var c = (Color32)_colour; GUIUtility.systemCopyBuffer = c.r + ", " + c.g + ", " + c.b; } copy.SetValueWithoutNotify("Copiar como…"); });
            }
            Press("select_btn", Accept, true); Press("cancel_btn", Cancel);
            Closed += () => { if (_ramp != null) UnityEngine.Object.DestroyImmediate(_ramp); if (gradient != null) UnityEngine.Object.DestroyImmediate(gradient); };
            Show();
        }

        private bool Immediate => Widgets.Check("apply_immediate")?.value ?? false;
        public void Accept() { _chosen?.Invoke(Hex); Close(); }
        public void Cancel() { if (_applied) _chosen?.Invoke("#" + ColorUtility.ToHtmlStringRGB(_original)); Close(); }

        /// <summary>Chooses a colour; with "apply now" on, a finished choice is sent at once.</summary>
        public void Set(Color colour, bool finished)
        {
            colour.a = 1f; _colour = colour; var (h, s, l) = ToHsl(colour); if (s > .001f && l > .001f && l < .999f) { _hue = h; _saturation = s; } _light = l; Show(); if (finished) Send();
        }

        private void SetHsl(float h, float s, float l, bool finished) { _hue = Mathf.Clamp01(h); _saturation = Mathf.Clamp01(s); _light = Mathf.Clamp01(l); _colour = FromHsl(_hue, _saturation, _light); Show(); if (finished) Send(); }
        private void Send() { if (!Immediate) return; _applied = true; _chosen?.Invoke(Hex); }

        private void Show()
        {
            _filling = true; var c = (Color32)_colour;
            foreach (var (name, value) in new[] { ("rspin", (float)c.r), ("gspin", c.g), ("bspin", c.b), ("hspin", Mathf.Round(_hue * 360f)), ("sspin", Mathf.Round(_saturation * 100f)), ("lspin", Mathf.Round(_light * 100f)) }) Widgets.Spinner(name)?.Show(value, true);
            Widgets.Text("hex_color")?.SetValueWithoutNotify(ColorUtility.ToHtmlStringRGB(_colour));
            _filling = false;
            if (_current == null) return; _current.style.backgroundColor = _colour;
            _mark.style.left = _hue * 296f - 6f; _mark.style.top = (1f - _saturation) * 208f - 6f; _barMark.style.top = (1f - _light) * 208f - 6f;
            if (_ramp == null) { _ramp = new Texture2D(1, 48, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave }; _bar.style.backgroundImage = new StyleBackground(_ramp); }
            for (var y = 0; y < 48; y++) _ramp.SetPixel(0, y, FromHsl(_hue, _saturation, y / 47f)); _ramp.Apply(); _bar.MarkDirtyRepaint();
        }

        private VisualElement Painted(string name, Rect area) { var box = new VisualElement(); box.AddToClassList("bt-swatch"); Widgets.Put(name, box, area, Page); return box; }
        private static VisualElement Dot(VisualElement into) { var dot = new VisualElement { pickingMode = PickingMode.Ignore }; dot.AddToClassList("bt-dot"); into.Add(dot); return dot; }

        private void Drag(VisualElement area, Action<Vector2> at)
        {
            var dragging = false;
            void Read(Vector2 local) => at(new Vector2(Mathf.Clamp01(local.x / Mathf.Max(1f, area.layout.width)), Mathf.Clamp01(local.y / Mathf.Max(1f, area.layout.height))));
            area.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; dragging = true; area.CapturePointer(e.pointerId); Read(e.localPosition); e.StopPropagation(); });
            area.RegisterCallback<PointerMoveEvent>(e => { if (dragging) Read(e.localPosition); });
            area.RegisterCallback<PointerUpEvent>(e => { if (!dragging) return; dragging = false; area.ReleasePointer(e.pointerId); Send(); });
        }

        public static (float H, float S, float L) ToHsl(Color c)
        {
            var max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); var min = Mathf.Min(c.r, Mathf.Min(c.g, c.b)); var l = (max + min) / 2f; var d = max - min;
            if (d < .00001f) return (0f, 0f, l);
            var s = d / (1f - Mathf.Abs(2f * l - 1f)); var h = max == c.r ? (c.g - c.b) / d % 6f : max == c.g ? (c.b - c.r) / d + 2f : (c.r - c.g) / d + 4f; h /= 6f; if (h < 0f) h += 1f;
            return (h, Mathf.Clamp01(s), l);
        }

        public static Color FromHsl(float h, float s, float l)
        {
            var chroma = (1f - Mathf.Abs(2f * l - 1f)) * s; var sector = Mathf.Repeat(h, 1f) * 6f; var x = chroma * (1f - Mathf.Abs(sector % 2f - 1f)); var m = l - chroma / 2f;
            var (r, g, b) = sector < 1f ? (chroma, x, 0f) : sector < 2f ? (x, chroma, 0f) : sector < 3f ? (0f, chroma, x) : sector < 4f ? (0f, x, chroma) : sector < 5f ? (x, 0f, chroma) : (chroma, 0f, x);
            return new Color(Mathf.Clamp01(r + m), Mathf.Clamp01(g + m), Mathf.Clamp01(b + m), 1f);
        }
    }

    /// <summary>The texture picker of the build tool: the picture chosen, the patterns of the system and the images of the inventory, with a search.</summary>
    public sealed class BuildTextureWindow : BuildDialog
    {
        private readonly ApiContext _c; private readonly Action<bool, string> _chosen; private readonly Action _cleared; private readonly bool _system;
        private readonly (bool Builtin, string Id)? _original; private (bool Builtin, string Id)? _selected; private bool _empty, _applied;
        private readonly ScrollView _list; private readonly VisualElement _preview; private readonly List<(string Id, string Name)> _images = new List<(string, string)>(); private string _inventoryNote = "Lendo o inventário…"; private int _shown = -1;
        /// <summary>How many images of the inventory the list holds (-1 while it has not answered).</summary>
        public int InventoryCount => _shown;

        public BuildTextureWindow(ApiContext context, string title, (bool Builtin, string Id)? current, Action<bool, string> chosen, Action cleared, bool system) : base(context.Ui, "build-texture", title, "texture-picker", new Vector2(492, 120))
        {
            _c = context; _chosen = chosen; _cleared = cleared; _system = system; _original = current; _selected = current;
            _preview = new VisualElement(); _preview.AddToClassList("bt-swatch"); Widgets.Put("texture-preview", _preview, new Rect(14f, 12f, 190f, 190f), Page);
            if (Place("mode_selection") is DropdownField mode) { mode.choices = new List<string> { "Inventário" }; mode.SetValueWithoutNotify("Inventário"); mode.SetEnabled(false); }
            Place("size_lbl");
            Press("Default", () => Pick((true, "plywood"))); Press("Blank", Empty); Press("None", Empty);
            Widgets.Enable("Default", system);
            if (cleared == null) { Widgets.Enable("Blank", false); Widgets.Enable("None", false); }
            else { Widgets.Enable("Blank", system); Widgets.Enable("None", !system); }
            if (Widgets.Button("Blank") is { } blank) blank.tooltip = "Só a cor, sem imagem";
            Press("Pipette", () => { })?.Add(new Icon("pin", 15)); Widgets.Unavailable("Pipette", "pegar a textura do mundo chega com o editor 3D.");
            if (Place("inventory search editor") is TextField search) search.RegisterValueChangedCallback(_ => Draw());
            _list = new ScrollView(ScrollViewMode.Vertical) { name = "texture-list" }; _list.AddToClassList("bt-list"); _list.AddToClassList("bt-scroll"); Widgets.Put("inventory panel", _list, At("inventory panel"), Page);
            Press("Select", Accept, true); Press("Cancel", Cancel);
            if (Place("apply_immediate_check") is Toggle now) now.value = true;
            Draw(); ShowPreview();
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/inventory/items?category=textures&page=1&pageSize=100", null), r =>
            {
                if (r.Status != 200) { _inventoryNote = UiTasks.Explain(r.Status, r.Body); _shown = 0; Draw(); return; }
                foreach (var row in Json.Rows(r.Body)) { var asset = row.Id("assetId"); var label = row.First("name", "displayName"); _images.Add((asset.ToString(), label.Length > 0 ? label : asset.ToString().Substring(0, 8))); }
                _inventoryNote = _images.Count == 0 ? "Você não tem imagens no inventário." : ""; Draw(); ShowPreview();
            });
        }

        /// <summary>Takes a texture as the choice and applies it at once.</summary>
        public void Choose(bool builtin, string id) { _chosen?.Invoke(builtin, id); Close(); }

        public void Pick((bool Builtin, string Id) choice)
        {
            _selected = choice; _empty = false; Draw(); ShowPreview(); if (Immediate) { _applied = true; _chosen?.Invoke(choice.Builtin, choice.Id); }
        }

        private void Empty() { _selected = null; _empty = true; Draw(); ShowPreview(); if (Immediate) { _applied = true; _cleared?.Invoke(); } }
        private bool Immediate => Widgets.Check("apply_immediate_check")?.value ?? false;

        public void Accept()
        {
            if (!_applied || !Immediate) { if (_empty) _cleared?.Invoke(); else if (_selected is { } s) _chosen?.Invoke(s.Builtin, s.Id); } Close();
        }

        /// <summary>Closes; what was applied while choosing goes back to what there was.</summary>
        public void Cancel()
        {
            if (_applied) { if (_original is { } o) _chosen?.Invoke(o.Builtin, o.Id); else _cleared?.Invoke(); }
            Close();
        }

        private void Draw()
        {
            _list.Clear(); var wanted = SettingsCatalog.Fold(Widgets.Text("inventory search editor")?.value ?? "").Trim();
            bool Matches(string name) => wanted.Length == 0 || SettingsCatalog.Fold(name).Contains(wanted);
            if (_system)
            {
                Head("Do sistema");
                foreach (var (id, label) in BuiltInTextures.All) if (Matches(label)) Entry(true, id, label);
            }
            Head("Meu inventário");
            foreach (var (id, label) in _images) if (Matches(label)) Entry(false, id, label);
            if (_inventoryNote.Length > 0) { var note = UiKit.Text(_inventoryNote, "lv-small", "lv-muted"); note.style.whiteSpace = WhiteSpace.Normal; _list.Add(note); }
            if (_inventoryNote != "Lendo o inventário…") _shown = _images.Count;
        }

        private void Head(string text) { var head = UiKit.Text(text, "lv-small", "lv-bold"); head.style.marginTop = 4; head.style.flexShrink = 0; _list.Add(head); }

        private void Entry(bool builtin, string id, string label)
        {
            var row = new Button(() => Pick((builtin, id))) { name = "texture-" + id }; row.AddToClassList("lv-btn"); row.AddToClassList("bt-row"); row.EnableInClassList("bt-row-on", _selected is { } s && s.Builtin == builtin && s.Id == id);
            row.RegisterCallback<ClickEvent>(e => { if (e.clickCount == 2) { Pick((builtin, id)); Accept(); } });
            var thumb = new VisualElement { pickingMode = PickingMode.Ignore }; thumb.style.width = 20; thumb.style.height = 20; thumb.style.marginRight = 7; thumb.style.marginLeft = 8; thumb.style.flexShrink = 0;
            if (builtin) thumb.style.backgroundImage = new StyleBackground(BuiltInTextures.Get(id)); else thumb.Add(new Icon("image", 18));
            row.Add(thumb); row.Add(UiKit.Text(label, "bt-text")); _list.Add(row);
        }

        private void ShowPreview()
        {
            _preview.Clear(); var builtin = _selected is { Builtin: true } s ? BuiltInTextures.Get(s.Id) : null;
            _preview.style.backgroundImage = builtin != null ? new StyleBackground(builtin) : new StyleBackground(StyleKeyword.None);
            if (builtin == null) { var mark = new Icon(_selected != null ? "image" : "close", 48); mark.style.alignSelf = UnityEngine.UIElements.Align.Center; mark.style.marginTop = StyleKeyword.Auto; mark.style.marginBottom = StyleKeyword.Auto; _preview.Add(mark); }
            if (Widgets.Words("size_lbl") is { } size) size.text = builtin != null ? builtin.width + " × " + builtin.height : _selected is { Builtin: false } own ? _images.FirstOrDefault(i => i.Id == own.Id).Name ?? "Imagem do inventário" : "Sem imagem";
        }
    }

    /// <summary>The media of a surface: an address shown as picture or video, looping and starting by itself.</summary>
    public sealed class BuildMediaWindow : BuildDialog
    {
        private readonly Action<JsonObject> _apply; private readonly DropdownField _type; private readonly Label _problem;
        public TextField Address => Widgets.Text("home_url");

        public BuildMediaWindow(UiRoot ui, string materialJson, Action<JsonObject> apply) : base(ui, "build-media", "Mídia da superfície", "media", new Vector2(492, 90))
        {
            _apply = apply; JsonObject media = null; try { media = JsonNode.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson)?["media"] as JsonObject; } catch (JsonException) { }
            string Read(string key) => media?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
            bool Flag(string key) => media?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;
            var tabs = new VisualElement(); tabs.style.flexDirection = FlexDirection.Row; Widgets.Put("tab_container", tabs, new Rect(3f, 3f, 400f, 28f), Page);
            foreach (var tab in new[] { "Geral", "Segurança", "Permissões" }) { var button = new Button { text = tab }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.style.width = 130; button.style.height = 26; if (tab == "Geral") button.AddToClassList("lv-btn-selected"); else { button.SetEnabled(false); button.tooltip = "Ainda não: precisa de campos novos no servidor."; } tabs.Add(button); }
            Place("home_label"); Place("current_url_label"); Place("preview_label"); Place("media_setting_note"); Place("size_label"); Place("X_label");
            var preview = new Label("Sem prévia: o jogo mostra a mídia no próprio objeto.") { pickingMode = PickingMode.Ignore }; preview.AddToClassList("bt-text"); preview.AddToClassList("bt-wrap"); preview.AddToClassList("bt-soft"); preview.AddToClassList("bt-swatch"); preview.style.unityTextAlign = TextAnchor.MiddleCenter; Widgets.Put("preview_media", preview, At("preview_media"), Page);
            var address = Place("home_url") as TextField; address.maxLength = 500; address.isDelayed = false; address.SetValueWithoutNotify(Read("url"));
            var current = Place("current_url") as Label; current.text = Read("url"); current.tooltip = current.text;
            Press("current_url_reset_btn", () => address.value = current.text);
            var loop = Place("auto_loop") as Toggle; loop.value = Flag("loop"); var play = Place("auto_play") as Toggle; play.value = Flag("playing");
            Place("auto_zoom"); Place("auto_scale"); Place("width_pixels"); Place("height_pixels");
            Widgets.Spinner("width_pixels")?.Blank(); Widgets.Spinner("height_pixels")?.Blank();
            foreach (var name in new[] { "auto_zoom", "auto_scale", "width_pixels", "height_pixels" }) Widgets.Unavailable(name, "o servidor guarda só o endereço, o tipo, repetir e tocar.");
            var kind = At("current_url_reset_btn"); Words("Tipo", new Rect(16f, kind.y, 40f, 29f));
            _type = new DropdownField(new List<string> { "Imagem", "Vídeo" }, Read("type") == "video" ? 1 : 0); _type.AddToClassList("bt-combo"); Widgets.Put("media-type", _type, new Rect(60f, kind.y, 130f, 29f), Page);
            _problem = Words("", new Rect(16f, At("OK").y - 40f, 495f, 36f), "bt-wrap", "bt-bad-words");
            Press("OK", () => { if (Apply()) Close(); }, true); Press("Cancel", Close); Press("Apply", () => Apply());
        }

        /// <summary>Sends the media, or says what is wrong with the address.</summary>
        public bool Apply()
        {
            var type = _type.index == 1 ? "video" : "image"; var url = Address.value.Trim(); var problem = BuildWindow.MediaProblem(type, url); _problem.text = problem ?? "";
            if (problem != null) return false;
            _apply?.Invoke(new JsonObject { ["type"] = type, ["url"] = url, ["playing"] = Widgets.Check("auto_play").value, ["loop"] = Widgets.Check("auto_loop").value });
            Widgets.Words("current_url").text = url; return true;
        }
    }

    /// <summary>The rest of the light of an object: its shadow, the face it comes from and the opening of the cone.</summary>
    public sealed class BuildLightWindow : FloatingWindow
    {
        private readonly BuildEditor _b; private bool _filling;
        public Toggle Shadow { get; }
        public DropdownField Face { get; }
        public BuildSpinner Cone { get; }
        private int[] _faces = Array.Empty<int>();

        public BuildLightWindow(ApiContext context, BuildEditor editor, Action<Action<JsonObject>> edit) : base(context.Ui, "build-light", "Mais sobre a luz", 340f, -1f, new Vector2(492, 200))
        {
            _b = editor;
            Shadow = UiKit.Check("Projeta sombra (só as primeiras luzes do mundo)", false, v => { if (!_filling) edit(l => l["shadow"] = v); });
            Face = new DropdownField("Sai de", new List<string> { "O objeto inteiro" }, 0); Face.RegisterValueChangedCallback(_ => { if (_filling) return; var i = Face.index; edit(l => { if (i <= 0 || i - 1 >= _faces.Length) l.Remove("face"); else l["face"] = _faces[i - 1]; }); });
            Cone = new BuildSpinner("Cone (graus)", 110f, "Abertura do cone") { Min = 10f, Max = 180f, Step = 5f, Format = "0" }; Cone.style.height = 27; Cone.style.marginTop = 6; Cone.Committed += v => { if (!_filling) edit(l => l["cone"] = Math.Round(v)); };
            var note = UiKit.Text("Luzes e reflexos custam à placa de vídeo: só as primeiras do mundo ficam acesas (o limite está em Configurações › Gráficos).", "lv-small", "lv-muted"); note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 8;
            Body.Add(Shadow); Body.Add(Face); Body.Add(Cone); Body.Add(note);
            _b.Changed += Refresh; Closed += () => _b.Changed -= Refresh; Refresh();
        }

        private void Refresh()
        {
            var id = _b.Selection.Count > 0 ? _b.Selection[0] : Guid.Empty; var state = id != Guid.Empty ? _b.Latest(id) : null; var light = state != null ? WorldObjects.ReadLight(state.MaterialJson) : null;
            _filling = true;
            try
            {
                Shadow.SetEnabled(light != null); Face.SetEnabled(light != null); Cone.SetEnabled(light != null);
                _faces = _b.FacesOfSelection(); var choices = new List<string> { "O objeto inteiro" }; choices.AddRange(_faces.Select(i => "Só a face " + BuildWindow.FaceName(i))); if (!choices.SequenceEqual(Face.choices)) Face.choices = choices;
                if (light == null) { Cone.Blank(); return; }
                Shadow.SetValueWithoutNotify(light.Shadow); Cone.Show(light.Cone);
                var at = light.Face >= 0 ? Array.IndexOf(_faces, light.Face) + 1 : 0; Face.SetValueWithoutNotify(choices[Mathf.Clamp(at, 0, choices.Count - 1)]);
            }
            finally { _filling = false; }
        }
    }

    /// <summary>The groups of the person, to give an object to one of them (or to none).</summary>
    public sealed class BuildGroupWindow : FloatingWindow
    {
        private readonly VisualElement _list;
        public int Count { get; private set; } = -1;

        public BuildGroupWindow(ApiContext context, Action<Guid?> chosen) : base(context.Ui, "build-group", "Escolher o grupo", 320f, 360f, new Vector2(492, 160))
        {
            Body.Add(UiKit.Button("Nenhum grupo", () => { chosen?.Invoke(null); Close(); }));
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; _list = new VisualElement(); scroll.Add(_list); Body.Add(scroll);
            this.Then(context.Api.ApiAsync("GET", "/api/v1/groups/mine"), r =>
            {
                if (r.Status != 200) { _list.Add(UiKit.Text(UiTasks.Explain(r.Status, r.Body), "lv-bad")); Count = 0; return; }
                Count = 0;
                foreach (var row in Json.Rows(r.Body)) { var group = row.Id("id", "groupId"); if (group == Guid.Empty) continue; Count++; var label = row.First("name"); _list.Add(UiKit.Button(label.Length > 0 ? label : group.ToString().Substring(0, 8), () => { chosen?.Invoke(group); Close(); }, "lv-btn-flat")); }
                if (Count == 0) _list.Add(UiKit.Text("Você não está em nenhum grupo.", "lv-small", "lv-muted"));
            });
        }
    }
}
