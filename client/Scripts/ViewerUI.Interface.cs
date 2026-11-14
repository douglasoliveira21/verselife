using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts.Interface;

namespace LifeVerse.Client.Scripts;

/// The bars of the redesigned interface: the location bar (under the menu), the customizable bottom toolbar and the collapsible shortcut bar on the left.
public partial class ViewerUI
{
    public const int LocationBarHeight = 34;

    // ---- location bar ----------------------------------------------------------------------------------------------

    private Button _backButton = null!, _forwardButton = null!, _favoriteButton = null!;
    private MenuButton _favoritesMenu = null!;
    private PanelContainer _locationBar = null!;
    private readonly List<Guid> _navHistory = [];
    private int _navIndex = -1;
    private Guid _navLast;
    private bool _navBusy;

    public PanelContainer LocationBarForTesting => _locationBar;
    public IReadOnlyList<Guid> NavigationHistoryForTesting => _navHistory;
    public int NavigationIndexForTesting => _navIndex;

    private void BuildLocationBar()
    {
        var bar = _locationBar = new PanelContainer { Name = "LocationBarPanel", CustomMinimumSize = new Vector2(0, LocationBarHeight) };
        var style = UiStyle.Box(new Color(UiStyle.PanelFill, .80f), UiStyle.BorderBright, 0, 0, 12, 0);
        style.BorderWidthBottom = 1; style.BorderColor = new Color(UiStyle.BorderBright, .6f);
        bar.AddThemeStyleboxOverride("panel", style);
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        bar.OffsetTop = TopBarHeight; bar.OffsetBottom = TopBarHeight + LocationBarHeight;
        _root.AddChild(bar);
        var row = new HBoxContainer { Name = "Row" }; row.AddThemeConstantOverride("separation", 4); bar.AddChild(row);

        _backButton = LocationButton("chevron-left", "Voltar para a ilha anterior", () => Navigate(-1)); row.AddChild(_backButton);
        _forwardButton = LocationButton("chevron-right", "Avançar para a próxima ilha", () => Navigate(1)); row.AddChild(_forwardButton);
        row.AddChild(LocationButton("home", "Ir para a Ilha de Boas-vindas", () => GoToWelcome()));
        row.AddChild(Divider(20));

        row.AddChild(UiStyle.IconRect("pin-filled", UiStyle.Accent, 18));
        _islandName = new Label { Text = "Sem ilha", TooltipText = "Ilha atual. Clique para gerenciar a ilha.", MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, VerticalAlignment = VerticalAlignment.Center, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(80, 0) };
        _islandName.AddThemeFontSizeOverride("font_size", 16); _islandName.AddThemeFontOverride("font", UiStyle.Medium);
        _islandName.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _network.CurrentIslandId != Guid.Empty)
                _world.GetNodeOrNull<IslandManagement.IslandManagementWindow>("IslandManagementWindow")?.Toggle();
        };
        row.AddChild(_islandName);
        _coords = new Label { Text = "", TooltipText = "Sua posição (x, z, altura)", VerticalAlignment = VerticalAlignment.Center };
        _coords.AddThemeFontSizeOverride("font_size", 15); _coords.AddThemeColorOverride("font_color", UiStyle.TextMuted);
        row.AddChild(_coords);

        _favoriteButton = LocationButton("star", "Favoritar esta ilha", ToggleFavorite); row.AddChild(_favoriteButton);
        _favoritesMenu = new MenuButton { Flat = true, FocusMode = Control.FocusModeEnum.None, Icon = UiStyle.Icon("caret-down", UiStyle.Text, 12), ExpandIcon = false, TooltipText = "Ilhas favoritas", CustomMinimumSize = new Vector2(26, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        StyleFlatMenu(_favoritesMenu, 14);
        _favoritesMenu.AboutToPopup += RebuildFavoritesMenu;
        _favoritesMenu.GetPopup().IdPressed += id => { var at = (int)id; if (at >= 0 && at < _interface.Favorites.Count) JoinIslandById(_interface.Favorites[at].Id); };
        row.AddChild(_favoritesMenu);

        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _indicator = new StatusDot { CustomMinimumSize = new Vector2(12, 12), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Estado da conexão" };
        row.AddChild(_indicator);
        var state = new MenuButton { Text = "Desconectado", Flat = true, FocusMode = Control.FocusModeEnum.None, Icon = UiStyle.Icon("caret-down", UiStyle.Text, 12), IconAlignment = HorizontalAlignment.Right, ExpandIcon = false, TooltipText = "Conexão com a ilha" };
        StyleFlatMenu(state, 15);
        state.GetPopup().AddItem("Sair da ilha", 0);
        state.GetPopup().IdPressed += _ => _network.Disconnect();
        row.AddChild(state);
        _connectionMenu = state;
        UpdateNavButtons(); UpdateFavoriteButton();
    }

    private static Button LocationButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 18), Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = tooltip, CustomMinimumSize = new Vector2(28, 26), ExpandIcon = false, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var hover = UiStyle.Box(new Color(1, 1, 1, .10f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 0, 0);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("disabled", new StyleBoxEmpty());
        button.AddThemeColorOverride("icon_disabled_color", new Color(1, 1, 1, .25f)); button.AddThemeConstantOverride("icon_max_width", 18);
        button.Pressed += action;
        return button;
    }

    /// <summary>The island the person is on has changed: the history of the "back" and "forward" buttons follows it.</summary>
    public void NoteIsland(Guid id)
    {
        if (id == Guid.Empty || id == _navLast) return;
        _navLast = id;
        if (_navBusy && _navIndex >= 0 && _navIndex < _navHistory.Count && _navHistory[_navIndex] == id) _navBusy = false;      // arrived where back/forward was going
        else
        {
            _navBusy = false;
            if (_navIndex < _navHistory.Count - 1) _navHistory.RemoveRange(_navIndex + 1, _navHistory.Count - _navIndex - 1);
            _navHistory.Add(id); if (_navHistory.Count > 30) _navHistory.RemoveAt(0);
            _navIndex = _navHistory.Count - 1;
        }
        UpdateNavButtons(); UpdateFavoriteButton();
    }

    private void Navigate(int step)
    {
        var to = _navIndex + step; if (to < 0 || to >= _navHistory.Count) return;
        _navIndex = to; _navBusy = true; UpdateNavButtons();
        JoinIslandById(_navHistory[to]);
    }

    private void UpdateNavButtons()
    {
        if (_backButton is null) return;
        _backButton.Disabled = _navIndex <= 0; _forwardButton.Disabled = _navIndex < 0 || _navIndex >= _navHistory.Count - 1;
    }

    private string CurrentIslandTitle() => _islandNames.TryGetValue(_network.CurrentIslandId, out var title) ? title : "Ilha";

    private bool IsFavorite(Guid id) => _interface.Favorites.Any(f => f.Id == id);

    private void ToggleFavorite()
    {
        var id = _network.CurrentIslandId; if (id == Guid.Empty) { Toast("Entre em uma ilha para favoritá-la."); return; }
        if (IsFavorite(id)) { _interface.Favorites.RemoveAll(f => f.Id == id); Toast("Ilha removida dos favoritos."); }
        else if (_interface.Favorites.Count >= 50) { Toast("Você já tem 50 favoritos: remova algum antes."); return; }
        else { _interface.Favorites.Add((id, CurrentIslandTitle())); Toast("Ilha adicionada aos favoritos."); }
        _interface.Save(); UpdateFavoriteButton();
    }

    private void UpdateFavoriteButton()
    {
        if (_favoriteButton is null) return;
        var on = IsFavorite(_network.CurrentIslandId);
        _favoriteButton.Icon = UiStyle.Icon("star", on ? UiStyle.Coin : UiStyle.TextMuted, 18);
        _favoriteButton.TooltipText = on ? "Tirar esta ilha dos favoritos" : "Favoritar esta ilha";
    }

    private void RebuildFavoritesMenu()
    {
        var popup = _favoritesMenu.GetPopup(); popup.Clear();
        if (_interface.Favorites.Count == 0) { popup.AddItem("Nenhuma ilha favorita ainda", 0); popup.SetItemDisabled(0, true); return; }
        for (var i = 0; i < _interface.Favorites.Count; i++) popup.AddItem(_interface.Favorites[i].Name, i);
    }

    /// <summary>"Conectado · 47 avatares": the connection state with how many avatars are on the island (counting you).</summary>
    private string ConnectionText(bool online, bool connecting)
    {
        if (!online) return connecting ? "Conectando…" : "Desconectado";
        var count = _network.RemoteAvatarCount + 1;
        return $"Conectado · {count} avatar{(count == 1 ? "" : "es")}";
    }

    // ---- bottom toolbar: customization -----------------------------------------------------------------------------

    private HBoxContainer _toolRow = null!;
    private int _toolFirst;
    private MenuButton _toolCustomizer = null!;
    private readonly List<(string Id, string Label, Button Control)> _tools = [];

    public IReadOnlyList<string> ToolbarIdsForTesting => _tools.Select(t => t.Id).ToList();
    public bool ToolbarShownForTesting(string id) => _tools.FirstOrDefault(t => t.Id == id).Control is { } c && c.Visible;
    public bool ToolbarIconsOnlyForTesting => _tools.Count > 0 && _tools.All(t => t.Control.Text.Length == 0);
    public InterfaceSettings InterfaceSettingsForTesting => _interface;
    public void ApplyToolbarForTesting() => ApplyToolbar();
    public void SetToolbarCompactForTesting(bool compact) { _interface.CompactToolbar = compact; ApplyToolbar(); }
    public void ToolbarMoveForTesting(string id, int step) => MoveTool(id, step);
    public void ToolbarShowForTesting(string id, bool show) => ShowTool(id, show);
    public void ToolbarRestoreForTesting() => RestoreToolbar();

    private Button Reg(string id, string label, Button control) { control.SetMeta("label", label); _tools.Add((id, label, control)); return control; }

    private void BuildToolbarCustomizer(HBoxContainer row)
    {
        _toolCustomizer = new MenuButton { Flat = true, FocusMode = Control.FocusModeEnum.None, Icon = UiStyle.Icon("dots", UiStyle.Text, 18), ExpandIcon = false, TooltipText = "Personalizar a barra: ordem, botões e só ícones", CustomMinimumSize = new Vector2(34, ToolbarButtonHeight) };
        StyleFlatMenu(_toolCustomizer, 14); _toolCustomizer.AddThemeConstantOverride("icon_max_width", 18);
        _toolCustomizer.AboutToPopup += RebuildToolbarMenu;
        row.AddChild(_toolCustomizer);
    }

    private void RebuildToolbarMenu()
    {
        var popup = _toolCustomizer.GetPopup(); popup.Clear(true);
        popup.AddCheckItem("Só ícones (modo compacto)", 1000); popup.SetItemChecked(0, _interface.CompactToolbar);
        popup.AddSeparator();
        var order = ToolbarLayout.Order(_tools.Select(t => t.Id).ToList(), _interface.ToolbarOrder);
        var at = 0;
        foreach (var id in order)
        {
            var tool = _tools.First(t => t.Id == id);
            var sub = new PopupMenu { Name = $"Tool_{id}" }; popup.AddChild(sub);
            sub.AddCheckItem("Mostrar na barra", 0); sub.SetItemChecked(0, !_interface.ToolbarHidden.Contains(id));
            sub.AddItem("Mover para a esquerda", 1); sub.AddItem("Mover para a direita", 2);
            sub.IdPressed += choice => { switch ((int)choice) { case 0: ShowTool(id, _interface.ToolbarHidden.Contains(id)); break; case 1: MoveTool(id, -1); break; default: MoveTool(id, 1); break; } };
            popup.AddSubmenuNodeItem(tool.Label + (_interface.ToolbarHidden.Contains(id) ? " (oculto)" : ""), sub); at++;
        }
        popup.AddSeparator();
        popup.AddItem("Restaurar a barra padrão", 1001);
        popup.IdPressed -= OnToolbarMenu; popup.IdPressed += OnToolbarMenu;
    }

    private void OnToolbarMenu(long id)
    {
        if (id == 1000) { _interface.CompactToolbar = !_interface.CompactToolbar; _interface.Save(); ApplyToolbar(); }
        else if (id == 1001) RestoreToolbar();
    }

    private void ShowTool(string id, bool show)
    {
        var all = _tools.Select(t => t.Id).ToList();
        if (show) _interface.ToolbarHidden.Remove(id);
        else if (ToolbarLayout.CanHide(all, _interface.ToolbarHidden, id) && !_interface.ToolbarHidden.Contains(id)) _interface.ToolbarHidden.Add(id);
        else if (!_interface.ToolbarHidden.Contains(id)) { Toast("A barra precisa manter ao menos um botão."); return; }
        _interface.Save(); ApplyToolbar();
    }

    private void MoveTool(string id, int step)
    {
        var order = ToolbarLayout.Order(_tools.Select(t => t.Id).ToList(), _interface.ToolbarOrder);
        var moved = ToolbarLayout.Move(order, id, step);
        _interface.ToolbarOrder.Clear(); _interface.ToolbarOrder.AddRange(moved); _interface.Save(); ApplyToolbar();
    }

    private void RestoreToolbar()
    {
        _interface.ToolbarOrder.Clear(); _interface.ToolbarHidden.Clear(); _interface.CompactToolbar = false; _interface.Save(); ApplyToolbar();
        Toast("Barra de ferramentas restaurada.");
    }

    /// <summary>Order, visibility and labels of the toolbar. With text the bar may not fit a narrow screen: then it turns to icons only by itself.</summary>
    private void ApplyToolbar()
    {
        if (_toolRow is null || _tools.Count == 0) return;
        var order = ToolbarLayout.Order(_tools.Select(t => t.Id).ToList(), _interface.ToolbarOrder);
        var index = _toolFirst;
        foreach (var id in order) { var tool = _tools.First(t => t.Id == id); _toolRow.MoveChild(tool.Control, index++); tool.Control.Visible = !_interface.ToolbarHidden.Contains(id); }
        _toolRow.MoveChild(_toolCustomizer, -1);
        void Labels(bool icons) { foreach (var tool in _tools) { tool.Control.Text = icons ? "" : tool.Label; tool.Control.CustomMinimumSize = new Vector2(icons ? ToolbarButtonHeight + 4 : 0, ToolbarButtonHeight); } }
        var iconsOnly = _interface.CompactToolbar || _interface.Mode == "compact";
        Labels(iconsOnly);
        if (!iconsOnly && _toolRow.GetCombinedMinimumSize().X + 24 > _root.Size.X) Labels(true);
    }

    // ---- shortcut bar (left edge) ----------------------------------------------------------------------------------

    private PanelContainer _sideBar = null!;
    private VBoxContainer _sideButtons = null!;
    private Button _sideToggle = null!;
    public PanelContainer SideBarForTesting => _sideBar;
    public bool SideBarOpenForTesting => _sideButtons.Visible;
    public void ToggleSideBarForTesting() => SetSideBarOpen(!_sideButtons.Visible);

    private void BuildSideBar()
    {
        var bar = _sideBar = new PanelContainer { Name = "SideToolbarPanel" };
        bar.AddThemeStyleboxOverride("panel", ViewerTheme.BarBox());
        bar.AnchorLeft = bar.AnchorRight = 0; bar.AnchorTop = bar.AnchorBottom = .5f;
        bar.OffsetLeft = 8; bar.GrowVertical = Control.GrowDirection.Both; bar.GrowHorizontal = Control.GrowDirection.End;
        _root.AddChild(bar);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 6); bar.AddChild(column);
        _sideToggle = LocationButton("chevron-left", "Recolher a barra de atalhos", () => SetSideBarOpen(!_sideButtons.Visible));
        _sideToggle.CustomMinimumSize = new Vector2(44, 26);
        column.AddChild(_sideToggle);
        _sideButtons = new VBoxContainer(); _sideButtons.AddThemeConstantOverride("separation", 6); column.AddChild(_sideButtons);
        void Add(string icon, string tip, Action action)
        {
            var b = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 22), TooltipText = tip, CustomMinimumSize = new Vector2(44, 44), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
            StyleTool(b, UiStyle.ButtonKind.ToolSelected, false); b.AddThemeConstantOverride("icon_max_width", 22); b.Pressed += action; _sideButtons.AddChild(b);
        }
        Add("bag", "Inventário", () => _world.GetNodeOrNull<InventoryPanel>("InventoryPanel")?.Toggle());
        Add("people", "Pessoas, amigos e grupos", () => _people?.Toggle());
        Add("pin", "Mapa e destinos", () => _world.GetNodeOrNull<DiscoveryPanel>("DiscoveryPanel")?.Toggle());
        Add("hammer", "Construir (B)", () => BuildHost.SetActive(_world, true));
        Add("photo", "Tirar foto", TakeSnapshot);
        Add("settings", "Configurações", () => _world.GetNodeOrNull<SettingsPanel>("SettingsPanel")?.Toggle());
        SetSideBarOpen(_interface.SideToolbarOpen, save: false);
    }

    private void SetSideBarOpen(bool open, bool save = true)
    {
        _sideButtons.Visible = open;
        _sideToggle.Icon = UiStyle.Icon(open ? "chevron-left" : "chevron-right", UiStyle.Text, 18);
        _sideToggle.TooltipText = open ? "Recolher a barra de atalhos" : "Abrir a barra de atalhos";
        _sideBar.ResetSize();
        if (save) { _interface.SideToolbarOpen = open; _interface.Save(); }
    }
}

