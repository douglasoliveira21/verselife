using System.Globalization;
using System.Text.RegularExpressions;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The editor of a script that lives inside an object (opened from the Conteúdo tab): a code box with line numbers, Lua colours and completion from the real API; Save (Ctrl+S), Validate (errors and warnings with their line),
/// Restart, Stop, Pause and the documentation; and under the code four tabs: the console of the script (what it printed and its errors), the problems found by Validate, its use of the island and the permissions it holds.
/// Scripts run on the island server, on that object; a saved version is picked up by the island within about 15 seconds, and the island reports how the script is doing every 30 seconds.
/// </summary>
public partial class ScriptEditorWindow : Node
{
    private static readonly string[] Namespaces = ["object", "chat", "timer", "storage", "debug", "region", "script", "permissions", "dialog", "audio", "lv"];
    private static readonly string[] Keywords = ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while"];
    private static readonly Regex Member = new(@"([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z0-9_]*)$", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    private FloatingWindow _window = null!;
    private LineEdit _name = null!;
    private Label _heading = null!, _stateChip = null!;
    private CodeEdit _code = null!;
    private OptionButton _models = null!;
    private Label _info = null!, _console = null!, _performance = null!, _permissions = null!;
    private ItemList _problems = null!;
    private TabContainer _tabs = null!;
    private Button _pause = null!;
    private ScriptApi _api = null!;
    private Guid _object, _script;
    private int _version;
    private List<ScriptTemplate> _templates = [];
    private ScriptReference? _reference;
    private ScriptRuntimeView? _runtime;
    private List<ScriptDiagnosticView> _diagnostics = [];
    private ScriptDocsWindow? _docs;
    private bool _dirty, _refreshing;
    private double _refreshIn = 5;

    public event Action? Saved;
    public bool IsOpen => _window is not null && _window.Visible;
    public string SourceForTesting { get => _code.Text; set => _code.Text = value; }
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public string ConsoleForTesting => _console.Text;
    public string InfoForTesting => _info.Text;
    public bool DirtyForTesting => _dirty;
    public Guid ScriptIdForTesting => _script;
    public string StateForTesting => _stateChip.Text;
    public string PerformanceForTesting => _performance.Text;
    public string PermissionsForTesting => _permissions.Text;
    public IReadOnlyList<string> ProblemsForTesting => [.. Enumerable.Range(0, _problems.ItemCount).Select(i => _problems.GetItemText(i))];
    public string TabForTesting => _tabs.GetTabTitle(_tabs.CurrentTab);
    public IReadOnlyList<string> TabTitlesForTesting => [.. Enumerable.Range(0, _tabs.GetTabCount()).Select(i => _tabs.GetTabTitle(i))];
    public string PauseTextForTesting => _pause.Text;
    public ScriptDocsWindow? DocsForTesting => _docs;
    public int CaretLineForTesting => _code.GetCaretLine() + 1;
    public void SelectProblemForTesting(int index) { if (index >= 0 && index < _problems.ItemCount) OnProblem(index); }
    public Vector2 SizeForTesting => _window.Size;
    public void PressForTesting(string text) { if (_window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text && b.Visible) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public bool HasButtonForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text && b.Visible);

    public void Initialize(ScriptApi api) { _api = api; BuildWindow(); }
    public void UseApi(ScriptApi api) { _api = api; _docs?.UseApi(api); }

    private void BuildWindow()
    {
        _window = new FloatingWindow("Script do objeto", icon: "code") { Name = "ScriptEditor", Visible = false };
        _window.Place(0, 0, 520, 50, 1420, 960); AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); content.AddChild(head);
        _heading = new Label { Text = "Script", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _heading.AddThemeFontOverride("font", UiStyle.Bold); _heading.AddThemeColorOverride("font_color", UiStyle.AccentText); head.AddChild(_heading);
        _stateChip = new Label { Text = "", TooltipText = "Como o script está na ilha" }; _stateChip.AddThemeFontSizeOverride("font_size", 13); head.AddChild(_stateChip);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); content.AddChild(top);
        top.AddChild(new Label { Text = "Nome:" });
        _name = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 120, PlaceholderText = "Nome do script" }; _name.TextChanged += _ => MarkDirty(); top.AddChild(_name);
        _models = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Modelos para começar" }; _models.AddItem("Modelos…"); top.AddChild(_models);
        _models.ItemSelected += index => { if (index > 0 && index - 1 < _templates.Count) { _code.Text = _templates[(int)index - 1].Source; MarkDirty(); _models.Select(0); } };
        _code = new CodeEdit
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(760, 300), GuttersDrawLineNumbers = true, LineFolding = true, GuttersDrawFoldGutter = true, IndentUseSpaces = true, IndentSize = 2,
            AutoBraceCompletionEnabled = true, DrawTabs = false, SyntaxHighlighter = LuaHighlighter(), CodeCompletionEnabled = true, CodeCompletionPrefixes = ["."], DelimiterStrings = ["\" \"", "' '"], DelimiterComments = ["-- "]
        };
        _code.AddThemeFontSizeOverride("font_size", 16); _code.TextChanged += MarkDirty; _code.CodeCompletionRequested += RequestCompletion; content.AddChild(_code);
        _info = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart }; _info.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_info);

        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6); content.AddChild(buttons);
        Button Make(string text, string tip, UiStyle.ButtonKind kind, Action action) { var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tip }; UiStyle.StyleButton(b, kind, 14, 6, 14); b.Pressed += action; buttons.AddChild(b); return b; }
        Make("Salvar (Ctrl+S)", "Grava o script no objeto; a ilha aplica a nova versão em até 15 segundos", UiStyle.ButtonKind.Primary, () => _ = SaveAsync());
        Make("Validar", "Confere o código sem salvar: erros e avisos, cada um com a sua linha", UiStyle.ButtonKind.Secondary, () => _ = ValidateAsync());
        Make("Reiniciar", "Carrega o script de novo (on_reset e on_start); o que ele guardou com storage fica", UiStyle.ButtonKind.Secondary, () => _ = RestartAsync());
        Make("Parar", "Para o script: sem eventos nem temporizadores até reiniciar ou salvar", UiStyle.ButtonKind.Secondary, () => _ = StopAsync());
        _pause = Make("Pausar", "Pausa o script: ele não recebe eventos e os temporizadores param", UiStyle.ButtonKind.Secondary, () => _ = PauseAsync());
        Make("Atualizar", "Lê de novo o console, o desempenho e as permissões", UiStyle.ButtonKind.Secondary, () => _ = RefreshConsoleAsync());
        Make("Documentação", "Abre a referência da linguagem: funções, eventos, limites e exemplos", UiStyle.ButtonKind.Secondary, OpenDocs);

        _tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 170), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; content.AddChild(_tabs);
        _console = TextTab("Console", "Console: sem execuções ainda.");
        _problems = new ItemList { Name = "Problemas", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _problems.ItemSelected += index => OnProblem((int)index); _tabs.AddChild(_problems);
        _performance = TextTab("Desempenho", "Sem dados ainda: a ilha informa o uso a cada 30 segundos."); _permissions = PermissionsTab();
        _tabs.SetTabTitle(1, "Problemas");
    }

    private static readonly (string Name, string Text)[] OwnerPermissions = [("CONTROL_PHYSICS", "Controlar a física do objeto (physics.*)"), ("HTTP_REQUESTS", "Acessar endereços externos (http.*)"), ("CONTROL_MEDIA", "Controlar a mídia do objeto"), ("CREATE_NPCS", "Criar NPCs")];
    private readonly Dictionary<string, CheckBox> _ownerBoxes = [];

    private Label PermissionsTab()
    {
        var scroll = new ScrollContainer { Name = "Permissões", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _tabs.AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 4); scroll.AddChild(column);
        var heading = new Label { Text = "O que você permite a este script (só o dono decide):" }; heading.AddThemeFontSizeOverride("font_size", 14); heading.AddThemeColorOverride("font_color", UiStyle.TextMuted); column.AddChild(heading);
        foreach (var (name, text) in OwnerPermissions)
        {
            var permission = name; var box = new CheckBox { Text = $" {text}", FocusMode = Control.FocusModeEnum.None, Name = name }; box.AddThemeFontSizeOverride("font_size", 14);
            box.Toggled += on => { if (_script != Guid.Empty && !_syncingOwnerBoxes) _ = SetOwnerPermissionAsync(permission, on); }; column.AddChild(box); _ownerBoxes[name] = box;
        }
        var label = new Label { Text = "Este script não recebeu nenhuma permissão de avatar.", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", 14); column.AddChild(label); return label;
    }

    private bool _syncingOwnerBoxes;
    public IReadOnlyDictionary<string, bool> OwnerPermissionsForTesting => _ownerBoxes.ToDictionary(p => p.Key, p => p.Value.ButtonPressed);
    public void ToggleOwnerPermissionForTesting(string name) { if (_ownerBoxes.TryGetValue(name, out var box)) box.ButtonPressed = !box.ButtonPressed; }

    public async Task SetOwnerPermissionAsync(string permission, bool granted)
    {
        var result = await _api.SetOwnerPermissionAsync(_object, _script, permission, granted); _info.Text = result.Message;
        if (!result.Ok) { _syncingOwnerBoxes = true; _ownerBoxes[permission].ButtonPressed = !granted; _syncingOwnerBoxes = false; }
    }

    private Label TextTab(string title, string text)
    {
        var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _tabs.AddChild(scroll);
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; label.AddThemeFontSizeOverride("font_size", 14); scroll.AddChild(label); return label;
    }

    private static CodeHighlighter LuaHighlighter()
    {
        var highlighter = new CodeHighlighter { NumberColor = new Color("#F4A340"), SymbolColor = new Color("#A9B3BD"), FunctionColor = new Color("#8FE3FF"), MemberVariableColor = new Color("#C3CCD3") };
        foreach (var word in new[] { "and", "break", "do", "else", "elseif", "end", "for", "function", "if", "in", "local", "not", "or", "repeat", "return", "then", "until", "while" }) highlighter.AddKeywordColor(word, new Color("#12F2E0"));
        foreach (var word in new[] { "true", "false", "nil" }) highlighter.AddKeywordColor(word, new Color("#F4A340"));
        foreach (var word in Namespaces) highlighter.AddKeywordColor(word, new Color("#B79CFF"));
        highlighter.AddColorRegion("--", "", new Color("#6D818D"), true); highlighter.AddColorRegion("\"", "\"", new Color("#B6F09C")); highlighter.AddColorRegion("'", "'", new Color("#B6F09C"));
        return highlighter;
    }

    // ---- completion: from the real API (what the server documents), with the namespaces as a fallback ----

    /// <summary>The suggestions for what is typed before the cursor: after <c>name.</c> the functions of that namespace; otherwise namespaces, events, keywords and the names already in the code.</summary>
    public List<(string Shown, string Insert)> CompletionFor(string beforeCursor)
    {
        var result = new List<(string, string)>();
        if (Member.Match(beforeCursor) is { Success: true } member)
        {
            var ns = member.Groups[1].Value; var typed = member.Groups[2].Value;
            foreach (var name in FunctionsOf(ns).Where(n => n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal)) result.Add((name, name + "("));
            return result;
        }
        var word = Word.Match(beforeCursor) is { Success: true } w ? w.Groups[1].Value : "";
        if (word.Length == 0) return result;
        foreach (var ns in Namespaces.Where(n => n.StartsWith(word, StringComparison.OrdinalIgnoreCase) && n != word)) result.Add((ns, ns));
        foreach (var name in EventNames().Where(n => n.StartsWith(word, StringComparison.OrdinalIgnoreCase) && n != word)) result.Add((name, name + "()"));
        foreach (var keyword in Keywords.Where(k => k.StartsWith(word, StringComparison.Ordinal) && k != word)) result.Add((keyword, keyword));
        foreach (Match local in Regex.Matches(_code.Text, @"\blocal\s+([A-Za-z_][A-Za-z0-9_]*)")) if (local.Groups[1].Value.StartsWith(word, StringComparison.Ordinal) && local.Groups[1].Value != word && result.All(r => r.Item1 != local.Groups[1].Value)) result.Add((local.Groups[1].Value, local.Groups[1].Value));
        return result;
    }

    private IEnumerable<string> FunctionsOf(string ns) => _reference is not null ? _reference.Functions.Where(f => f.Namespace == ns).Select(f => f.Name).Distinct() : [];
    private IEnumerable<string> EventNames() => _reference is not null ? _reference.Events.Select(e => e.Name) : ["on_start", "on_touch", "on_enter", "on_leave", "on_timer", "on_chat", "on_message"];

    private void RequestCompletion()
    {
        var line = _code.GetLine(_code.GetCaretLine())[.._code.GetCaretColumn()];
        foreach (var (shown, insert) in CompletionFor(line)) _code.AddCodeCompletionOption(CodeEdit.CodeCompletionKind.Function, shown, insert);
        _code.UpdateCodeCompletionOptions(true);
    }

    // ---- the window ----

    private void MarkDirty() { _dirty = true; if (_window is not null) _heading.Text = "Script: " + (_name.Text.Length > 0 ? _name.Text : "sem nome") + " *"; }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (IsOpen && input is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.S }) { _ = SaveAsync(); GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        if (!IsOpen || _script == Guid.Empty) return;
        _refreshIn -= delta; if (_refreshIn > 0) return; _refreshIn = 5; _ = RefreshConsoleAsync();
    }

    /// <summary>Opens a script of the object (read with its source) in the window.</summary>
    public async Task OpenAsync(Guid objectId, Guid scriptId)
    {
        var detail = await _api.GetAsync(objectId, scriptId);
        if (detail is null) { Show(objectId, scriptId, "Script", "", 0, "", "Não foi possível abrir o script."); return; }
        Show(objectId, detail.Id, detail.Name, detail.Source, detail.Version, detail.Status, null);
        _templates = await _api.TemplatesAsync(); FillModels(); _reference ??= await _api.ReferenceAsync(); await RefreshConsoleAsync();
    }

    private void Show(Guid objectId, Guid scriptId, string name, string source, int version, string status, string? problem)
    {
        _object = objectId; _script = scriptId; _version = version; _name.Text = name; _code.Text = source; _dirty = false; _heading.Text = "Script: " + name; _runtime = null; _diagnostics = []; _problems.Clear(); _stateChip.Text = "";
        _info.Text = problem ?? $"Versão {version} · {(status == "active" ? "ativo" : status == "paused" ? "pausado" : status == "stopped" ? "parado (salvar ou reiniciar ativa de novo)" : status)} · roda no servidor da ilha, neste objeto.";
        _window.Visible = true; _window.MoveToFront(); _code.GrabFocus(); _refreshIn = 5; ShowPauseState(status);
    }

    private void FillModels()
    {
        _models.Clear(); _models.AddItem("Modelos…"); foreach (var template in _templates) _models.AddItem(template.Name.Replace('_', ' '));
    }

    private void ShowPauseState(string status) => _pause.Text = status == "paused" ? "Continuar" : "Pausar";

    public async Task SaveAsync()
    {
        if (_script == Guid.Empty) return;
        var name = _name.Text.Trim(); if (name.Length == 0) { _info.Text = "Dê um nome ao script."; return; }
        _info.Text = "Salvando…";
        var result = await _api.SaveAsync(_object, _script, name, _code.Text);
        if (!result.Ok) { _info.Text = result.Message; return; }
        _dirty = false; _version++; _heading.Text = "Script: " + name; _info.Text = $"{result.Message} (versão {_version})"; ShowPauseState("active"); Saved?.Invoke();
    }

    /// <summary>Validar: asks the server to check the code in the box (saved or not). Errors and warnings go to the Problemas tab, each with its line; picking one takes the cursor there. True when there are no errors.</summary>
    public async Task<bool> ValidateAsync()
    {
        if (_script == Guid.Empty) return false;
        _info.Text = "Validando…";
        var (valid, diagnostics, handlers, message) = await _api.CheckAsync(_object, _code.Text);
        _diagnostics = [.. diagnostics.OrderByDescending(d => d.Severity == "error").ThenBy(d => d.Line)]; FillProblems();
        if (message.Length > 0) { _info.Text = message; return false; }
        var errors = _diagnostics.Count(d => d.Severity == "error"); var warnings = _diagnostics.Count - errors;
        if (valid) { _info.Text = (warnings == 0 ? "Sem erros." : $"Sem erros e com {warnings} aviso(s).") + (handlers.Count > 0 ? $" Eventos tratados: {string.Join(", ", handlers)}." : ""); if (warnings > 0) _tabs.CurrentTab = 1; return true; }
        var first = _diagnostics.First(d => d.Severity == "error"); _tabs.CurrentTab = 1;
        _info.Text = first.Line > 0 ? $"Erro na linha {first.Line}: {first.Message}" : first.Message;
        GoToLine(first.Line); return false;
    }

    private void FillProblems()
    {
        _problems.Clear();
        foreach (var d in _diagnostics) { var index = _problems.AddItem($"{(d.Severity == "error" ? "Erro" : "Aviso")}{(d.Line > 0 ? $" (linha {d.Line})" : "")}: {d.Message}"); _problems.SetItemCustomFgColor(index, d.Severity == "error" ? UiStyle.Danger : UiStyle.Warning); }
        _tabs.SetTabTitle(1, _diagnostics.Count == 0 ? "Problemas" : $"Problemas ({_diagnostics.Count})");
    }

    private void OnProblem(int index) { if (index >= 0 && index < _diagnostics.Count) GoToLine(_diagnostics[index].Line); }

    private void GoToLine(int line) { if (line > 0 && line <= _code.GetLineCount()) { _code.SetCaretLine(line - 1); _code.CenterViewportToCaret(); _code.GrabFocus(); } }

    public async Task RestartAsync()
    {
        if (_script == Guid.Empty) return;
        var result = await _api.ResetAsync(_object, _script); _info.Text = result.Ok ? "Script reiniciado: a ilha o carrega de novo em até 15 segundos." : result.Message; if (result.Ok) { _version++; ShowPauseState("active"); }
    }

    public async Task StopAsync()
    {
        if (_script == Guid.Empty) return;
        var result = await _api.StopAsync(_object, _script); _info.Text = result.Message; if (result.Ok) _stateChip.Text = "parado (aplicado em até 15 s)";
    }

    public async Task PauseAsync()
    {
        if (_script == Guid.Empty) return;
        var pause = _pause.Text == "Pausar"; var result = await _api.PauseAsync(_object, _script, pause); _info.Text = result.Message; if (result.Ok) ShowPauseState(pause ? "paused" : "active");
    }

    private void OpenDocs()
    {
        if (_docs is null) { _docs = new ScriptDocsWindow { Name = "ScriptDocs" }; _docs.UseApi(_api); _docs.InsertExample = text => { if (IsOpen) { _code.InsertTextAtCaret(text); MarkDirty(); } }; AddChild(_docs); }
        _ = _docs.OpenAsync(_reference);
    }

    /// <summary>The console, the performance and the permissions: what the island reported about the script (state, counters, the lines it wrote, who allowed it what). Without a report yet it falls back to the list of the last runs.</summary>
    public async Task RefreshConsoleAsync()
    {
        if (_script == Guid.Empty || _refreshing) return;
        _refreshing = true;
        try
        {
            _runtime = await _api.RuntimeAsync(_object, _script);
            if (_runtime is null || (!_runtime.Reported && _runtime.Logs.Count == 0)) { await RunsFallbackAsync(); ShowRuntime(_runtime); return; }
            ShowRuntime(_runtime);
            _console.Text = _runtime.Logs.Count == 0 ? "Console: o script ainda não escreveu nada." : string.Join("\n", _runtime.Logs.Select(FormatLog));
        }
        finally { _refreshing = false; }
    }

    private async Task RunsFallbackAsync()
    {
        var runs = await _api.RunsAsync(_object, _script);
        _console.Text = runs.Count == 0 ? "Console: sem execuções ainda. Toque no objeto, entre perto dele ou espere o temporizador." : string.Join("\n", runs.Select(r => $"{r.At.Replace('T', ' ')[..Math.Min(19, r.At.Length)]}  {r.EventName}: {r.Status}{(r.Error.Length > 0 ? " — " + r.Error : "")}"));
    }

    private static string FormatLog(ScriptLogView line)
    {
        var at = line.At.Length >= 19 ? line.At.Replace('T', ' ')[11..19] : line.At; var level = line.Level switch { "error" => "ERRO", "warning" => "AVISO", _ => "info" };
        return $"{at}  {level,-5}  {line.Event}: {line.Text}{(line.Stack.Length > 0 ? $"\n               ↳ {line.Stack}" : "")}";
    }

    private void ShowRuntime(ScriptRuntimeView? runtime)
    {
        if (runtime is null) { _performance.Text = "Não foi possível ler o desempenho agora."; return; }
        ShowPauseState(runtime.Status);
        _stateChip.Text = runtime.Reported ? $"● {runtime.StateText}" : "● aguardando a ilha"; _stateChip.AddThemeColorOverride("font_color", !runtime.Reported ? UiStyle.TextMuted : runtime.State is "Suspended" or "Faulted" ? UiStyle.Danger : runtime.State == "Paused" ? UiStyle.Warning : UiStyle.SuccessBright);
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        _performance.Text = !runtime.Reported ? "A ilha ainda não informou o uso deste script (ela informa a cada 30 segundos)." : string.Join("\n",
            $"Estado: {runtime.StateText}{(runtime.SuspendedReason.Length > 0 ? $" — {runtime.SuspendedReason}" : "")}", $"Eventos tratados: {runtime.Events.ToString("N0", pt)} (descartados por limite: {runtime.EventsDropped.ToString("N0", pt)})",
            $"CPU: {runtime.CpuMilliseconds.ToString("N1", pt)} ms no total · o evento mais lento levou {runtime.WorstEventMilliseconds.ToString("N2", pt)} ms", $"Instruções: {runtime.Instructions.ToString("N0", pt)} · memória alocada: {(runtime.AllocatedBytes / 1024.0).ToString("N0", pt)} KB",
            $"Chamadas à API: {runtime.ApiCalls.ToString("N0", pt)} · mensagens enviadas: {runtime.MessagesSent.ToString("N0", pt)} · temporizadores ativos: {runtime.ActiveTimers} · chaves guardadas: {runtime.StateKeys}",
            $"Erros: {runtime.Errors} · estouros de limite: {runtime.Violations} · suspensões: {runtime.Suspensions}", runtime.LastError.Length > 0 ? $"Último erro{(runtime.LastErrorLine > 0 ? $" (linha {runtime.LastErrorLine})" : "")}: {runtime.LastError}" : "Nenhum erro até agora.");
        _syncingOwnerBoxes = true; foreach (var (name, box) in _ownerBoxes) box.ButtonPressed = runtime.Grants.Any(g => g.Permission == name && Guid.TryParse(g.Subject, out var subject) && subject == Guid.Empty); _syncingOwnerBoxes = false;
        var fromAvatars = runtime.Grants.Where(g => !(Guid.TryParse(g.Subject, out var one) && one == Guid.Empty)).ToList();
        _permissions.Text = fromAvatars.Count == 0 ? "Este script não recebeu nenhuma permissão de avatar." : "Permissões dadas por avatares:\n" + string.Join("\n", fromAvatars.Select(g => $"• {g.Permission} — dada pelo avatar {g.Subject[..Math.Min(8, g.Subject.Length)]}…"));
    }

    public void Close() { if (_window is not null) _window.Visible = false; }
}
