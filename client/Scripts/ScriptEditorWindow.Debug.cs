using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// Depuração of a script from the editor: click the gutter to put a breakpoint on a line, press Depurar, and when the script reaches that line on the island it stops: the editor shows the line, what the function holds and what the script keeps,
/// and Continuar, Passo, Por cima and Sair let it go on. The commands travel through the connection to the island the player already has and only the OWNER of the script may use them; the island debugs the SAVED code, so the editor asks to save first.
/// While a script is stopped it hears no events (like a paused script) and the island is never held up. A script left stopped for two minutes is let go.
/// </summary>
public partial class ScriptEditorWindow
{
    private Button _debugButton = null!;
    private VBoxContainer _debugBox = null!;
    private Label _debugLabel = null!;
    private readonly List<Button> _debugControls = [];
    private NetworkManager? _debugNetwork;
    private bool _debugging, _debugStopped; private int _stoppedLine;

    /// <summary>Where the commands go. The game sends them to the island; tests replace it.</summary>
    public Action<string>? DebugSender { get; set; }
    public bool DebuggingForTesting => _debugging;
    public bool StoppedForTesting => _debugStopped;
    public int StoppedLineForTesting => _stoppedLine;
    public string DebugTextForTesting => _debugLabel.Text;
    public IReadOnlyList<int> BreakpointsForTesting => BreakpointLines();
    public void ToggleBreakpointForTesting(int line) { if (line >= 1 && line <= _code.GetLineCount()) _code.SetLineAsBreakpoint(line - 1, !_code.IsLineBreakpointed(line - 1)); }          // the code editor announces the change itself
    public bool DebugControlEnabledForTesting(string text) => _debugControls.FirstOrDefault(b => b.Text == text) is { Disabled: false };
    public void ReceiveDebugForTesting(string json) => OnDebugOperation(json);
    public void TypeForTesting(string text) { _code.InsertTextAtCaret(text); MarkDirty(); }
    public void MarkSavedForTesting() => _dirty = false;