public partial class ViewerUI
{
    /// <summary>People window: who is near (from the island), who was seen recently (kept per account) and the Perfil / IM / Ir até actions by name.</summary>
    private void WirePeople()
    {
        if (_people is null) return;
        _people.NearbyProvider = () => _player is null ? [] : _network.RemoteAvatars.Select(a => (a.Name, a.Position.DistanceTo(_player.GlobalPosition)));
        _people.SetRecent(_interface.RecentPeople);
        _people.RecentChanged += names => { _interface.RecentPeople.Clear(); _interface.RecentPeople.AddRange(names); _interface.Save(); };
        _people.ProfileByNameRequested += name => _ = _world.GetNodeOrNull<ProfileWindow>("ProfileWindow")?.OpenByNameAsync(name);
        _people.MessageByNameRequested += name => Toast($"Mensagem privada para {name}: abra a aba IM na conversa.");
        _people.GoToRequested += name =>
        {
            var found = _network.RemoteAvatars.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)).Select(a => ((Vector3, bool))(a.Position, true)).FirstOrDefault();
            if (found.Item2 && _player is not null) _player.WalkTo(found.Item1); else Toast($"{name} não está por perto.");
        };
    }
}

public partial class ViewerUI
{
    // ---- display modes: completo / compacto / imersivo ------------------------------------------------------------

