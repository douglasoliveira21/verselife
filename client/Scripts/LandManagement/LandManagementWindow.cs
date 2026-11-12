using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.IslandManagement;

namespace LifeVerse.Client.Scripts.LandManagement;

/// <summary>
/// "Gerenciamento de Terrenos": the window where the staff of an island sees and runs its land. Title bar, a menu of ten sections, the island header that stays on every section and the page of the chosen one.
/// The overview, the map, the parcels, the offers, the contracts and the history are what the server counted; this only draws them and sends back what the person asks for.
/// </summary>
public partial class LandManagementWindow : Node
{
    public sealed record Section(string Id, string Title, string Icon);

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("visao", "Visão Geral", "home"), new("mapa", "Mapa de Parcelas", "map"), new("demarcacao", "Demarcação", "ruler"), new("lista", "Lista de Terrenos", "list"), new("vendas", "Vendas", "coins"),
        new("alugueis", "Aluguéis", "people"), new("contratos", "Contratos", "doc"), new("prims", "Prims e Recursos", "chart"), new("permissoes", "Permissões", "shield"), new("historico", "Histórico", "history"),
    ];

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Control _body = null!, _failure = null!, _grip = null!, _loadingLayer = null!;
    private Label _failureText = null!, _name = null!, _areaLine = null!, _statsA = null!, _statsB = null!, _toast = null!;
    private Control _stateDot = null!;
    private Label _stateText = null!;
    private TextureRect _thumbnail = null!;
    private Button _minimize = null!;
    private AcceptDialog _help = null!;
    private Control _pageHost = null!;
    private readonly Dictionary<string, Button> _menuButtons = [];
    private readonly Dictionary<string, Control> _pages = [];
    private LandManagementApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private LandOverviewData? _data;
    private Guid _island;
    private string _current = "visao";
    private bool _dragging, _resizing, _moved, _minimized, _loadingNow;
    private Vector2 _size = new(1480, 940);
    private Vector2? _viewportOverride;
    private double _toastLeft;

    public Func<Guid>? IslandProvider { get; set; }
    public Func<Guid>? MyUser { get; set; }
    /// <summary>Asked to open the "Sobre o terreno" window of a parcel.</summary>
    public Action<LandParcelRow>? Inspect { get; set; }
    public Action<Guid>? Visit { get; set; }

    public bool IsOpen => _root is not null && _root.Visible;
    public bool Loading => _loadingNow;
    public LandOverviewData? Data => _data;
    public string CurrentSection => _current;
    public LandTerritoryMap Map => _map;
    public IReadOnlyList<string> MenuForTesting => [.. Sections.Select(s => _menuButtons[s.Id].Text.Trim())];
    public string NameForTesting => _name.Text;
    public string AreaLineForTesting => _areaLine.Text;
    public string StatsForTesting => _statsA.Text + "|" + _statsB.Text;
    public string ToastForTesting => _toast.Text;
    public bool FailureVisibleForTesting => _failure.Visible;
    public string FailureTextForTesting => _failureText.Text;
    public bool PageVisibleForTesting(string id) => _pages.TryGetValue(id, out var page) && page.Visible;
    public Vector2 PanelSizeForTesting => _panel.Size;
    public void SetViewportForTesting(Vector2 size) { _viewportOverride = size; Layout(); }
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new LandManagementApi(new IslandManagementApi(transport)); }
    public void SelectSectionForTesting(string id) => Select(id);
    public void PressCloseForTesting() => RequestClose();

    private Guid CurrentIsland => IslandProvider?.Invoke() ?? GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.CurrentIslandId ?? Guid.Empty;
    private Texture2D? _cover;
    private Guid Me => MyUser?.Invoke() ?? GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty;

    public override void _Ready()
    {
        _api ??= new LandManagementApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));
        _root = new Control { Name = "LandManagementRoot", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ViewerTheme.Create() }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _panel = new PanelContainer { Name = "LandManagementPanel", ClipContents = true }; _panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .97f), IslandUi.BorderDefault, IslandUi.RadiusWindow, 1, 0, 0, 28)); _root.AddChild(_panel);
        var shell = new VBoxContainer(); shell.AddThemeConstantOverride("separation", 0); _panel.AddChild(shell);
        shell.AddChild(BuildTitleBar());
        var middle = IslandUi.Row(0); middle.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _body = middle; shell.AddChild(middle);
        middle.AddChild(BuildSidebar());
        var content = IslandUi.Column(12); content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var margin = IslandUi.Padded(content, 14, 12, 16, 12); margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill; middle.AddChild(margin);
        content.AddChild(BuildHeader());
        content.AddChild(BuildPages());

        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(20, 8 + i * 5), new Vector2(8 + i * 5, 20), new Color(IslandUi.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _resizing = button.Pressed; };
        _root.AddChild(_grip);
        _toast = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _toast.Visible = false; _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color("#0E3A47"), IslandUi.AccentPrimary, 10, 1, 16, 9)); _toast.MouseFilter = Control.MouseFilterEnum.Ignore; _root.AddChild(_toast);
        _help = new AcceptDialog { Title = "Ajuda", OkButtonText = "Entendi", Unresizable = true, DialogText = "Aqui a equipe da ilha cuida dos terrenos.\n\n• Visão Geral e Mapa mostram cada terreno de verdade, coloridos pelo estado: verde disponível, laranja à venda, roxo alugado, cinza vendido, azul área comum.\n• Clique num terreno para ver os detalhes (Ctrl ou Shift soma mais). Roda do mouse dá zoom; botão direito ou do meio move o mapa.\n• Em Demarcação, arraste sobre o mapa para marcar quadrados de 4 × 4 m e crie, divida ou una terrenos.\n• Vender, alugar e editar valem para os terrenos que são seus; os administradores da ilha podem mudar a finalidade de qualquer um." };
        _panel.AddChild(_help);
        GetViewport().SizeChanged += Layout;
        Select("visao");
    }

    public override void _ExitTree() { if (IsInsideTree() && GetViewport() is { } viewport) viewport.SizeChanged -= Layout; }

    public override void _Process(double delta) { if (_toastLeft > 0) { _toastLeft -= delta; if (_toastLeft <= 0 && _toast is not null) _toast.Visible = false; } }

    // ---- structure --------------------------------------------------------------------------------------------------------

    private Control BuildTitleBar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 60), MouseDefaultCursorShape = Control.CursorShape.Move };
        var box = IslandUi.Box(IslandUi.TitleBar, null, IslandUi.RadiusWindow, 0, 22, 0); box.CornerRadiusBottomLeft = 0; box.CornerRadiusBottomRight = 0; box.BorderWidthBottom = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .7f); bar.AddThemeStyleboxOverride("panel", box);
        var row = IslandUi.Row(0); bar.AddChild(row);
        var logo = UiStyle.Brand(true, 34); logo.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; logo.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(logo);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new ColorRect { Color = new Color(IslandUi.BorderDefault, .9f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore });
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = IslandUi.Text("Gerenciamento de Terrenos", 21, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
        var help = IslandUi.IconButton("help", "Ajuda", 36); help.Pressed += () => _help.PopupCentered(new Vector2I(560, 0)); row.AddChild(help);
        _minimize = IslandUi.IconButton("minimize", "Minimizar", 36); _minimize.Pressed += ToggleMinimized; row.AddChild(_minimize);
        var close = IslandUi.IconButton("close", "Fechar", 36); close.Pressed += RequestClose; row.AddChild(close);
        bar.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _dragging = button.Pressed;
            else if (input is InputEventMouseMotion motion && _dragging) { _panel.Position += motion.Relative; _moved = true; KeepOnScreen(); }
        };
        return bar;
    }

    private Control BuildSidebar()
    {
        var side = new PanelContainer { CustomMinimumSize = new Vector2(240, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var box = IslandUi.Box(IslandUi.SidebarBackground, null, 0, 0); box.BorderWidthRight = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .6f); side.AddThemeStyleboxOverride("panel", box);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; side.AddChild(scroll);
        var menu = IslandUi.Column(4); menu.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; var margin = IslandUi.Padded(menu, 12, 18, 12, 16); margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(margin);
        foreach (var section in Sections)
        {
            var item = new Button { Text = "  " + section.Title, Icon = UiStyle.Icon(section.Icon, IslandUi.TextSecondary, 24), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 50), ExpandIcon = false };
            item.AddThemeConstantOverride("h_separation", 16); item.AddThemeFontSizeOverride("font_size", 16); item.AddThemeFontOverride("font", UiStyle.Medium);
            foreach (var color in new[] { "font_color", "font_pressed_color", "font_focus_color", "font_hover_color" }) item.AddThemeColorOverride(color, IslandUi.TextPrimary);
            var id = section.Id; item.Pressed += () => Select(id); _menuButtons[id] = item; menu.AddChild(item);
        }
        return side;
    }

    private Control BuildHeader()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 84) };
        var row = IslandUi.Row(16); bar.AddChild(row);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(72, 72), ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        _thumbnail = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(_thumbnail); row.AddChild(frame);
        var identity = IslandUi.Column(4); identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(identity);
        var nameRow = IslandUi.Row(12); _name = IslandUi.Text("Carregando…", 23, IslandUi.TextPrimary, true); nameRow.AddChild(_name);
        var pill = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; var pillRow = IslandUi.Row(7); _stateDot = IslandUi.Dot(IslandUi.Disabled, 9); pillRow.AddChild(_stateDot); _stateText = IslandUi.Text("", 14, IslandUi.Success, true); pillRow.AddChild(_stateText); pill.AddChild(pillRow); nameRow.AddChild(pill);
        identity.AddChild(nameRow); _areaLine = IslandUi.Text("", 14, IslandUi.TextSecondary); identity.AddChild(_areaLine);
        var stats = IslandUi.Column(4); stats.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; _statsA = IslandUi.Text("", 14, IslandUi.TextPrimary); _statsA.HorizontalAlignment = HorizontalAlignment.Right; _statsB = IslandUi.Text("", 14, IslandUi.TextPrimary); _statsB.HorizontalAlignment = HorizontalAlignment.Right; stats.AddChild(_statsA); stats.AddChild(_statsB); row.AddChild(stats);
        return bar;
    }

    private Control BuildPages()
    {
        _pageHost = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
        var workspace = BuildWorkspace(); workspace.SetAnchorsPreset(Control.LayoutPreset.FullRect); _pageHost.AddChild(workspace);
        foreach (var id in new[] { "visao", "mapa", "demarcacao" }) _pages[id] = workspace;
        foreach (var id in new[] { "lista", "vendas", "alugueis", "contratos", "prims", "permissoes", "historico" })
        {
            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false }; scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            var column = IslandUi.Column(12); column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(column); _pageHost.AddChild(scroll); _pages[id] = scroll; _pageBodies[id] = column;
        }
        _failure = new PanelContainer { Visible = false }; _failure.SetAnchorsPreset(Control.LayoutPreset.Center); _failure.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 24, 18));
        var box = IslandUi.Column(12); _failureText = IslandUi.Text("", 16, IslandUi.TextPrimary, true, true); _failureText.CustomMinimumSize = new Vector2(420, 0); _failureText.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(_failureText);
        var retry = IslandUi.Action("Tentar novamente", "reset", IslandUi.Kind.Secondary, 42); retry.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; retry.Pressed += () => _ = LoadAsync(); box.AddChild(retry); _failure.AddChild(box);
        _pageHost.AddChild(_failure);
        return _pageHost;
    }

    private readonly Dictionary<string, VBoxContainer> _pageBodies = [];

    // ---- window behaviour ---------------------------------------------------------------------------------------------------

    private void Layout()
    {
        if (_panel is null) return;
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; var margin = 16f;
        var minWidth = Math.Min(1100f, view.X - 2 * margin); var minHeight = Math.Min(680f, view.Y - 2 * margin);
        var size = new Vector2(Math.Clamp(_size.X, minWidth, Math.Max(minWidth, view.X - 2 * margin)), Math.Clamp(_size.Y, minHeight, Math.Max(minHeight, view.Y - 2 * margin)));
        _panel.CustomMinimumSize = new Vector2(minWidth, _minimized ? 60 : minHeight); _panel.Size = _minimized ? new Vector2(size.X, 60) : size;
        if (!_moved) _panel.Position = ((view - _panel.Size) / 2).Floor(); else KeepOnScreen();
        _grip.Visible = !_minimized; _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
    }

    private void KeepOnScreen()
    {
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(Math.Clamp(_panel.Position.X, 16 - _panel.Size.X + 120, view.X - 120), Math.Clamp(_panel.Position.Y, 8, view.Y - 60));
        if (_grip is not null) _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
    }

    public override void _Input(InputEvent input)
    {
        if (!IsOpen) return;
        if (_resizing && input is InputEventMouseMotion motion) { _size += motion.Relative; Layout(); }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) { _resizing = false; _dragging = false; }
    }

    private void ToggleMinimized() { _minimized = !_minimized; _body.Visible = !_minimized; Layout(); }

    public void Toggle() { if (IsOpen) RequestClose(); else _ = OpenAsync(); }

    public void RequestClose() { _root.Visible = false; }

    public async Task OpenAsync(Guid? island = null)
    {
        var target = island ?? CurrentIsland;
        if (target == Guid.Empty) { _root.Visible = true; Layout(); ShowFailure("Entre em uma ilha para ver os terrenos dela."); return; }
        _island = target; _root.Visible = true; Layout(); await LoadAsync();
    }

    /// <summary>Opens the window on an island and goes straight to one of its sections (the island window's buttons use it); with <paramref name="startOffer"/> it also starts a sale offer.</summary>
    public async Task OpenSectionAsync(Guid island, string section, bool startOffer = false)
    {
        await OpenAsync(island); if (_data is null) return;
        Select(section); if (startOffer) CreateSaleOffer();
    }

    private void ShowFailure(string text) { _failureText.Text = text; _failure.Visible = true; }

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty || _loadingNow) return;
        _loadingNow = true; _failure.Visible = false;
        var reply = await _api.Overview(_island);
        _loadingNow = false;
        if (!reply.Ok) { _data = null; ShowFailure(reply.Status == 403 ? "Só o dono e os administradores da ilha podem gerenciar os terrenos." : reply.Status == 404 ? "O servidor ainda não tem o gerenciamento de terrenos. Ele chega com a próxima publicação da API." : LandManagementApi.Words(reply)); return; }
        var data = LandOverviewData.Parse(reply.Body);
        if (data is null) { ShowFailure("O servidor mandou uma resposta que não entendi. Tente de novo."); return; }
        Apply(data);
    }

    private void Notify(string text) { _toast.Text = text; _toast.Visible = true; _toastLeft = 3.5; var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; _toast.Position = new Vector2((view.X - Math.Max(260, _toast.GetCombinedMinimumSize().X)) / 2, view.Y - 90); }

    private void Select(string id)
    {
        if (!_pages.ContainsKey(id)) return;
        _current = id;
        foreach (var (key, page) in _pages) page.Visible = key == id || (page == _pages[id]);
        ApplyMode();
        foreach (var section in Sections) PaintItem(section);
        if (_data is not null) RefreshPage(id);
    }

    private void PaintItem(Section section)
    {
        var item = _menuButtons[section.Id]; var selected = section.Id == _current;
        var normal = selected ? IslandUi.Box(IslandUi.AccentSelected, IslandUi.AccentPrimary, IslandUi.RadiusButton, 1, 14, 0) : IslandUi.Box(new Color(0, 0, 0, 0), null, IslandUi.RadiusButton, 0, 14, 0);
        item.AddThemeStyleboxOverride("normal", normal); item.AddThemeStyleboxOverride("hover", selected ? normal : IslandUi.Box(new Color(1, 1, 1, .07f), null, IslandUi.RadiusButton, 0, 14, 0));
        item.AddThemeStyleboxOverride("pressed", selected ? normal : IslandUi.Box(new Color(1, 1, 1, .10f), null, IslandUi.RadiusButton, 0, 14, 0)); item.AddThemeStyleboxOverride("focus", normal);
        item.Icon = UiStyle.Icon(section.Icon, selected ? IslandUi.AccentPrimary : IslandUi.TextSecondary, 24);
    }

    // ---- the data ----------------------------------------------------------------------------------------------------------

    private void Apply(LandOverviewData data)
    {
        _data = data; _failure.Visible = false;
        var t = data.Totals;
        _name.Text = data.IslandName; _stateText.Text = IslandUi.StateText(data.RuntimeState); _stateText.AddThemeColorOverride("font_color", IslandUi.StateColor(data.RuntimeState));
        if (_stateDot is ColorRect dot) dot.Color = IslandUi.StateColor(data.RuntimeState);
        _areaLine.Text = $"Área total: {LandMath.Area(t.Area)} ({data.Size} m × {data.Size} m)";
        _statsA.Text = $"Parcelas: {t.Parcels}   |   Vendidas: {t.Sold}   |   Alugadas: {t.Rented}";
        _statsB.Text = $"Disponíveis: {t.Available}   |   Área livre: {LandMath.Area(t.AreaFree)}";
        if (data.CoverImageUrl.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(data.CoverImageUrl[6..], out var asset))
            RequestTexture(asset, texture => { if (_data?.CoverImageUrl == data.CoverImageUrl) { _cover = texture; _thumbnail.Texture = texture ?? UiStyle.Icon("map", IslandUi.TextMuted, 40); _map.SetCover(texture); RefreshDetail(); } });
        else { _cover = null; _thumbnail.Texture = UiStyle.Icon("map", IslandUi.TextMuted, 40); _map.SetCover(null); }
        _map.SetData(data);
        if (_map.Selected is { } keep && data.Parcels.All(p => p.Id != keep)) _map.Select(null);
        RefreshWorkspace(); RefreshPage(_current);
    }

    private void RequestTexture(Guid asset, Action<Texture2D?> done)
    {
        if (GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is { } cache) cache.Request(asset, done); else done(null);
    }

    private LandParcelRow? Selected => _data is null || _map.Selected is not { } id ? null : _data.Parcels.FirstOrDefault(p => p.Id == id);
    private bool IsMine(LandParcelRow parcel) => parcel.OwnerId == Me;

    private async Task<bool> Do(Task<IslandReply> call, string done)
    {
        var reply = await call;
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); return false; }
        Notify(done); await LoadAsync(); return true;
    }
}
