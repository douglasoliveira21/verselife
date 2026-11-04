using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The top menu bar and the bottom toolbar, built to match the design references (client/Assets/ui/01 and 02).
public partial class ViewerUI
{
    private const int TopBarHeight = 44;
    private const int ToolbarButtonHeight = 46;

    // ---- top bar ---------------------------------------------------------------------------------------------------

    private void BuildTopBar()
    {
        var bar = new PanelContainer { Name = "MenuBarPanel", CustomMinimumSize = new Vector2(0, TopBarHeight) };
        var style = UiStyle.Box(new Color(UiStyle.PanelFill, .88f), UiStyle.BorderBright, 0, 0, 14, 0);
        style.BorderWidthBottom = 1; style.BorderColor = new Color(UiStyle.BorderBright, .85f);
        bar.AddThemeStyleboxOverride("panel", style);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        _root.AddChild(bar);
        var row = new HBoxContainer { Name = "Row" }; row.AddThemeConstantOverride("separation", 4); bar.AddChild(row);
        row.AddChild(UiStyle.Brand(false, 34));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0) });

        var me = AddMenu(row, "Eu",
            ("Espelho: mudar o visual aqui…", () => _world.GetNodeOrNull<AppearanceWindow>("AppearanceWindow")?.Toggle(), false),
            ("Editar aparência (tela completa)…", () => GetNode<UIManager>("/root/UIManager").ShowCharacterCreator(), false),
            ("Inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(), false),
            ("Preferências…", () => _world.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle(), false),
            ("-", null, false),
            ("Sair da ilha", _network.Disconnect, false),
            ("Sair do LifeVerse", () => GetTree().Quit(), false));
        AddChoiceSubmenu(me, "Escala da interface", ["80%", "90%", "100%", "110%", "125%", "150%"], index => SetUiScale(UiScales[index]));
        AddChoiceSubmenu(me, "Volume", ["Mudo", "25%", "50%", "75%", "100%"], index => SetVolume(Volumes[index]));
        AddMenu(row, "Comunicar",
            ("Conversa local   Enter", () => _chat.GrabFocus(), false),
            ("Pessoas, amigos e grupos…", () => _people?.Toggle(), false),
            ("Grupos e organizações…", () => _world.GetNodeOrNull<GroupsWindow>("GroupsWindow")?.Toggle(), false),
            ("Mensagens privadas…", () => _world.GetNodeOrNull<SocialPanel>("SocialPanel")?.Toggle(), false),
            ("Gestos", ToggleGestures, false),
            ("-", null, false),
            ("Voz: segure o botão Falar (use fones)", null, true),
            ("Voz: microfone aberto (cadeado)  liga/desliga", ToggleOpenMic, false),
            ("Voz: silenciar tudo  liga/desliga", ToggleVoice, false));
        var world = AddMenu(row, "Mundo",
            ("Mapa e destinos…", () => _world.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle(), false),
            ("Mini-mapa", () => ToggleWindow(_miniMapWindow), false),
            ("Ir para a Ilha de Boas-vindas", GoToWelcome, false),
            ("Ir para uma ilha…", () => { _joinWindow.Visible = true; _joinWindow.MoveToFront(); }, false),
            ("-", null, false),
            ("Editar terreno   T", () => _world.GetNodeOrNull<TerrainEditor>("TerrainEditor")?.SetActive(true), false),
            ("Buscar no LifeVerse…", () => _world.GetNodeOrNull<SearchWindow>("SearchWindow")?.Toggle(), false),
            ("Mapa de terrenos e busca…", () => _world.GetNodeOrNull<LandMapWindow>("LandMapWindow")?.Toggle(), false),
            ("Selecionar terreno (limites e donos)", () => _world.GetNodeOrNull<LandManager>("LandManager")?.SetActive(true), false),
            ("Tirar foto", TakeSnapshot, false));
        var environment = new PopupMenu { Name = "Ambiente" };
        world.AddChild(environment);
        foreach (var label in new[] { "Nascer do sol", "Meio-dia", "Pôr do sol", "Meia-noite" }) environment.AddItem(label);
        environment.AddItem("Ciclo automático");
        environment.IdPressed += id => SetEnvironment((int)id);
        world.AddSubmenuNodeItem("Ambiente (somente neste computador)", environment);
        AddChoiceSubmenu(world, "Clima (somente neste computador)", ["Limpo", "Nublado", "Chuva", "Tempestade", "Neblina"], index => SetWeather(WeatherNames[index]));
        AddMenu(row, "Conteúdo",
            ("Construir   B", () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true), false),
            ("Inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(), false),
            ("Marketplace e carteira…", () => _world.GetNodeOrNull<MarketplacePanel>("MarketplacePanel")?.Toggle(), false),
            ("-", null, false),
            ("Editor de scripts Lua…", () => _world.GetNodeOrNull<LuaScriptEditor>("LuaScriptEditor")?.Toggle(), false),
            ("Editor de HUD…", () => _world.GetNodeOrNull<HudEditor>("HudEditor")?.Toggle(), false));
        AddMenu(row, "Ajuda",
            ("Tutorial: primeiros passos", ShowTutorial, false),
            ("Controles", ShowControlsHelp, false),
            ("Baixar atualização", () => GetNode<AppManager>("/root/AppManager").Updates.OpenDownload(), false),
            ("Sobre o LifeVerse", () => Toast($"LifeVerse · cliente {AppManager.Version}"), false));
        AddMenu(row, "Desenvolver",
            ("Métricas de desempenho", () => _world.GetNodeOrNull<PerformanceOverlay>("PerformanceOverlay")?.Toggle(), false));

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        // Coins: gold coin, balance and the "+" button.
        row.AddChild(new CoinIcon { CustomMinimumSize = new Vector2(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        _balance = new Label { Text = "—", TooltipText = "Saldo de LV Coins", MouseFilter = Control.MouseFilterEnum.Stop };
        _balance.AddThemeFontSizeOverride("font_size", 20); _balance.AddThemeFontOverride("font", UiStyle.Medium);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(4, 0) });
        row.AddChild(_balance);
        var plus = new Button { Icon = UiStyle.Icon("plus", UiStyle.Accent, 20), TooltipText = "Carteira e marketplace", CustomMinimumSize = new Vector2(32, 32), FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, ExpandIcon = false };
        plus.AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(UiStyle.AccentFill, .55f), UiStyle.Accent, 8, 2, 0, 0));
        plus.AddThemeStyleboxOverride("hover", UiStyle.Box(UiStyle.AccentFillHover, UiStyle.Accent, 8, 2, 0, 0));
        plus.AddThemeStyleboxOverride("pressed", UiStyle.Box(UiStyle.AccentFillPressed, UiStyle.Accent, 8, 2, 0, 0));
        plus.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        plus.Pressed += () => _world.GetNodeOrNull<MarketplacePanel>("MarketplacePanel")?.Toggle();
        row.AddChild(plus);
        row.AddChild(Divider(26));

        // Where you are, and whether you are connected.
        row.AddChild(UiStyle.IconRect("pin-filled", UiStyle.Accent, 22));
        _islandName = new Label { Text = "Sem ilha", TooltipText = "Ilha atual" };
        _islandName.AddThemeFontSizeOverride("font_size", 17);
        row.AddChild(_islandName);
        row.AddChild(Divider(26));
        _indicator = new StatusDot { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Estado da conexão" };
        row.AddChild(_indicator);
        var state = new MenuButton { Text = "Desconectado", Flat = true, FocusMode = Control.FocusModeEnum.None, Icon = UiStyle.Icon("caret-down", UiStyle.Text, 12), IconAlignment = HorizontalAlignment.Right, ExpandIcon = false, TooltipText = "Conexão com a ilha" };
        StyleFlatMenu(state, 17);
        state.GetPopup().AddItem("Sair da ilha", 0);
        state.GetPopup().IdPressed += _ => _network.Disconnect();
        row.AddChild(state);
        _connection = new Label { Visible = false };   // text is mirrored onto the menu button by UpdateStatus
        _connectionMenu = state;
        row.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });

        // Position, world clock and frame rate live in the performance overlay now; the labels stay so existing updates keep working.
        _coords = new Label(); _clock = new Label(); _fps = new Label();
    }

    private MenuButton _connectionMenu = null!;

    private static Control Divider(float height) => new ColorRect { Color = new Color(UiStyle.BorderBright, .75f), CustomMinimumSize = new Vector2(1, height), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };

    private static void StyleFlatMenu(MenuButton button, int fontSize)
    {
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty { ContentMarginLeft = 10, ContentMarginRight = 10 });
        button.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(1, 1, 1, .08f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 10, 6));
        button.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color(UiStyle.Accent, .16f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 10, 6));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.Box(new Color(UiStyle.Accent, .16f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 10, 6));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", UiStyle.Regular); button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", Colors.White); button.AddThemeColorOverride("font_hover_color", Colors.White); button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("icon_normal_color", Colors.White); button.AddThemeColorOverride("icon_hover_color", Colors.White); button.AddThemeColorOverride("icon_pressed_color", Colors.White);
        button.AddThemeConstantOverride("icon_max_width", 12); button.AddThemeConstantOverride("h_separation", 8);
    }

    private static PopupMenu AddMenu(HBoxContainer row, string title, params (string Label, Action? Action, bool Disabled)[] items)
    {
        var button = new MenuButton { Text = title, Flat = true, FocusMode = Control.FocusModeEnum.None, Icon = UiStyle.Icon("caret-down", UiStyle.Text, 12), IconAlignment = HorizontalAlignment.Right, ExpandIcon = false, SwitchOnHover = true, Name = title.Replace(' ', '_') };
        StyleFlatMenu(button, 19);
        row.AddChild(button);
        var popup = button.GetPopup();
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

    // ---- bottom toolbar --------------------------------------------------------------------------------------------

    private void BuildBottomBar()
    {
        var bar = new PanelContainer { Name = "ToolbarPanel" };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.BarBox());
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        bar.OffsetLeft = 12; bar.OffsetRight = -12; bar.OffsetBottom = -10; bar.OffsetTop = -10 - (ToolbarButtonHeight + 16);
        bar.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(bar);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); bar.AddChild(row);

        // Chat field: bubble icon, text, send arrow, all inside one rounded field.
        var field = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(380, ToolbarButtonHeight) };
        var fieldNormal = UiStyle.Box(UiStyle.FieldFill, UiStyle.Border, 12, 1, 12, 0);
        var fieldFocus = UiStyle.Box(UiStyle.FieldFill, UiStyle.Accent, 12, 2, 12, 0);
        field.AddThemeStyleboxOverride("panel", fieldNormal);
        var inner = new HBoxContainer(); inner.AddThemeConstantOverride("separation", 10); field.AddChild(inner);
        inner.AddChild(UiStyle.IconRect("chat", UiStyle.Text, 22));
        _chat = new LineEdit { PlaceholderText = "Clique aqui para conversar", MaxLength = 256, Flat = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Conversa local (alcance de 30 m). Enter para focar, Esc para sair." };
        _chat.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _chat.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _chat.AddThemeFontSizeOverride("font_size", 17);
        _chat.TextSubmitted += OnChatSubmit;
        _chat.FocusEntered += () => field.AddThemeStyleboxOverride("panel", fieldFocus);
        _chat.FocusExited += () => field.AddThemeStyleboxOverride("panel", fieldNormal);
        inner.AddChild(_chat);
        var send = new Button { Icon = UiStyle.Icon("send", UiStyle.Text, 22), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Enviar", CustomMinimumSize = new Vector2(34, 0), ExpandIcon = false };
        send.Pressed += () => { OnChatSubmit(_chat.Text); _chat.Clear(); };
        inner.AddChild(send);
        row.AddChild(field);

        var voiceBar = new VoiceBar { Name = "VoiceBar", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; row.AddChild(voiceBar); voiceBar.Bind(_network.Voice);
        row.AddChild(Divider(32));

        _gestureButton = ToolButton(row, "hand", "Gestos", "Acenar, dançar e outras animações", ToggleGestures, toggle: true);
        _moveButton = ToolButton(row, "move", "Mover", "Andar, correr e voar", () => ToggleWindow(_moveWindow), toggle: true);
        var view = new MenuButton { Text = "Ver", TooltipText = "Modos de câmera", FocusMode = Control.FocusModeEnum.None, Flat = false, Icon = UiStyle.Icon("camera", UiStyle.Text, 22), CustomMinimumSize = new Vector2(0, ToolbarButtonHeight) };
        StyleTool(view, UiStyle.ButtonKind.ToolSelected, selectedLook: false);
        var viewPopup = view.GetPopup();
        viewPopup.AddItem("Terceira pessoa (padrão)", 0); viewPopup.AddItem("Primeira pessoa   C", 1); viewPopup.AddItem("Câmera livre   V", 2); viewPopup.AddSeparator(); viewPopup.AddItem("Redefinir câmera   R", 3);
        viewPopup.IdPressed += id => { switch (id) { case 0: _player?.SetCameraMode("ThirdPerson"); break; case 1: _player?.SetCameraMode("FirstPerson"); break; case 2: _player?.SetCameraMode("FreeLook"); break; default: _player?.ResetCamera(); break; } };
        view.Text = "Ver";
        row.AddChild(view);
        ToolButton(row, "photo", "Foto", "Capturar a tela sem a interface", TakeSnapshot);
        ToolButton(row, "hammer", "Construir", "Ferramentas de construção (B)", () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true));
        _peopleButton = ToolButton(row, "people", "Pessoas", "Buscar pessoas, amigos e grupos", () => _people?.Toggle(), toggle: true);
        ToolButton(row, "pin", "Mapa", "Mapa, busca e destinos", () => _world.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle());
        _inventoryButton = ToolButton(row, "bag", "Inventário", "Seu inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(), toggle: true, orange: true);
        _miniMapButton = ToolButton(row, "map", "Mini-mapa", "Mostrar ou ocultar o mini-mapa", () => ToggleWindow(_miniMapWindow), toggle: true);
        _miniMapButton.ButtonPressed = true;
    }

    private Button _peopleButton = null!;
    private Button _inventoryButton = null!;

    /// Toolbar button: icon and label. A toggle button looks "selected" (teal, or orange for the inventory) while its window is open.
    private static Button ToolButton(HBoxContainer row, string icon, string text, string tooltip, Action? action, bool toggle = false, bool orange = false)
    {
        var button = new Button { Text = text, TooltipText = tooltip, ToggleMode = toggle, Icon = UiStyle.Icon(icon, UiStyle.Text, 22), CustomMinimumSize = new Vector2(0, ToolbarButtonHeight), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        StyleTool(button, orange ? UiStyle.ButtonKind.Secondary : UiStyle.ButtonKind.ToolSelected, selectedLook: toggle);
        if (action is not null) button.Pressed += action;
        row.AddChild(button);
        return button;
    }

    private static void StyleTool(Button button, UiStyle.ButtonKind selected, bool selectedLook)
    {
        const float marginX = 16, marginY = 6;
        button.AddThemeStyleboxOverride("normal", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Normal, marginX, marginY, 12));
        button.AddThemeStyleboxOverride("hover", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Hover, marginX, marginY, 12));
        var pressedKind = selectedLook ? selected : UiStyle.ButtonKind.Tool;
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(pressedKind, selectedLook ? UiStyle.ButtonState.Normal : UiStyle.ButtonState.Pressed, marginX, marginY, 12));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(pressedKind, selectedLook ? UiStyle.ButtonState.Hover : UiStyle.ButtonState.Pressed, marginX, marginY, 12));
        button.AddThemeStyleboxOverride("disabled", UiStyle.ButtonBox(UiStyle.ButtonKind.Tool, UiStyle.ButtonState.Disabled, marginX, marginY, 12));
        button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.AddThemeFontOverride("font", UiStyle.Medium); button.AddThemeFontSizeOverride("font_size", 17);
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" })
            button.AddThemeColorOverride(color, Colors.White);
        button.AddThemeConstantOverride("icon_max_width", 22); button.AddThemeConstantOverride("h_separation", 10);
    }

    private static string FormatCoins(long coins) => coins.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}