    public static readonly string[] InterfaceModes = ["full", "compact", "immersive"];
    public static readonly string[] InterfaceModeLabels = ["Completo", "Compacto", "Imersivo"];
    private bool _immersiveReveal, _revealForChat;
    private bool _miniMapWasVisible;

    public string InterfaceModeForTesting => _interface.Mode;
    public bool ImmersiveRevealForTesting => _immersiveReveal;
    public void SetInterfaceModeForTesting(string mode) => SetInterfaceMode(mode);
    public void ToggleImmersiveRevealForTesting() => ToggleImmersiveReveal();
    public bool BarVisibleForTesting(string name) => name switch { "top" => _root.GetNode<Control>("MenuBarPanel").Visible, "location" => _locationBar.Visible, "side" => _sideBar.Visible, "bottom" => _root.GetNode<Control>("ToolbarPanel").Visible, _ => false };

    /// <summary>
    /// Completo: everything. Compacto: only the menu bar and the toolbar, with icons. Imersivo: nothing but the world (toasts and the chat button stay; F10 shows the interface for a while).
    /// </summary>
    public void SetInterfaceMode(string mode, bool save = true)
    {
        if (!InterfaceModes.Contains(mode)) mode = "full";
        _interface.Mode = mode; _immersiveReveal = false; _revealForChat = false;
        if (save) _interface.Save();
        ApplyInterfaceMode();
        if (save) Toast($"Modo da interface: {InterfaceModeLabels[Array.IndexOf(InterfaceModes, mode)]}" + (mode == "immersive" ? ". F10 mostra ou esconde a interface." : ""));
    }

