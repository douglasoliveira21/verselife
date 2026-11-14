using LifeVerse.Client.Build;
using System.Text;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.Interface;

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
    private Control _chatWindow = null!;
    private ulong _chatVisibleUntil;
    private VBoxContainer _conversations = null!;
    private FloatingWindow _miniMapWindow = null!;
    private FloatingWindow _moveWindow = null!;
    private FloatingWindow _joinWindow = null!;
    private WorldEntryCoordinator _entry = null!;
    private TeleportCoordinator _teleport = null!;
    private PeoplePanel? _people;
    private FloatingWindow? _helpWindow;
    private Control? _gestures;
    private Label _coords = null!;
    private Label _clock = null!;
    private Label _fps = null!;
    private Label _connection = null!;
    private Label _balance = null!;
    private StatusDot _indicator = null!;
    private Label _islandName = null!;
    private readonly Dictionary<Guid, string> _islandNames = [];
    private Button _walk = null!;
    private Button _run = null!;
    private Button _fly = null!;
    private Button _miniMapButton = null!;
    private Button _moveButton = null!;
    private Button? _gestureButton;
    private LineEdit _islandId = null!;
    private Label _joinStatus = null!;
    private VBoxContainer _toasts = null!;
    private Notifications.NotificationCenterUi? _notificationCenter;
    private HttpRequest _wallet = null!;
    private HttpRequest _islands = null!;
    private HttpRequest _start = null!;
    private bool _entering;
    private double _facingTimer;
    private Guid _arrivedAt;
    private ItemList _islandList = null!;
    private readonly List<Guid> _islandIds = [];
    private double _statusTimer;
    private double _walletTimer = 55;
    private bool _snapshotBusy;
    private string _state = "offline";
    private Theme _theme = null!;
    private const string PrefsPath = "user://viewer.cfg";
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
        // The interface preferences belong to the account (user://interface_<account>.cfg); the old per-machine viewer.cfg only seeds them. Loaded first: the windows read their saved layout from it.
        WindowManager.Settings = _interface = InterfaceSettings.Load(GetNodeOrNull<SessionManager>("/root/SessionManager")?.Username);
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
        var notifications = new NotificationClient { Name = "Notifications" }; AddChild(notifications); notifications.Notified += text => Toast(text); notifications.Raw += json => { _world.GetNodeOrNull<GroupsWindow>("GroupsWindow")?.OnLiveEvent(json); _world.GetNodeOrNull<ItemOffersWindow>("ItemOffersWindow")?.OnLiveEvent(json); _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.OnLiveEvent(json); _notificationCenter?.Poke(); };
        AnnounceUpdate();
        _people = new PeoplePanel { Name = "People" }; AddChild(_people); _people.AttachTo(_root); _people.Message += text => Toast(text); _people.ProfileRequested += id => _ = _world.GetNodeOrNull<ProfileWindow>("ProfileWindow")?.OpenByIdAsync(id); _people.ChatRequested += (_, name) => Toast($"Mensagem privada para {name}: abra a aba Conversas.");
        BuildTopBar();
        BuildLocationBar();
        BuildBottomBar();
        BuildSideBar();
        WirePeople();
        ApplyInterfaceMode();
        _wallet = new HttpRequest(); AddChild(_wallet); _wallet.RequestCompleted += OnWallet;
        _islands = new HttpRequest(); AddChild(_islands); _islands.RequestCompleted += OnIslands;
        RefreshIslands();
        _start = new HttpRequest(); AddChild(_start); _start.RequestCompleted += OnStart;
        _network.ChatReceived += OnChat; _network.ConnectionStateChanged += OnConnectionState; _network.TerrainEditRejected += OnTerrainRejected; _network.VehicleMessage += Toast;
        AppendSystem("Bem-vindo ao LifeVerse. Pressione Enter para conversar.");
        // The interface preferences belong to the account (user://interface_<account>.cfg); the old per-machine viewer.cfg only seeds them.
        ApplyUiScale();
        GetWindow().SizeChanged += () => ApplyUiScale();     // "Automático" follows the window
        SetVolume((float)(double)_prefs.GetValue("audio", "volume", 1.0), save: false);
        UpdateStatus();
        BuildRadial();
        if (_world.GetNodeOrNull<LandManager>("LandManager") is null) { var land = new LandManager { Name = "LandManager" }; _world.AddChild(land); land.Said += text => Toast(text); land.WalletChanged += RequestWallet; }
        BuildHost.Ensure(_world);
        if (_world.GetNodeOrNull<AssetLoadingService>("AssetLoadingService") is null) _world.AddChild(new AssetLoadingService { Name = "AssetLoadingService" });
        if (_world.GetNodeOrNull<PhysicsSync>("PhysicsSync") is null) _world.AddChild(new PhysicsSync { Name = "PhysicsSync" });
        if (_world.GetNodeOrNull<BuildClickActions>("BuildClickActions") is null) _world.AddChild(new BuildClickActions { Name = "BuildClickActions" });
        if (_world.GetNodeOrNull<ObjectEffects>("ObjectEffects") is null) _world.AddChild(new ObjectEffects { Name = "ObjectEffects" });
        if (_world.GetNodeOrNull<ScriptDialogWindow>("ScriptDialogWindow") is null) _world.AddChild(new ScriptDialogWindow { Name = "ScriptDialogWindow" });
        if (_world.GetNodeOrNull<ScriptHudLayer>("ScriptHudLayer") is null) _world.AddChild(new ScriptHudLayer { Name = "ScriptHudLayer" });
        if (_world.GetNodeOrNull<WorldEntryCoordinator>("WorldEntryCoordinator") is null) _world.AddChild(new WorldEntryCoordinator { Name = "WorldEntryCoordinator" });
        if (_world.GetNodeOrNull<TeleportCoordinator>("TeleportCoordinator") is null) { var trip = new TeleportCoordinator { Name = "TeleportCoordinator" }; _world.AddChild(trip); trip.Initialize(_world.GetNode<WorldEntryCoordinator>("WorldEntryCoordinator")); }
        _teleport = _world.GetNode<TeleportCoordinator>("TeleportCoordinator");
        _entry = _world.GetNode<WorldEntryCoordinator>("WorldEntryCoordinator"); _entry.RetryLocate = () => { _entering = false; EnterFirstIsland(); }; _entry.LeaveToMenu = () => { _joinWindow.Visible = true; _joinWindow.MoveToFront(); RefreshIslands(); };
        EnterFirstIsland(); // after the loading screen exists: it covers the world from the first moment
        if (_world.GetNodeOrNull<LandMapWindow>("LandMapWindow") is null) _world.AddChild(new LandMapWindow { Name = "LandMapWindow", JoinIsland = JoinIslandById });
        if (_world.GetNodeOrNull<ItemRightsWindow>("ItemRightsWindow") is null) _world.AddChild(new ItemRightsWindow { Name = "ItemRightsWindow" });
        if (_world.GetNodeOrNull<ItemOffersWindow>("ItemOffersWindow") is null) _world.AddChild(new ItemOffersWindow { Name = "ItemOffersWindow" });
        if (_world.GetNodeOrNull<SearchWindow>("SearchWindow") is null)
        {
            var land = _world.GetNodeOrNull<LandManager>("LandManager");
            _world.AddChild(new SearchWindow
            {
                Name = "SearchWindow", OpenProfile = id => _ = _world.GetNodeOrNull<ProfileWindow>("ProfileWindow")?.OpenByIdAsync(id), JoinIsland = JoinIslandById, CurrentIsland = () => _network.CurrentIslandId,
                TeleportToPoint = (x, z) => land?.TeleportToPoint(x, z),
                TeleportToParcel = id => { if (land?.Parcels.FirstOrDefault(p => p.Id == id) is { } parcel) { land.TeleportTo(parcel); return true; } return false; },
            });
        }
        if (_world.GetNodeOrNull<AppearanceWindow>("AppearanceWindow") is null) _world.AddChild(new AppearanceWindow { Name = "AppearanceWindow", Player = _player });
        if (_world.GetNodeOrNull<GroupsWindow>("GroupsWindow") is null) _world.AddChild(new GroupsWindow { Name = "GroupsWindow" });
        if (_world.GetNodeOrNull<Groups.GroupProfileWindow>("GroupProfileWindow") is null)
            _world.AddChild(new Groups.GroupProfileWindow { Name = "GroupProfileWindow", Said = text => Toast(text), Visit = JoinIslandById, OpenProfile = id => _ = _world.GetNodeOrNull<ProfileWindow>("ProfileWindow")?.OpenByIdAsync(id), OpenAdmin = (id, tab) => _world.GetNodeOrNull<GroupsWindow>("GroupsWindow")?.OpenAt(id, tab), ManageLand = id => _world.GetNodeOrNull<GroupsWindow>("GroupsWindow")?.OpenAt(id, 4), Changed = () => _ = _world.GetNodeOrNull<Groups.GroupsHubWindow>("GroupsHubWindow")?.LoadAsync(true) });
        if (_world.GetNodeOrNull<Groups.GroupCreateWindow>("GroupCreateWindow") is null)
            _world.AddChild(new Groups.GroupCreateWindow { Name = "GroupCreateWindow", Said = text => Toast(text), Created = id => { _world.GetNodeOrNull<Groups.GroupsHubWindow>("GroupsHubWindow")?.Open(); if (id != Guid.Empty) _ = _world.GetNodeOrNull<Groups.GroupProfileWindow>("GroupProfileWindow")?.OpenAsync(id); } });
        if (_world.GetNodeOrNull<Groups.GroupDiscoverWindow>("GroupDiscoverWindow") is null)
            _world.AddChild(new Groups.GroupDiscoverWindow { Name = "GroupDiscoverWindow", OpenGroup = id => _ = _world.GetNodeOrNull<Groups.GroupProfileWindow>("GroupProfileWindow")?.OpenAsync(id) });
        if (_world.GetNodeOrNull<Groups.GroupsHubWindow>("GroupsHubWindow") is null)
            _world.AddChild(new Groups.GroupsHubWindow { Name = "GroupsHubWindow", Said = text => Toast(text), OpenGroup = (id, tab) => _ = _world.GetNodeOrNull<Groups.GroupProfileWindow>("GroupProfileWindow")?.OpenAsync(id, tab == 1 ? "chat" : tab == 2 ? "avisos" : "geral"), CreateGroup = () => _world.GetNodeOrNull<Groups.GroupCreateWindow>("GroupCreateWindow")?.Open(), FindGroups = () => _world.GetNodeOrNull<Groups.GroupDiscoverWindow>("GroupDiscoverWindow")?.Open() });
        if (_world.GetNodeOrNull<ProfileWindow>("ProfileWindow") is null) _world.AddChild(new ProfileWindow { Name = "ProfileWindow" });
        if (_world.GetNodeOrNull<IslandLookService>("IslandLookService") is null) _world.AddChild(new IslandLookService { Name = "IslandLookService" });
        if (_world.GetNodeOrNull<IslandManagement.IslandManagementWindow>("IslandManagementWindow") is null)
            _world.AddChild(new IslandManagement.IslandManagementWindow { Name = "IslandManagementWindow", OpenLandManagement = (island, section, offer) => _ = _world.GetNodeOrNull<LandManagement.LandManagementWindow>("LandManagementWindow")?.OpenSectionAsync(island, section, offer), ViewInSearch = (_, name) => _world.GetNodeOrNull<SearchWindow>("SearchWindow")?.OpenWithQuery(name), ViewProfile = id => _ = _world.GetNodeOrNull<ProfileWindow>("ProfileWindow")?.OpenByIdAsync(id), CurrentPosition = () => _player is null ? null : (_player.GlobalPosition.X, _player.GlobalPosition.Z) });
        if (_world.GetNodeOrNull<RealEstate.MyPropertiesWindow>("MyPropertiesWindow") is null)
            _world.AddChild(new RealEstate.MyPropertiesWindow { Name = "MyPropertiesWindow", Visit = JoinIslandById, VisitParcel = JoinParcel, NoticeArrived = text => Toast(text), ManageIsland = id => _ = _world.GetNodeOrNull<IslandManagement.IslandManagementWindow>("IslandManagementWindow")?.OpenAsync(id), ManageLand = id => _ = _world.GetNodeOrNull<LandManagement.LandManagementWindow>("LandManagementWindow")?.OpenAsync(id) });
        if (_world.GetNodeOrNull<RealEstate.RealEstateMarketWindow>("RealEstateMarketWindow") is null)
            _world.AddChild(new RealEstate.RealEstateMarketWindow { Name = "RealEstateMarketWindow", Visit = JoinIslandById, VisitParcel = JoinParcel });
        if (_world.GetNodeOrNull<ParcelInfo.ParcelInfoWindow>("ParcelInfoWindow") is null)
            _world.AddChild(new ParcelInfo.ParcelInfoWindow { Name = "ParcelInfoWindow", Visit = JoinIslandById, VisitParcel = JoinParcel, Position = () => _player is null ? null : (_player.GlobalPosition.X, _player.GlobalPosition.Z, Mathf.PosMod(Mathf.RadToDeg(_player.Rotation.Y), 360.0)) });
        if (_world.GetNodeOrNull<LandManager>("LandManager") is { } landForAbout) landForAbout.ParcelAbout = id => _ = _world.GetNodeOrNull<ParcelInfo.ParcelInfoWindow>("ParcelInfoWindow")?.OpenAsync(_network.CurrentIslandId, id);
        if (_world.GetNodeOrNull<LandManagement.LandManagementWindow>("LandManagementWindow") is null)
            _world.AddChild(new LandManagement.LandManagementWindow { Name = "LandManagementWindow", Inspect = parcel => _ = _world.GetNodeOrNull<ParcelInfo.ParcelInfoWindow>("ParcelInfoWindow")?.OpenAsync(_world.GetNodeOrNull<LandManagement.LandManagementWindow>("LandManagementWindow")?.Data?.IslandId ?? _network.CurrentIslandId, parcel.Id) });
        foreach (var name in new[] { "InventoryPanel", "SocialPanel", "DiscoveryPanel", "MarketplacePanel", "SettingsPanel", "LuaScriptEditor", "HudEditor", "BuildEditorController", "TerrainEditor", "LandManager", "LandMapWindow", "GroupsWindow", "ProfileWindow", "AppearanceWindow", "SearchWindow", "ItemRightsWindow", "ItemOffersWindow" })
            foreach (var child in (_world.GetNodeOrNull(name)?.GetChildren() ?? []).OfType<PanelContainer>()) { child.Theme = theme; if (child is not FloatingWindow) FloatingWindow.MakeDraggable(child); }
    }

    public override void _ExitTree()
    {
        if (_network is null) return;
        _network.ChatReceived -= OnChat; _network.ConnectionStateChanged -= OnConnectionState; _network.TerrainEditRejected -= OnTerrainRejected; _network.VehicleMessage -= Toast;
    }

    public override void _Process(double delta)
    {
        _statusTimer -= delta; _walletTimer += delta; RememberFacing(delta);
        if (_chatWindow.Visible && !_chat.HasFocus() && _chatVisibleUntil > 0 && Time.GetTicksMsec() >= _chatVisibleUntil)
        {
            _chatWindow.Visible = false;
            _chatVisibleUntil = 0;
        }
        if (_statusTimer <= 0) { _statusTimer = .25; UpdateStatus(); }
        if (_walletTimer >= 60 && _network.IsAuthenticated && _wallet.GetHttpClientStatus() == Godot.HttpClient.Status.Disconnected) { _walletTimer = 0; RequestWallet(); }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Delete } && GetViewport().GuiGetFocusOwner() is null && _context?.DeleteSelectedObject() == true)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F10 } && GetViewport().GuiGetFocusOwner() is null)
        {
            ToggleImmersiveReveal();         // immersive mode: show or hide the interface
            GetViewport().SetInputAsHandled();
            return;
        }
        if (input is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Enter or Key.KpEnter } && GetViewport().GuiGetFocusOwner() is null)
        {
            RevealForChat();
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
        _toasts.OffsetTop = 84; _toasts.OffsetLeft = -280; _toasts.OffsetRight = 280; _toasts.AddThemeConstantOverride("separation", 10);
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
        _gestureButton?.SetPressedNoSignal(_gestures?.Visible ?? false);
    }

    private static void AddChoiceSubmenu(PopupMenu parent, string title, string[] choices, Action<int> chosen)
    {
        var submenu = new PopupMenu { Name = title.Replace(' ', '_').Replace('(', '_').Replace(')', '_') };
        parent.AddChild(submenu);
        foreach (var choice in choices) submenu.AddItem(choice);
        submenu.IdPressed += id => chosen((int)id);
        parent.AddSubmenuNodeItem(title, submenu);
    }

    private InterfaceSettings _interface = null!;

    /// <summary>Menu choice: 0 is "Automático", the others are the fixed factors of <see cref="InterfaceScale.Choices"/>.</summary>
    private void ChooseUiScale(int index)
    {
        if (index <= 0) _interface.AutoScale = true; else { _interface.AutoScale = false; _interface.Scale = InterfaceScale.Choices[Math.Clamp(index - 1, 0, InterfaceScale.Choices.Length - 1)]; }
        _interface.Save();
        var factor = ApplyUiScale();
        Toast(_interface.AutoScale ? $"Escala da interface: Automático ({InterfaceScale.Label(factor)})" : $"Escala da interface: {InterfaceScale.Label(factor)}");
    }

    private float ApplyUiScale()
    {
        var factor = InterfaceScale.Apply(GetWindow(), _interface.AutoScale, _interface.Scale);
        Callable.From(WindowManager.KeepAll).CallDeferred();      // a new size or scale must not leave a window outside the screen
        return factor;
    }

    private void RestoreWindowLayout() { WindowManager.ResetLayout(); Toast("Layout das janelas restaurado."); }

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
        "Andar e correr\n\nW/S ou as setas andam para frente e para trás, A/D giram o personagem e Q/Z dão passos para o lado. Segure Shift ou toque duas vezes para frente para correr. Toque Espaço para pular; segure Espaço para começar a voar. F também alterna o voo.",
        "Câmera\n\nSegure o botão direito do mouse e arraste para girar a câmera, use a roda para aproximar ou afastar e o botão do meio para olhar para um ponto. C alterna a primeira pessoa; nela, o mouse funciona como mira e W segue a direção apontada.",
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
                _islandIds.Add(id); _islandNames[id] = island.GetProperty("name").GetString() ?? "Ilha";
                if (string.IsNullOrWhiteSpace(_islandId.Text) && active) _islandId.Text = id.ToString();
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    /// <summary>
    /// Right after signing in the person is taken to an island by the server: the one they were on last (to the same spot, which the island server
    /// keeps), or the welcome island when it is a new avatar or the last island is gone. Only once per visit to the world, and not when already on an island.
    /// </summary>
    private void EnterFirstIsland()
    {
        if (_entering || _network.IsAuthenticated || _network.CurrentIslandId != Guid.Empty) return;
        var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return;
        _entering = true; _joinStatus.Text = "Procurando a sua ilha…"; _joinWindow.Visible = false; // no list of islands while the server is taking the person to theirs
        _entry.BeginLocating(); // the loading screen covers the world from this moment on
        _start.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/me/start", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    private void OnStart(long result, long code, string[] headers, byte[] body)
    {
        _entering = false;
        if (code is < 200 or >= 300 || StartPlace.Parse(Encoding.UTF8.GetString(body)) is not { } info) { GoToWelcome(fromStart: true); return; } // an older server may not know "where do I start": the welcome island is the next best answer
        Toast(StartPlace.Message(info)); JoinIslandById(info.IslandId);
    }

    /// <summary>Neither the island of last time nor the welcome island could be found: only then the person is asked to choose from the list.</summary>
    private void NoWelcome(bool fromStart)
    {
        Toast("Não foi possível achar a Ilha de Boas-vindas.");
        if (_entry.Manager.Active) { _entry.LocateFailed("Não foi possível achar a Ilha de Boas-vindas. O servidor pode estar desatualizado."); return; }
        if (!fromStart) return;
        _joinStatus.Text = "Não achei a sua ilha de início. Escolha uma ilha da lista (o servidor pode estar desatualizado)."; _joinWindow.Visible = true; _joinWindow.MoveToFront(); RefreshIslands();
    }

    /// <summary>Goes to the welcome island, on purpose.</summary>
    private void GoToWelcome(bool fromStart = false)
    {
        var session = GetNode<SessionManager>("/root/SessionManager"); if (!session.IsAuthenticated) return;
        var http = new HttpRequest(); AddChild(http);
        http.RequestCompleted += (result, code, _, body) =>
        {
            http.QueueFree();
            try { using var d = JsonDocument.Parse(Encoding.UTF8.GetString(body)); if (code is >= 200 and < 300 && Guid.TryParse(d.RootElement.GetProperty("islandId").GetString(), out var id)) { Toast("Indo para a Ilha de Boas-vindas."); JoinIslandById(id); } else NoWelcome(fromStart); }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { NoWelcome(fromStart); }
        };
        http.Request($"{GetNode<AppManager>("/root/AppManager").ApiBaseUrl}/api/v1/worlds/welcome", [$"Authorization: Bearer {session.AccessToken}"]);
    }

    /// <summary>On this computer: which way the avatar faces, so coming back puts it looking the same way.</summary>
    private void RememberFacing(double delta)
    {
        _facingTimer += delta; if (_facingTimer < 5) return; _facingTimer = 0;
        if (!_network.IsAuthenticated || _player is null) return;
        StartPlace.Remember(GetNode<SessionManager>("/root/SessionManager").UserId, _network.CurrentIslandId, _player.Rotation.Y);
    }

    private void FaceWhereIWas()
    {
        if (_player is null || _network.CurrentIslandId == _arrivedAt) return;
        _arrivedAt = _network.CurrentIslandId;
        if (SearchTravel.TakeFor(_arrivedAt) is { } spot) // the person chose a result on this island from the search: once the avatar is standing here, go to the spot
        {
            var land = _world.GetNodeOrNull<LandManager>("LandManager");
            GetTree().CreateTimer(1.5).Timeout += () => land?.TeleportToPoint(spot.X, spot.Z);
            return;
        }
        if (SearchTravel.TakeParcelFor(_arrivedAt) is { } parcel) // "Visitar" on a parcel of this island: once the avatar is standing here, ask to be taken to it
        {
            var land = _world.GetNodeOrNull<LandManager>("LandManager");
            GetTree().CreateTimer(1.5).Timeout += () => land?.TeleportToParcel(parcel);
            return;
        }
        if (StartPlace.FacingOn(GetNode<SessionManager>("/root/SessionManager").UserId, _network.CurrentIslandId) is { } yaw) _player.FaceYaw(yaw);
    }

    /// Joins an island by its id (from the property search).
    private void JoinIslandById(Guid islandId) { _islandId.Text = islandId.ToString(); Join(); }

    /// <summary>
    /// "Visitar" on a parcel: on the same island the avatar is taken to the parcel at once; on another island it joins first and, once there,
    /// asks to be taken to the parcel (the island's rules for that parcel still decide).
    /// </summary>
    private void JoinParcel(Guid islandId, Guid parcelId)
    {
        if (_network.IsAuthenticated && islandId == _network.CurrentIslandId) { _world.GetNodeOrNull<LandManager>("LandManager")?.TeleportToParcel(parcelId); return; }
        SearchTravel.SetParcel(islandId, parcelId); JoinIslandById(islandId);
    }

    private void Join()
    {
        if (!Guid.TryParse(_islandId.Text.Trim(), out var islandId)) { _joinStatus.Text = "UUID de ilha inválido."; return; }
        // already on an island: going to another one is a controlled trip (reserve, ticket, arrival), not a plain entry
        if (_network.IsAuthenticated) { if (islandId == _network.CurrentIslandId) { Toast("Você já está nessa ilha."); return; } if (!_teleport.Begin(islandId)) Toast("Você já está viajando para outra ilha."); return; }
        var error = _network.JoinIsland(islandId);
        if (error != Error.Ok) _joinStatus.Text = $"Não foi possível entrar: {error}";
    }

    private void OnChatSubmit(string text)
    {
        text = text.Trim();
        _chat.Clear(); _chat.ReleaseFocus();
        if (text.Length == 0) return;
        ShowChatTemporarily();
        if (!_network.IsAuthenticated) { AppendSystem("Você precisa estar em uma ilha para conversar."); return; }
        _network.SendChatMessage(text);
    }

    private void OpenChatHistory()
    {
        if (_chatMinimized) SetChatMinimized(false);
        _chatWindow.Visible = true;
        _chatVisibleUntil = 0;
        _player?.SetTextInputActive(true);
        _root.MoveChild(_chatWindow, -1);
    }

    private void ShowChatTemporarily(double seconds = 6)
    {
        if (_chatMinimized) return;          // minimized: the bubble counts the new messages instead
        _chatWindow.Visible = true;
        _chatVisibleUntil = Time.GetTicksMsec() + (ulong)(seconds * 1000);
    }

    private void CloseChatInput()
    {
        _player?.SetTextInputActive(false);
        EndChatReveal();
        if (_chatWindow.Visible) _chatVisibleUntil = Time.GetTicksMsec() + 6000;
    }


    private void OnConnectionState(string state, string detail)
    {
        _state = state; _joinStatus.Text = detail;
        if (state is "failed" or "offline") RefreshIslands();
        if (state == "online") { FaceWhereIWas(); Toast("Conectado à ilha."); _joinWindow.Visible = false; _walletTimer = 60; if (!(bool)_prefs.GetValue("tutorial", "done", false)) ShowTutorial(); }
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
        _indicator.Color = online ? UiStyle.SuccessBright : connecting ? UiStyle.Warning : UiStyle.Danger;
        _connectionMenu.Text = ConnectionText(online, connecting);
        NoteIsland(online ? _network.CurrentIslandId : Guid.Empty);
        _islandName.Text = online ? (_islandNames.TryGetValue(_network.CurrentIslandId, out var islandTitle) ? islandTitle : "Ilha") : "Sem ilha";
        _peopleButton.SetPressedNoSignal(_people?.Visible ?? false);
        _inventoryButton.SetPressedNoSignal(_world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.IsOpen ?? false);
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
            if (doc.RootElement.TryGetProperty("availableCoins", out var coins)) _balance.Text = FormatCoins(coins.GetInt64());
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
                Text = "Movimento\n  W/S ou ↑/↓ — andar    A/D ou ←/→ — girar    Q/Z — passo lateral\n  Shift ou duplo toque para frente — correr    Toque Espaço — pular    Segure Espaço — iniciar voo/subir\n  Ctrl — descer    F — alternar voo    C — primeira pessoa    V — câmera livre    R — redefinir câmera\n\nCâmera\n  Botão direito + arrastar — orbitar    Primeira pessoa — mouse aponta e W segue a mira\n  Roda — zoom    Botão do meio — olhar\n\nInterface\n  Enter — conversar    Esc — sair da conversa    B — construir    T — terreno\n  E — interagir/levantar    G — acenar    H — dançar    Duplo clique — andar até o ponto",
                AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0)
            });
            _root.AddChild(_helpWindow);
        }
        _helpWindow.Visible = true; _helpWindow.MoveToFront();
    }

}
