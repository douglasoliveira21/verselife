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
        var join = new Button { Text = "Entrar", TooltipText = "Entrar na ilha selecionada" }; var leave = new Button { Text = "Sair", TooltipText = "Desconectar da ilha" }; buttons.AddChild(join); buttons.AddChild(leave);
        var build = ActionButton("Construir (B)", "Abrir ferramentas de construção", () => GetTree().CurrentScene?.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true)); buttons.AddChild(build); var inventory = ActionButton("Inventário", "Abrir inventário", () => GetTree().CurrentScene?.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle()); buttons.AddChild(inventory); var social = ActionButton("Chat / Amigos / Grupos", "Abrir comunicação social", () => GetTree().CurrentScene?.GetNodeOrNull<SocialPanel>("SocialPanel")?.Toggle()); buttons.AddChild(social); var map = ActionButton("Mapa / Busca", "Explorar ilhas e destinos", () => GetTree().CurrentScene?.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle()); buttons.AddChild(map); var market = ActionButton("Marketplace", "Abrir carteira e marketplace", () => GetTree().CurrentScene?.GetNodeOrNull<MarketplacePanel>("MarketplacePanel")?.Toggle()); buttons.AddChild(market); var settings = ActionButton("Configurações", "Vídeo, áudio, acessibilidade e controles", () => GetTree().CurrentScene?.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle()); buttons.AddChild(settings);
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
    private static Button ActionButton(string text, string tooltip, Action action) { var button = new Button { Text = text, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.All }; button.Pressed += action; return button; }
}