    private void ApplyInterfaceMode()
    {
        var hidden = _interface.Mode == "immersive" && !_immersiveReveal;
        var compact = _interface.Mode == "compact";
        _root.GetNode<Control>("MenuBarPanel").Visible = !hidden;
        _root.GetNode<Control>("ToolbarPanel").Visible = !hidden;
        _locationBar.Visible = !hidden && !compact;
        _sideBar.Visible = !hidden && !compact;
        if (_miniMapWindow is not null)
        {
            if (hidden && _miniMapWindow.Visible) { _miniMapWasVisible = true; _miniMapWindow.Visible = false; }
            else if (!hidden && _miniMapWasVisible) { _miniMapWasVisible = false; _miniMapWindow.Visible = true; }
        }
        ApplyToolbar();
        Callable.From(WindowManager.KeepAll).CallDeferred();
    }

    private void ToggleImmersiveReveal()
    {
        if (_interface.Mode != "immersive") return;
        _immersiveReveal = !_immersiveReveal; _revealForChat = false; ApplyInterfaceMode();
    }

    /// <summary>Enter in immersive mode: the toolbar (with the chat field) shows until the message is sent or the field is left.</summary>
    private bool RevealForChat()
    {
        if (_interface.Mode != "immersive" || _immersiveReveal) return false;
        _immersiveReveal = true; _revealForChat = true; ApplyInterfaceMode(); return true;
    }