    private void BuildDebug()
    {
        _code.GuttersDrawBreakpointsGutter = true; _code.GuttersDrawExecutingLines = true; _code.BreakpointToggled += line => OnBreakpointToggled((int)line);
        _debugBox = new VBoxContainer { Name = "Depuração", SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill, SizeFlagsVertical = Godot.Control.SizeFlags.ExpandFill }; _debugBox.AddThemeConstantOverride("separation", 4); _tabs.AddChild(_debugBox);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); _debugBox.AddChild(row);
        void AddStep(string text, string tip, string action) { var b = new Button { Text = text, FocusMode = Godot.Control.FocusModeEnum.None, TooltipText = tip, Disabled = true }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary, 13, 4, 10); b.Pressed += () => SendDebug(action); row.AddChild(b); _debugControls.Add(b); }
        AddStep("Continuar", "Deixa o script seguir até o próximo ponto de parada", "continue"); AddStep("Passo", "Vai para a próxima linha, entrando nas funções chamadas", "stepin");
        AddStep("Por cima", "Vai para a próxima linha sem entrar nas funções chamadas", "stepover"); AddStep("Sair", "Segue até a função atual terminar", "stepout");
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill, SizeFlagsVertical = Godot.Control.SizeFlags.ExpandFill }; _debugBox.AddChild(scroll);
        _debugLabel = new Label { Text = DebugHelp, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill }; _debugLabel.AddThemeFontSizeOverride("font_size", 14); scroll.AddChild(_debugLabel);
    }

    private const string DebugHelp = "Clique na margem esquerda do código para marcar uma linha de parada e aperte Depurar (o script precisa estar salvo: a ilha depura o código salvo). Quando o script chegar à linha, ele para aqui, com o que ele guarda. Enquanto está parado ele não recebe eventos; depois de 2 minutos parado ele é solto.";

    /// <summary>The button "Depurar" / "Parar depuração" of the top bar.</summary>
    private void ToggleDebugging()
    {
        if (_script == Guid.Empty) return;
        if (_debugging) { SendDebug("stop"); return; }
        if (_dirty) { _info.Text = "Salve o script antes de depurar: a ilha depura o código salvo."; return; }
        SendDebug("start"); _info.Text = "Pedindo à ilha para depurar o script…";
    }

    private List<int> BreakpointLines() => [.. _code.GetBreakpointedLines().Select(l => l + 1).Order()];

    private void OnBreakpointToggled(int zeroBasedLine) { if (_debugging) SendDebug("breakpoints"); }

    private void SendDebug(string action)
    {
        var json = JsonSerializer.Serialize(new { debug = true, script = _script, action, breakpoints = BreakpointLines() });
        if (DebugSender is not null) { DebugSender(json); return; }
        _debugNetwork ??= GetNodeOrNull<NetworkManager>("/root/NetworkManager"); _debugNetwork?.SendScriptDialogResponse(json);
    }

    private void HookDebugNetwork()
    {
        if (_debugNetwork is not null) return;
        _debugNetwork = GetNodeOrNull<NetworkManager>("/root/NetworkManager"); if (_debugNetwork is null) return;
        _debugNetwork.BuildOperationReceived += OnDebugOperation;
    }

    /// <summary>What the island says about the script being debugged: started, stopped at a line, running, breakpoints, ended.</summary>
    private void OnDebugOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.GetProperty("operation").GetString() != "script_debug" || !Guid.TryParse(root.GetProperty("script").GetString(), out var script) || script != _script) return;
            var kind = root.GetProperty("event").GetString();
            switch (kind)
            {
                case "refused": _debugging = false; _debugButton.Text = "Depurar"; _info.Text = "A ilha já depura outros scripts: termine um deles primeiro."; return;
                case "ended": EndDebug(root.TryGetProperty("reason", out var why) && why.GetString() == "timeout" ? "A depuração terminou: o script ficou parado por mais de 2 minutos e foi solto." : "Depuração encerrada."); return;
            }
            _debugging = root.TryGetProperty("debugging", out var d) ? d.GetBoolean() : _debugging; _debugButton.Text = _debugging ? "Parar depuração" : "Depurar";
            var stopped = root.TryGetProperty("stopped", out var s) && s.GetBoolean(); ClearStoppedLine();
            if (stopped)
            {
                _debugStopped = true; _stoppedLine = root.GetProperty("line").GetInt32(); _code.SetLineAsExecuting(_stoppedLine - 1, true); _code.SetCaretLine(_stoppedLine - 1); _code.CenterViewportToCaret();
                _debugLabel.Text = StoppedText(root); _tabs.CurrentTab = _debugBox.GetIndex();
            }
            else { _debugStopped = false; _debugLabel.Text = kind == "running" ? "O script está rodando (ele para de novo no próximo ponto de parada)." : _debugging ? "Depurando: aguardando o script chegar a uma linha de parada. Toque no objeto ou espere o evento." : DebugHelp; }
            foreach (var b in _debugControls) b.Disabled = !_debugStopped;
            if (kind == "started") _info.Text = "Depurando: o script foi carregado de novo (on_start rodou).";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private static string StoppedText(JsonElement root)
    {
        var text = new StringBuilder($"Parado na linha {root.GetProperty("line").GetInt32()} ({(root.GetProperty("reason").GetString() == "step" ? "passo" : "ponto de parada")}) · evento {root.GetProperty("handler").GetString()}\n\nVariáveis locais:\n");
        void List(string key) { var any = false; foreach (var v in root.GetProperty(key).EnumerateArray()) { any = true; text.Append($"  {v.GetProperty("name").GetString()} = {v.GetProperty("value").GetString()}  ({v.GetProperty("type").GetString()})\n"); } if (!any) text.Append("  (nenhuma)\n"); }
        List("locals"); text.Append("\nO que o script guarda (variáveis globais dele):\n"); List("globals");
        return text.ToString();
    }

    private void ClearStoppedLine() { if (_stoppedLine > 0 && _stoppedLine <= _code.GetLineCount()) _code.SetLineAsExecuting(_stoppedLine - 1, false); _stoppedLine = 0; }

    private void EndDebug(string message)
    {
        _debugging = false; _debugStopped = false; ClearStoppedLine(); _debugButton.Text = "Depurar"; foreach (var b in _debugControls) b.Disabled = true; _debugLabel.Text = DebugHelp; _info.Text = message;
    }
}

public partial class ScriptEditorWindow
{
    public override void _ExitTree() { if (_debugNetwork is not null) _debugNetwork.BuildOperationReceived -= OnDebugOperation; }
}