/// The small coloured dot of the connection state, with a soft glow.
public partial class StatusDot : Control
{
    private Color _color = UiStyle.Danger;
    public Color Color { get => _color; set { if (_color == value) return; _color = value; QueueRedraw(); } }
    public override void _Draw()
    {
        var center = Size / 2; var radius = Mathf.Min(Size.X, Size.Y) / 2f - 1f;
        DrawCircle(center, radius + 2f, new Color(_color, .22f));
        DrawCircle(center, radius, _color);
    }
}

/// The gold coin shown next to the balance.
public partial class CoinIcon : Control
{
    public override void _Draw()
    {
        var center = Size / 2; var radius = Mathf.Min(Size.X, Size.Y) / 2f - .5f;
        DrawCircle(center, radius, new Color("#C98712"));
        DrawCircle(center, radius - 1.5f, UiStyle.Coin);
        DrawCircle(center, radius - 3.5f, new Color("#F9CF72"));
        DrawArc(center, radius - 3.5f, 0, Mathf.Tau, 24, new Color("#C98712"), 1f);
        var font = UiStyle.Bold; var size = (int)(radius * 1.35f);
        var text = "$"; var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawString(font, new Vector2(center.X - width / 2f, center.Y + size * .35f), text, HorizontalAlignment.Left, -1, size, new Color("#A86A0C"));
    }
}