    private void EndChatReveal() { if (!_revealForChat) return; _revealForChat = false; _immersiveReveal = false; ApplyInterfaceMode(); }

    // ---- Configurações → Interface ----------------------------------------------------------------------------------

    private FloatingWindow? _interfaceWindow;
    private OptionButton _scaleChoice = null!, _modeChoice = null!;
    private HSlider _windowOpacitySlider = null!, _chatOpacitySlider = null!, _mapOpacitySlider = null!;
    private CheckBox _compactToolbarCheck = null!, _sideBarCheck = null!;
    private bool _syncingInterface;
    public FloatingWindow? InterfaceWindowForTesting => _interfaceWindow;
    public void OpenInterfaceSettingsForTesting() => ToggleInterfaceSettings(true);
    public void ResetInterfaceForTesting() => ResetInterfaceToDefaults();

    private void ToggleInterfaceSettings(bool? open = null)
    {
        if (_interfaceWindow is null) BuildInterfaceSettings();
        _interfaceWindow!.Visible = open ?? !_interfaceWindow.Visible;
        if (_interfaceWindow.Visible) { _interfaceWindow.MoveToFront(); SyncInterfaceSettings(); }
    }

    private void BuildInterfaceSettings()
    {
        var window = _interfaceWindow = new FloatingWindow("Interface", icon: "settings") { Visible = false };
        window.Place(.5f, .5f, -250, -270, 250, 270);
        window.Content.AddThemeConstantOverride("separation", 10);
        Control Row(string text, Control control, string? tip = null) { var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); var label = new Label { Text = text, CustomMinimumSize = new Vector2(170, 0), TooltipText = tip ?? "" }; row.AddChild(label); control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(control); return row; }
        HSlider Slider(Action<float> changed) { var s = new HSlider { MinValue = 30, MaxValue = 100, Step = 5, CustomMinimumSize = new Vector2(120, 26), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, FocusMode = Control.FocusModeEnum.None }; s.ValueChanged += v => { if (!_syncingInterface) changed((float)v / 100f); }; return s; }

