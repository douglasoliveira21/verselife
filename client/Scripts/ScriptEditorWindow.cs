using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The editor of a script that lives inside an object (opened from the Conteúdo tab): a code box with line numbers and Lua colours, models to start from, Save (Ctrl+S)
/// and a console with how the last runs went (the event, the result and the error when there was one). Scripts run on the island server, on that object; a saved version is
/// picked up by the island within about 15 seconds.
/// </summary>
public partial class ScriptEditorWindow : Node
{
    private FloatingWindow _window = null!;
    private LineEdit _name = null!;
    private Label _heading = null!;
    private CodeEdit _code = null!;
    private OptionButton _models = null!;
    private Label _info = null!, _console = null!;
    private ScriptApi _api = null!;
    private Guid _object, _script;
    private int _version;
    private List<ScriptTemplate> _templates = [];
    private bool _dirty;

    public event Action? Saved;
    public bool IsOpen => _window is not null && _window.Visible;
    public string SourceForTesting { get => _code.Text; set => _code.Text = value; }
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public string ConsoleForTesting => _console.Text;
    public string InfoForTesting => _info.Text;
    public bool DirtyForTesting => _dirty;
    public Guid ScriptIdForTesting => _script;

    public void Initialize(ScriptApi api) { _api = api; BuildWindow(); }
    public void UseApi(ScriptApi api) => _api = api;

