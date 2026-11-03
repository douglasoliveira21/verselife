using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// Desktop-viewer style interface: menu bar with status, bottom toolbar with the local chat bar,
/// chat history, mini-map, movement controls, snapshot and floating windows for every feature.
public partial class ViewerUI : CanvasLayer
{
    private NetworkManager _network = null!;
    private Node _world = null!;
    private PlayerController? _player;
    private IslandTerrain? _terrain;
    private IslandEnvironmentController? _environment;
    private Control _root = null!;
    private LineEdit _chat = null!;
    private RichTextLabel _chatLog = null!;
    private FloatingWindow _chatWindow = null!;
    private FloatingWindow _miniMapWindow = null!;
    private FloatingWindow _moveWindow = null!;
    private FloatingWindow _joinWindow = null!;
    private PeoplePanel? _people;
    private FloatingWindow? _helpWindow;
    private Control? _gestures;
    private Label _coords = null!;
    private Label _clock = null!;
    private Label _fps = null!;
    private Label _connection = null!;
    private Label _balance = null!;
    private ColorRect _indicator = null!;
    private Button _walk = null!;
    private Button _run = null!;
    private Button _fly = null!;
    private Button _miniMapButton = null!;
    private Button _moveButton = null!;
    private Button _gestureButton = null!;
    private LineEdit _islandId = null!;
    private Label _joinStatus = null!;
    private VBoxContainer _toasts = null!;
    private HttpRequest _wallet = null!;
    private HttpRequest _islands = null!;
    private ItemList _islandList = null!;
    private readonly List<Guid> _islandIds = [];
    private double _statusTimer;
    private double _walletTimer = 55;
    private bool _snapshotBusy;
    private string _state = "offline";
    private Theme _theme = null!;
    private const string PrefsPath = "user://viewer.cfg";
    private static readonly float[] UiScales = [.8f, .9f, 1f, 1.1f, 1.25f, 1.5f];
    private static readonly float[] Volumes = [0f, .25f, .5f, .75f, 1f];
    private static readonly string[] WeatherNames = ["clear", "cloudy", "rain", "storm", "fog"];
    private readonly ConfigFile _prefs = new();
    private FloatingWindow? _tutorial;
    private Label _tutorialText = null!;
    private Label _tutorialPage = null!;
    private CheckBox _tutorialSkip = null!;
    private int _page;