public partial class ViewerUI
{
    /// Opens a few windows with sample content so the look of the interface can be captured and compared with the design references.
    private ContextInteractionManager? _context;
    public RadialMenu? Radial => _context?.Menu;
    public ContextInteractionManager? ContextManager => _context;

    /// The right-click menu: a radial circle around the cursor whose slices depend on what was clicked.
    private void BuildRadial()
    {
        var menu = new RadialMenu(); _root.AddChild(menu);
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        var people = new PersonActions { Name = "PersonActions" }; AddChild(people); people.Setup(_root); people.Said += text => Toast(text);
        var services = new ContextServices
        {
            AddFriend = people.AddFriend, Message = people.AskMessage, Block = people.Block, Report = people.AskReport,
            ToggleInventory = () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle(),
            ToggleGestures = ToggleGestures,
            TogglePeople = () => _people?.Toggle(),
            OpenSettings = () => _world.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle(),
            SearchPerson = name => _people?.SearchFor(name),
            Tell = text => Toast(text),
            WalkTo = point => _player?.WalkTo(point),
            Gesture = name => _player?.Gesture(name),
            Sit = seat => _player?.SitOn(seat),
            Touch = id => _network.TouchObject(id),
            EditObject = (id, mode) => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.EditObject(id, mode),
            ObjectCommand = (_, command) => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.RunCommand(command),
            StartBuilding = () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.SetActive(true),
            StartPlacing = () => _world.GetNodeOrNull<BuildEditor>("BuildEditor")?.StartPlacing(),
            AboutLand = point => _world.GetNodeOrNull<LandManager>("LandManager")?.AboutAt(point),
            SelectLand = () => _world.GetNodeOrNull<LandManager>("LandManager")?.SetActive(true),
            EnterVehicle = id => _network.EnterVehicle(id),
            ExitVehicle = () => _network.ExitVehicle(),
            IsDriving = id => _network.Vehicles.DrivenBy(_network.OwnPeerId)?.VehicleId == id,
            Owns = view => session is null || session.UserId == Guid.Empty || view.State.OwnerUserId == session.UserId
        };
        _context = new ContextInteractionManager { Name = "ContextInteraction" }; AddChild(_context);
        _context.Setup(_world, _player, menu, services);
        if (_player is not null) _player.ContextRequested += position => { if (!ToolsOwnTheMouse()) _context.OpenAt(position); };
    }

