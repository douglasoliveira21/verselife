using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// "Documentação" of the Lua scripts: the functions and events of the language as the platform documents them (/api/v1/scripting/reference, the same list the island uses), with a search and a
    /// filter by category. For each one: what it does, what it returns, the permission it needs, its limits, how it fails and an example that can be put into the script being edited.
    /// </summary>
    public sealed class ScriptDocsWindow : FloatingWindow
    {
        private sealed class Entry { public string Title, Category, Detail, Example; }
        private readonly ApiContext _c; private readonly TextField _search; private readonly DropdownField _category; private readonly ScrollView _list; private readonly Label _detail, _note; private readonly Button _insert;
        private readonly List<Entry> _entries = new List<Entry>(), _shown = new List<Entry>(); private Entry _picked; private readonly Action<string> _insertExample;
        public int Count => _shown.Count;
        public string DetailText => _detail.text;
        public IReadOnlyList<string> Titles => _shown.Select(e => e.Title).ToList();
        public IReadOnlyList<string> Categories => _category.choices;

        public ScriptDocsWindow(ApiContext context, Action<string> insertExample) : base(context.Ui, "script-docs", "Documentação do Lua", 760f, 520f, new Vector2(240, 60))
        {
            _c = context; _insertExample = insertExample;
            var bar = UiKit.Row(); _search = UiKit.Field("Buscar função ou evento", false, 60); _search.style.flexGrow = 1; _search.RegisterValueChangedCallback(_ => Apply()); bar.Add(_search);
            _category = new DropdownField(new List<string> { "Todas as categorias" }, 0); _category.style.width = 200; _category.RegisterValueChangedCallback(_ => Apply()); bar.Add(_category); Body.Add(bar);
            _note = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_note);
            var row = UiKit.Row(); row.style.flexGrow = 1;
            _list = new ScrollView(); _list.style.width = 270; _list.style.flexShrink = 0; row.Add(_list);
            var side = new VisualElement(); side.style.flexGrow = 1; side.style.marginLeft = 8;
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; _detail = UiKit.Text("Escolha uma função ou evento.", "lv-muted"); _detail.style.whiteSpace = WhiteSpace.Normal; _detail.selection.isSelectable = true; scroll.Add(_detail); side.Add(scroll);
            _insert = UiKit.Button("Inserir exemplo no script", () => { if (_picked != null && _picked.Example.Length > 0) _insertExample?.Invoke(_picked.Example); }, "lv-btn-primary"); _insert.SetEnabled(false); side.Add(_insert);
            row.Add(side); Body.Add(row); Load();
        }

        public void Load() => this.Then(_c.Api.ApiAsync("GET", "/api/v1/scripting/reference", null), r =>
        {
            _entries.Clear(); if (r.Status != 200) { _note.text = "Não foi possível carregar a documentação agora (" + UiTasks.Explain(r.Status) + ")"; Apply(); return; }
            try
            {
                using var doc = JsonDocument.Parse(r.Body);
                foreach (var f in doc.RootElement.GetProperty("functions").EnumerateArray()) { var row = new Row(f.Clone()); _entries.Add(new Entry { Title = row.First("signature").Length > 0 ? row.First("signature") : row.Text("namespace") + "." + row.Text("name"), Category = row.First("category"), Example = row.Text("example"), Detail = row.Text("signature") + "\n\n" + row.Text("description") + "\n\nRetorna: " + row.Text("returns") + "\nPermissão: " + row.Text("permission") + "\nLimites: " + row.Text("limits") + "\nErros possíveis: " + row.Text("errors") + "\n\nExemplo:\n" + row.Text("example") }); }
                foreach (var e in doc.RootElement.GetProperty("events").EnumerateArray()) { var row = new Row(e.Clone()); _entries.Add(new Entry { Title = row.Text("name"), Category = "Eventos", Example = row.Text("example"), Detail = row.Text("name") + "\n\n" + row.Text("when") + "\n\nO que recebe (a tabela event): " + row.Text("fields") + "\n\nExemplo:\n" + row.Text("example") }); }
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is InvalidOperationException) { _note.text = "A documentação veio num formato que não entendi."; }
            var choices = new List<string> { "Todas as categorias" }; choices.AddRange(_entries.Select(e => e.Category).Where(c => c.Length > 0).Distinct()); _category.choices = choices; _category.SetValueWithoutNotify(choices[0]); Apply();
        });

        public void Search(string text) { _search.value = text; Apply(); }
        public void PickCategory(string name) { _category.value = name; Apply(); }
        public void Pick(int index) { if (index >= 0 && index < _shown.Count) Choose(_shown[index]); }
        public void PressInsert() { if (_picked != null && _picked.Example.Length > 0) _insertExample?.Invoke(_picked.Example); }

        private void Apply()
        {
            var query = _search.value.Trim(); var category = _category.index > 0 ? _category.value : "";
            _shown.Clear(); _list.Clear(); _picked = null; _insert.SetEnabled(false);
            foreach (var entry in _entries.Where(e => (category.Length == 0 || e.Category == category) && (query.Length == 0 || e.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || e.Detail.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
            { _shown.Add(entry); var captured = entry; var button = UiKit.Button(entry.Title, () => Choose(captured)); button.style.unityTextAlign = TextAnchor.MiddleLeft; _list.Add(button); }
            _detail.text = _shown.Count == 0 ? "Nada encontrado com essa busca." : "Escolha uma função ou evento.";
        }

        private void Choose(Entry entry) { _picked = entry; _detail.text = entry.Detail; _insert.SetEnabled(entry.Example.Length > 0); }
    }

    /// <summary>"Histórico" of a script: the versions of its code that were kept (each save keeps the one it replaced), a preview of the code of the one picked and a button to go back to it.</summary>
    public sealed class ScriptHistoryWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly string _base; private readonly Label _note; private readonly ScrollView _list; private readonly TextField _preview; private readonly Button _restore;
        private readonly Action _restored; private int _picked; private readonly List<int> _versions = new List<int>();
        public int VersionCount => _versions.Count;
        public string Preview => _preview.value;
        public string Note => _note.text;
        public bool CanRestore => _restore.enabledSelf;

        public ScriptHistoryWindow(ApiContext context, Guid island, Guid obj, Guid script, Action restored) : base(context.Ui, "script-history", "Histórico do script", 720f, 480f, new Vector2(260, 80))
        {
            _c = context; _restored = restored; _base = "/api/v1/worlds/" + island + "/objects/" + obj + "/scripts/" + script;
            _note = UiKit.Text("", "lv-small", "lv-muted"); _note.style.whiteSpace = WhiteSpace.Normal; Body.Add(_note);
            var row = UiKit.Row(); row.style.flexGrow = 1; _list = new ScrollView(); _list.style.width = 240; _list.style.flexShrink = 0; row.Add(_list);
            var side = new VisualElement(); side.style.flexGrow = 1; side.style.marginLeft = 8; _preview = UiKit.Field("", false, 70000); _preview.multiline = true; _preview.isReadOnly = true; _preview.style.flexGrow = 1; _preview.AddToClassList("lv-mono"); side.Add(_preview);
            _restore = UiKit.Button("Restaurar esta versão", Restore, "lv-btn-primary"); _restore.SetEnabled(false); side.Add(_restore); row.Add(side); Body.Add(row);
            Reload();
        }

        public void Reload() => this.Then(_c.Api.ApiAsync("GET", _base + "/versions", null), r =>
        {
            _list.Clear(); _versions.Clear(); _preview.value = ""; _restore.SetEnabled(false); _picked = 0;
            if (r.Status != 200) { _note.text = "Não foi possível ler o histórico (" + UiTasks.Explain(r.Status) + ")"; return; }
            var root = Json.One(r.Body); var current = root.Number("current"); var rows = Json.Rows(r.Body);
            _note.text = rows.Count == 0 ? "Versão atual: " + current + ". Ainda não há versões anteriores: cada vez que você salva, o código que estava antes fica guardado aqui (as últimas 20)." : "Versão atual: " + current + ". Escolha uma versão anterior para ver o código e voltar a ela.";
            foreach (var row in rows) { var version = (int)row.Number("version"); _versions.Add(version); var label = "Versão " + version + " · " + row.Number("lines") + " linhas · " + DiscoveryWindow.When(row.Text("savedAt")); _list.Add(UiKit.Button(label, () => Pick(version))); }
        });

        public void Pick(int version) => this.Then(_c.Api.ApiAsync("GET", _base + "/versions/" + version, null), r =>
        {
            _picked = version; _restore.SetEnabled(false); if (r.Status != 200) { _preview.value = "Não foi possível ler esta versão."; return; }
            _preview.value = Json.One(r.Body).Text("source"); _restore.SetEnabled(true);
        });

        public void Restore()
        {
            if (_picked <= 0) return; var version = _picked; _restore.SetEnabled(false);
            this.Then(_c.Api.ApiAsync("POST", _base + "/versions/" + version + "/restore", "{}"), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show("Versão " + version + " restaurada como a atual.", NoticeKind.Good); _restored?.Invoke(); Reload(); } else { _note.text = UiTasks.Explain(r.Status, r.Body); _restore.SetEnabled(true); }
            });
        }
    }

    /// <summary>
    /// The dialogs and permission requests that scripts of objects show ("script_dialog" announced by the island server): a window with who is asking, the message and the buttons (and a text box when
    /// the script asks for one). What is picked goes back to the island, which checks it was really asked of this player. Closing answers "Negar" to a permission request, "Recusar" to a payment and
    /// nothing to a dialog. One at a time; the others wait their turn, and a request nobody answers goes away when its time is up.
    /// </summary>
    public sealed class ScriptDialogs
    {
        public sealed class Pending { public Guid Id; public string Kind, Title, Message, ObjectName; public List<string> Buttons; public bool Input; public double Timeout; }
        private readonly UiRoot _ui; private readonly Queue<Pending> _queue = new Queue<Pending>(); private FloatingWindow _window; private Label _countdown; private TextField _input; private IVisualElementScheduledItem _tick; private float _until;
        public Action<string> Send { get; set; }
        public Pending Current { get; private set; }
        public int Waiting => _queue.Count;
        public bool IsOpen => _window != null && _window.IsOpen;
        public IReadOnlyList<string> Buttons => _window == null ? new List<string>() : _window.Query<Button>(className: "lv-script-answer").ToList().Select(b => b.text).ToList();
        public string Text => _window == null ? "" : string.Join("|", _window.Query<Label>().ToList().Select(l => l.text));
        public TextField InputField => _input;

        public ScriptDialogs(UiRoot ui) { _ui = ui; }

        public void OnOperation(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement;
                if (!root.TryGetProperty("operation", out var op) || op.GetString() != "script_dialog") return;
                var buttons = root.TryGetProperty("buttons", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(b => b.GetString() ?? "").Where(b => b.Length > 0).Take(12).ToList() : new List<string>();
                string T(string name, int max) { var text = root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; return text.Length > max ? text.Substring(0, max) : text; }
                Enqueue(new Pending { Id = Guid.Parse(root.GetProperty("id").GetString()), Kind = T("kind", 16), Title = T("title", 60), Message = T("message", 400), Buttons = buttons, Input = root.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.True, Timeout = root.TryGetProperty("timeout", out var timeout) && timeout.TryGetDouble(out var seconds) ? Math.Max(5, Math.Min(300, seconds)) : 60, ObjectName = T("objectName", 60) });
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is FormatException || exception is InvalidOperationException) { }
        }

        public void Enqueue(Pending pending) { if (_queue.Count < 10) _queue.Enqueue(pending); if (Current == null) Next(); }

        private void Next()
        {
            _tick?.Pause(); var old = _window; _window = null; old?.Close(); // _window is null first: closing it here is not the person dismissing it
            if (_queue.Count == 0) { Current = null; return; }
            Current = _queue.Dequeue(); var pending = Current; _until = Time.realtimeSinceStartup + (float)pending.Timeout;
            _window = new FloatingWindow(_ui, "script-dialog", "Objeto", 400f, -1f, new Vector2(Mathf.Max(20, Screen.width / 2f - 200), Mathf.Max(20, Screen.height / 2f - 150)));
            _window.Closed += () => { if (Current == pending && _window != null) Dismiss(); };
            _window.Body.Add(UiKit.Text(pending.ObjectName.Length > 0 ? pending.ObjectName + " pergunta:" : "Um objeto pergunta:", "lv-accent", "lv-bold"));
            var message = UiKit.Text(pending.Message.Length > 0 ? pending.Message : pending.Title, "lv-body"); message.style.whiteSpace = WhiteSpace.Normal; message.style.marginTop = 6; message.style.marginBottom = 8; _window.Body.Add(message);
            _input = null; if (pending.Input) { _input = UiKit.Field("Digite aqui", false, 200); _input.RegisterCallback<KeyDownEvent>(e => { if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && pending.Buttons.Count > 0) Answer(pending.Buttons[0]); }, TrickleDown.TrickleDown); _window.Body.Add(_input); }
            foreach (var label in pending.Buttons) { var text = label; var primary = pending.Kind == "permission" && text == "Permitir" || pending.Kind == "payment" && text == "Pagar"; var button = UiKit.Button(text, () => Answer(text), primary ? "lv-btn-primary" : "lv-btn"); button.AddToClassList("lv-script-answer"); button.style.marginTop = 4; _window.Body.Add(button); }
            _countdown = UiKit.Text("", "lv-small", "lv-muted"); _countdown.style.unityTextAlign = TextAnchor.MiddleRight; _window.Body.Add(_countdown);
            _window.Open(); _tick = _window.schedule.Execute(Tick).Every(250);
        }

        private void Tick()
        {
            if (Current == null) return; var left = _until - Time.realtimeSinceStartup; _countdown.text = Math.Max(0, Mathf.CeilToInt(left)) + " s"; if (left <= 0) Dismiss();
        }

        /// <summary>Forces the time out (tests).</summary>
        public void Expire() => Dismiss();

        public void Answer(string response)
        {
            var pending = Current; if (pending == null) return;
            Send?.Invoke(JsonSerializer.Serialize(new { id = pending.Id, response, input = pending.Input && _input != null ? _input.value : null }));
            Next();
        }

        private void Dismiss()
        {
            var asked = Current; if (asked == null) return;
            if (asked.Kind == "permission" || asked.Kind == "payment") Send?.Invoke(JsonSerializer.Serialize(new { id = asked.Id, response = asked.Kind == "payment" ? "Recusar" : "Negar" }));
            Current = null; var closing = _window; _window = null; closing?.Close(); Next();
        }

        public void Press(string text) { if (Current != null && Current.Buttons.Contains(text)) Answer(text); }
        public void Clear() { _queue.Clear(); Current = null; _tick?.Pause(); var w = _window; _window = null; w?.Close(); }
    }

    /// <summary>
    /// The panels that scripts of objects show on this player's screen ("script_hud" from the island server): a title, a close button and what the script put in it (text, buttons, progress bars, text
    /// fields). Clicks, texts and closing go back to the island, which checks them against the HUD the script really shows. The player can always close a panel with the X; at most eight panels are shown
    /// at once and nothing a script sends can draw outside its own panel.
    /// </summary>
    public sealed class ScriptHud
    {
        public const int MaxPanels = 8;
        private sealed class PanelInfo { public Guid Script; public VisualElement Box, Body; public string Object = ""; public Dictionary<string, string> Typed = new Dictionary<string, string>(); }
        private readonly UiRoot _ui; private readonly Dictionary<Guid, PanelInfo> _panels = new Dictionary<Guid, PanelInfo>();
        public Action<string> Send { get; set; }
        public int PanelCount => _panels.Count;
        public bool Has(Guid script) => _panels.ContainsKey(script);
        public string TextOf(Guid script) => _panels.TryGetValue(script, out var p) ? string.Join("|", p.Box.Query<Label>().ToList().Select(l => l.text)) : "";
        public IReadOnlyList<string> ButtonsOf(Guid script) => _panels.TryGetValue(script, out var p) ? p.Body.Query<Button>(className: "lv-script-element").ToList().Select(b => b.text).ToList() : new List<string>();
        public TextField InputOf(Guid script) => _panels.TryGetValue(script, out var p) ? p.Body.Q<TextField>() : null;
        public ProgressBar BarOf(Guid script) => _panels.TryGetValue(script, out var p) ? p.Body.Q<ProgressBar>() : null;
        public void Press(Guid script, string text) { if (_panels.TryGetValue(script, out var p)) foreach (var b in p.Body.Query<Button>(className: "lv-script-element").ToList()) if (b.text == text) { (b.userData as Action)?.Invoke(); return; } }
        public void CloseFromMenu(Guid script) { if (!_panels.ContainsKey(script)) return; Send?.Invoke(JsonSerializer.Serialize(new { hud = true, script, action = "close" })); Remove(script); }

        public ScriptHud(UiRoot ui) { _ui = ui; }

        public void Clear() { foreach (var p in _panels.Values) p.Box.RemoveFromHierarchy(); _panels.Clear(); }

        public void OnOperation(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json); var root = document.RootElement;
                if (!root.TryGetProperty("operation", out var op) || op.GetString() != "script_hud") return;
                var script = Guid.Parse(root.GetProperty("script").GetString());
                if (!root.GetProperty("visible").GetBoolean()) { Remove(script); return; }
                string T(JsonElement e, string name, int max) { var text = e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; return text.Length > max ? text.Substring(0, max) : text; }
                double N(JsonElement e, string name, double fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback;
                var elements = root.GetProperty("elements").EnumerateArray().Take(12).Select(e => new[] { T(e, "id", 32), T(e, "type", 12), T(e, "text", 120), T(e, "color", 9), N(e, "value", 0).ToString(System.Globalization.CultureInfo.InvariantCulture), Math.Max(1, N(e, "max", 100)).ToString(System.Globalization.CultureInfo.InvariantCulture), T(e, "placeholder", 120) }).ToList();
                Show(script, T(root, "objectName", 60), T(root, "title", 120), (float)Math.Max(.05, Math.Min(.95, N(root, "x", .8))), (float)Math.Max(.05, Math.Min(.95, N(root, "y", .25))), (int)Math.Max(160, Math.Min(420, N(root, "width", 260))), elements);
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException || exception is FormatException || exception is InvalidOperationException) { }
        }

        private void Remove(Guid script) { if (_panels.TryGetValue(script, out var p)) { p.Box.RemoveFromHierarchy(); _panels.Remove(script); } }

        private void Show(Guid script, string objectName, string title, float x, float y, int width, List<string[]> elements)
        {
            if (!_panels.TryGetValue(script, out var panel))
            {
                if (_panels.Count >= MaxPanels) return;
                panel = new PanelInfo { Script = script, Box = new VisualElement(), Body = new VisualElement() }; _panels[script] = panel;
                panel.Box.AddToClassList("lv-glass"); panel.Box.style.position = Position.Absolute; panel.Box.pickingMode = PickingMode.Position; panel.Box.style.paddingLeft = panel.Box.style.paddingRight = 12; panel.Box.style.paddingTop = panel.Box.style.paddingBottom = 10;
                panel.Box.Add(panel.Body); _ui.HudLayer.Add(panel.Box);
            }
            panel.Object = objectName; var captured = panel;
            foreach (var edit in panel.Body.Query<TextField>().ToList()) if (edit.userData is string field) panel.Typed[field] = edit.value; // an update from the script must not wipe what is being typed
            panel.Body.Clear();
            panel.Box.style.left = Length.Percent(x * 100f); panel.Box.style.top = Length.Percent(y * 100f); panel.Box.style.width = width; panel.Box.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            var header = UiKit.Row(); var heading = new VisualElement(); heading.style.flexGrow = 1; heading.Add(UiKit.Text(title.Length > 0 ? title : objectName, "lv-bold", "lv-accent"));
            if (objectName.Length > 0 && title.Length > 0) heading.Add(UiKit.Text(objectName, "lv-small", "lv-muted")); header.Add(heading);
            header.Add(UiKit.Button("✕", () => { Send?.Invoke(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "close" })); Remove(captured.Script); }, "lv-close")); panel.Body.Add(header);
            foreach (var e in elements)
            {
                var id = e[0];
                switch (e[1])
                {
                    case "label": var label = UiKit.Text(e[2], "lv-body"); label.style.whiteSpace = WhiteSpace.Normal; if (e[3].Length >= 7 && e[3][0] == '#' && ColorUtility.TryParseHtmlString(e[3], out var color)) label.style.color = color; panel.Body.Add(label); break;
                    case "button": Action click = () => Send?.Invoke(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "click", element = id })); var button = UiKit.Button(e[2], click); button.userData = click; button.AddToClassList("lv-script-element"); button.style.marginTop = 4; panel.Body.Add(button); break;
                    case "progress":
                        var max = float.Parse(e[5], System.Globalization.CultureInfo.InvariantCulture); var value = float.Parse(e[4], System.Globalization.CultureInfo.InvariantCulture);
                        var bar = new ProgressBar { lowValue = 0, highValue = max, value = Mathf.Clamp(value, 0, max), title = e[2] }; bar.style.marginTop = 4; panel.Body.Add(bar); break;
                    case "input":
                        var edit = UiKit.Field(e[6], false, 200, panel.Typed.TryGetValue(id, out var typed) ? typed : ""); edit.userData = id; edit.style.marginTop = 4;
                        edit.RegisterCallback<KeyDownEvent>(ev => { if (ev.keyCode != KeyCode.Return && ev.keyCode != KeyCode.KeypadEnter) return; Send?.Invoke(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "input", element = id, text = edit.value })); edit.value = ""; captured.Typed.Remove(id); }, TrickleDown.TrickleDown); panel.Body.Add(edit); break;
                }
            }
        }
    }
}