    public override void _Ready()
    {
        Layer = 10;
        _network = GetNode<NetworkManager>("/root/NetworkManager");
        _world = GetParent();
        _player = _world.GetNodeOrNull<PlayerController>("Player");
        _terrain = _world.GetNodeOrNull<IslandTerrain>("IslandTerrain");
        _environment = _world.GetNodeOrNull<IslandEnvironmentController>("EnvironmentController");
        _prefs.Load(PrefsPath);
        var theme = _theme = ViewerTheme.Create();
        GetWindow().Theme = theme;
        _root = new Control { Name = "Viewer", Theme = theme, MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        TidyLegacyHud();
        BuildChatWindow();
        BuildMiniMap();
        BuildMoveWindow();
        BuildJoinWindow();
        BuildToasts();
        var notifications = new NotificationClient { Name = "Notifications" }; AddChild(notifications); notifications.Notified += text => Toast(text);
        AnnounceUpdate();
        _people = new PeoplePanel { Name = "People" }; AddChild(_people); _people.AttachTo(_root); _people.Message += Toast;
        BuildTopBar();
        BuildBottomBar();
        _wallet = new HttpRequest(); AddChild(_wallet); _wallet.RequestCompleted += OnWallet;
        _islands = new HttpRequest(); AddChild(_islands); _islands.RequestCompleted += OnIslands;
        RefreshIslands();
        _network.ChatReceived += OnChat; _network.ConnectionStateChanged += OnConnectionState; _network.TerrainEditRejected += OnTerrainRejected; _network.VehicleMessage += Toast;
        AppendSystem("Bem-vindo ao LifeVerse. Pressione Enter para conversar.");
        SetUiScale((float)(double)_prefs.GetValue("ui", "scale", 1.0), save: false);
        SetVolume((float)(double)_prefs.GetValue("audio", "volume", 1.0), save: false);
        UpdateStatus();
        foreach (var name in new[] { "InventoryPanel", "SocialPanel", "DiscoveryPanel", "MarketplacePanel", "SettingsPanel", "LuaScriptEditor", "HudEditor", "BuildEditor", "TerrainEditor" })
            foreach (var child in (_world.GetNodeOrNull(name)?.GetChildren() ?? []).OfType<PanelContainer>()) { child.Theme = theme; FloatingWindow.MakeDraggable(child); }
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.ChatReceived -= OnChat; _network.ConnectionStateChanged -= OnConnectionState; _network.TerrainEditRejected -= OnTerrainRejected; _network.VehicleMessage -= Toast;
    }

    public override void _Process(double delta)
    {
        _statusTimer -= delta; _walletTimer += delta;
        if (_statusTimer <= 0) { _statusTimer = .25; UpdateStatus(); }
        if (_walletTimer >= 60 && _network.IsAuthenticated && _wallet.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) { _walletTimer = 0; RequestWallet(); }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Enter or Key.KpEnter } && GetViewport().GuiGetFocusOwner() is null)
        {
            _chat.GrabFocus();
            GetViewport().SetInputAsHandled();
        }
    }

    // ---- layout ----------------------------------------------------------------------------------------------

    /// The old always-on HUD widgets are replaced by menus, the toolbar and windows; the controller keeps updating them.
    private void TidyLegacyHud()
    {
        var hud = _player?.GetNodeOrNull<CanvasLayer>("HUD");
        if (hud is null) return;
        if (hud.GetNodeOrNull<Control>("Status") is { } status) status.Visible = false;
        if (hud.GetNodeOrNull<Control>("EditAvatar") is { } edit) edit.Visible = false;
        if (hud.GetNodeOrNull<Control>("Crosshair") is { } crosshair) crosshair.Modulate = new Color(1, 1, 1, .45f);
        _gestures = hud.GetNodeOrNull<Control>("GesturePanel");
        if (_gestures is not null) { _gestures.Visible = false; _gestures.Theme = _theme; FloatingWindow.MakeDraggable(_gestures); }
        if (hud.GetNodeOrNull<Control>("ContextMenu") is { } contextMenu) contextMenu.Theme = _theme;
        _world.GetNodeOrNull<Control>("Hint")?.Hide();
    }

    private void BuildTopBar()
    {
        var bar = new PanelContainer { Name = "MenuBarPanel" };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.BarBox());
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _root.AddChild(bar);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); bar.AddChild(row);
        var menu = new MenuBar { Name = "Menus" }; row.AddChild(menu);

        var me = AddMenu(menu, "Eu",
            ("Editar aparência…", () => GetNode<UIManager>("/root/UIManager").ShowCharacterCreator(), false),
            ("Inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(), false),
            ("Preferências…", () => _world.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle(), false),
            ("-", null, false),
            ("Sair da ilha", _network.Disconnect, false),
            ("Sair do LifeVerse", () => GetTree().Quit(), false));
        AddChoiceSubmenu(me, "Escala da interface", ["80%", "90%", "100%", "110%", "125%", "150%"], index => SetUiScale(UiScales[index]));
        AddChoiceSubmenu(me, "Volume", ["Mudo", "25%", "50%", "75%", "100%"], index => SetVolume(Volumes[index]));
        AddMenu(menu, "Comunicar",
            ("Conversa local   Enter", () => _chat.GrabFocus(), false),
            ("Pessoas, amigos e grupos…", () => _people?.Toggle(), false),
            ("Mensagens privadas…", () => _world.GetNodeOrNull<SocialPanel>("SocialPanel")?.Toggle(), false),
            ("Gestos", ToggleGestures, false),
            ("-", null, false),
            ("Voz: segure o botão Falar (use fones)", null, true),
            ("Voz: microfone aberto (cadeado)  liga/desliga", ToggleOpenMic, false),
            ("Voz: silenciar tudo  liga/desliga", ToggleVoice, false));
        var world = AddMenu(menu, "Mundo",
            ("Mapa e destinos…", () => _world.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle(), false),
            ("Mini-mapa", () => ToggleWindow(_miniMapWindow), false),
            ("Ir para uma ilha…", () => { _joinWindow.Visible = true; _joinWindow.MoveToFront(); }, false),
            ("-", null, false),
            ("Editar terreno   T", () => _world.GetNodeOrNull<TerrainEditor>("TerrainEditor")?.SetActive(true), false),
            ("Tirar foto", TakeSnapshot, false));
        var environment = new PopupMenu { Name = "Ambiente" };
        world.AddChild(environment);
        foreach (var label in new[] { "Nascer do sol", "Meio-dia", "Pôr do sol", "Meia-noite" }) environment.AddItem(label);
        environment.AddItem("Ciclo automático");
        environment.IdPressed += id => SetEnvironment((int)id);
        world.AddSubmenuNodeItem("Ambiente (somente neste computador)", environment);
        AddChoiceSubmenu(world, "Clima (somente neste computador)", ["Limpo", "Nublado", "Chuva", "Tempestade", "Neblina"], index => SetWeather(WeatherNames[index]));
        AddMenu(menu, "Conteúdo",
            ("Construir   B", () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true), false),
            ("Inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(), false),
            ("Marketplace e carteira…", () => _world.GetNodeOrNull<MarketplacePanel>("MarketplacePanel")?.Toggle(), false),
            ("-", null, false),
            ("Editor de scripts Lua…", () => _world.GetNodeOrNull<LuaScriptEditor>("LuaScriptEditor")?.Toggle(), false),
            ("Editor de HUD…", () => _world.GetNodeOrNull<HudEditor>("HudEditor")?.Toggle(), false));
        AddMenu(menu, "Ajuda",
            ("Tutorial: primeiros passos", ShowTutorial, false),
            ("Controles", ShowControlsHelp, false),
            ("Baixar atualização", () => GetNode<AppManager>("/root/AppManager").Updates.OpenDownload(), false),
            ("Sobre o LifeVerse", () => Toast($"LifeVerse · cliente {AppManager.Version}"), false));
        AddMenu(menu, "Desenvolver",
            ("Métricas de desempenho", () => _world.GetNodeOrNull<PerformanceOverlay>("PerformanceOverlay")?.Toggle(), false));

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _balance = StatusLabel("LV$ —", "Saldo de LV Coins");
        _coords = StatusLabel("(0, 0, 0)", "Posição na ilha (leste, norte, altura)");
        _clock = StatusLabel("12:00", "Hora do mundo");
        _fps = StatusLabel("0 FPS", "Quadros por segundo");
        _indicator = new ColorRect { CustomMinimumSize = new Vector2(10, 10), Color = ViewerTheme.Bad, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Estado da conexão" };
        row.AddChild(_indicator);
        _connection = StatusLabel("Offline", "Conexão com a ilha");
        row.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
    }

    private Label StatusLabel(string text, string tooltip)
    {
        var label = new Label { Text = text, TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Stop };
        label.AddThemeColorOverride("font_color", new Color("#cfcfcf"));
        ((HBoxContainer)GetNode("Viewer/MenuBarPanel").GetChild(0)).AddChild(label);
        return label;
    }

    private static PopupMenu AddMenu(MenuBar bar, string title, params (string Label, Action? Action, bool Disabled)[] items)
    {
        var popup = new PopupMenu { Name = title };
        bar.AddChild(popup);
        var actions = new Dictionary<int, Action>();
        var id = 0;
        foreach (var (label, action, disabled) in items)
        {
            if (label == "-") { popup.AddSeparator(); continue; }
            popup.AddItem(label, id);
            if (disabled) popup.SetItemDisabled(popup.GetItemIndex(id), true);
            if (action is not null) actions[id] = action;
            id++;
        }
        popup.IdPressed += pressed => { if (actions.TryGetValue((int)pressed, out var run)) run(); };
        return popup;
    }

    private void BuildBottomBar()
    {
        var bar = new PanelContainer { Name = "ToolbarPanel" };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.BarBox());
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        bar.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(bar);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 4); bar.AddChild(row);
        _chat = new LineEdit { PlaceholderText = "Clique aqui para conversar.", CustomMinimumSize = new Vector2(360, 36), MaxLength = 256, TooltipText = "Conversa local (alcance de 30 m). Enter para focar, Esc para sair." };
        _chat.TextSubmitted += OnChatSubmit;
        row.AddChild(_chat);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        ToolButton(row, "Conversar", "Focar a barra de conversa (Enter)", () => _chat.GrabFocus());
        var voiceBar = new VoiceBar { Name = "VoiceBar" }; row.AddChild(voiceBar); voiceBar.Bind(_network.Voice);
        _gestureButton = ToolButton(row, "Gestos", "Acenar, dançar e outras animações", ToggleGestures, toggle: true);
        _moveButton = ToolButton(row, "Mover", "Andar, correr e voar", () => ToggleWindow(_moveWindow), toggle: true);
        var view = new MenuButton { Text = "Ver", CustomMinimumSize = new Vector2(66, 36), TooltipText = "Modos de câmera", FocusMode = Control.FocusModeEnum.None, Flat = false };
        var viewPopup = view.GetPopup();
        viewPopup.AddItem("Terceira pessoa (padrão)", 0); viewPopup.AddItem("Primeira pessoa   C", 1); viewPopup.AddItem("Câmera livre   V", 2); viewPopup.AddSeparator(); viewPopup.AddItem("Redefinir câmera   R", 3);
        viewPopup.IdPressed += id => { switch (id) { case 0: _player?.SetCameraMode("ThirdPerson"); break; case 1: _player?.SetCameraMode("FirstPerson"); break; case 2: _player?.SetCameraMode("FreeLook"); break; default: _player?.ResetCamera(); break; } };
        row.AddChild(view);
        ToolButton(row, "Foto", "Capturar a tela sem a interface", TakeSnapshot);
        ToolButton(row, "Construir", "Ferramentas de construção (B)", () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true));
        ToolButton(row, "Pessoas", "Buscar pessoas, amigos e grupos", () => _people?.Toggle());
        ToolButton(row, "Mapa", "Mapa, busca e destinos", () => _world.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle());
        ToolButton(row, "Inventário", "Seu inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle());
        _miniMapButton = ToolButton(row, "Mini-mapa", "Mostrar ou ocultar o mini-mapa", () => ToggleWindow(_miniMapWindow), toggle: true);
        _miniMapButton.ButtonPressed = true;
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(360, 0) });
    }

    private static Button ToolButton(HBoxContainer row, string text, string tooltip, Action? action, bool toggle = false, bool disabled = false)
    {
        var button = new Button { Text = text, TooltipText = tooltip, ToggleMode = toggle, Disabled = disabled, CustomMinimumSize = new Vector2(78, 36), FocusMode = Control.FocusModeEnum.None };
        if (action is not null) button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private void BuildChatWindow()
    {
        _chatWindow = new FloatingWindow("Conversa local", closable: false);
        _chatWindow.SetOpacity(.72f);
        _chatWindow.Place(0, 1, 10, -258, 400, -52);
        _chatLog = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, CustomMinimumSize = new Vector2(372, 150), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _chatWindow.Content.AddChild(_chatLog);
        _root.AddChild(_chatWindow);
    }

    private void BuildMiniMap()
    {
        _miniMapWindow = new FloatingWindow("Mini-mapa");
        _miniMapWindow.Place(1, 0, -218, 38, -10, 270);
        var map = new MiniMap();
        _miniMapWindow.Content.AddChild(map);
        if (_terrain is not null) map.Bind(_terrain, _player, _network);
        _miniMapWindow.CloseRequested += () => _miniMapButton.ButtonPressed = false;
        _root.AddChild(_miniMapWindow);
    }

    private void BuildMoveWindow()
    {
        _moveWindow = new FloatingWindow("Mover") { Visible = false };
        _moveWindow.Place(.5f, 1, -130, -250, 130, -60);
        _moveWindow.CloseRequested += () => _moveButton.ButtonPressed = false;
        var modes = new HBoxContainer(); modes.AddThemeConstantOverride("separation", 4);
        _walk = ModeButton(modes, "Andar", () => { if (_player is null) return; _player.AlwaysRun = false; if (_player.IsFlying) _player.RequestFlightToggle(); });
        _run = ModeButton(modes, "Correr", () => { if (_player is null) return; _player.AlwaysRun = true; if (_player.IsFlying) _player.RequestFlightToggle(); });
        _fly = ModeButton(modes, "Voar", () => _player?.RequestFlightToggle());
        _moveWindow.Content.AddChild(modes);
        _moveWindow.Content.AddChild(new Label { Text = "W/S andar · A/D girar · Q/Z lado\nShift inverte andar/correr · Espaço pula ou sobe\nCtrl desce · F voar · duplo clique anda até o ponto", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0), ThemeTypeVariation = "" });
        _root.AddChild(_moveWindow);
    }

    private static Button ModeButton(HBoxContainer row, string text, Action action)
    {
        var button = new Button { Text = text, ToggleMode = true, CustomMinimumSize = new Vector2(76, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private void BuildJoinWindow()
    {
        _joinWindow = new FloatingWindow("Entrar em uma ilha");
        _joinWindow.Place(.5f, .5f, -230, -170, 230, 170);
        _islandId = new LineEdit { PlaceholderText = "ID (UUID) da ilha", Text = OS.GetEnvironment("LIFEVERSE_ISLAND_ID") };
        _joinWindow.Content.AddChild(new Label { Text = "Escolha uma ilha (duplo clique entra) ou cole o ID." });
        _islandList = new ItemList { CustomMinimumSize = new Vector2(440, 120), TooltipText = "Ilhas públicas. Uma ilha adormecida inicia quando você entra (leva até um minuto)." };
        _islandList.ItemSelected += index => { if (index >= 0 && index < _islandIds.Count) _islandId.Text = _islandIds[(int)index].ToString(); };
        _islandList.ItemActivated += index => { if (index >= 0 && index < _islandIds.Count) { _islandId.Text = _islandIds[(int)index].ToString(); Join(); } };
        _joinWindow.Content.AddChild(_islandList);
        var refresh = new Button { Text = "Atualizar lista" }; refresh.Pressed += RefreshIslands; _joinWindow.Content.AddChild(refresh);
        _joinWindow.Content.AddChild(_islandId);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 6);
        var join = new Button { Text = "Entrar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; join.Pressed += Join;
        var leave = new Button { Text = "Sair", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; leave.Pressed += _network.Disconnect;
        buttons.AddChild(join); buttons.AddChild(leave);
        _joinWindow.Content.AddChild(buttons);
        _joinStatus = new Label { Text = "Offline", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        _joinWindow.Content.AddChild(_joinStatus);
        _islandId.TextSubmitted += _ => Join();
        _root.AddChild(_joinWindow);
    }


    private void ToggleOpenMic()
    {
        _network.Voice.OpenMic = !_network.Voice.OpenMic;
        Toast(_network.Voice.OpenMic ? "Cadeado fechado: o microfone fica aberto e sua voz é enviada quando você fala. Use fones para evitar eco." : "Cadeado aberto: segure o botão Falar para falar.");
    }

    private void ToggleVoice()
    {
        _network.Voice.Enabled = !_network.Voice.Enabled;
        if (!_network.Voice.Enabled) _network.Voice.Reset();
        Toast(_network.Voice.Enabled ? "Voz ligada." : "Voz desligada: você não fala nem ouve.");
    }

    /// Mentions a newer client once, as a toast, when the start-up check says one exists.
    private void AnnounceUpdate()
    {
        var updates = GetNode<AppManager>("/root/AppManager").Updates;
        void Show() { if (updates.Status is LifeVerse.Shared.Updates.UpdateStatus.Available or LifeVerse.Shared.Updates.UpdateStatus.Required) Toast(updates.Describe()); }
        Show();
        updates.Checked += Show;
        TreeExiting += () => updates.Checked -= Show;
    }

    private void BuildToasts()
    {
        _toasts = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _toasts.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _toasts.OffsetTop = 40; _toasts.OffsetLeft = -230; _toasts.OffsetRight = 230;
        _root.AddChild(_toasts);
    }

    // ---- behaviour -------------------------------------------------------------------------------------------

    private void ToggleWindow(FloatingWindow window)
    {
        window.Visible = !window.Visible;
        if (window.Visible) window.MoveToFront();
        SyncToggles();
    }

    private void ToggleGestures()
    {
        if (_gestures is null) return;
        _gestures.Visible = !_gestures.Visible;
        if (_gestures.Visible)
        {
            var viewport = GetViewport().GetVisibleRect().Size;
            _gestures.Position = new Vector2(viewport.X / 2f - 138 - _gestures.Size.X, viewport.Y - 64 - _gestures.Size.Y - 8);
            _gestures.GetParent()?.MoveChild(_gestures, -1);
        }
        SyncToggles();
    }

    private void SyncToggles()
    {
        _miniMapButton.SetPressedNoSignal(_miniMapWindow.Visible);
        _moveButton.SetPressedNoSignal(_moveWindow.Visible);
        _gestureButton.SetPressedNoSignal(_gestures?.Visible ?? false);
    }

    private static void AddChoiceSubmenu(PopupMenu parent, string title, string[] choices, Action<int> chosen)
    {
        var submenu = new PopupMenu { Name = title.Replace(' ', '_').Replace('(', '_').Replace(')', '_') };
        parent.AddChild(submenu);
        foreach (var choice in choices) submenu.AddItem(choice);
        submenu.IdPressed += id => chosen((int)id);
        parent.AddSubmenuNodeItem(title, submenu);
    }

    private void SetUiScale(float scale, bool save = true)
    {
        scale = Mathf.Clamp(scale, .6f, 2f);
        GetWindow().ContentScaleFactor = scale;
        if (save) { _prefs.SetValue("ui", "scale", (double)scale); _prefs.Save(PrefsPath); Toast($"Escala da interface: {scale:P0}"); }
    }

    private void SetVolume(float volume, bool save = true)
    {
        volume = Mathf.Clamp(volume, 0f, 1f);
        AudioServer.SetBusVolumeDb(0, volume <= 0f ? -80f : Mathf.LinearToDb(volume));
        AudioServer.SetBusMute(0, volume <= 0f);
        if (save) { _prefs.SetValue("audio", "volume", (double)volume); _prefs.Save(PrefsPath); Toast(volume <= 0f ? "Som desligado." : $"Volume: {volume:P0}"); }
    }

    private void SetWeather(string weather)
    {
        if (_environment is null) return;
        _environment.Weather = weather;
        Toast("Clima alterado somente para você.");
    }

    private static readonly string[] TutorialPages =
    [
        "Bem-vindo ao LifeVerse!\n\nEste é um mundo persistente onde você anda, conversa, constrói e explora ilhas com outras pessoas. Este guia rápido mostra o essencial. Você pode abri-lo de novo em Ajuda > Tutorial.",
        "Andar e correr\n\nW/S andam para frente e para trás, A/D giram o personagem e Q/Z dão passos para o lado. Segure Shift para alternar entre andar e correr, Espaço pula e F liga o voo. Dê duplo clique no chão para andar até lá.",
        "Câmera\n\nSegure o botão direito do mouse e arraste para girar a câmera, use a roda para aproximar ou afastar e o botão do meio para olhar para um ponto. C alterna a primeira pessoa e R redefine a câmera.",
        "Conversar\n\nPressione Enter, digite e envie para falar com quem está a até 30 metros. Esc sai da barra de conversa. Em Pessoas você encontra amigos, grupos e mensagens.",
        "Construir e moldar o terreno\n\nB abre as ferramentas de construção e T abre as de terreno: elevar, rebaixar, nivelar, suavizar e pintar texturas, além do nível do mar. Só o dono da ilha, ou quem tem permissão, pode editar.",
        "Explorar\n\nO Mini-mapa mostra onde você está, o Mapa lista as ilhas e a Foto salva uma imagem sem a interface. O oceano cerca a ilha: entre na água e você nada. Divirta-se!"
    ];

    public void ShowTutorial()
    {
        if (_tutorial is null)
        {
            _tutorial = new FloatingWindow("Primeiros passos");
            _tutorial.Place(.5f, .5f, -250, -190, 250, 190);
            _tutorialText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(470, 190), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            _tutorial.Content.AddChild(_tutorialText);
            _tutorialPage = new Label { HorizontalAlignment = HorizontalAlignment.Center };
            _tutorial.Content.AddChild(_tutorialPage);
            var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
            var back = new Button { Text = "Voltar", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; back.Pressed += () => ShowTutorialPage(_page - 1);
            var next = new Button { Text = "Próximo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; next.Pressed += () => { if (_page >= TutorialPages.Length - 1) FinishTutorial(); else ShowTutorialPage(_page + 1); };
            row.AddChild(back); row.AddChild(next); _tutorial.Content.AddChild(row);
            _tutorialSkip = new CheckBox { Text = "Não mostrar ao conectar", ButtonPressed = (bool)_prefs.GetValue("tutorial", "done", false) };
            _tutorial.Content.AddChild(_tutorialSkip);
            _tutorial.CloseRequested += FinishTutorial;
            _root.AddChild(_tutorial);
        }
        ShowTutorialPage(0);
        _tutorial.Visible = true; _tutorial.MoveToFront();
    }

    private void ShowTutorialPage(int page)
    {
        _page = Mathf.Clamp(page, 0, TutorialPages.Length - 1);
        _tutorialText.Text = TutorialPages[_page];
        _tutorialPage.Text = $"{_page + 1} / {TutorialPages.Length}";
    }

    private void FinishTutorial()
    {
        if (_tutorial is null) return;
        _tutorial.Visible = false;
        if (_tutorialSkip.ButtonPressed || _page >= TutorialPages.Length - 1) { _prefs.SetValue("tutorial", "done", true); _prefs.Save(PrefsPath); }
    }

    private void SetEnvironment(int id)
    {
        if (_environment is null) return;
        float[] hours = [6.3f, 12f, 17.7f, 0f];
        if (id < hours.Length) { _environment.DayNightCycle = false; _environment.Hour = hours[id]; Toast("Ambiente alterado somente para você."); }
        else { _environment.DayNightCycle = true; Toast("Ciclo de dia e noite automático."); }
    }

    private void RefreshIslands()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (_islands.GetHttpClientStatus() != Godot.HttpClient.Status.Disconnected) return;
        var headers = session.IsAuthenticated ? new[] { $"Authorization: Bearer {session.AccessToken}" } : [];
        _islands.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/islands", headers);
    }

    private void OnIslands(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) return;
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body));
            _islandList.Clear(); _islandIds.Clear();
            foreach (var island in doc.RootElement.EnumerateArray())
            {
                if (!Guid.TryParse(island.GetProperty("id").GetString(), out var id)) continue;
                var active = island.TryGetProperty("runtimeState", out var state) && state.GetString() == "running";
                _islandList.AddItem($"{island.GetProperty("name").GetString()}  ·  {(active ? "ativa" : "adormecida (inicia ao entrar)")}");
                _islandList.SetItemCustomFgColor(_islandList.ItemCount - 1, active ? ViewerTheme.Good : ViewerTheme.TextDim);
                _islandIds.Add(id);
                if (string.IsNullOrWhiteSpace(_islandId.Text) && active) _islandId.Text = id.ToString();
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private void Join()
    {
        if (!Guid.TryParse(_islandId.Text.Trim(), out var islandId)) { _joinStatus.Text = "UUID de ilha inválido."; return; }
        var error = _network.JoinIsland(islandId);
        if (error != Error.Ok) _joinStatus.Text = $"Não foi possível entrar: {error}";
    }

    private void OnChatSubmit(string text)
    {
        text = text.Trim();
        _chat.Clear(); _chat.ReleaseFocus();
        if (text.Length == 0) return;
        if (!_network.IsAuthenticated) { AppendSystem("Você precisa estar em uma ilha para conversar."); return; }
        _network.SendChatMessage(text);
    }

    private void OnChat(string line)
    {
        var close = line.IndexOf("] ", StringComparison.Ordinal);
        var colon = close >= 0 ? line.IndexOf(": ", close, StringComparison.Ordinal) : -1;
        if (close < 0 || colon < 0) { AppendLine(Esc(line)); return; }
        AppendLine($"[color=#8d8d8d]{Esc(line[..(close + 1)])}[/color] [color=#8fd0ff]{Esc(line[(close + 2)..colon])}[/color]: {Esc(line[(colon + 2)..])}");
    }

    private void OnConnectionState(string state, string detail)
    {
        _state = state; _joinStatus.Text = detail;
        if (state is "failed" or "offline") RefreshIslands();
        if (state == "online") { Toast("Conectado à ilha."); _joinWindow.Visible = false; _walletTimer = 60; if (!(bool)_prefs.GetValue("tutorial", "done", false)) ShowTutorial(); }
        else if (state is "failed" or "incompatible") { Toast(detail); _joinWindow.Visible = true; }
        else if (state == "offline") { Toast("Desconectado da ilha."); _joinWindow.Visible = true; }
    }

    private void OnTerrainRejected(string code) => Toast(code switch { "no_permission" => "Você não pode editar o terreno desta ilha.", "rate_limit" => "Muitas edições de terreno seguidas.", _ => $"Edição de terreno recusada: {code}" });

    private void AppendSystem(string text) => AppendLine($"[color=#e0b050]{Esc(text)}[/color]");
    private void AppendLine(string bbcode) => _chatLog.AppendText(bbcode + "\n");
    private static string Esc(string text) => text.Replace("[", "[lb]");

    private void UpdateStatus()
    {
        if (_player is not null)
        {
            var p = _player.GlobalPosition;
            var half = _terrain?.Map.HalfExtent ?? 128f;
            _coords.Text = $"({Mathf.RoundToInt(p.X + half)}, {Mathf.RoundToInt(p.Z + half)}, {Mathf.RoundToInt(p.Y)})";
            _walk.SetPressedNoSignal(!_player.IsFlying && !_player.AlwaysRun);
            _run.SetPressedNoSignal(!_player.IsFlying && _player.AlwaysRun);
            _fly.SetPressedNoSignal(_player.IsFlying);
        }
        var hour = _environment?.Hour ?? 12f;
        _clock.Text = $"{(int)hour:00}:{(int)((hour - (int)hour) * 60):00}";
        _fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";
        var online = _network.IsAuthenticated;
        var connecting = !online && _state is "requesting_ticket" or "connecting" or "authenticating" or "waiting_island";
        _indicator.Color = online ? ViewerTheme.Good : connecting ? ViewerTheme.Warn : ViewerTheme.Bad;
        _connection.Text = online ? "Online" : connecting ? "Conectando" : "Offline";
    }

    private void RequestWallet()
    {
        var session = GetNode<SessionManager>("/root/SessionManager");
        if (!session.IsAuthenticated) return;
        _wallet.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/economy/wallet", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnWallet(long result, long code, string[] headers, byte[] body)
    {
        if (code is < 200 or >= 300) return;
        try
        {
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(body));
            if (doc.RootElement.TryGetProperty("availableCoins", out var coins)) _balance.Text = $"LV$ {coins.GetInt64():N0}";
        }
        catch (JsonException) { }
    }

    private async void TakeSnapshot()
    {
        if (_snapshotBusy) return;
        _snapshotBusy = true;
        var hud = _player?.GetNodeOrNull<CanvasLayer>("HUD");
        _root.Visible = false; if (hud is not null) hud.Visible = false;
        for (var i = 0; i < 3; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        _root.Visible = true; if (hud is not null) hud.Visible = true;
        var directory = ProjectSettings.GlobalizePath("user://snapshots");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        var path = $"{directory}/lifeverse-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        Toast(image.SavePng(path) == Error.Ok ? $"Foto salva em {path}" : "Não foi possível salvar a foto.");
        _snapshotBusy = false;
    }

    private void ShowControlsHelp()
    {
        if (_helpWindow is null)
        {
            _helpWindow = new FloatingWindow("Controles");
            _helpWindow.Place(.5f, .5f, -230, -170, 230, 170);
            _helpWindow.Content.AddChild(new Label
            {
                Text = "Movimento\n  W/S ou ↑/↓ — andar    A/D ou ←/→ — girar    Q/Z — passo lateral\n  Shift — inverte andar/correr    Espaço — pular/subir    Ctrl — descer\n  F — voar    C — primeira pessoa    V — câmera livre    R — redefinir câmera\n\nCâmera\n  Botão direito + arrastar — orbitar    Roda — zoom    Botão do meio — olhar\n\nInterface\n  Enter — conversar    Esc — sair da conversa    B — construir    T — terreno\n  E — interagir/levantar    G — acenar    H — dançar    Duplo clique — andar até o ponto",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0)
            });
            _root.AddChild(_helpWindow);
        }
        _helpWindow.Visible = true; _helpWindow.MoveToFront();
    }

    private void Toast(string text)
    {
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", ViewerTheme.Box(new Color(.1f, .1f, .1f, .92f), new Color("#6a6a6a"), 3, 1, 8));
        panel.AddChild(new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        _toasts.AddChild(panel);
        var tween = CreateTween();
        tween.TweenInterval(3.5); tween.TweenProperty(panel, "modulate:a", 0f, .6); tween.TweenCallback(Callable.From(panel.QueueFree));
    }
}