    /// While the build or terrain tools are active a right click belongs to them (the editing camera), not to the menu.
    private bool ToolsOwnTheMouse() => (_world.GetNodeOrNull<BuildEditor>("BuildEditor")?.EditCameraActive ?? false) || (_world.GetNodeOrNull<TerrainEditor>("TerrainEditor")?.Active ?? false);

    private static string LandDemo()
    {
        static string Runs(int cx0, int cz0, int cx1, int cz1) => "[" + string.Join(",", Enumerable.Range(cz0, cz1 - cz0 + 1).Select(z => $"[{z},{cx0},{cx1}]")) + "]";
        string Parcel(string name, string owner, string state, bool mine, int cx0, int cz0, int cx1, int cz1, int objects, string extra = "") =>
            $"{{\"id\":\"{Guid.NewGuid()}\",\"name\":\"{name}\",\"description\":\"Terreno de demonstração.\",\"ownerName\":\"{owner}\",\"mine\":{(mine ? "true" : "false")},\"state\":\"{state}\",\"area\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1) * 16},\"cells\":{(cx1 - cx0 + 1) * (cz1 - cz0 + 1)},\"maxObjects\":100,\"maxCost\":2000,\"objects\":{objects},\"cost\":{objects * 24},\"revision\":1,\"runs\":{Runs(cx0, cz0, cx1, cz1)}{extra}}}";
        return "{\"size\":256,\"version\":1,\"parcels\":[" + string.Join(",", Parcel("Praça", "Marina Costa", "own", true, 0, 0, 6, 5, 37, ",\"arrivalX\":12.0,\"arrivalZ\":10.0,\"arrivalYaw\":90,\"teleportMode\":\"forced\",\"environment\":\"{\\\"hour\\\":19.5,\\\"weather\\\":\\\"cloudy\\\",\\\"light\\\":0.8}\",\"media\":\"{\\\"musicUrl\\\":\\\"https://cdn.exemplo.com/lofi.mp3\\\",\\\"volume\\\":0.5,\\\"autoplay\\\":true,\\\"name\\\":\\\"Lofi da praça\\\"}\""), Parcel("Loja do Rafa", "Rafa Lima", "sale", false, 7, 0, 11, 5, 12, ",\"salePrice\":2500,\"saleIncludesObjects\":true,\"saleDescription\":\"Esquina movimentada, perto do píer.\""), Parcel("Pier", "Ilha", "public", false, 0, 6, 11, 8, 3), Parcel("Cofre", "Lua Fox", "restricted", false, 12, 0, 15, 8, 0)) + "]}";
    }

