using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The dialogs and permission requests that scripts of objects show to the player ("script_dialog" announced by the island server): a window with who is asking, the message and the buttons
/// (and a text box when the script asks for one). What the player picks goes back to the island, which checks it was really asked of this player before telling the script.
/// Closing the window answers "Negar" to a permission request, "Recusar" to a payment request and nothing to a dialog. Only one is shown at a time; the others wait their turn.
/// </summary>
public partial class ScriptDialogWindow : Node
{
    public sealed record Pending(Guid Id, string Kind, string Title, string Message, IReadOnlyList<string> Buttons, bool Input, double Timeout, string ObjectName);

    private FloatingWindow _window = null!;
    private Label _from = null!, _message = null!, _countdown = null!;
    private VBoxContainer _buttons = null!;
    private LineEdit _input = null!;
    private readonly Queue<Pending> _queue = new();
    private Pending? _current;
    private double _left;
    private NetworkManager? _network;

    /// <summary>Sends the answer (JSON) to the island. Replaced in tests.</summary>
    public Action<string>? Send { get; set; }
    private void Transmit(string json) { if (Send is not null) Send(json); else _network?.SendScriptDialogResponse(json); }
    public bool IsOpen => _window is not null && _window.Visible;
    public Pending? Current => _current;
    public int Waiting => _queue.Count;
    public string TextForTesting => string.Join("|", _window.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public IReadOnlyList<string> ButtonsForTesting => [.. _buttons.GetChildren().OfType<Button>().Where(b => !b.IsQueuedForDeletion()).Select(b => b.Text)];
    public LineEdit InputForTesting => _input;
    public void PressForTesting(string text) { if (_buttons.GetChildren().OfType<Button>().FirstOrDefault(b => !b.IsQueuedForDeletion() && b.Text == text) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public void CloseForTesting() => Dismiss();

    public override void _Ready()
    {
        BuildWindow();
        _network = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
        if (_network is not null) _network.BuildOperationReceived += OnOperation;
    }

    public override void _ExitTree() { if (_network is not null && IsInstanceValid(_network)) _network.BuildOperationReceived -= OnOperation; }

    private void BuildWindow()
    {
        _window = new FloatingWindow("Objeto", icon: "info") { Name = "ScriptDialog", Visible = false }; _window.Place(.5f, .5f, -200, -150, 200, 150); AddChild(_window);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 10);
        _from = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) }; _from.AddThemeColorOverride("font_color", UiStyle.AccentText); _from.AddThemeFontOverride("font", UiStyle.Medium); content.AddChild(_from);
        _message = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(340, 0) }; content.AddChild(_message);
        _input = new LineEdit { PlaceholderText = "Digite aqui…", MaxLength = 200, Visible = false }; _input.TextSubmitted += _ => Answer(_current?.Buttons.FirstOrDefault() ?? "OK"); content.AddChild(_input);
        _buttons = new VBoxContainer(); _buttons.AddThemeConstantOverride("separation", 6); content.AddChild(_buttons);
        _countdown = new Label { HorizontalAlignment = HorizontalAlignment.Right }; _countdown.AddThemeColorOverride("font_color", UiStyle.TextMuted); _countdown.AddThemeFontSizeOverride("font_size", 12); content.AddChild(_countdown);
        _window.CloseRequested += Dismiss;
    }

    public void OnOperation(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.GetProperty("operation").GetString() != "script_dialog") return;
            var buttons = root.TryGetProperty("buttons", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(b => b.GetString() ?? "").Where(b => b.Length > 0).Take(12).ToList() : [];
            string Text(string name, int max) { var text = root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; return text.Length > max ? text[..max] : text; }
            Enqueue(new Pending(Guid.Parse(root.GetProperty("id").GetString()!), Text("kind", 16), Text("title", 60), Text("message", 400), buttons, root.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.True, root.TryGetProperty("timeout", out var timeout) && timeout.TryGetDouble(out var seconds) ? Math.Clamp(seconds, 5, 300) : 60, Text("objectName", 60)));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { }
    }

    public void Enqueue(Pending pending) { if (_queue.Count < 10) _queue.Enqueue(pending); if (_current is null) Next(); }

    private void Next()
    {
        if (!_queue.TryDequeue(out var pending)) { _current = null; _window.Visible = false; return; }
        _current = pending; _left = pending.Timeout;
        _from.Text = pending.ObjectName.Length > 0 ? $"{pending.ObjectName} pergunta:" : "Um objeto pergunta:"; _message.Text = pending.Message.Length > 0 ? pending.Message : pending.Title;
        _input.Visible = pending.Input; _input.Text = "";
        foreach (var child in _buttons.GetChildren()) { _buttons.RemoveChild(child); child.QueueFree(); }
        foreach (var label in pending.Buttons)
        {
            var text = label; var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
            UiStyle.StyleButton(button, (pending.Kind == "permission" && text == "Permitir" || pending.Kind == "payment" && text == "Pagar") ? UiStyle.ButtonKind.Primary : UiStyle.ButtonKind.Secondary, 12, 8); button.Pressed += () => Answer(text); _buttons.AddChild(button);
        }
        _window.Visible = true; _window.MoveToFront();
    }

    /// <summary>The player picked a button (or sent the text).</summary>
    public void Answer(string response)
    {
        if (_current is not { } pending) return;
        Transmit(JsonSerializer.Serialize(new { id = pending.Id, response, input = pending.Input ? _input.Text : null }));
        _current = null; Next();
    }

    private void Dismiss() { if (_current is { Kind: "permission" or "payment" } asked) Transmit(JsonSerializer.Serialize(new { id = asked.Id, response = asked.Kind == "payment" ? "Recusar" : "Negar" })); _current = null; Next(); }

    public override void _Process(double delta)
    {
        if (_current is null) return;
        _left -= delta; _countdown.Text = $"{Math.Max(0, (int)Math.Ceiling(_left))} s";
        if (_left <= 0) Dismiss();
    }
}
