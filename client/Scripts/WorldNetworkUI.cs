using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

public partial class WorldNetworkUI : Node3D
{
    private NetworkManager _network = null!;
    private LineEdit _island = null!;
    private LineEdit _chat = null!;
    private Label _status = null!;
    private RichTextLabel _history = null!;

    public override void _Ready()
    {
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        var panel = new PanelContainer { Name = "MultiplayerPanel" };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight); panel.OffsetLeft = -430; panel.OffsetTop = 18; panel.OffsetRight = -18; panel.OffsetBottom = 300;
        var box = new VBoxContainer(); panel.AddChild(box);
        box.AddChild(new Label { Text = "LifeVerse Multiplayer · ENet v1" });
        _island = new LineEdit { PlaceholderText = "UUID da ilha", Text = OS.GetEnvironment("LIFEVERSE_ISLAND_ID") };
        box.AddChild(_island);
        var buttons = new HBoxContainer(); box.AddChild(buttons);
        var join = new Button { Text = "Entrar" }; var leave = new Button { Text = "Sair" }; buttons.AddChild(join); buttons.AddChild(leave);
        var build = new Button { Text = "Construir (B)" }; buttons.AddChild(build); build.Pressed += () => GetTree().CurrentScene?.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true);
        _status = new Label { Text = "Offline" }; box.AddChild(_status);
        _history = new RichTextLabel { CustomMinimumSize = new Vector2(380, 105), BbcodeEnabled = false }; box.AddChild(_history);
        _chat = new LineEdit { PlaceholderText = "Chat local (Enter para enviar)", MaxLength = 256 }; box.AddChild(_chat);
        AddChild(panel);
        join.Pressed += Join; leave.Pressed += _network.Disconnect; _chat.TextSubmitted += SendChat;
        _network.ConnectionStateChanged += OnConnectionStateChanged; _network.ChatReceived += OnChatReceived;
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.ConnectionStateChanged -= OnConnectionStateChanged; _network.ChatReceived -= OnChatReceived;
    }

    private void Join()
    {
        if (!Guid.TryParse(_island.Text.Trim(), out var islandId)) { _status.Text = "UUID de ilha inválido."; return; }
        var error = _network.JoinIsland(islandId); if (error != Error.Ok) _status.Text = $"Não foi possível entrar: {error}";
    }
    private void SendChat(string text) { _network.SendChatMessage(text); _chat.Clear(); }
    private void OnConnectionStateChanged(string state, string detail) => _status.Text = $"{state}: {detail}";
    private void OnChatReceived(string line) { _history.AppendText(line + "\n"); _history.ScrollToLine(Math.Max(0, _history.GetLineCount() - 1)); }
}