    public void PreviewForTesting(string view = "people")
    {
        _joinWindow.Visible = false;
        _moveWindow.Visible = view == "people"; _moveButton.SetPressedNoSignal(view == "people");
        if (view == "inventory" && _world.GetNodeOrNull<InventoryPanel>("InventoryPanel") is { } inventory)
        {
            inventory.Toggle();
            var a = Guid.NewGuid(); var b = Guid.NewGuid();
            inventory.PopulateForTesting($"[{{\"id\":\"{a}\",\"name\":\"Construções\"}},{{\"id\":\"{b}\",\"name\":\"Móveis\"}}]",
                "[" + string.Join(",", new[] { ("Casa Tropical", "models", "model/gltf-binary", 52428800), ("Quiosque", "models", "model/gltf-binary", 31457280), ("Palmeira", "models", "model/gltf-binary", 12582912), ("Textura Areia.jpg", "textures", "image/jpeg", 4194304), ("Textura Água.png", "textures", "image/png", 3145728), ("Som Ondas.ogg", "sounds", "audio/ogg", 6291456) }.Select(x => $"{{\"itemId\":\"{Guid.NewGuid()}\",\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"{x.Item1}\",\"category\":\"{x.Item2}\",\"mimeType\":\"{x.Item3}\",\"contentHash\":\"p{x.Item1}\",\"size\":{x.Item4},\"version\":1,\"favorite\":false,\"deleted\":false,\"folderId\":null}}")) + "]");
        }
        if (view == "terrain" && _world.GetNodeOrNull<TerrainEditor>("TerrainEditor") is { } terrainEditor && _world.GetNodeOrNull<IslandTerrain>("IslandTerrain") is { } ground)
        {
            for (var i = 0; i < 8; i++) { var x = -28f + (i % 4) * 18f; var z = -10f + (i / 4) * 18f; ground.ApplyEdit(LifeVerse.Shared.Terrain.TerrainTool.Raise, x, z, 7, .35f, 0, 0); ground.ApplyEdit(LifeVerse.Shared.Terrain.TerrainTool.Paint, x, z, 8, 1f, i, 0); }
            terrainEditor.SetActive(true); terrainEditor.SelectToolForTesting(LifeVerse.Shared.Terrain.TerrainTool.Paint); terrainEditor.SelectLayerForTesting(5);
            var overview = new Camera3D { Fov = 55, Transform = new Transform3D(Basis.Identity, new Vector3(-10, 60, 40)).LookingAt(new Vector3(-10, 0, 0), Vector3.Back) }; _world.AddChild(overview); overview.MakeCurrent();
        }
        if (view is "land" or "land-trade" or "land-admin" or "land-style" or "land-map" && _world.GetNodeOrNull<LandManager>("LandManager") is { } land)
        {
            land.Apply(LandDemo());
            land.SetActive(true);
            land.SelectCells(LifeVerse.Shared.Land.LandMath.Between(8, 8, 27, 19), true);
            if (view == "land-admin") { land.AboutAt(new Vector3(8, 0, 8)); land.ShowPageForTesting(3); }
            if (view == "land-style") { land.AboutAt(new Vector3(8, 0, 8)); land.ShowStyleForTesting(); }
            if (view == "land-map") { land.SetActive(false); land.ClearSelection(); var map = _world.GetNodeOrNull<LandMapWindow>("LandMapWindow"); map?.Open(); map?.Pick(36, 20); }
            if (view == "land-trade") { land.AboutAt(new Vector3(36, 0, 8)); land.ShowTradeForTesting(); land.OpenBuyForTesting(); }
            var overview = new Camera3D { Fov = 55, Transform = new Transform3D(Basis.Identity, new Vector3(30, 80, 50)).LookingAt(new Vector3(30, 0, 14), Vector3.Back) }; _world.AddChild(overview); overview.MakeCurrent();
        }
        if (view.StartsWith("build", StringComparison.Ordinal) && _world.GetNodeOrNull<BuildEditor>("BuildEditor") is { } editor)
        {
            var state = new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", Name = "Torre", Description = "Torre de tijolos torcida e vazada, construção demonstrativa do LifeVerse.", GeometryJson = "{\"hollow\":0.45,\"twist\":70,\"taperX\":0.35,\"taperY\":0.35,\"cutStart\":0.0,\"cutEnd\":0.8}", PositionX = 0, PositionY = 1, PositionZ = -4, ScaleX = 2, ScaleY = 2, ScaleZ = 2, Revision = 1, ContentJson = "[{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Campainha\",\"category\":\"sounds\"},{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Madeira\",\"category\":\"textures\"},{\"assetId\":\"" + Guid.NewGuid() + "\",\"name\":\"Porta\",\"category\":\"models\"}]", MaterialJson = "{\"color\":\"#FFFFFF\",\"builtin\":\"bricks\",\"uvRepeatX\":2,\"uvRepeatY\":3,\"uvRotation\":0,\"light\":{\"color\":\"#FFD9A0\",\"energy\":1.5,\"range\":8}}" };
            editor.SetActive(true); editor.LoadStatesForTesting([state]); editor.SelectIds([state.Id]);
            editor.SetPackageNameForTesting("Torre Torcida");
            editor.ShowLimits("{\"objects\":1234,\"maxObjects\":5000,\"cost\":52000,\"maxCost\":200000,\"canBuildHere\":true,\"isAdmin\":false,\"parcel\":{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Praça\",\"mine\":true,\"publicBuild\":false,\"objects\":45,\"maxObjects\":100,\"cost\":300,\"maxCost\":2000}}");
            editor.PreviewTutorialForTesting(2);
            var tab = Array.IndexOf(new[] { "geral", "objeto", "textura", "recursos", "conteudo", "alinhar" }, view.Contains('-') ? view[(view.IndexOf('-') + 1)..] : "objeto");
            editor.ShowTabForTesting(Math.Max(tab, 0));
            if (tab > 0) editor.HideTutorialForTesting();
        }
        if (view == "people" && _people is not null)
        {
            _people.RequestSink = (_, _, _) => true;
            _people.Toggle();
            _people.DeliverForTesting("Friends", """[{"id":"11111111-1111-1111-1111-111111111111","username":"RafaLima","online":true,"location":"Ilha Esmeralda"},{"id":"22222222-2222-2222-2222-222222222222","username":"LuaFox","online":false,"lastSeen":"2026-11-03T08:00:00Z"}]""");
            _people.DeliverForTesting("Requests", """[{"userId":"33333333-3333-3333-3333-333333333333","username":"GabrielXY","direction":"in"}]""");
        }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _network.OnChatReceived(1, "RafaLima", "Oi, Marina! Tudo bem?", now);
        _network.OnChatReceived(2, "MarinaCosta", "Tudo ótimo! E por aí?", now);
        _network.OnChatReceived(3, "LuaFox", "Gente, vocês viram a nova área da ilha? Ficou incrível!!!", now);
        _balance.Text = FormatCoins(3450);
        Toast("Nova mensagem: oi!");
    }
}