    private void BuildWindow()
    {
        _window = new FloatingWindow("Script do objeto", icon: "code") { Name = "ScriptEditor", Visible = false };
        _window.Place(0, 0, 560, 70, 1400, 920); AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _heading = new Label { Text = "Script" }; _heading.AddThemeFontOverride("font", UiStyle.Bold); _heading.AddThemeColorOverride("font_color", UiStyle.AccentText); content.AddChild(_heading);
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); content.AddChild(top);
        var nameLabel = new Label { Text = "Nome:" }; top.AddChild(nameLabel);
        _name = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 120, PlaceholderText = "Nome do script" }; _name.TextChanged += _ => MarkDirty(); top.AddChild(_name);
        _models = new OptionButton { FocusMode = Control.FocusModeEnum.None, TooltipText = "Modelos para começar" }; _models.AddItem("Modelos…"); top.AddChild(_models);
        _models.ItemSelected += index => { if (index > 0 && index - 1 < _templates.Count) { _code.Text = _templates[(int)index - 1].Source; MarkDirty(); _models.Select(0); } };
        _code = new CodeEdit
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(760, 360), GuttersDrawLineNumbers = true, LineFolding = false, IndentUseSpaces = true, IndentSize = 2,
            AutoBraceCompletionEnabled = true, DrawTabs = false, SyntaxHighlighter = LuaHighlighter()
        };
        _code.AddThemeFontSizeOverride("font_size", 16); _code.TextChanged += MarkDirty; content.AddChild(_code);
        _info = new Label { Text = string.Empty, AutowrapMode = TextServer.AutowrapMode.WordSmart }; _info.AddThemeColorOverride("font_color", UiStyle.TextMuted); content.AddChild(_info);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); content.AddChild(buttons);
        var save = new Button { Text = "Salvar (Ctrl+S)", FocusMode = Control.FocusModeEnum.None, TooltipText = "Grava o script no objeto; a ilha aplica a nova versão em até 15 segundos" }; UiStyle.StyleButton(save, UiStyle.ButtonKind.Primary, 16, 6, 14); save.Pressed += () => _ = SaveAsync(); buttons.AddChild(save);
        var runs = new Button { Text = "Atualizar console", FocusMode = Control.FocusModeEnum.None, TooltipText = "Mostra como foram as últimas execuções" }; UiStyle.StyleButton(runs, UiStyle.ButtonKind.Secondary, 16, 6, 14); runs.Pressed += () => _ = RefreshConsoleAsync(); buttons.AddChild(runs);
        var help = new Label { Text = "Eventos: on_start, on_touch, on_enter, on_timer, on_chat, on_message · object.say, get_position/rotation/scale, set_position/rotation/scale, move, rotate, set_color, set_material, create_timer, cancel_timers, state_get, state_set, send_message, get_id, get_name, play_sound, stop_sound, set_text · lv.time", AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; help.AddThemeFontSizeOverride("font_size", 14); help.AddThemeColorOverride("font_color", UiStyle.TextMuted); buttons.AddChild(help);
        _console = new Label { Text = "Console: sem execuções ainda.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(0, 120) }; _console.AddThemeFontSizeOverride("font_size", 14);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 130), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(_console); _console.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; content.AddChild(scroll);
    }

    private static CodeHighlighter LuaHighlighter()
    {
        var highlighter = new CodeHighlighter { NumberColor = new Color("#F4A340"), SymbolColor = new Color("#A9B3BD"), FunctionColor = new Color("#8FE3FF"), MemberVariableColor = new Color("#C3CCD3") };
        foreach (var word in new[] { "and", "break", "do", "else", "elseif", "end", "for", "function", "if", "in", "local", "not", "or", "repeat", "return", "then", "until", "while" }) highlighter.AddKeywordColor(word, new Color("#12F2E0"));
        foreach (var word in new[] { "true", "false", "nil" }) highlighter.AddKeywordColor(word, new Color("#F4A340"));
        highlighter.AddColorRegion("--", "", new Color("#6D818D"), true); highlighter.AddColorRegion("\"", "\"", new Color("#B6F09C")); highlighter.AddColorRegion("'", "'", new Color("#B6F09C"));
        return highlighter;
    }

    private void MarkDirty() { _dirty = true; if (_window is not null) _heading.Text = "Script: " + (_name.Text.Length > 0 ? _name.Text : "sem nome") + " *"; }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (IsOpen && input is InputEventKey { Pressed: true, CtrlPressed: true, Keycode: Key.S }) { _ = SaveAsync(); GetViewport().SetInputAsHandled(); }
    }

    /// <summary>Opens a script of the object (read with its source) in the window.</summary>
    public async Task OpenAsync(Guid objectId, Guid scriptId)
    {
        var detail = await _api.GetAsync(objectId, scriptId);
        if (detail is null) { Show(objectId, scriptId, "Script", "", 0, "", "Não foi possível abrir o script."); return; }
        Show(objectId, detail.Id, detail.Name, detail.Source, detail.Version, detail.Status, null);
        _templates = await _api.TemplatesAsync(); FillModels(); await RefreshConsoleAsync();
    }

    private void Show(Guid objectId, Guid scriptId, string name, string source, int version, string status, string? problem)
    {
        _object = objectId; _script = scriptId; _version = version; _name.Text = name; _code.Text = source; _dirty = false; _heading.Text = "Script: " + name;
        _info.Text = problem ?? $"Versão {version} · {(status == "active" ? "ativo" : status == "stopped" ? "parado (salvar ativa de novo)" : status)} · roda no servidor da ilha, neste objeto.";
        _window.Visible = true; _window.MoveToFront(); _code.GrabFocus();
    }

    private void FillModels()
    {
        _models.Clear(); _models.AddItem("Modelos…"); foreach (var template in _templates) _models.AddItem(template.Name.Replace('_', ' '));
    }

    public async Task SaveAsync()
    {
        if (_script == Guid.Empty) return;
        var name = _name.Text.Trim(); if (name.Length == 0) { _info.Text = "Dê um nome ao script."; return; }
        _info.Text = "Salvando…";
        var result = await _api.SaveAsync(_object, _script, name, _code.Text);
        if (!result.Ok) { _info.Text = result.Message; return; }
        _dirty = false; _version++; _heading.Text = "Script: " + name; _info.Text = $"{result.Message} (versão {_version})"; Saved?.Invoke();
    }

    public async Task RefreshConsoleAsync()
    {
        if (_script == Guid.Empty) return;
        var runs = await _api.RunsAsync(_object, _script);
        _console.Text = runs.Count == 0 ? "Console: sem execuções ainda. Toque no objeto, entre perto dele ou espere o temporizador." : string.Join("\n", runs.Select(r => $"{r.At.Replace('T', ' ')[..Math.Min(19, r.At.Length)]}  {r.EventName}  {r.Status}  ({r.Instructions} instruções){(r.Error.Length > 0 ? "  ERRO: " + r.Error : "")}"));
    }

    public void Close() { if (_window is not null) _window.Visible = false; }
}