        _scaleChoice = new OptionButton(); foreach (var label in InterfaceScale.MenuLabels()) _scaleChoice.AddItem(label);
        _scaleChoice.ItemSelected += index => { if (!_syncingInterface) ChooseUiScale((int)index); };
        window.Content.AddChild(Row("Escala da interface", _scaleChoice, "Automático acompanha o tamanho da janela do jogo"));
        _modeChoice = new OptionButton(); foreach (var label in InterfaceModeLabels) _modeChoice.AddItem(label);
        _modeChoice.ItemSelected += index => { if (!_syncingInterface) SetInterfaceMode(InterfaceModes[(int)index]); };
        window.Content.AddChild(Row("Modo", _modeChoice, "Completo: tudo. Compacto: só menu e barra de ferramentas. Imersivo: só o mundo (F10 mostra a interface)"));
        _windowOpacitySlider = Slider(alpha => WindowManager.SetWindowOpacity(alpha)); window.Content.AddChild(Row("Opacidade das janelas", _windowOpacitySlider));
        _chatOpacitySlider = Slider(alpha => SetChatOpacity(alpha)); window.Content.AddChild(Row("Opacidade do chat", _chatOpacitySlider));
        _mapOpacitySlider = Slider(alpha => { SetMiniMapOpacity(alpha); _miniMapOpacity.SetValueNoSignal(Math.Round(alpha * 100)); }); window.Content.AddChild(Row("Opacidade do mini-mapa", _mapOpacitySlider));
        _compactToolbarCheck = new CheckBox { Text = "Barra inferior só com ícones" };
        _compactToolbarCheck.Toggled += on => { if (_syncingInterface) return; _interface.CompactToolbar = on; _interface.Save(); ApplyToolbar(); };
        window.Content.AddChild(_compactToolbarCheck);
        _sideBarCheck = new CheckBox { Text = "Barra de atalhos aberta (esquerda)" };
        _sideBarCheck.Toggled += on => { if (!_syncingInterface) SetSideBarOpen(on); };
        window.Content.AddChild(_sideBarCheck);

