using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The editor of the Lua scripts of an object, as the Godot client's: the scripts it has, a source field with completion (Ctrl+Espaço, from the reference of the language), a name and the event,
    /// a syntax check made here with the same Lua interpreter the island runs and the full check of the server (errors and warnings by line), save, restart, stop and pause; and under the code the
    /// console of the script, its problems, its use of the island, the permissions it holds and the debugger (lines of parada, then Continuar, Passo, Por cima and Sair, with what the function
    /// holds). What runs, with which permissions, is always the island's decision.
    /// </summary>
    public sealed class LuaEditorWindow : FloatingWindow
    {
        public const int MaxSourceLength = 64 * 1024;
        private readonly ApiContext _c; private readonly Guid _island; private readonly Guid _object;
        private readonly ScrollView _scripts; private readonly TextField _name, _eventName, _source, _breakpoints; private readonly Label _status, _state, _cursor;
        private readonly VisualElement _suggest, _tabBody; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>(); private readonly Dictionary<string, Button> _tabButtons = new Dictionary<string, Button>();
        private readonly Label _console, _problems, _performance, _grants, _debugText; private readonly Button _pause, _debug; private readonly List<Button> _steps = new List<Button>();
        private readonly Dictionary<string, Toggle> _owner = new Dictionary<string, Toggle>(); private readonly VisualElement _revokes; private bool _syncing;
        private Guid _open; private bool _dirty; private LuaEditorRules.Reference _reference;
        public string Source { get => _source.value; set => _source.value = value; }
        public Guid OpenScript => _open;
        public string StatusText => _status.text;
        public string Tab { get; private set; } = "Console";
        public string ConsoleText => _console.text;
        public string ProblemsText => _problems.text;
        public string PerformanceText => _performance.text;
        public string StateText => _state.text;
        public string PauseText => _pause.text;
        public string DebugText => _debugText.text;
        public bool Debugging { get; private set; }
        public bool Stopped { get; private set; }
        public int StoppedLine { get; private set; }
        public bool Dirty => _dirty;
        public List<int> Breakpoints => LuaEditorRules.ParseLines(_breakpoints.value, LuaEditorRules.LineCount(_source.value));
        public IReadOnlyDictionary<string, Toggle> OwnerPermissions => _owner;
        public List<(string Shown, string Insert)> Suggestions { get; private set; } = new List<(string, string)>();
        public bool StepsEnabled => _steps.All(b => b.enabledSelf);
        /// <summary>Where the commands of the debugger go (the connection to the island); tests replace it.</summary>
        public Action<string> DebugSender;

        public LuaEditorWindow(ApiContext context, Guid island, Guid obj) : base(context.Ui, "lua-" + obj.ToString("N").Substring(0, 8), "Script do objeto", 620f, 700f, new Vector2(300, 10))
        {
            _c = context; _island = island; _object = obj;
            var page = new ScrollView { name = "script-page" }; page.style.flexGrow = 1; page.style.minHeight = 0; Body.style.minHeight = 0; Body.style.overflow = Overflow.Hidden; Body.Add(page); FitScreen();
            _scripts = new ScrollView(); _scripts.style.maxHeight = 64; _scripts.style.minHeight = 30; page.Add(_scripts);
            var head = UiKit.Row(); _name = UiKit.Field("Nome", false, 60, "script"); _eventName = UiKit.Field("Evento", false, 40, "on_start"); _name.style.flexGrow = 1; _eventName.style.width = 150; _state = UiKit.Text("", "lv-small", "lv-muted"); _state.tooltip = "Como o script está na ilha"; head.Add(_name); head.Add(_eventName); head.Add(_state); page.Add(head);
            // the code: the numbers of the lines on the left (a click puts or takes a line of parada), the text field, and under its text the same code in colours; all scroll together
            _code = new ScrollView(ScrollViewMode.VerticalAndHorizontal) { name = "code" }; _code.style.height = 190; _code.style.flexShrink = 0; _code.AddToClassList("lv-mono"); _code.style.whiteSpace = WhiteSpace.NoWrap; page.Add(_code);
            var codeRow = new VisualElement(); codeRow.style.flexDirection = FlexDirection.Row; codeRow.style.alignItems = Align.FlexStart; _code.Add(codeRow);
            _gutter = new VisualElement { name = "gutter" }; _gutter.style.width = 46; _gutter.style.flexShrink = 0; codeRow.Add(_gutter);
            _source = UiKit.Field("", false, MaxSourceLength); _source.multiline = true; _source.style.flexGrow = 1; _source.style.minHeight = 180; _source.style.whiteSpace = WhiteSpace.NoWrap; _source.verticalScrollerVisibility = ScrollerVisibility.Hidden; codeRow.Add(_source);
                        _colours = new Label { name = "colours", enableRichText = true, pickingMode = PickingMode.Ignore }; _colours.style.position = Position.Absolute; _colours.style.whiteSpace = WhiteSpace.NoWrap; _colours.style.paddingLeft = _colours.style.paddingRight = _colours.style.paddingTop = _colours.style.paddingBottom = 0; _colours.style.marginLeft = _colours.style.marginTop = 0;
            _source.RegisterCallback<GeometryChangedEvent>(_ => PlaceColours());
            _source.RegisterValueChangedCallback(e => { _dirty = true; ShowCursor(); Paint(); if (AutoComplete && e.newValue != null && e.previousValue != null && e.newValue.Length == e.previousValue.Length + 1) Body.schedule.Execute(AutoSuggest); });
            _code.style.fontSize = Mathf.Clamp(FontSize, 10, 32); _source.RegisterCallback<KeyUpEvent>(_ => ShowCursor()); _source.RegisterCallback<PointerUpEvent>(_ => ShowCursor());
            _source.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Tab && !e.shiftKey) { InsertText(new string(' ', Mathf.Clamp(Indent, 1, 8))); e.StopImmediatePropagation(); _source.focusController?.IgnoreEvent(e); return; } if (e.keyCode == KeyCode.Space && (e.ctrlKey || e.commandKey)) { Complete(); e.StopImmediatePropagation(); } else if (e.keyCode == KeyCode.Escape) HideSuggestions(); }, TrickleDown.TrickleDown);
            _suggest = new VisualElement { name = "suggestions" }; _suggest.style.flexDirection = FlexDirection.Row; _suggest.style.flexWrap = Wrap.Wrap; _suggest.style.display = DisplayStyle.None; page.Add(_suggest);
            var line = UiKit.Row(); _cursor = UiKit.Text("Linha 1", "lv-small", "lv-muted"); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; _status.style.flexGrow = 1; _status.style.flexShrink = 1; line.Add(_status); line.Add(_cursor); page.Add(line);
            page.Add(Tools(UiKit.Button("Verificar", () => Validate()), UiKit.Button("Salvar", Save, "lv-btn-primary"), UiKit.Button("Novo", NewScript), UiKit.Button("Modelos…", OpenTemplates), UiKit.Button("Recarregar", LoadList), UiKit.Button("Histórico", OpenHistory), UiKit.Button("Documentação", OpenDocs)));
            _pause = UiKit.Button("Pausar", () => Pause()); _pause.tooltip = "Pausa o script: ele não recebe eventos e os temporizadores param"; _debug = UiKit.Button("Depurar", ToggleDebugging); _debug.tooltip = "Para o script nas linhas de parada para ver o que ele guarda";
            var stop = UiKit.Button("Parar", Stop); stop.tooltip = "Para o script: sem eventos nem temporizadores até reiniciar ou salvar"; var restart = UiKit.Button("Reiniciar", Restart); restart.tooltip = "Carrega o script de novo (on_start roda outra vez)";
            var complete = UiKit.Button("Completar", Complete); complete.tooltip = "Sugestões para a palavra antes do cursor (Ctrl+Espaço)";
            page.Add(Tools(restart, stop, _pause, _debug, complete));
            var tabRow = UiKit.Row(); tabRow.AddToClassList("lv-tabs"); page.Add(tabRow); _tabBody = new ScrollView(); _tabBody.style.height = 170; _tabBody.style.flexShrink = 0; page.Add(_tabBody);
            VisualElement AddTab(string title) { var content = new VisualElement(); _tabs[title] = content; var b = UiKit.Button(title, () => ShowTab(title)); _tabButtons[title] = b; tabRow.Add(b); return content; }
            Label Text(VisualElement tab, string text) { var l = UiKit.Text(text, "lv-small"); l.AddToClassList("lv-mono"); l.style.whiteSpace = WhiteSpace.Normal; tab.Add(l); return l; }
            _console = Text(AddTab("Console"), "Console: sem execuções ainda.");
            _problems = Text(AddTab("Problemas"), "Aperte Verificar para conferir o script no servidor.");
            _performance = Text(AddTab("Desempenho"), "Sem dados ainda: a ilha informa o uso a cada 30 segundos.");
            var permissions = AddTab("Permissões"); permissions.Add(UiKit.Text("O que você permite a este script (só o dono decide):", "lv-small", "lv-muted"));
            foreach (var (perm, text) in LuaEditorRules.OwnerPermissions) { var p = perm; var box = UiKit.Check(text, false, on => { if (!_syncing) SetOwnerPermission(p, on); }); _owner[p] = box; permissions.Add(box); }
            _grants = UiKit.Text("Este script não recebeu nenhuma permissão de avatar.", "lv-small"); _grants.style.whiteSpace = WhiteSpace.Normal; permissions.Add(_grants); _revokes = new VisualElement(); permissions.Add(_revokes);
            var debugTab = AddTab("Depuração");
            _breakpoints = UiKit.Field("Linhas de parada (ex.: 3, 7)", false, 200); _breakpoints.RegisterValueChangedCallback(_ => { PaintGutter(); if (Debugging) SendDebug("breakpoints"); });
            debugTab.Add(UiKit.Row(_breakpoints, UiKit.Button("Marcar a linha do cursor", ToggleCursorLine)));
            var steps = UiKit.Row(); void Step(string text, string tip, string action) { var b = UiKit.Button(text, () => SendDebug(action)); b.tooltip = tip; b.SetEnabled(false); _steps.Add(b); steps.Add(b); }
            Step("Continuar", "Deixa o script seguir até o próximo ponto de parada", "continue"); Step("Passo", "Vai para a próxima linha, entrando nas funções chamadas", "stepin"); Step("Por cima", "Vai para a próxima linha sem entrar nas funções chamadas", "stepover"); Step("Sair", "Segue até a função atual terminar", "stepout");
            debugTab.Add(steps); _debugText = Text(debugTab, DebugHelp);
            ShowTab("Console");
            _c.IslandScript += OnIslandScript; Closed += () => { _c.IslandScript -= OnIslandScript; if (Debugging) SendDebug("stop"); };
            Body.schedule.Execute(() => { if (_open != Guid.Empty && Body.panel != null) RefreshRuntime(); }).Every(5000);
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/scripting/reference"), r => { if (r.Status == 200) _reference = LuaEditorRules.ReadReference(r.Body); });
            Paint(); LoadList();
        }

        /// <summary>A row of tools that wraps instead of squeezing the words of its buttons.</summary>
        private static VisualElement Tools(params VisualElement[] buttons) { var row = UiKit.Row(buttons); row.style.flexWrap = Wrap.Wrap; foreach (var b in buttons) { b.style.flexShrink = 0; b.style.marginBottom = 3; } return row; }

        public const string DebugHelp = "Escreva as linhas de parada (ou marque a linha do cursor) e aperte Depurar (o script precisa estar salvo: a ilha depura o código salvo). Quando o script chegar à linha, ele para aqui, com o que ele guarda. Enquanto está parado ele não recebe eventos; depois de 2 minutos parado ele é solto.";

        public void ShowTab(string title)
        {
            if (!_tabs.ContainsKey(title)) return; Tab = title; _tabBody.Clear(); _tabBody.Add(_tabs[title]);
            foreach (var pair in _tabButtons) pair.Value.EnableInClassList("lv-btn-primary", pair.Key == title);
        }

        private readonly ScrollView _code; private readonly VisualElement _gutter; private readonly Label _colours; private TextElement _text; private float _lineHeight = 17f;
        /// <summary>The code as drawn in colours (rich text), and how many line numbers the margin shows.</summary>
        public string Colours => _colours.text;
        public int GutterLines => _gutter.childCount;

        /// <summary>Puts the coloured copy of the code exactly under the text of the field (inside its input, so it takes the same font), and hides the plain text.</summary>
        private void PlaceColours()
        {
            if (_text == null)
            {
                _text = _source.Q<TextElement>(className: "unity-text-element"); if (_text == null) return;
                _text.parent.Insert(0, _colours); _text.style.color = new Color(0f, 0f, 0f, 0f);
            }
            _colours.style.left = _text.layout.x; _colours.style.top = _text.layout.y;
            var lines = LuaEditorRules.LineCount(_source.value); if (_text.layout.height > 1f && lines > 0) { var h = _text.layout.height / lines; if (Mathf.Abs(h - _lineHeight) > .1f && h > 6f && h < 60f) { _lineHeight = h; PaintGutter(); } }
            _gutter.style.paddingTop = _text.worldBound.y - _source.worldBound.y + _source.resolvedStyle.marginTop;
        }

        private void Paint() { _colours.text = LuaHighlight.Rich(_source.value); PaintGutter(); }

        /// <summary>The numbers of the lines; a red dot on the lines of parada and a yellow mark where the debugger stopped. A click puts or takes the line.</summary>
        private void PaintGutter()
        {
            var lines = LuaEditorRules.LineCount(_source.value); var marks = new HashSet<int>(Breakpoints);
            while (_gutter.childCount > lines) _gutter.RemoveAt(_gutter.childCount - 1);
            while (_gutter.childCount < lines) { var number = _gutter.childCount + 1; var cell = new Label(); cell.style.unityTextAlign = TextAnchor.MiddleRight; cell.style.paddingRight = 6; cell.style.marginTop = cell.style.marginBottom = 0; cell.style.paddingTop = cell.style.paddingBottom = 0; cell.tooltip = "Clique para marcar ou tirar a linha de parada"; cell.RegisterCallback<ClickEvent>(_ => ToggleLine(number)); _gutter.Add(cell); }
            for (var i = 0; i < lines; i++)
            {
                var cell = (Label)_gutter[i]; var line = i + 1; var on = marks.Contains(line); cell.text = (on ? "● " : "") + line; cell.style.height = _lineHeight;
                cell.style.color = on ? new Color(1f, .35f, .35f) : new Color(.55f, .6f, .65f); cell.style.backgroundColor = Stopped && StoppedLine == line ? new Color(1f, .8f, .2f, .35f) : new Color(0f, 0f, 0f, 0f);
            }
        }

        /// <summary>Puts or takes a line of parada (the same as clicking its number).</summary>
        public void ToggleLine(int line) { if (line < 1 || line > LuaEditorRules.LineCount(_source.value)) return; SetLines(LuaEditorRules.Toggle(Breakpoints, line)); }

        /// <summary>Changes the lines of parada (the field may be in a tab not on screen, where it hears no change): the margin and the island are told here.</summary>
        private void SetLines(System.Collections.Generic.IEnumerable<int> lines) { _breakpoints.SetValueWithoutNotify(LuaEditorRules.LinesText(lines)); PaintGutter(); if (Debugging) SendDebug("breakpoints"); }
        public bool GutterMarked(int line) => line >= 1 && line <= _gutter.childCount && ((Label)_gutter[line - 1]).text.StartsWith("●");

        /// <summary>The size of the code's letters, how many spaces the Tab key puts and whether suggestions come while typing (Configurações › Scripts).</summary>
        public static int FontSize = 16, Indent = 4; public static bool AutoComplete = true;

        /// <summary>Puts text at the cursor.</summary>
        public void InsertText(string text) { var value = _source.value ?? ""; var at = Mathf.Clamp(_source.cursorIndex, 0, value.Length); _source.value = value.Substring(0, at) + text + value.Substring(at); var after = at + text.Length; _source.SelectRange(after, after); }

        /// <summary>While typing: after "nome." or three letters of a word, the suggestions show by themselves.</summary>
        private void AutoSuggest()
        {
            var text = _source.value ?? ""; var cursor = Mathf.Clamp(_source.cursorIndex, 0, text.Length); if (cursor == 0) return; var lineStart = text.LastIndexOf('\n', cursor - 1) + 1; var before = text.Substring(lineStart, cursor - lineStart);
            if (before.EndsWith(".") || LuaEditorRules.Typed(before).Length >= 3) { Suggestions = LuaEditorRules.CompletionFor(before, _reference, text); if (Suggestions.Count > 0 && Suggestions.Count <= 30) Complete(); else HideSuggestions(); } else HideSuggestions();
        }

        private void ShowCursor() => _cursor.text = "Linha " + LuaEditorRules.LineOf(_source.value, _source.cursorIndex) + " de " + LuaEditorRules.LineCount(_source.value);

        // ---- completing code ----

        /// <summary>Suggestions for the word before the cursor; clicking one puts it in.</summary>
        public void Complete()
        {
            var text = _source.value ?? ""; var cursor = Mathf.Clamp(_source.cursorIndex, 0, text.Length); var lineStart = text.LastIndexOf('\n', Math.Max(0, cursor - 1)) + 1; if (cursor == 0) lineStart = 0;
            var before = text.Substring(lineStart, cursor - lineStart); Suggestions = LuaEditorRules.CompletionFor(before, _reference, text); _suggest.Clear();
            if (Suggestions.Count == 0) { HideSuggestions(); Say("Nada para completar aqui.", false); return; }
            foreach (var (shown, insert) in Suggestions.Take(24)) { var put = insert; _suggest.Add(UiKit.Button(shown, () => Insert(put), "lv-btn-flat")); }
            _suggest.style.display = DisplayStyle.Flex;
        }

        /// <summary>Replaces the word being typed with a suggestion.</summary>
        public void Insert(string insert)
        {
            var text = _source.value ?? ""; var cursor = Mathf.Clamp(_source.cursorIndex, 0, text.Length); var lineStart = cursor == 0 ? 0 : text.LastIndexOf('\n', cursor - 1) + 1;
            var typed = LuaEditorRules.Typed(text.Substring(lineStart, cursor - lineStart));
            var at = cursor - typed.Length; _source.value = text.Substring(0, at) + insert + text.Substring(cursor); var after = at + insert.Length; _source.Focus(); _source.SelectRange(after, after); HideSuggestions();
        }

        /// <summary>Puts the cursor of the source at a position (the checks use it before completing).</summary>
        public void PlaceCursor(int index) { var at = Mathf.Clamp(index, 0, (_source.value ?? "").Length); _source.SelectRange(at, at); }

        private void HideSuggestions() { _suggest.Clear(); _suggest.style.display = DisplayStyle.None; }

        // ---- the scripts of the object ----

        /// <summary>The versions kept of the open script (needs it to be saved first).</summary>
        public void OpenHistory()
        {
            if (_open == Guid.Empty) { Say("Salve o script primeiro: o histórico guarda as versões de um script salvo.", true); return; }
            new ScriptHistoryWindow(_c, _island, _object, _open, () => Open(_open)).Open();
        }

        /// <summary>The manual of the language; "Inserir exemplo" adds the example at the end of the code.</summary>
        public void OpenDocs() => new ScriptDocsWindow(_c, example => { _source.value = _source.value.TrimEnd() + "\n\n" + example.Trim() + "\n"; Say("Exemplo inserido no fim do script.", false); }).Open();

        private string Path => "/api/v1/worlds/" + _island + "/objects/" + _object + "/scripts";

        public void LoadList() => this.Then(_c.Api.ApiAsync("GET", Path), r =>
        {
            _scripts.Clear(); if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; }
            var row = UiKit.Row(); row.style.flexWrap = Wrap.Wrap; _scripts.Add(row);
            foreach (var item in Json.Rows(r.Body)) { var id = item.Id(); var name = item.First("name", "title"); row.Add(UiKit.Button(name.Length > 0 ? name : id.ToString().Substring(0, 8), () => Open(id))); }
            if (row.childCount == 0) _scripts.Add(UiKit.Text("Este objeto não tem scripts.", "lv-muted"));
        });

        public void Open(Guid id) => this.Then(_c.Api.ApiAsync("GET", Path + "/" + id), r =>
        {
            if (r.Status != 200) { Say(UiTasks.Explain(r.Status), true); return; }
            var row = Json.One(r.Body); if (Debugging && id != _open) EndDebug("Depuração encerrada.");
            _open = id; _name.value = row.Text("name"); _eventName.value = row.First("eventName", "event"); _source.value = row.First("source", "code", "body"); _dirty = false; ShowPauseState(row.Text("status"));
            Say("Aberto: " + _name.value, false); _console.text = "Console: lendo…"; RefreshRuntime();
        });

        /// <summary>The ready-made scripts of the platform (<c>/api/v1/scripting/templates</c>: a door, a lift, sounds…); choosing one starts a new script with it, to save on the object.</summary>
        public void OpenTemplates()
        {
            _templatesWindow?.Close(); var window = new FloatingWindow(_c.Ui, "script-templates", "Modelos de script", 380f, 420f, new Vector2(480, 120)); var list = new ScrollView(); list.style.flexGrow = 1; window.Body.Add(list); window.Open(); _templatesWindow = window;
            if (!_closesTemplates) { _closesTemplates = true; Closed += () => _templatesWindow?.Close(); }
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/scripting/templates"), r =>
            {
                var templates = TemplatesOf(r.Status, r.Body); Templates = templates.Count;
                foreach (var t in templates) { var pick = t; list.Add(UiKit.Row(UiKit.Text(pick.Name, "lv-bold"), UiKit.Text(pick.Event, "lv-small", "lv-muted"), UiKit.Spacer(), UiKit.Button("Usar", () => { UseTemplate(pick.Name, pick.Event, pick.Source); window.Close(); }, "lv-btn-primary"))); }
            });
        }

        public int Templates { get; private set; }
        private FloatingWindow _templatesWindow; private bool _closesTemplates;
        public static readonly (string Name, string Event, string Source) DefaultTemplate = ("Porta ao toque", "on_touch", @"-- Roda no servidor da ilha, neste objeto.
-- Eventos: on_start, on_touch, on_enter, on_timer, on_chat, on_message
function on_touch(event)
  object.say(""Olá! Você me tocou."")
end
");

        /// <summary>The templates of the API (name, event, source); the built-in one when the API has none or cannot be reached.</summary>
        public static List<(string Name, string Event, string Source)> TemplatesOf(int status, string body)
        {
            var list = new List<(string, string, string)>();
            if (status >= 200 && status < 300) try { using var d = JsonDocument.Parse(body); if (d.RootElement.ValueKind == JsonValueKind.Array) foreach (var item in d.RootElement.EnumerateArray()) { string S(string n) => item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; if (S("source").Length > 0) list.Add((S("name").Length > 0 ? S("name") : "modelo", S("eventName").Length > 0 ? S("eventName") : "on_start", S("source"))); } } catch (JsonException) { }
            if (list.Count == 0) list.Add(DefaultTemplate); return list;
        }

        public void UseTemplate(string name, string eventName, string source) { _open = Guid.Empty; _name.value = name; _eventName.value = eventName; _source.value = source; Say("Modelo " + name + ": salve para pôr no objeto.", false); }

        public void NewScript() { if (Debugging) EndDebug("Depuração encerrada."); _open = Guid.Empty; _name.value = "novo_script"; _eventName.value = "on_start"; _source.value = "-- seu script em Lua\n"; _state.text = ""; Say("Novo script.", false); }

        /// <summary>The syntax check with the island's Lua interpreter. Null when the source loads; otherwise the first mistake with its line.</summary>
        public static string SyntaxProblem(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return "O script está vazio.";
            if (source.Length > MaxSourceLength) return "O script passa de " + MaxSourceLength / 1024 + " KB.";
            try { new MoonSharp.Interpreter.Script(MoonSharp.Interpreter.CoreModules.None).LoadString(source); return null; }
            catch (MoonSharp.Interpreter.SyntaxErrorException exception) { return exception.DecoratedMessage ?? exception.Message; }
            catch (MoonSharp.Interpreter.ScriptRuntimeException exception) { return exception.DecoratedMessage ?? exception.Message; }
        }

        public bool Check()
        {
            var problem = SyntaxProblem(_source.value); if (problem != null) { Say("Erro: " + problem, true); return false; }
            Say("Sintaxe correta.", false); return true;
        }

        /// <summary>The syntax here, then the full check of the server (unknown events, typos): the problems by line go to the Problemas tab.</summary>
        public void Validate()
        {
            if (!Check()) { _problems.text = _status.text; ShowTab("Problemas"); return; }
            Say("Validando no servidor…", false);
            this.Then(_c.Api.ApiAsync("POST", Path + "/validate", JsonSerializer.Serialize(new { source = _source.value })), r =>
            {
                if (r.Status < 200 || r.Status >= 300) { Say(UiTasks.Explain(r.Status, r.Body), true); return; }
                var (valid, problems, handlers) = LuaEditorRules.ReadCheck(r.Body); Problems = problems.Count;
                _problems.text = problems.Count == 0 ? "Nenhum problema." : string.Join("\n", problems.Select(p => (p.Severity == "error" ? "ERRO" : "AVISO") + (p.Line > 0 ? " linha " + p.Line : "") + ": " + p.Message));
                _tabButtons["Problemas"].text = problems.Count == 0 ? "Problemas" : "Problemas (" + problems.Count + ")";
                var warnings = problems.Count(p => p.Severity == "warning");
                if (valid) { Say((warnings == 0 ? "Sem erros." : "Sem erros e com " + warnings + " aviso(s).") + (handlers.Count > 0 ? " Eventos tratados: " + string.Join(", ", handlers) + "." : ""), false); if (warnings > 0) ShowTab("Problemas"); return; }
                var first = problems.FirstOrDefault(p => p.Severity == "error"); Say("Erro" + (first.Line > 0 ? " na linha " + first.Line : "") + ": " + first.Message, true); ShowTab("Problemas"); GoToLine(first.Line);
            });
        }

        public int Problems { get; private set; }

        public void GoToLine(int line) { var (start, end) = LuaEditorRules.LineRange(_source.value, line); if (start < 0) return; _code.scrollOffset = new Vector2(0f, Mathf.Max(0f, (line - 4) * _lineHeight)); _source.Focus(); _source.SelectRange(start, end); ShowCursor(); }

        public void Save()
        {
            if (!Check()) return;
            var body = JsonSerializer.Serialize(new { name = _name.value.Trim().Length == 0 ? "script" : _name.value.Trim(), source = _source.value, eventName = _eventName.value.Trim().Length == 0 ? "on_start" : _eventName.value.Trim() });
            this.Then(_c.Api.ApiAsync(_open == Guid.Empty ? "POST" : "PUT", _open == Guid.Empty ? Path : Path + "/" + _open, body), r =>
            {
                if (r.Status >= 200 && r.Status < 300) { var row = Json.One(r.Body); if (_open == Guid.Empty && row.Id() != Guid.Empty) _open = row.Id(); _dirty = false; ShowPauseState("active"); _c.Notices.Show("Script salvo.", NoticeKind.Good); Say("Salvo.", false); LoadList(); } else Say(UiTasks.Explain(r.Status, r.Body), true);
            });
        }

        // ---- running: restart, stop, pause, the console and the use ----

        public void Restart()
        {
            if (_open == Guid.Empty) { Say("Salve o script primeiro.", true); return; }
            this.Then(_c.Api.ApiAsync("POST", Path + "/" + _open + "/reset", "{}"), r => { if (r.Status >= 200 && r.Status < 300) { ShowPauseState("active"); Say("Script reiniciado: a ilha o carrega de novo em até 15 segundos.", false); } else Say(UiTasks.Explain(r.Status, r.Body), true); });
        }

        public void Stop()
        {
            if (_open == Guid.Empty) { Say("Salve o script primeiro.", true); return; }
            this.Then(_c.Api.ApiAsync("POST", Path + "/" + _open + "/stop", "{}"), r => { if (r.Status >= 200 && r.Status < 300) { _state.text = "parado (aplicado em até 15 s)"; Say("Script parado. Reiniciar ou salvar liga de novo.", false); } else Say(UiTasks.Explain(r.Status, r.Body), true); });
        }

        public void Pause()
        {
            if (_open == Guid.Empty) { Say("Salve o script primeiro.", true); return; }
            var pause = _pause.text == "Pausar";
            this.Then(_c.Api.ApiAsync("POST", Path + "/" + _open + "/" + (pause ? "pause" : "resume"), "{}"), r =>
            {
                if (r.Status == 409) { Say("O script está parado: reinicie para ligar de novo.", true); return; }
                if (r.Status >= 200 && r.Status < 300) { ShowPauseState(pause ? "paused" : "active"); Say(pause ? "Script pausado." : "Script continua.", false); } else Say(UiTasks.Explain(r.Status, r.Body), true);
            });
        }

        private void ShowPauseState(string status) => _pause.text = status == "paused" ? "Continuar" : "Pausar";

        public LuaEditorRules.Runtime Runtime { get; private set; }

        /// <summary>How the script is doing (every 5 seconds while the window is open): the state, the console, the use and the permissions. Before the island reports, the runs the platform recorded.</summary>
        public void RefreshRuntime()
        {
            if (_open == Guid.Empty) return; var script = _open;
            this.Then(_c.Api.ApiAsync("GET", Path + "/" + script + "/runtime"), r =>
            {
                if (script != _open) return;
                var runtime = r.Status == 200 ? LuaEditorRules.ReadRuntime(r.Body) : null; Runtime = runtime; _performance.text = LuaEditorRules.Performance(runtime);
                if (runtime != null)
                {
                    ShowPauseState(runtime.Status); _state.text = runtime.Reported ? "● " + (runtime.StateText.Length > 0 ? runtime.StateText : runtime.State) : "● aguardando a ilha";
                    _state.EnableInClassList("lv-bad", runtime.State is "Suspended" or "Faulted"); _state.EnableInClassList("lv-good", runtime.Reported && runtime.State is "Running" or "Idle" or "Active");
                    _syncing = true; foreach (var pair in _owner) pair.Value.value = runtime.Grants.Any(g => g.Permission == pair.Key && Guid.TryParse(g.Subject, out var s) && s == Guid.Empty); _syncing = false;
                    ShowGrants(runtime.Grants.Where(g => !(Guid.TryParse(g.Subject, out var s) && s == Guid.Empty)).ToList());
                }
                if (runtime != null && (runtime.Reported || runtime.Logs.Count > 0)) { _console.text = runtime.Logs.Count == 0 ? "Console: o script ainda não escreveu nada." : string.Join("\n", runtime.Logs.Select(LuaEditorRules.FormatLog)); return; }
                this.Then(_c.Api.ApiAsync("GET", Path + "/" + script + "/executions"), e =>
                {
                    if (script != _open) return; var runs = e.Status == 200 ? Json.Rows(e.Body) : new List<Row>();
                    _console.text = runs.Count == 0 ? "Console: sem execuções ainda. Toque no objeto, entre perto dele ou espere o temporizador." : string.Join("\n", runs.Select(x => x.Text("createdAt").Replace('T', ' ').PadRight(19).Substring(0, 19) + "  " + x.Text("eventName") + ": " + x.Text("status") + (x.Text("error").Length > 0 ? " — " + x.Text("error") : "")));
                });
            });
        }

        private void ShowGrants(List<(string Subject, string Permission)> grants)
        {
            _revokes.Clear(); _grants.text = grants.Count == 0 ? "Este script não recebeu nenhuma permissão de avatar." : "Permissões dadas por avatares:";
            foreach (var (subject, permission) in grants) { var s = subject; var p = permission; _revokes.Add(UiKit.Row(UiKit.Text("• " + p + " — avatar " + s.Substring(0, Math.Min(8, s.Length)) + "…", "lv-small"), UiKit.Spacer(), UiKit.Button("Revogar", () => Revoke(s, p)))); }
        }

        /// <summary>The owner gives the script a permission over the object (or takes it back); the island applies it within about 15 seconds.</summary>
        public void SetOwnerPermission(string permission, bool granted)
        {
            if (_open == Guid.Empty) { Say("Salve o script primeiro.", true); _syncing = true; _owner[permission].value = !granted; _syncing = false; return; }
            this.Then(_c.Api.ApiAsync("PUT", Path + "/" + _open + "/permissions/" + permission, JsonSerializer.Serialize(new { granted })), r => Say(r.Status >= 200 && r.Status < 300 ? permission + (granted ? " concedida: a ilha aplica em até 15 segundos." : " retirada.") : UiTasks.Explain(r.Status, r.Body), !(r.Status >= 200 && r.Status < 300)));
        }

        /// <summary>Ticks or unticks a permission of the owner and sends it (the same as clicking its box).</summary>
        public void Grant(string permission, bool granted) { if (!_owner.TryGetValue(permission, out var box)) return; _syncing = true; box.value = granted; _syncing = false; SetOwnerPermission(permission, granted); }

        private void Revoke(string subject, string permission) => this.Then(_c.Api.ApiAsync("DELETE", Path + "/" + _open + "/avatar-permissions/" + subject + "/" + permission), r => { Say(r.Status >= 200 && r.Status < 300 ? "Permissão revogada: a ilha deixa de aceitá-la em até 15 segundos." : UiTasks.Explain(r.Status, r.Body), !(r.Status >= 200 && r.Status < 300)); RefreshRuntime(); });

        // ---- the debugger (through the connection to the island; only the owner of the script may use it) ----

        /// <summary>Puts or takes the line of the cursor from the lines of parada.</summary>
        public void ToggleCursorLine() => ToggleLine(LuaEditorRules.LineOf(_source.value, _source.cursorIndex));
        public void SetBreakpoints(params int[] lines) => SetLines(lines);

        /// <summary>"Depurar" / "Parar depuração".</summary>
        public void ToggleDebugging()
        {
            if (_open == Guid.Empty) { Say("Salve o script antes de depurar: a ilha depura o código salvo.", true); return; }
            if (Debugging) { SendDebug("stop"); return; }
            if (_dirty) { Say("Salve o script antes de depurar: a ilha depura o código salvo.", true); return; }
            if (Breakpoints.Count == 0) { Say("Marque ao menos uma linha de parada na aba Depuração.", true); ShowTab("Depuração"); return; }
            SendDebug("start"); Say("Pedindo à ilha para depurar o script…", false); ShowTab("Depuração");
        }

        private void SendDebug(string action)
        {
            var json = LuaEditorRules.DebugCommand(_open, action, Breakpoints);
            if (DebugSender != null) { DebugSender(json); return; }
            if (_c.SendToIsland == null || !_c.SendToIsland(json)) Say("Sem conexão com a ilha: entre na ilha do objeto para depurar.", true);
        }

        private void OnIslandScript(string json) { if (Body.panel != null || DebugSender != null) ReceiveDebug(json); }

        /// <summary>What the island says about the script being debugged: started, stopped at a line, running, refused, ended.</summary>
        public void ReceiveDebug(string json)
        {
            var e = LuaEditorRules.ReadDebug(json, _open); if (e == null) return;
            if (e.Kind == "refused") { EndDebug("A ilha já depura outros scripts: termine um deles primeiro."); return; }
            if (e.Kind == "ended") { EndDebug(e.Reason == "timeout" ? "A depuração terminou: o script ficou parado por mais de 2 minutos e foi solto." : "Depuração encerrada."); return; }
            Debugging = e.Debugging; _debug.text = Debugging ? "Parar depuração" : "Depurar"; Stopped = e.Stopped; StoppedLine = e.Stopped ? e.Line : 0; PaintGutter();
            if (Stopped) { _debugText.text = e.Text; ShowTab("Depuração"); GoToLine(e.Line); Say("Parado na linha " + e.Line + ".", false); }
            else _debugText.text = e.Kind == "running" ? "O script está rodando (ele para de novo no próximo ponto de parada)." : Debugging ? "Depurando: aguardando o script chegar a uma linha de parada. Toque no objeto ou espere o evento." : DebugHelp;
            foreach (var b in _steps) b.SetEnabled(Stopped);
            if (e.Kind == "started") Say("Depurando: o script foi carregado de novo (on_start rodou).", false);
        }

        private void EndDebug(string message) { Debugging = false; Stopped = false; StoppedLine = 0; PaintGutter(); _debug.text = "Depurar"; foreach (var b in _steps) b.SetEnabled(false); _debugText.text = DebugHelp; Say(message, false); }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); _status.EnableInClassList("lv-muted", !error); }
    }
}