public enum ToastKind { Info, Success, Warning, Error }

public partial class ViewerUI
{
    private void Toast(string text) => Toast(text, ToastKind.Info);

    /// A notification at the top of the screen: coloured outline, icon, divider, message and a close button (client/Assets/ui/08).
    private void Toast(string text, ToastKind kind)
    {
        var (icon, border, fill) = kind switch
        {
            ToastKind.Success => ("ok", new Color("#67F869"), new Color("#0F3A22")),
            ToastKind.Warning => ("alert", new Color("#FFC55D"), new Color("#35281C")),
            ToastKind.Error => ("error", new Color("#F87576"), new Color("#462F30")),
            _ => (text.StartsWith("Nova mensagem", StringComparison.Ordinal) ? "mail" : "info", new Color("#0BF6F6"), new Color("#0D2A2B"))
        };
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(480, 56) };
        var style = UiStyle.Box(new Color(fill, .94f), border, 14, 2, 16, 10, 16);
        style.ShadowColor = new Color(border, .28f);
        panel.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); panel.AddChild(row);
        row.AddChild(UiStyle.IconRect(icon, border, 28));
        row.AddChild(new ColorRect { Color = new Color(1, 1, 1, .28f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", 18); label.AddThemeFontOverride("font", UiStyle.Medium);
        row.AddChild(label);
        var close = new Button { Icon = UiStyle.Icon("close", Colors.White, 22), Flat = true, FocusMode = Control.FocusModeEnum.None, ExpandIcon = false, CustomMinimumSize = new Vector2(32, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        close.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); close.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(1, 1, 1, .12f), new Color(0, 0, 0, 0), 8, 0, 0, 0)); close.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        close.Pressed += panel.QueueFree;
        row.AddChild(close);
        _toasts.AddChild(panel);
        var tween = CreateTween();
        tween.TweenInterval(4.0); tween.TweenProperty(panel, "modulate:a", 0f, .6); tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(panel)) panel.QueueFree(); }));
    }
}