        window.Content.AddChild(new HSeparator());
        Button Action(string text, string tip, Action run) { var b = new Button { Text = text, TooltipText = tip, CustomMinimumSize = new Vector2(0, 38) }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Secondary); b.Pressed += run; window.Content.AddChild(b); return b; }
        Action("Restaurar layout das janelas", "Cada janela volta ao lugar e tamanho originais", RestoreWindowLayout);
        Action("Restaurar a barra de ferramentas", "Ordem, botões ocultos e modo só ícones voltam ao padrão", RestoreToolbar);
        Action("Restaurar toda a interface ao padrão", "Escala, modo, opacidades, barras e janelas voltam ao padrão", ResetInterfaceToDefaults);
        _root.AddChild(window);
    }

    private void SyncInterfaceSettings()
    {
        _syncingInterface = true;
        _scaleChoice.Select(InterfaceScale.MenuIndex(_interface.AutoScale, _interface.Scale));
        _modeChoice.Select(Math.Max(0, Array.IndexOf(InterfaceModes, _interface.Mode)));
        _windowOpacitySlider.Value = Math.Round(_interface.WindowOpacity * 100); _chatOpacitySlider.Value = Math.Round(_interface.ChatOpacity * 100); _mapOpacitySlider.Value = Math.Round(_interface.MiniMapOpacity * 100);
        _compactToolbarCheck.ButtonPressed = _interface.CompactToolbar; _sideBarCheck.ButtonPressed = _interface.SideToolbarOpen;
        _syncingInterface = false;
    }

    /// <summary>Everything about the interface back to the default: scale, mode, opacities, toolbar, shortcut bar and window layout (favorites and recent people stay).</summary>
    private void ResetInterfaceToDefaults()
    {
        _interface.AutoScale = true; _interface.Scale = InterfaceSettings.DefaultScale; _interface.Save(); ApplyUiScale();
        WindowManager.SetWindowOpacity(.92f); SetChatOpacity(.8f); SetMiniMapOpacity(.92f); _miniMapOpacity.SetValueNoSignal(92);
        SetSideBarOpen(true); RestoreToolbar(); SetInterfaceMode("full", save: true); WindowManager.ResetLayout();
        if (_interfaceWindow?.Visible == true) SyncInterfaceSettings();
        Toast("Interface restaurada ao padrão.");
    }
}
