using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeVerse.Unity.Avatar;
using LifeVerse.Unity.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>The five tools at the top of the window, in the viewer's order.</summary>
    public enum BuildMode { Focus, Move, Edit, Create, Land }

    /// <summary>What the build tool reaches outside itself. Everything is optional: the checks open the window with only an editor.</summary>
    public sealed class BuildLinks
    {
        public TerrainEditor Terrain;
        public Func<FollowCamera> Camera;
        /// <summary>Where the person is (island coordinates).</summary>
        public Func<Vector3> Where;
        /// <summary>Keeps a build choice among the settings (key, value).</summary>
        public Action<string, string> Remember;
        /// <summary>Opens "Sobre este terreno" and "Gerenciar terrenos" for where the person is.</summary>
        public Action AboutLand, ManageLand;
    }

    /// <summary>
    /// The build tool, laid out as the Build/Edit floater of the official Second Life viewer (positions in <see cref="BuildLayout"/>) with the look of LifeVerse: five tools at the top
    /// (Focus, Move, Edit, Create, Land), what the chosen tool offers, the line about the selection and the five tabs (General, Object, Features, Texture, Content); in Land the parcel
    /// panel takes the place of the tabs. It only reads what the island confirmed and sends commands: the island and the API decide. What has nothing behind it yet is greyed out and
    /// says why.
    /// </summary>
    public sealed partial class BuildWindow : FloatingWindow
    {
        /// <summary>Where the window opens (reference pixels) and where the part that can scroll begins.</summary>
        public const float DefaultX = 50f, DefaultY = 70f, ScrollTop = 296f;
        /// <summary>The width of the game's bottom bar, in panel units: on a screen narrow enough for it to reach under the window, the window ends above it.</summary>
        public const float BottomBarWidth = 864f;

        public static readonly string[] TabNames = { "Geral", "Objeto", "Recursos", "Textura", "Conteúdo" };
        private static readonly (string Slot, BuildMode Mode, string Icon, string Name)[] ModeButtons = { ("button focus", BuildMode.Focus, "search", "Foco"), ("button move", BuildMode.Move, "hand", "Mover"), ("button edit", BuildMode.Edit, "move-all", "Editar"), ("button create", BuildMode.Create, "cube", "Criar"), ("button land", BuildMode.Land, "mountain", "Terreno") };

        private readonly ApiContext _c; private readonly BuildEditor _b; private readonly BuildLinks _links;
        private readonly BuildCanvas _ui = new BuildCanvas("tools");
        private readonly VisualElement _top, _under, _land; private readonly ScrollView _scroll; private readonly Dictionary<string, VisualElement> _pages = new Dictionary<string, VisualElement>();
        private readonly Dictionary<string, Button> _tabs = new Dictionary<string, Button>(); private VisualElement _tabBar;
        private string _message = ""; private bool _messageBad; private float _messageUntil; private bool _filling;
        private readonly List<FloatingWindow> _children = new List<FloatingWindow>();

        public BuildEditor Editor => _b;
        public BuildCanvas Widgets => _ui;
        public BuildMode Mode { get; private set; } = BuildMode.Edit;
        public string CurrentTab { get; private set; } = "Geral";
        /// <summary>The last thing the window had to say (the instruction of the tool when it had nothing).</summary>
        public string StatusText => _message.Length > 0 ? _message : _ui.Words("text status").text;
        public event Action ModeChanged;

        public BuildWindow(ApiContext context, BuildEditor editor, BuildLinks links = null) : base(context.Ui, "build", "Construir", BuildLayout.Sheet("tools").Size.x, -1f, new Vector2(DefaultX, DefaultY))
        {
            _c = context; _b = editor; _links = links ?? new BuildLinks(); name = "build-window";
            var sheet = Resources.Load<StyleSheet>("LifeVerse/UI/BuildTool"); if (sheet != null) styleSheets.Add(sheet);
            AddToClassList("bt-window"); if (!context.Ui.Settings.Windows.ContainsKey("build")) style.top = DefaultY;   // under the top bars the floating windows avoid: the viewer's tool is that tall
            Bar.AddToClassList("bt-bar"); Title.style.display = DisplayStyle.None; if (this.Q("title-icon") is { } mark) mark.style.display = DisplayStyle.None;
            var menu = new Button(OpenMenu) { tooltip = "Mais ações" }; menu.name = "build-menu-button"; menu.AddToClassList("lv-btn"); menu.AddToClassList("lv-window-button"); menu.Add(new Icon("dots", 13)); Bar.Insert(Bar.IndexOf(MinimizeButton), menu);

            Body.RemoveFromClassList("lv-window-body"); Body.AddToClassList("bt-body"); Body.pickingMode = PickingMode.Ignore; Body.style.left = -1; Body.style.top = -1; Body.style.width = _ui.Sheet.Size.x;
            _top = Layer("build-top"); Body.Add(_top);
            _scroll = new ScrollView(ScrollViewMode.Vertical) { name = "tab-scroll" }; _scroll.AddToClassList("bt-scroll"); _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden; _scroll.style.left = 0; _scroll.style.top = ScrollTop; _scroll.style.width = _ui.Sheet.Size.x; Body.Add(_scroll);
            _under = new VisualElement { name = "build-under" }; _under.style.height = _ui.Sheet.Size.y - ScrollTop; _under.style.flexShrink = 0; _scroll.Add(_under);
            foreach (var tab in TabNames) { var page = Layer("page-" + tab); _pages[tab] = page; _under.Add(page); }
            _land = Layer("page-terrain"); _under.Add(_land);

            foreach (var slot in _ui.Sheet.Slots)
            {
                if (slot.Type == "radio_group") continue;
                var element = _ui.Make(slot, Special); if (element == null) continue;
                var area = BuildLayout.Place(slot, _ui.Sheet.Size.x);   // by the slot: two names repeat from tab to tab
                var parent = slot.Tab != null ? _pages[slot.Tab == "Conteúdo" ? "Conteúdo" : slot.Tab] : slot.Mode == "Terreno" && area.y >= ScrollTop ? _land : _top;
                if (parent != _top) area.y -= ScrollTop;
                if (slot.Type == "view_border") { if (slot.Area.width < 1f) area = new Rect(area.x, area.y, 1f, Mathf.Min(slot.Area.height, _ui.Sheet.Size.y - slot.Area.y - 12f)); else area.height = 1f; }
                if (slot.Type == "tab_container") area.height = 36f;
                _ui.Put(slot.Name, element, area, parent);
                if (slot.Hidden) element.style.display = DisplayStyle.None;
            }
            BuildTop(); BuildGeneral(); BuildObject(); BuildFeatures(); BuildTexture(); BuildContent(); BuildLand();
            _b.Changed += Refresh; _b.Said += Hear; if (_links.Terrain != null) _links.Terrain.Changed += Refresh;
            Closed += Left;
            // a model takes a moment to arrive, and a message goes away by itself
            schedule.Execute(Tick).Every(400);
            Ui.WindowLayer.RegisterCallback<GeometryChangedEvent>(OnScreen); RegisterCallback<GeometryChangedEvent>(_ => Fit());
            SetMode(BuildMode.Edit);
        }

        private VisualElement Layer(string name)
        {
            var layer = new VisualElement { name = name, pickingMode = PickingMode.Ignore }; layer.style.position = Position.Absolute; layer.style.left = 0; layer.style.top = 0; layer.style.width = _ui.Sheet.Size.x; layer.style.height = 0; return layer;
        }

        // ---- the frame ----

        private void OnScreen(GeometryChangedEvent _) => Fit();

        /// <summary>As tall as the layout when the screen allows; otherwise what is under the tabs scrolls (and on a narrow screen the window ends above the bottom bar).</summary>
        private void Fit()
        {
            if (Minimized) return;
            var screen = Ui.WindowLayer.layout; var width = screen.width > 1f ? screen.width : 1920f; var height = screen.height > 1f ? screen.height : 1080f;
            var left = float.IsFinite(resolvedStyle.left) ? resolvedStyle.left : DefaultX; var top = float.IsFinite(resolvedStyle.top) ? resolvedStyle.top : DefaultY;
            var underBar = left + _ui.Sheet.Size.x > (width - BottomBarWidth) / 2f && left < (width + BottomBarWidth) / 2f;
            var bottom = underBar ? BottomInset : 8f;
            if (top + _ui.Sheet.Size.y > height - bottom && top > 8f) { top = Mathf.Max(8f, height - bottom - _ui.Sheet.Size.y); style.top = top; }
            var tall = Mathf.Min(_ui.Sheet.Size.y, Mathf.Max(ScrollTop + 160f, height - top - bottom));
            if (!Mathf.Approximately(style.height.value.value, tall) || style.height.keyword != StyleKeyword.Undefined) { style.height = tall; Body.style.height = tall; _scroll.style.height = tall - ScrollTop - 6f; }
            _scroll.verticalScrollerVisibility = tall < _ui.Sheet.Size.y - 1f ? ScrollerVisibility.Auto : ScrollerVisibility.Hidden;
        }

        protected override void OnMinimized(bool minimized)
        {
            Bar.EnableInClassList("bt-bar-folded", minimized); Title.style.display = minimized ? DisplayStyle.Flex : DisplayStyle.None; if (!minimized) { style.height = StyleKeyword.Null; Fit(); } RefreshTutorial();
        }

        // ---- special components of the sheet ----

        private VisualElement Special(BuildSlot slot)
        {
            var mode = Array.FindIndex(ModeButtons, m => m.Slot == slot.Name);
            if (mode >= 0)
            {
                var entry = ModeButtons[mode]; var button = new Button(() => SetMode(entry.Mode)) { tooltip = entry.Name }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-icon-btn"); button.AddToClassList("bt-mode"); button.Add(new Icon(entry.Icon, 22)); return button;
            }
            if (slot.Name.StartsWith("Tool", StringComparison.Ordinal)) return ShapeButton(slot);
            switch (slot.Name)
            {
                case "Object Info Tabs": return TabBar();
                case "text status": { var label = new Label { pickingMode = PickingMode.Ignore }; label.AddToClassList("bt-text"); label.AddToClassList("bt-status"); return label; }
                case "selection_count": { var button = new Button(OpenInfo) { tooltip = "Limites e histórico" }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-link"); return button; }
                case "Options...": { var button = new Button(OpenGrid) { tooltip = "Opções da grade" }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-icon-btn"); button.Add(new Icon("settings", 15)); return button; }
                case "Creator Icon": case "Owner Icon": { var face = new VisualElement(); face.AddToClassList("bt-face"); var initial = new Label(); initial.AddToClassList("lv-face-initial"); face.Add(initial); return face; }
                case "button set group": { var button = new Button(PickGroup) { tooltip = "Escolher o grupo" }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-icon-btn"); button.Add(new Icon("people", 15)); return button; }
                case "Group Name Proxy": { var label = new TextField { isReadOnly = true }; label.AddToClassList("bt-input"); return label; }
                case "colorswatch": return null;   // two sheets of components have this name; the tabs make their own
                case "light texture control": case "texture control": case "bumpytexture control": case "pbr_control": { var tile = new Button(); tile.AddToClassList("lv-btn"); tile.AddToClassList("bt-picture"); return tile; }
                case "contents_inventory": { var list = new ScrollView(ScrollViewMode.Vertical); list.AddToClassList("bt-list"); list.AddToClassList("bt-scroll"); return list; }
                case "media_info": { var label = new Label(); label.AddToClassList("bt-text"); return label; }
                case "sculpt texture control": case "title_media": case "Owner Group Icon": case "pathfinding_attrs_panel": return new VisualElement { style = { display = DisplayStyle.None } };
            }
            if (slot.Type == "menu_button") { var button = new Button { tooltip = "Copiar e colar" }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-btn"); button.AddToClassList("bt-icon-btn"); button.AddToClassList("lv-btn-flat"); button.Add(new Icon("copy", 13)); return button; }
            return null;
        }

        private VisualElement TabBar()
        {
            _tabBar = new VisualElement(); _tabBar.AddToClassList("bt-tabs");
            foreach (var tab in TabNames) { var captured = tab; var button = new Button(() => Show(captured)) { text = tab, name = "tab-" + tab }; button.AddToClassList("lv-btn"); button.AddToClassList("bt-tab"); button.style.height = 33; _tabs[tab] = button; _tabBar.Add(button); }
            var line = new VisualElement { pickingMode = PickingMode.Ignore }; line.AddToClassList("bt-tab-line"); _tabBar.Add(line); return _tabBar;
        }

        // ---- modes and tabs ----

        public void SetMode(BuildMode mode)
        {
            Mode = mode;
            var terrain = _links.Terrain;
            if (mode == BuildMode.Land) { _b.SetActive(false); terrain?.SetActive(true); }
            else { terrain?.SetActive(false); _b.SetActive(true); }
            _b.Selecting = mode != BuildMode.Focus; _b.Handles = mode == BuildMode.Edit && !_b.FacePick;
            if (mode != BuildMode.Edit) _b.FacePick = false;
            var camera = _links.Camera?.Invoke(); if (camera != null) camera.ToolGesture = mode == BuildMode.Focus ? FocusGesture : LifeVerse.Shared.Camera.CameraGesture.None;
            _message = ""; ModeChanged?.Invoke(); Refresh();
        }

        /// <summary>Shows a tab ("Geral", "Objeto", "Recursos", "Textura", "Conteúdo"); "Limites" opens the window with the limits and the history.</summary>
        public void Show(string tab)
        {
            if (tab == "Limites") { OpenInfo(); return; }
            if (!_pages.ContainsKey(tab)) return; if (Mode == BuildMode.Land) SetMode(BuildMode.Edit); CurrentTab = tab; Refresh();
        }

        private void Left()
        {
            if (_links.Camera?.Invoke() is { } camera) camera.ToolGesture = LifeVerse.Shared.Camera.CameraGesture.None;
            _b.Changed -= Refresh; _b.Said -= Hear; if (_links.Terrain != null) { _links.Terrain.Changed -= Refresh; _links.Terrain.SetActive(false); }
            Ui.WindowLayer.UnregisterCallback<GeometryChangedEvent>(OnScreen); _b.Selecting = true; _b.Handles = true; _b.FacePick = false; _b.SetActive(false);
            foreach (var child in _children.ToList()) child.Close(); _children.Clear();
        }

        /// <summary>A window of the tool: it closes with it, and asking for the same one again brings it forward (<paramref name="fresh"/> opens it anew, with what is chosen now).</summary>
        private FloatingWindow Child(string id, Func<FloatingWindow> make, bool fresh = false)
        {
            var open = _children.FirstOrDefault(w => w.IsOpen && w.Id == id); if (open != null && !fresh) { open.Raise(); return open; }
            open?.Close();
            var window = make(); if (Resources.Load<StyleSheet>("LifeVerse/UI/BuildTool") is { } sheet) window.styleSheets.Add(sheet);
            _children.Add(window); window.Closed += () => _children.Remove(window); window.Open(); return window;
        }

        // ---- messages ----

        private void Hear(string text) => Say(text, text.Contains("recusa") || text.Contains("não") || text.Contains("Não"));

        private void Say(string text, bool error) { _message = text ?? ""; _messageBad = error; _messageUntil = Time.realtimeSinceStartup + 7f; ShowStatus(); }

        private string Instruction()
        {
            switch (Mode)
            {
                case BuildMode.Focus: return "Arraste no mundo para mover a câmera";
                case BuildMode.Move: return "Clique num objeto para escolhê-lo";
                case BuildMode.Create: return "Escolha uma forma: ela aparece à sua frente";
                case BuildMode.Land: return _links.Terrain != null ? "Segure o botão para mudar o terreno" : "Terreno";
                default: return _b.FacePick ? "Clique numa face" : _b.Mode == BuildEditor.GizmoMode.Rotate ? "Arraste uma alça para girar" : _b.Mode == BuildEditor.GizmoMode.Scale ? "Arraste uma alça para esticar" : "Arraste uma seta para mover";
            }
        }

        private void ShowStatus()
        {
            var label = _ui.Words("text status"); var own = _message.Length > 0 && Time.realtimeSinceStartup < _messageUntil;
            // on the terrain the line is the terrain's own: what it may do, and how the last order went
            var terrain = Mode == BuildMode.Land && _links.Terrain != null && _links.Terrain.Status.Length > 0 ? _links.Terrain.Status : null;
            label.text = own ? _message : terrain ?? Instruction(); label.tooltip = label.text; label.EnableInClassList("lv-bad", own ? _messageBad : terrain != null && !_links.Terrain.CanSculpt);
        }

        // ---- reading what the island confirmed ----

        private float _asked = -10f; private string _askedFor = "";

        private WorldObjectState First() => _b.Selection.Count > 0 ? _b.StateOf(_b.Selection[0]) : null;
        private List<WorldObjectState> Selected() => _b.Selection.Select(_b.StateOf).Where(s => s != null).ToList();

        private void Tick()
        {
            if (_message.Length > 0 && Time.realtimeSinceStartup >= _messageUntil) _message = "";
            ShowStatus(); var first = First();
            if (first != null && CurrentTab == "Recursos" && ClipsKey(first) != _clipsShown) Refresh();
        }

        public void Refresh()
        {
            _filling = true;
            try
            {
                var all = Selected(); var first = all.Count > 0 ? all[0] : null;
                RefreshTop(all, first);
                var land = Mode == BuildMode.Land;
                Display(_tabBar, !land); Display(_land, land); foreach (var pair in _pages) Display(pair.Value, !land && pair.Key == CurrentTab);
                foreach (var pair in _tabs) { pair.Value.EnableInClassList("lv-btn-selected", pair.Key == CurrentTab); var usable = pair.Key == "Geral" || pair.Key == "Objeto" || first != null; pair.Value.SetEnabled(usable); }
                if (first == null && CurrentTab != "Geral" && CurrentTab != "Objeto") { CurrentTab = "Geral"; foreach (var pair in _pages) Display(pair.Value, !land && pair.Key == CurrentTab); foreach (var pair in _tabs) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == CurrentTab); }
                RefreshGeneral(all, first); RefreshObject(all, first); RefreshFeatures(all, first); RefreshTexture(all, first); RefreshContent(all, first); RefreshLand(); RefreshTutorial();
                ShowStatus(); AskLimits(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is FormatException) { Debug.LogWarning("[LifeVerse] construção: " + exception.Message); }
            finally { _filling = false; }
        }

        private static void Display(VisualElement element, bool on) { if (element != null) element.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; }

        // ---- helpers shared by the tabs ----

        private static JsonObject Parse(string json) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? new JsonObject(); } catch (JsonException) { return new JsonObject(); } }
        private static string Text(JsonObject a, string key) => a?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        private static bool Flag(JsonObject a, string key, bool fallback) => a?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;
        private static float Number(JsonObject a, string key, float fallback) => a?[key] is JsonValue v && v.TryGetValue<double>(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? (float)n : fallback;

        /// <summary>The value several objects share, or null when they differ.</summary>
        private static float? Same(IReadOnlyList<WorldObjectState> all, Func<WorldObjectState, float> read)
        {
            if (all.Count == 0) return null; var first = read(all[0]); for (var i = 1; i < all.Count; i++) if (Mathf.Abs(read(all[i]) - first) > 1e-4f) return null; return first;
        }

        private void Number(string name, float? value, bool enabled)
        {
            var spinner = _ui.Spinner(name); if (spinner == null) return; _ui.Enable(name, enabled);
            if (!enabled && value == null) spinner.Blank(); else spinner.Show(value);
        }

        private void Check(string name, IReadOnlyList<WorldObjectState> all, Func<WorldObjectState, bool> read, bool enabled)
        {
            var toggle = _ui.Check(name); if (toggle == null) return; _ui.Enable(name, enabled);
            var values = all.Select(read).Distinct().ToList(); var mixed = values.Count > 1;
            toggle.showMixedValue = mixed; if (!mixed) toggle.SetValueWithoutNotify(values.Count == 1 && values[0]);
        }

        private void Choice(string name, IReadOnlyList<string> choices, int index, bool enabled)
        {
            var combo = _ui.Combo(name); if (combo == null) return; _ui.Enable(name, enabled);
            if (!choices.SequenceEqual(combo.choices)) combo.choices = choices.ToList();
            if (index < 0 || index >= choices.Count) combo.SetValueWithoutNotify(choices.Count > 0 && index != -1 ? choices[0] : "(vários)"); else combo.SetValueWithoutNotify(choices[index]);
        }

        private void OnCheck(string name, Action<bool> run) { var toggle = _ui.Check(name); toggle?.RegisterValueChangedCallback(e => { if (_filling) return; LifeVerse.Unity.Audio.UiSounds.Play(LifeVerse.Unity.Audio.UiSound.Toggle); toggle.showMixedValue = false; run(e.newValue); }); }
        private void OnNumber(string name, float min, float max, float step, string format, Action<float> run)
        {
            var spinner = _ui.Spinner(name); if (spinner == null) return; spinner.Min = min; spinner.Max = max; spinner.Step = step; spinner.Format = format;
            spinner.Committed += v => { if (!_filling) run(v); }; spinner.Refused += why => Say(why, true);
        }
        private void OnPick(string name, Action<int> run) { var combo = _ui.Combo(name); combo?.RegisterValueChangedCallback(e => { if (!_filling && combo.index >= 0) run(combo.index); }); }
        private void OnPress(string name, Action run) { var button = _ui.Button(name); if (button != null) button.clicked += () => { LifeVerse.Unity.Audio.UiSounds.Click(); run(); }; }

        /// <summary>A group of options where one is on.</summary>
        private void Options(IReadOnlyList<string> names, Func<string, bool> chosen) { foreach (var name in names) if (_ui.Check(name) is { } radio) radio.SetValueWithoutNotify(chosen(name)); }
        private void OnOption(string name, Action run) { var radio = _ui.Check(name); radio?.RegisterValueChangedCallback(e => { if (_filling) return; if (!e.newValue) { radio.SetValueWithoutNotify(true); return; } LifeVerse.Unity.Audio.UiSounds.Click(); run(); Refresh(); }); }

        // ---- editing ----

        /// <summary>Changes the material of the selected objects.</summary>
        public void EditMaterial(Action<JsonObject> change)
        {
            if (_filling) return; if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return; }
            _b.Edit(_b.Selection.ToList(), s => { var m = Parse(s.MaterialJson); change(m); s.MaterialJson = m.ToJsonString(); }, "aparência");
        }

        private void EditGeometry(Action<JsonObject> change)
        {
            if (_filling || _b.Selection.Count == 0) return;
            _b.Edit(_b.Selection.ToList(), s => { var g = Parse(s.GeometryJson); change(g); s.GeometryJson = g.ToJsonString(); }, "forma");
        }

        private void EditPhysics(Func<LifeVerse.Shared.Building.PhysicsSettings, LifeVerse.Shared.Building.PhysicsSettings> change)
        {
            if (_filling || _b.Selection.Count == 0) return;
            _b.Edit(_b.Selection.ToList(), s => s.PhysicsJson = LifeVerse.Shared.Building.PhysicsRules.Write(s.PhysicsJson, change(LifeVerse.Shared.Building.PhysicsRules.Read(s.PhysicsJson))), "física");
        }

        /// <summary>The "properties" command: only what is in the object changes, and the island decides who may.</summary>
        public bool SendProperties(JsonObject properties)
        {
            if (_b.Selection.Count == 0) { Say("Selecione um objeto.", true); return false; }
            var ok = _b.SendProperties(_b.Selection.ToList(), properties); if (ok) Say("Enviado.", false); return ok;
        }
    }
}
