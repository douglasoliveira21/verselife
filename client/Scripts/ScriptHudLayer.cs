using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The panels that scripts of objects show on this player's screen after the player allowed it ("script_hud" from the island server): a title, a close button and what the script put in it (text, buttons, progress bars, text fields).
/// Clicks, texts and closing go back to the island, which checks them against the HUD the script really shows. The player can always close a panel with the X. At most eight panels are shown at once, and nothing a script sends can draw
/// outside its own panel or read what the player types anywhere else.
/// </summary>
public partial class ScriptHudLayer : Node
{
    public const int MaxPanels = 8;

    private sealed record Element(string Id, string Type, string Text, string Color, double Value, double Max, string Placeholder);

    private sealed class Panel { public Guid Script; public PanelContainer Box = null!; public VBoxContainer Body = null!; public string Object = ""; public Dictionary<string, string> Typed = []; public string Signature = ""; }

    private readonly Dictionary<Guid, Panel> _panels = [];
    private NetworkManager? _network;

    /// <summary>Sends an action (JSON) to the island. Replaced in tests.</summary>
    public Action<string>? Send { get; set; }
    public int PanelCount => _panels.Count;
    public bool Has(Guid script) => _panels.ContainsKey(script);
    public string TextOf(Guid script) => _panels.TryGetValue(script, out var panel) ? string.Join("|", panel.Box.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)) : "";
    public IReadOnlyList<string> ButtonsOf(Guid script) => _panels.TryGetValue(script, out var panel) ? [.. panel.Body.FindChildren("*", "Button", true, false).OfType<Button>().Select(b => b.Text)] : [];
    public void PressForTesting(Guid script, string text) { if (_panels.TryGetValue(script, out var panel) && panel.Body.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text) is { } button) button.EmitSignal(BaseButton.SignalName.Pressed); }
    public void CloseForTesting(Guid script) { if (_panels.TryGetValue(script, out var panel) && panel.Box.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Name == "Close") is { } button) button.EmitSignal(BaseButton.SignalName.Pressed); }
    public LineEdit? InputOf(Guid script) => _panels.TryGetValue(script, out var panel) ? panel.Body.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().FirstOrDefault() : null;
    public ProgressBar? BarOf(Guid script) => _panels.TryGetValue(script, out var panel) ? panel.Body.FindChildren("*", "ProgressBar", true, false).OfType<ProgressBar>().FirstOrDefault() : null;
    public PanelContainer? BoxOf(Guid script) => _panels.GetValueOrDefault(script)?.Box;

    public override void _Ready()
    {
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        if (_network is null) return;
        _network.BuildOperationReceived += OnOperation; _network.ConnectionStateChanged += OnState;
    }

    public override void _ExitTree()
    {
        if (_network is null || !IsInstanceValid(_network)) return;
        _network.BuildOperationReceived -= OnOperation; _network.ConnectionStateChanged -= OnState;
    }

    private void OnState(string state, string detail) { if (state is "switching" or "offline" or "failed") Clear(); }

    public void Clear() { foreach (var panel in _panels.Values) if (IsInstanceValid(panel.Box)) panel.Box.QueueFree(); _panels.Clear(); }

    private void Transmit(string json) { if (Send is not null) Send(json); else _network?.SendScriptDialogResponse(json); }

    public void OnOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.GetProperty("operation").GetString() != "script_hud") return;
            var script = Guid.Parse(root.GetProperty("script").GetString()!);
            if (!root.GetProperty("visible").GetBoolean()) { Remove(script); return; }
            string Text(JsonElement e, string name, int max) { var text = e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; return text.Length > max ? text[..max] : text; }
            double Number(JsonElement e, string name, double fallback) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
            var elements = root.GetProperty("elements").EnumerateArray().Take(12).Select(e => new Element(Text(e, "id", 32), Text(e, "type", 12), Text(e, "text", 120), Text(e, "color", 9), Number(e, "value", 0), Math.Max(1, Number(e, "max", 100)), Text(e, "placeholder", 120))).ToList();
            Show(script, Text(root, "objectName", 60), Text(root, "title", 120), (float)Math.Clamp(Number(root, "x", .8), .05, .95), (float)Math.Clamp(Number(root, "y", .25), .05, .95), (int)Math.Clamp(Number(root, "width", 260), 160, 420), elements);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { }
    }

    private void Remove(Guid script) { if (_panels.Remove(script, out var panel) && IsInstanceValid(panel.Box)) panel.Box.QueueFree(); }

    private void Show(Guid script, string objectName, string title, float x, float y, int width, List<Element> elements)
    {
        if (!_panels.TryGetValue(script, out var panel))
        {
            if (_panels.Count >= MaxPanels) return;
            panel = new Panel { Script = script }; _panels[script] = panel;
            panel.Box = new PanelContainer { Name = $"ScriptHud_{script:N}", MouseFilter = Control.MouseFilterEnum.Stop }; panel.Box.AddThemeStyleboxOverride("panel", UiStyle.Glass(.9f, 14, 12)); AddChild(panel.Box);
            panel.Body = new VBoxContainer(); panel.Body.AddThemeConstantOverride("separation", 6); panel.Box.AddChild(panel.Body);
        }
        panel.Object = objectName; var captured = panel;
        // remember what is typed so an update from the script does not wipe it
        foreach (var edit in panel.Body.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>()) if (edit.HasMeta("element")) panel.Typed[edit.GetMeta("element").AsString()] = edit.Text;
        var focused = panel.Body.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().FirstOrDefault(e => e.HasFocus())?.GetMeta("element", "").AsString() ?? "";
        foreach (var child in panel.Body.GetChildren()) { panel.Body.RemoveChild(child); child.QueueFree(); }
        panel.Box.AnchorLeft = panel.Box.AnchorRight = x; panel.Box.AnchorTop = panel.Box.AnchorBottom = y; panel.Box.GrowHorizontal = Control.GrowDirection.Both; panel.Box.GrowVertical = Control.GrowDirection.Both; panel.Box.CustomMinimumSize = new Vector2(width, 0); panel.Box.OffsetLeft = panel.Box.OffsetRight = panel.Box.OffsetTop = panel.Box.OffsetBottom = 0;
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 6); panel.Body.AddChild(header);
        var heading = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; heading.AddThemeConstantOverride("separation", 0); header.AddChild(heading);
        var name = new Label { Text = title.Length > 0 ? title : objectName, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; name.AddThemeFontOverride("font", UiStyle.Bold); name.AddThemeFontSizeOverride("font_size", 15); name.AddThemeColorOverride("font_color", UiStyle.Accent); heading.AddChild(name);
        if (objectName.Length > 0 && title.Length > 0) { var from = new Label { Text = objectName }; from.AddThemeFontSizeOverride("font_size", 11); from.AddThemeColorOverride("font_color", UiStyle.TextMuted); heading.AddChild(from); }
        var close = new Button { Name = "Close", Text = "×", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Fechar este painel", CustomMinimumSize = new Vector2(28, 28) }; close.AddThemeFontSizeOverride("font_size", 18); close.Pressed += () => { Transmit(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "close" })); Remove(captured.Script); }; header.AddChild(close);
        foreach (var element in elements)
        {
            var id = element.Id;
            switch (element.Type)
            {
                case "label":
                    var label = new Label { Text = element.Text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width - 28, 0) }; if (element.Color.Length is 7 or 9 && element.Color[0] == '#') label.AddThemeColorOverride("font_color", new Color(element.Color)); panel.Body.AddChild(label); break;
                case "button":
                    var button = new Button { Text = element.Text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 12, 6); button.Pressed += () => Transmit(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "click", element = id })); panel.Body.AddChild(button); break;
                case "progress":
                    var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 2); panel.Body.AddChild(column);
                    if (element.Text.Length > 0) { var caption = new Label { Text = element.Text }; caption.AddThemeFontSizeOverride("font_size", 12); caption.AddThemeColorOverride("font_color", UiStyle.TextMuted); column.AddChild(caption); }
                    column.AddChild(new ProgressBar { MinValue = 0, MaxValue = element.Max, Value = Math.Clamp(element.Value, 0, element.Max), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 14) }); break;
                case "input":
                    var edit = new LineEdit { PlaceholderText = element.Placeholder, MaxLength = 200, CustomMinimumSize = new Vector2(0, 34), Text = panel.Typed.GetValueOrDefault(id, "") }; edit.SetMeta("element", id);
                    edit.TextSubmitted += text => { Transmit(JsonSerializer.Serialize(new { hud = true, script = captured.Script, action = "input", element = id, text })); edit.Text = ""; captured.Typed.Remove(id); }; panel.Body.AddChild(edit); if (focused == id) edit.GrabFocus(); break;
            }
        }
    }
}