public partial class ViewerUI
{
    // ---- mini-map window (client/Assets/ui/06) ---------------------------------------------------------------------

    private void BuildMiniMap()
    {
        _miniMapWindow = new FloatingWindow("Mini-mapa", icon: "target", expandable: true);
        _miniMapWindow.Place(1, 0, -352, 56, -12, 420);
        // The map is clipped to a rounded frame; the frame's own drawing is the mask.
        var frame = new Panel { CustomMinimumSize = new Vector2(300, 300), ClipChildren = CanvasItem.ClipChildrenMode.Only, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color("#133F73"), new Color(0, 0, 0, 0), 16, 0, 0, 0));
        var map = new MiniMap(); map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(map);
        map.BuildOverlay(frame);
        _miniMapWindow.Content.AddChild(frame);
        if (_terrain is not null) map.Bind(_terrain, _player, _network);
        _miniMapWindow.CloseRequested += () => _miniMapButton.ButtonPressed = false;
        _miniMapWindow.ExpandToggled += large => { frame.CustomMinimumSize = large ? new Vector2(520, 520) : new Vector2(300, 300); _miniMapWindow.ResetSize(); };
        _root.AddChild(_miniMapWindow);
    }

    // ---- move window (client/Assets/ui/07) -------------------------------------------------------------------------

    private void BuildMoveWindow()
    {
        _moveWindow = new FloatingWindow("Mover", icon: "run", minimizable: true) { Visible = false };
        _moveWindow.Place(.5f, 1, -250, -300, 250, -100);
        _moveWindow.CloseRequested += () => _moveButton.ButtonPressed = false;
        var body = new HBoxContainer(); body.AddThemeConstantOverride("separation", 18);
        _moveWindow.Content.AddChild(body);

        // Direction pad: holding a button holds the matching movement key.
        var pad = new GridContainer { Columns = 3 }; pad.AddThemeConstantOverride("h_separation", 4); pad.AddThemeConstantOverride("v_separation", 4);
        void Cell() => pad.AddChild(new Control { CustomMinimumSize = new Vector2(72, 72) });
        Cell(); pad.AddChild(PadButton("up", "Andar para frente", "move_forward")); Cell();
        pad.AddChild(PadButton("left", "Girar para a esquerda", "move_left")); Cell(); pad.AddChild(PadButton("right", "Girar para a direita", "move_right"));
        Cell(); pad.AddChild(PadButton("down", "Andar para trás", "move_back")); Cell();
        body.AddChild(pad);
        body.AddChild(new ColorRect { Color = UiStyle.Divider, CustomMinimumSize = new Vector2(1, 0), MouseFilter = Control.MouseFilterEnum.Ignore });

        var modes = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; modes.AddThemeConstantOverride("separation", 12);
        _walk = new Button { ToggleMode = true };   // kept for the status updates; the design has no separate "walk" button
        _run = ModeButton(modes, "run-fast", "Correr", true, () => { if (_player is null) return; _player.AlwaysRun = !_player.AlwaysRun; if (_player.IsFlying) _player.RequestFlightToggle(); });
        _fly = ModeButton(modes, "wing", "Voar", false, () => _player?.RequestFlightToggle());
        body.AddChild(modes);
        _root.AddChild(_moveWindow);
    }


    private static Button PadButton(string direction, string tooltip, string action)
    {
        var button = new PadArrowButton(direction) { TooltipText = tooltip, CustomMinimumSize = new Vector2(72, 72), FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, UiStyle.ButtonKind.Tool, 0, 0, 16);
        button.ButtonDown += () => Input.ActionPress(action);
        button.ButtonUp += () => Input.ActionRelease(action);
        button.TreeExiting += () => Input.ActionRelease(action);
        return button;
    }

    private static Button ModeButton(VBoxContainer column, string icon, string text, bool orange, Action action)
    {
        var button = new Button { Text = text, ToggleMode = true, Icon = UiStyle.Icon(icon, orange ? UiStyle.Orange : UiStyle.Accent, 34), CustomMinimumSize = new Vector2(236, 66), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        var kind = orange ? UiStyle.ButtonKind.Secondary : UiStyle.ButtonKind.Primary;
        UiStyle.StyleButton(button, kind, 18, 6, 14);
        button.AddThemeStyleboxOverride("pressed", UiStyle.ButtonBox(kind, UiStyle.ButtonState.Hover, 18, 6, 14));
        button.AddThemeStyleboxOverride("hover_pressed", UiStyle.ButtonBox(kind, UiStyle.ButtonState.Hover, 18, 6, 14));
        button.AddThemeFontSizeOverride("font_size", 24);
        foreach (var color in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color" }) button.AddThemeColorOverride(color, Colors.White);
        button.AddThemeConstantOverride("icon_max_width", 34); button.AddThemeConstantOverride("h_separation", 16);
        button.Pressed += action;
        column.AddChild(button);
        return button;
    }
}

public partial class ViewerUI
{
    // ---- chat window (client/Assets/ui/05) -------------------------------------------------------------------------

    private static readonly Color[] NameColors = [new("#16E0D0"), new("#F4B045"), new("#F0689B"), new("#5BE37A"), new("#B38CFF"), new("#F2D24B")];

    private void BuildChatWindow()
    {
        var panel = new PanelContainer { Name = "Conversa_local" };
        panel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFill, .80f), UiStyle.BorderBright, 16, 1, 6, 6, 14));
        panel.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        panel.AnchorTop = panel.AnchorBottom = 1;
        panel.OffsetLeft = 12; panel.OffsetRight = 612; panel.OffsetBottom = -(10 + ToolbarButtonHeight + 16 + 10); panel.OffsetTop = panel.OffsetBottom - 250;
        panel.GrowVertical = Control.GrowDirection.Begin;
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 6); panel.AddChild(column);

        var tabs = new UiTabs();
        tabs.AddTab("Chat Local"); tabs.AddTab("Conversas"); tabs.AddTab("Nova conversa", "plus", iconOnly: true);
        var header = new HBoxContainer(); header.AddThemeConstantOverride("separation", 6);
        header.AddChild(tabs);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        header.AddChild(ChatChromeButton("settings", "Preferências de conversa", () => _world.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle()));
        header.AddChild(ChatChromeButton("expand", "Aumentar ou diminuir a conversa", () => { panel.OffsetTop = Mathf.Abs(panel.OffsetBottom - panel.OffsetTop - 250) < 1 ? panel.OffsetBottom - 480 : panel.OffsetBottom - 250; }));
        column.AddChild(header);
        column.AddChild(new ColorRect { Color = new Color(UiStyle.BorderBright, .35f), CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });

        _chatLog = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, FitContent = false };
        _chatLog.AddThemeFontSizeOverride("normal_font_size", 17); _chatLog.AddThemeFontSizeOverride("bold_font_size", 17);
        _chatLog.AddThemeFontOverride("normal_font", UiStyle.Regular); _chatLog.AddThemeFontOverride("bold_font", UiStyle.Bold);
        _chatLog.AddThemeConstantOverride("line_separation", 4);
        column.AddChild(_chatLog);

        _conversations = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        _conversations.AddThemeConstantOverride("separation", 10);
        var none = new Label { Text = "Nenhuma conversa privada aberta.", Modulate = new Color(1, 1, 1, .7f) };
        var open = new Button { Text = "Abrir mensagens privadas", Icon = UiStyle.Icon("chat", Colors.White, 20), ExpandIcon = false };
        UiStyle.StyleButton(open, UiStyle.ButtonKind.Primary, 16, 8, 12);
        open.Pressed += () => _world.GetNodeOrNull<SocialPanel>("SocialPanel")?.Toggle();
        _conversations.AddChild(none); _conversations.AddChild(open);
        column.AddChild(_conversations);
        tabs.TabChanged += index =>
        {
            if (index == 2) { tabs.Select(1, notify: false); _world.GetNodeOrNull<SocialPanel>("SocialPanel")?.Toggle(); index = 1; }
            _chatLog.Visible = index == 0; _conversations.Visible = index == 1;
        };
        _chatWindow = panel;
        _root.AddChild(panel);
    }

    private static Button ChatChromeButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, Colors.White, 24), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, CustomMinimumSize = new Vector2(40, 40), ExpandIcon = false };
        var hover = UiStyle.Box(new Color(1, 1, 1, .10f), new Color(0, 0, 0, 0), 8, 0, 0, 0);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += action;
        return button;
    }

    private Color ColorForName(string name) => NameColors[(int)((uint)name.GetHashCode() % NameColors.Length)];

    private void OnChat(string line)
    {
        var close = line.IndexOf("] ", StringComparison.Ordinal);
        var colon = close >= 0 ? line.IndexOf(": ", close, StringComparison.Ordinal) : -1;
        if (close < 0 || colon < 0) { AppendLine(Esc(line)); return; }
        var name = line[(close + 2)..colon];
        AppendLine($"[color=#A9B3BD]{Esc(line[..(close + 1)])}[/color] [b][color=#{ColorForName(name).ToHtml(false)}]{Esc(name)}:[/color][/b] {Esc(line[(colon + 2)..])}");
    }
}

/// A direction-pad button that draws a solid white triangle pointing the way.
public partial class PadArrowButton(string direction) : Button
{
    public override void _Draw()
    {
        var c = Size / 2f; var r = Mathf.Min(Size.X, Size.Y) * .17f;
        var points = direction switch
        {
            "up" => new Vector2[] { c + new Vector2(0, -r), c + new Vector2(r * 1.15f, r * .8f), c + new Vector2(-r * 1.15f, r * .8f) },
            "down" => new Vector2[] { c + new Vector2(0, r), c + new Vector2(r * 1.15f, -r * .8f), c + new Vector2(-r * 1.15f, -r * .8f) },
            "left" => new Vector2[] { c + new Vector2(-r, 0), c + new Vector2(r * .8f, r * 1.15f), c + new Vector2(r * .8f, -r * 1.15f) },
            _ => new Vector2[] { c + new Vector2(r, 0), c + new Vector2(-r * .8f, r * 1.15f), c + new Vector2(-r * .8f, -r * 1.15f) }
        };
        DrawColoredPolygon(points, Disabled ? UiStyle.TextDisabled : Colors.White);
    }
}
