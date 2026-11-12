using System.Globalization;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// "Informações e Gerenciamento da Ilha": title bar, side menu with the thirteen sections, the island summary that stays on every tab, the page of the chosen section and the footer with
/// Restaurar Padrão / Salvar Alterações. The server decides what the person may see and change; this only draws what it sent and sends back what the person edited.
/// </summary>
public partial class IslandManagementWindow : Node
{
    public sealed record Section(string Id, string Title, string Icon, string Summary, string[] Covers);

    /// <summary>The sections, in the order of the menu. Only those with a page of their own work today; the others say so instead of showing made-up data.</summary>
    public static readonly IReadOnlyList<Section> Sections =
    [
        new("geral", "Geral", "home", "", []),
        new("acesso", "Acesso", "people", "Modo de acesso, regras de entrada e lista de pessoas e grupos permitidos.", ["Pública, privada, somente convidados ou somente grupo", "Regras de entrada pelo mapa, teleporte e convites", "Lista de usuários e grupos permitidos, com expiração"]),
        new("banimentos", "Banimentos", "ban", "Quem está banido da ilha, por quê e até quando.", ["Banir e desbanir pessoas, com motivo e duração", "Lista com pesquisa, filtros e histórico", "Aplicação no servidor da ilha"]),
        new("permissoes", "Permissões", "shield", "O que cada papel pode fazer na ilha.", ["Construção, social e administração", "Papéis: proprietário, administrador, moderador, construtor e visitante", "Papéis personalizados"]),
        new("objetos", "Objetos e Prims", "cube", "Consumo de prims e lista de objetos da ilha.", ["Indicadores e consumo por proprietário", "Tabela de objetos com detalhes", "Devolver e excluir objetos"]),
        new("midia", "Mídia", "play", "Música e vídeo da ilha.", ["Stream de música com volume padrão", "Vídeo da ilha e superfície de exibição"]),
        new("terreno", "Terreno", "mountain", "Altura, água, texturas e backup do terreno.", ["Importar e exportar heightmap", "Backup e restauração do terreno"]),
        new("ambiente", "Ambiente", "sun", "Luz, céu, neblina e água.", ["Horário, ciclo do dia e intensidade solar", "Predefinições de ambiente"]),
        new("fisica", "Física", "rotate", "Objetos físicos e limites de simulação.", ["Corpos ativos e tempo de física", "Congelar e liberar objetos"]),
        new("desempenho", "Desempenho", "chart", "Como o servidor da ilha está se saindo.", ["FPS, tick, CPU e memória do servidor", "Scripts e objetos que mais consomem"]),
        new("reinicializacao", "Reinicialização", "power", "Reiniciar a ilha pelo gerenciador de mundos.", ["Reiniciar agora ou agendar, com aviso aos presentes", "Histórico de reinicializações"]),
        new("administracao", "Administração", "settings", "Jogadores conectados e operações da ilha.", ["Expulsar, banir e enviar avisos", "Fechar, reabrir e criar backup da ilha"]),
        new("auditoria", "Histórico e Auditoria", "history", "Quem fez o quê na ilha.", ["Filtros por período, pessoa e tipo de evento", "Valores anteriores e novos de cada alteração"]),
        new("propriedade", "Propriedade", "doc", "De quem é a ilha e o que ela está fazendo no mercado.", ["Dono, classe da região e tamanho", "Venda, aluguel e transferência da ilha"]),
    ];

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Control _body = null!, _footer = null!, _loading = null!, _failure = null!;
    private Label _failureText = null!, _name = null!, _id = null!, _visitors = null!, _stateText = null!, _updated = null!, _toast = null!, _loadingText = null!;
    private Control _statePill = null!, _stateDot = null!, _updatedDot = null!;
    private TextureRect _thumbnail = null!;
    private Button _save = null!, _restore = null!, _viewInSearch = null!, _minimize = null!;
    private VBoxContainer _menu = null!;
    private Control _pageHost = null!;
    private readonly Dictionary<string, Button> _menuButtons = [];
    private readonly Dictionary<string, Control> _pages = [];
    private IslandGeneralPage _general = null!;
    private IslandAccessPage _access = null!;
    private IslandBansPage _bans = null!;
    private IslandPermissionsPage _perms = null!;
    private IslandObjectsPage _objects = null!;
    private IslandAuditPage _audit = null!;
    private IslandAdminPage _admin = null!;
    private IslandPerformancePage _performance = null!;
    private IslandRestartPage _restart = null!;
    private IslandEnvironmentPage _environment = null!;
    private IslandMediaPage _media = null!;
    private IslandPhysicsPage _physics = null!;
    private IslandTerrainPage _terrain = null!;
    private IslandPropertyPage _property = null!;
    private ConfirmationDialog _confirm = null!;
    private AcceptDialog _help = null!;
    private Action? _confirmed;
    private IslandManagementApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private IslandInfo? _info;
    private Guid _island;
    private string _current = "geral";
    private bool _dragging, _resizing, _moved, _saving, _loadingNow, _minimized;
    private Vector2 _size = new(1480, 980);
    private double _toastLeft, _lastSyncAt;

    public Func<Guid>? IslandProvider { get; set; }
    public Func<(double X, double Z)?>? CurrentPosition { get => _general?.CurrentPosition; set { if (_general is not null) _general.CurrentPosition = value; else _pendingPosition = value; } }
    private Func<(double X, double Z)?>? _pendingPosition;
    /// <summary>Asked to show the island in the search window.</summary>
    public Action<Guid, string>? ViewInSearch { get; set; }
    /// <summary>Raised after the server accepted a new name, so the rest of the game can show it.</summary>
    public event Action<Guid, string>? IslandRenamed;

    public bool IsOpen => _root is not null && _root.Visible;
    public bool Dirty => (_general is not null && _general.IsDirty) || (_access is not null && _access.IsDirty);
    public IslandAccessPage Access => _access;
    public IslandBansPage Bans => _bans;
    public IslandPermissionsPage Permissions => _perms;
    public IslandObjectsPage Objects => _objects;
    public IslandAuditPage Audit => _audit;
    public IslandAdminPage Administration => _admin;
    public IslandPerformancePage Performance => _performance;
    public IslandRestartPage Restart => _restart;
    public IslandEnvironmentPage Environment => _environment;
    public IslandMediaPage Media => _media;
    public IslandPhysicsPage Physics => _physics;
    public IslandTerrainPage Terrain => _terrain;
    public IslandPropertyPage Property => _property;
    /// <summary>Asked to open the profile of a person.</summary>
    public Action<Guid>? ViewProfile { get; set; }
    public bool Loading => _loadingNow;
    public IslandGeneralPage General => _general;
    public IslandInfo? Info => _info;
    public string CurrentSection => _current;
    public IReadOnlyList<string> MenuForTesting => [.. Sections.Select(s => _menuButtons[s.Id].Text.Trim())];
    public bool SaveEnabledForTesting => !_save.Disabled;
    public string SaveTextForTesting => _save.Text.Trim();
    public string NameForTesting => _name.Text;
    public string IdForTesting => _id.Text;
    public string VisitorsForTesting => _visitors.Text;
    public string StateForTesting => _stateText.Text;
    public string ToastForTesting => _toast.Text;
    public bool FailureVisibleForTesting => _failure.Visible;
    public string FailureTextForTesting => _failureText.Text;
    public bool ConfirmOpenForTesting => _confirm.Visible;
    public string UpdatedTextForTesting => _updated.Text;
    public Vector2 PanelSizeForTesting => _panel.Size;
    public Vector2 PanelMinForTesting => _panel.GetCombinedMinimumSize();
    public bool PageVisibleForTesting(string id) => _pages.TryGetValue(id, out var page) && page.Visible;
    public string PagePlaceholderTextForTesting(string id) => _pages.TryGetValue(id, out var page) ? string.Join("|", page.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)) : "";
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new IslandManagementApi(transport); _access?.Bind(_api); _bans?.Bind(_api); _perms?.Bind(_api); _objects?.Bind(_api); _audit?.Bind(_api); _admin?.Bind(_api); _performance?.Bind(_api); _restart?.Bind(_api); _environment?.Bind(_api); _media?.Bind(_api); _physics?.Bind(_api); _terrain?.Bind(_api); _property?.Bind(_api); }
    public void PressSaveForTesting() => _save.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressRestoreForTesting() => _restore.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressCloseForTesting() => RequestClose();
    public void ConfirmForTesting() { var action = _confirmed; _confirmed = null; _confirm.Hide(); action?.Invoke(); }
    public void CancelConfirmForTesting() { _confirmed = null; _confirm.Hide(); }
    public void SelectSectionForTesting(string id) => Select(id);
    public void SetViewportForTesting(Vector2 size) { _viewportOverride = size; Layout(); }
    private Vector2? _viewportOverride;

    public override void _Ready()
    {
        _api ??= new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this));
        _root = new Control { Name = "IslandManagementRoot", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ViewerTheme.Create() }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _panel = new PanelContainer { Name = "IslandManagementPanel", ClipContents = true }; _panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .96f), IslandUi.BorderDefault, IslandUi.RadiusWindow, 1, 0, 0, 28)); _root.AddChild(_panel);
        var shell = new VBoxContainer(); shell.AddThemeConstantOverride("separation", 0); _panel.AddChild(shell);
        shell.AddChild(BuildTitleBar());
        var middle = IslandUi.Row(0); middle.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _body = middle; shell.AddChild(middle);
        middle.AddChild(BuildSidebar());
        var content = IslandUi.Column(12); content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var contentMargin = IslandUi.Padded(content, 14, 12, 16, 8); contentMargin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; contentMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill; middle.AddChild(contentMargin);
        content.AddChild(BuildSummary());
        content.AddChild(BuildPages());
        _footer = BuildFooter(); shell.AddChild(_footer);

        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(20, 8 + i * 5), new Vector2(8 + i * 5, 20), new Color(IslandUi.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _resizing = button.Pressed; };
        _root.AddChild(_grip);
        _toast = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _toast.Visible = false; _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color("#0E3A47"), IslandUi.AccentPrimary, 10, 1, 16, 9)); _toast.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(_toast);
        _confirm = new ConfirmationDialog { Title = "Confirmar", OkButtonText = "Confirmar", CancelButtonText = "Cancelar", Exclusive = true, Unresizable = true }; _confirm.Confirmed += () => { var action = _confirmed; _confirmed = null; action?.Invoke(); }; _confirm.Canceled += () => _confirmed = null; _panel.AddChild(_confirm);
        _help = new AcceptDialog { Title = "Ajuda", OkButtonText = "Entendi", DialogText = "Aqui você administra a sua ilha.\n\n• O menu da esquerda troca de seção; o resumo no topo e o rodapé ficam sempre visíveis.\n• Nada vale até você clicar em “Salvar Alterações”.\n• “Restaurar Padrão” volta categoria, classificação, idioma, capacidade e visibilidade aos valores iniciais (depois é preciso salvar).\n• Só o proprietário e quem tem a permissão de administrador pode salvar.", Unresizable = true };
        _panel.AddChild(_help);
        if (_pendingPosition is not null) _general.CurrentPosition = _pendingPosition;
        GetViewport().SizeChanged += Layout;
        Select("geral");
    }

    public override void _ExitTree() { if (IsInsideTree() && GetViewport() is { } viewport) viewport.SizeChanged -= Layout; }

    // ---- structure --------------------------------------------------------------------------------------------------------

    private Control BuildTitleBar()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 60), MouseDefaultCursorShape = Control.CursorShape.Move };
        var box = IslandUi.Box(IslandUi.TitleBar, null, IslandUi.RadiusWindow, 0, 22, 0); box.CornerRadiusBottomLeft = 0; box.CornerRadiusBottomRight = 0; box.BorderWidthBottom = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .7f); bar.AddThemeStyleboxOverride("panel", box);
        var row = IslandUi.Row(0); bar.AddChild(row);
        var logo = UiStyle.Brand(true, 34); logo.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; logo.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(logo);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var separator = new ColorRect { Color = new Color(IslandUi.BorderDefault, .9f), CustomMinimumSize = new Vector2(1, 30), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddChild(separator);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var title = IslandUi.Text("Informações e Gerenciamento da Ilha", 21, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
        var help = IslandUi.IconButton("help", "Ajuda", 36); help.Pressed += () => _help.PopupCentered(new Vector2I(520, 0)); row.AddChild(help);
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
        var side = new PanelContainer { CustomMinimumSize = new Vector2(265, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var box = IslandUi.Box(IslandUi.SidebarBackground, null, 0, 0); box.BorderWidthRight = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .6f); side.AddThemeStyleboxOverride("panel", box);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; side.AddChild(scroll);
        _menu = IslandUi.Column(0); _menu.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; var menuMargin = IslandUi.Padded(_menu, 12, 18, 12, 16); menuMargin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(menuMargin);
        foreach (var section in Sections)
        {
            var item = new Button { Text = "  " + section.Title, Icon = UiStyle.Icon(section.Icon, IslandUi.TextSecondary, 25), Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 54), ExpandIcon = false, ToggleMode = false };
            item.AddThemeConstantOverride("h_separation", 18); item.AddThemeFontSizeOverride("font_size", 16); item.AddThemeFontOverride("font", UiStyle.Medium);
            foreach (var color in new[] { "font_color", "font_pressed_color", "font_focus_color" }) item.AddThemeColorOverride(color, IslandUi.TextPrimary); item.AddThemeColorOverride("font_hover_color", IslandUi.TextPrimary);
            item.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color(0, 0, 0, 0), null, IslandUi.RadiusButton, 0, 14, 0)); item.AddThemeStyleboxOverride("hover", IslandUi.Box(new Color(1, 1, 1, .07f), null, IslandUi.RadiusButton, 0, 14, 0));
            item.AddThemeStyleboxOverride("pressed", IslandUi.Box(new Color(1, 1, 1, .10f), null, IslandUi.RadiusButton, 0, 14, 0)); item.AddThemeStyleboxOverride("focus", IslandUi.Box(new Color(0, 0, 0, 0), null, IslandUi.RadiusButton, 0, 14, 0));
            var id = section.Id; item.Pressed += () => Select(id);
            _menuButtons[section.Id] = item; _menu.AddChild(item);
            if (section != Sections[^1]) _menu.AddChild(IslandUi.Divider());
        }
        return side;
    }

    private Control BuildSummary()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 88) };
        bar.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.CardBackground, .0f), null, 0, 0, 4, 0));
        var row = IslandUi.Row(16); bar.AddChild(row);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(72, 72), ClipContents = true, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, 9, 1));
        _thumbnail = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(_thumbnail); row.AddChild(frame);

        var identity = IslandUi.Column(4); identity.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; identity.CustomMinimumSize = new Vector2(120, 0); row.AddChild(identity);
        var nameRow = IslandUi.Row(12); _name = IslandUi.Text("Carregando…", 23, IslandUi.TextPrimary, true); nameRow.AddChild(_name);
        var pill = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; _statePill = pill; var pillRow = IslandUi.Row(7); _stateDot = IslandUi.Dot(IslandUi.Disabled, 9); pillRow.AddChild(_stateDot); _stateText = IslandUi.Text("", 14, IslandUi.Success, true); pillRow.AddChild(_stateText); pill.AddChild(pillRow); nameRow.AddChild(pill);
        identity.AddChild(nameRow);
        var idRow = IslandUi.Row(8); _id = IslandUi.Text("", 13, IslandUi.TextSecondary); _id.MouseFilter = Control.MouseFilterEnum.Pass; idRow.AddChild(_id);
        var copy = IslandUi.IconButton("copy", "Copiar o ID da ilha", 28); copy.Pressed += () => { if (_island != Guid.Empty) { DisplayServer.ClipboardSet(_island.ToString()); Notify("ID da ilha copiado."); } }; idRow.AddChild(copy); identity.AddChild(idRow);

        var visitors = IslandUi.Row(10); visitors.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; visitors.AddChild(UiStyle.IconRect("people", IslandUi.TextSecondary, 26));
        var visitorsText = IslandUi.Column(0); visitorsText.AddChild(IslandUi.Text("Visitantes:", 14, IslandUi.TextSecondary)); _visitors = IslandUi.Text("—", 17, IslandUi.TextPrimary, true); visitorsText.AddChild(_visitors); visitors.AddChild(visitorsText); row.AddChild(visitors);
        row.AddChild(new ColorRect { Color = new Color(IslandUi.BorderDefault, .8f), CustomMinimumSize = new Vector2(1, 52), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        var state = IslandUi.Column(0); state.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; state.AddChild(IslandUi.Text("Estado:", 14, IslandUi.TextSecondary));
        var stateRow = IslandUi.Row(8); _accessDot = IslandUi.Dot(IslandUi.Success, 12); stateRow.AddChild(_accessDot); _accessText = IslandUi.Text("", 16, IslandUi.TextPrimary); stateRow.AddChild(_accessText); state.AddChild(stateRow); row.AddChild(state);
        _viewInSearch = IslandUi.Action("Ver no Buscador", "external", IslandUi.Kind.Orange, 44); _viewInSearch.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; _viewInSearch.Pressed += () => { if (_info is not null) ViewInSearch?.Invoke(_info.Id, _info.Name); }; row.AddChild(_viewInSearch);
        return bar;
    }

    private Control _accessDot = null!, _grip = null!;
    private Label _accessText = null!;

    private Control BuildPages()
    {
        _pageHost = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
        _general = new IslandGeneralPage { Name = "General" }; _general.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _general.Changed += RefreshFooter; _general.ConfigureAccessRequested += () => Select("acesso"); _general.RequestTexture = RequestTexture; _general.PickerHost = GetParent();
        _pageHost.AddChild(_general); _pages["geral"] = _general;
        _access = new IslandAccessPage { Name = "Access", Visible = false }; _access.SetAnchorsPreset(Control.LayoutPreset.FullRect); _access.Bind(_api!); _access.Changed += RefreshFooter; _access.Notify += text => Notify(text); _access.ViewProfile += id => ViewProfile?.Invoke(id);
        _pageHost.AddChild(_access); _pages["acesso"] = _access;
        _bans = new IslandBansPage { Name = "Bans", Visible = false }; _bans.SetAnchorsPreset(Control.LayoutPreset.FullRect); _bans.Bind(_api!); _bans.Notify += text => Notify(text); _bans.ViewProfile += id => ViewProfile?.Invoke(id); _bans.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_bans); _pages["banimentos"] = _bans;
        _perms = new IslandPermissionsPage { Name = "Permissions", Visible = false }; _perms.SetAnchorsPreset(Control.LayoutPreset.FullRect); _perms.Bind(_api!); _perms.Notify += text => Notify(text); _perms.ViewProfile += id => ViewProfile?.Invoke(id); _perms.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_perms); _pages["permissoes"] = _perms;
        _objects = new IslandObjectsPage { Name = "Objects", Visible = false }; _objects.SetAnchorsPreset(Control.LayoutPreset.FullRect); _objects.Bind(_api!); _objects.Notify += text => Notify(text); _objects.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_objects); _pages["objetos"] = _objects;
        _audit = new IslandAuditPage { Name = "Audit", Visible = false }; _audit.SetAnchorsPreset(Control.LayoutPreset.FullRect); _audit.Bind(_api!);
        _pageHost.AddChild(_audit); _pages["auditoria"] = _audit;
        _admin = new IslandAdminPage { Name = "Admin", Visible = false }; _admin.SetAnchorsPreset(Control.LayoutPreset.FullRect); _admin.Bind(_api!); _admin.Notify += text => Notify(text); _admin.ViewProfile += id => ViewProfile?.Invoke(id); _admin.Ask = (text, ok, action) => Ask(text, ok, action); _admin.SetClosed = SetClosedAsync;
        _pageHost.AddChild(_admin); _pages["administracao"] = _admin;
        _performance = new IslandPerformancePage { Name = "Performance", Visible = false }; _performance.SetAnchorsPreset(Control.LayoutPreset.FullRect); _performance.Bind(_api!);
        _pageHost.AddChild(_performance); _pages["desempenho"] = _performance;
        _restart = new IslandRestartPage { Name = "Restart", Visible = false }; _restart.SetAnchorsPreset(Control.LayoutPreset.FullRect); _restart.Bind(_api!); _restart.Notify += text => Notify(text); _restart.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_restart); _pages["reinicializacao"] = _restart;
        _environment = new IslandEnvironmentPage { Name = "Environment", Visible = false }; _environment.SetAnchorsPreset(Control.LayoutPreset.FullRect); _environment.Bind(_api!); _environment.Notify += text => Notify(text); _environment.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_environment); _pages["ambiente"] = _environment;
        _media = new IslandMediaPage { Name = "Media", Visible = false }; _media.SetAnchorsPreset(Control.LayoutPreset.FullRect); _media.Bind(_api!); _media.Notify += text => Notify(text); _media.Ask = (text, ok, action) => Ask(text, ok, action);
        _media.PreviewStart = (url, volume) => Presence()?.Preview(url, volume); _media.PreviewStop = () => Presence()?.EndPreview();
        _pageHost.AddChild(_media); _pages["midia"] = _media;
        _physics = new IslandPhysicsPage { Name = "Physics", Visible = false }; _physics.SetAnchorsPreset(Control.LayoutPreset.FullRect); _physics.Bind(_api!); _physics.Notify += text => Notify(text); _physics.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_physics); _pages["fisica"] = _physics;
        _terrain = new IslandTerrainPage { Name = "Terrain", Visible = false }; _terrain.SetAnchorsPreset(Control.LayoutPreset.FullRect); _terrain.Bind(_api!); _terrain.Notify += text => Notify(text); _terrain.Ask = (text, ok, action) => Ask(text, ok, action);
        _pageHost.AddChild(_terrain); _pages["terreno"] = _terrain;
        _property = new IslandPropertyPage { Name = "Property", Visible = false }; _property.SetAnchorsPreset(Control.LayoutPreset.FullRect); _property.Bind(_api!); _property.Notify += text => Notify(text); _property.Changed += () => _ = LoadAsync();
        _pageHost.AddChild(_property); _pages["propriedade"] = _property;

        _loading = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop }; _loading.SetAnchorsPreset(Control.LayoutPreset.FullRect); ((PanelContainer)_loading).AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .72f), null, 10, 0));
        _loadingText = IslandUi.Text("Carregando dados da ilha…", 17, IslandUi.TextSecondary, true); _loadingText.HorizontalAlignment = HorizontalAlignment.Center; _loadingText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _loading.AddChild(_loadingText); _pageHost.AddChild(_loading);

        var failure = new PanelContainer { Visible = false }; failure.SetAnchorsPreset(Control.LayoutPreset.FullRect); failure.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .97f), IslandUi.Error, IslandUi.RadiusCard, 1));
        var box = IslandUi.Column(12); box.Alignment = BoxContainer.AlignmentMode.Center; box.AddChild(UiStyle.IconRect("alert", IslandUi.Error, 40));
        _failureText = IslandUi.Text("", 17, IslandUi.TextPrimary, true, true); _failureText.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(_failureText);
        var retry = IslandUi.Action("Tentar novamente", "reset", IslandUi.Kind.Secondary, 42); retry.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; retry.Pressed += () => _ = LoadAsync(); box.AddChild(retry);
        failure.AddChild(IslandUi.Padded(box, 40, 20, 40, 20)); _failure = failure; _pageHost.AddChild(failure);
        return _pageHost;
    }

    private Control BuildFooter()
    {
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0, 68) };
        var box = IslandUi.Box(IslandUi.TitleBar, null, IslandUi.RadiusWindow, 0, 22, 0); box.CornerRadiusTopLeft = 0; box.CornerRadiusTopRight = 0; box.BorderWidthTop = 1; box.BorderColor = new Color(IslandUi.BorderDefault, .7f); bar.AddThemeStyleboxOverride("panel", box);
        var row = IslandUi.Row(18); bar.AddChild(row);
        _updatedDot = IslandUi.Dot(IslandUi.Success, 14); row.AddChild(_updatedDot);
        _updated = IslandUi.Text("Aguardando dados do servidor", 13, IslandUi.TextSecondary); row.AddChild(_updated);
        row.AddChild(IslandUi.Spacer());
        _restore = IslandUi.Action("Restaurar Padrão", "reset", IslandUi.Kind.Secondary, 44, 20); _restore.CustomMinimumSize = new Vector2(190, 44); _restore.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; _restore.Pressed += RestoreDefaults; row.AddChild(_restore);
        _save = IslandUi.Action("Salvar Alterações", "ok", IslandUi.Kind.Primary, 44, 20); _save.CustomMinimumSize = new Vector2(210, 44); _save.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; _save.Pressed += () => _ = SaveAsync(); row.AddChild(_save);
        return bar;
    }

    // ---- open, close, layout ----------------------------------------------------------------------------------------------

    private Guid CurrentIsland => IslandProvider?.Invoke() ?? GetNodeOrNull<NetworkManager>("/root/NetworkManager")?.CurrentIslandId ?? Guid.Empty;

    public void Toggle() { if (IsOpen) RequestClose(); else _ = OpenAsync(); }

    public async Task OpenAsync(Guid? island = null)
    {
        var target = island ?? CurrentIsland;
        if (target == Guid.Empty) { _root.Visible = true; Layout(); ShowFailure("Entre em uma ilha para ver as informações dela."); return; }
        var changed = target != _island; _island = target;
        _root.Visible = true; Layout(); _panel.GetParent()?.MoveChild(_panel, -1);
        if (changed || _info is null) { _general.Visible = _current == "geral"; }
        await LoadAsync();
    }

    public void RequestClose()
    {
        if (!Dirty) { _media.EndTest(); _root.Visible = false; return; }
        Ask("Há alterações que ainda não foram salvas. Fechar a janela descarta essas alterações.", "Descartar e fechar", () => { _general.Revert(); _access.Revert(); _root.Visible = false; });
    }

    private void Ask(string text, string ok, Action action)
    {
        _confirmed = action; _confirm.DialogText = text; _confirm.OkButtonText = ok; _confirm.PopupCentered(new Vector2I(520, 0));
    }

    private void ToggleMinimized()
    {
        _minimized = !_minimized; _body.Visible = !_minimized; _footer.Visible = !_minimized; _panel.Size = new Vector2(_panel.Size.X, 0); Layout();
    }

    private void Layout()
    {
        if (_panel is null) return;
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size;
        var margin = 16f;
        var minWidth = Math.Min(1000f, view.X - 2 * margin); var minHeight = Math.Min(650f, view.Y - 2 * margin);
        var size = new Vector2(Math.Clamp(_size.X, minWidth, Math.Max(minWidth, view.X - 2 * margin)), Math.Clamp(_size.Y, minHeight, Math.Max(minHeight, view.Y - 2 * margin)));
        _panel.CustomMinimumSize = new Vector2(minWidth, _minimized ? 60 : minHeight);
        _panel.Size = _minimized ? new Vector2(size.X, 60) : size;
        if (!_moved) _panel.Position = ((view - _panel.Size) / 2).Floor(); else KeepOnScreen();
        _grip.Visible = !_minimized; _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
    }

    private void KeepOnScreen()
    {
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size;
        _panel.Position = new Vector2(Math.Clamp(_panel.Position.X, 16 - _panel.Size.X + 120, view.X - 120), Math.Clamp(_panel.Position.Y, 8, view.Y - 60));
    }

    public override void _Input(InputEvent input)
    {
        if (!IsOpen) return;
        if (_resizing && input is InputEventMouseMotion motion) { _size += motion.Relative; Layout(); }
        else if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) { _resizing = false; _dragging = false; }
    }

    public override void _Process(double delta)
    {
        if (_toastLeft > 0) { _toastLeft -= delta; if (_toastLeft <= 0) _toast.Visible = false; }
        if (IsOpen && _lastSyncAt > 0) _updated.Text = SyncText();
    }

    // ---- navigation -------------------------------------------------------------------------------------------------------

    private void Select(string id)
    {
        if (!_pages.ContainsKey(id)) return;
        if (_current == "midia" && id != "midia") _media.EndTest();
        _current = id;
        foreach (var (key, page) in _pages) page.Visible = key == id;
        ApplyOwnerLock(id);
        if (id == "acesso" && _info is not null) _ = _access.LoadAsync();
        if (id == "banimentos" && _info is not null) _ = _bans.LoadAsync();
        if (id == "permissoes" && _info is not null) _ = _perms.LoadAsync();
        if (id == "objetos" && _info is not null) _ = _objects.ReloadAllAsync();
        if (id == "auditoria" && _info is not null) _ = _audit.LoadAsync();
        if (id == "administracao" && _info is not null) _ = _admin.LoadAsync();
        if (id == "desempenho" && _info is not null) _ = _performance.LoadAsync();
        if (id == "reinicializacao" && _info is not null) _ = _restart.LoadAsync();
        if (id == "ambiente" && _info is not null) _ = _environment.LoadAsync();
        if (id == "midia" && _info is not null) _ = _media.LoadAsync();
        if (id == "fisica" && _info is not null) _ = _physics.LoadAsync();
        if (id == "terreno" && _info is not null) _ = _terrain.LoadAsync();
        if (id == "propriedade" && _info is not null) _ = _property.LoadAsync();
        foreach (var section in Sections) PaintItem(section);
        RefreshFooter();
    }

    /// These tabs only the owner may change; the administrators can look, but the page ignores clicks and says why.
    private static readonly HashSet<string> OwnerOnlyTabs = ["reinicializacao", "ambiente", "midia", "fisica", "terreno"];
    private PanelContainer? _ownerBanner;
    public bool OwnerLockedForTesting(string id) => _pages.TryGetValue(id, out var page) && page.ProcessMode == ProcessModeEnum.Disabled;
    public bool OwnerBannerVisibleForTesting => _ownerBanner?.Visible == true;
    public void SetInfoForTesting(IslandInfo info) { _info = info; ApplyOwnerLock(_current); RefreshFooter(); }

    private void ApplyOwnerLock(string id)
    {
        var locked = OwnerOnlyTabs.Contains(id) && _info is { IsOwner: false };
        foreach (var (key, page) in _pages)
        {
            var lockThis = locked && key == id;
            page.ProcessMode = lockThis ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit; page.Modulate = new Color(1, 1, 1, lockThis ? .6f : 1f);
        }
        if (_ownerBanner is null)
        {
            _ownerBanner = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; _ownerBanner.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.AccentSelected, IslandUi.AccentPrimary, IslandUi.RadiusButton, 1, 14, 8));
            var label = IslandUi.Text("Só o dono da ilha pode alterar esta aba. Você pode ver as informações.", 14, IslandUi.TextPrimary, true); label.MouseFilter = Control.MouseFilterEnum.Ignore; _ownerBanner.AddChild(label);
            _ownerBanner.SetAnchorsPreset(Control.LayoutPreset.BottomWide); _ownerBanner.OffsetLeft = 16; _ownerBanner.OffsetRight = -16; _ownerBanner.OffsetTop = -52; _ownerBanner.OffsetBottom = -10; _pageHost.AddChild(_ownerBanner);
        }
        _ownerBanner.Visible = locked;
    }

    private void PaintItem(Section section)
    {
        var item = _menuButtons[section.Id]; var selected = section.Id == _current;
        var normal = selected ? IslandUi.Box(IslandUi.AccentSelected, IslandUi.AccentPrimary, IslandUi.RadiusButton, 1, 14, 0) : IslandUi.Box(new Color(0, 0, 0, 0), null, IslandUi.RadiusButton, 0, 14, 0);
        item.AddThemeStyleboxOverride("normal", normal); if (selected) { item.AddThemeStyleboxOverride("hover", normal); item.AddThemeStyleboxOverride("pressed", normal); item.AddThemeStyleboxOverride("focus", normal); }
        else { item.AddThemeStyleboxOverride("hover", IslandUi.Box(new Color(1, 1, 1, .07f), null, IslandUi.RadiusButton, 0, 14, 0)); item.AddThemeStyleboxOverride("pressed", IslandUi.Box(new Color(1, 1, 1, .10f), null, IslandUi.RadiusButton, 0, 14, 0)); item.AddThemeStyleboxOverride("focus", IslandUi.Box(new Color(0, 0, 0, 0), null, IslandUi.RadiusButton, 0, 14, 0)); }
        item.Icon = UiStyle.Icon(section.Icon, selected ? IslandUi.AccentPrimary : IslandUi.TextSecondary, 25);
        item.Text = "  " + section.Title + (section.Id == "geral" && _general.IsDirty || section.Id == "acesso" && _access.IsDirty ? "  •" : "");
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private LandPresence? Presence() => GetParent()?.GetNodeOrNull<LandManager>("LandManager")?.Presence;

    private void RequestTexture(Guid asset, Action<Texture2D?> done)
    {
        if (GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is { } cache) cache.Request(asset, done); else done(null);
    }

    public async Task LoadAsync()
    {
        if (_loadingNow || _island == Guid.Empty) return;
        _loadingNow = true; _failure.Visible = false; _loading.Visible = true; RefreshFooter();
        var reply = await _api!.Get(_island);
        _loadingNow = false; _loading.Visible = false;
        if (!reply.Ok || IslandInfo.Parse(reply.Body) is not { } info) { ShowFailure(reply.Ok ? "O servidor mandou dados de ilha que não deu para ler." : reply.Message); RefreshFooter(); return; }
        Apply(info); RefreshFooter();
    }

    private void ShowFailure(string message) { _failureText.Text = message; _failure.Visible = true; _loading.Visible = false; }

    private void Apply(IslandInfo info, bool preserveAccessDraft = false)
    {
        _info = info; ApplyOwnerLock(_current); _island = info.Id; _lastSyncAt = Time.GetTicksMsec() / 1000.0; _failure.Visible = false;
        var keepMode = preserveAccessDraft && _access.IsDirty ? _access.DraftMode : null;
        _bans.SetIsland(info.Id); _property.SetIsland(info.Id); _perms.SetIsland(info.Id); _objects.SetIsland(info.Id); _audit.SetIsland(info.Id); _admin.SetIsland(info.Id); _performance.SetIsland(info.Id); _restart.SetIsland(info.Id); _environment.SetIsland(info.Id); _media.SetIsland(info.Id); _physics.SetIsland(info.Id); _terrain.SetIsland(info.Id); _admin.SetClosedState(info.AccessMode == "closed"); _general.Fill(info); _access.Fill(info); if (keepMode is not null) _access.SetDraft(keepMode);
        _name.Text = Fit(info.Name, 23, 380); _name.TooltipText = info.Name; _name.MouseFilter = Control.MouseFilterEnum.Pass; _id.Text = Fit($"ID da Ilha: {info.Id}", 13, 340, UiStyle.Medium); _id.TooltipText = info.Id.ToString(); _visitors.Text = $"{info.Visitors} / {info.MaxPlayers}";
        var color = IslandUi.StateColor(info.State); _stateText.Text = IslandUi.StateText(info.State); _stateText.AddThemeColorOverride("font_color", color);
        ((PanelContainer)_statePill).AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(color, .15f), color, 7, 1, 11, 3)); ((ColorRect)_stateDot).Color = color;
        _accessText.Text = info.IsPublic ? "Aberta (Pública)" : "Privada"; ((ColorRect)_accessDot).Color = info.IsPublic ? IslandUi.Success : IslandUi.AccentSecondary;
        _thumbnail.Texture = null;
        if (info.CoverImageUrl.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(info.CoverImageUrl[6..], out var asset)) RequestTexture(asset, texture => { if (_info?.CoverImageUrl == info.CoverImageUrl) _thumbnail.Texture = texture; });
        else _thumbnail.Texture = UiStyle.Icon("map", IslandUi.TextMuted, 40);
        _updated.Text = SyncText();
    }


    /// <summary>The text cut with an ellipsis so it is at most this wide (so a very long island name cannot push the rest of the header off the window).</summary>
    private static string Fit(string text, int size, float width, Font? font = null)
    {
        font ??= UiStyle.Bold; if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X <= width) return text;
        var cut = text.Length; while (cut > 1 && font.GetStringSize(text[..cut] + "…", HorizontalAlignment.Left, -1, size).X > width) cut--;
        return text[..cut].TrimEnd() + "…";
    }
    private string SyncText()
    {
        var seconds = Time.GetTicksMsec() / 1000.0 - _lastSyncAt;
        return seconds < 10 ? "Última atualização: agora" : seconds < 90 ? $"Última atualização: há {(int)seconds} s" : seconds < 3600 ? $"Última atualização: há {(int)(seconds / 60)} min" : $"Última atualização: {DateTime.Now.AddSeconds(-seconds):HH:mm}";
    }

    private void RefreshFooter()
    {
        var canEdit = _info?.CanEdit == true && (_info.IsOwner || _current != "geral");
        _save.Disabled = !(canEdit && Dirty && !_saving && !_loadingNow);
        _restore.Disabled = !(_current == "geral" && canEdit && !_saving && !_loadingNow);
        _save.Text = _saving ? "Salvando…" : "Salvar Alterações";
        _save.TooltipText = !canEdit && _info is not null ? (_info.CanEdit ? "Só o dono da ilha pode alterar esta aba." : "Você não tem permissão para alterar esta ilha.") : !Dirty ? "Nenhuma alteração pendente." : "";
        _restore.TooltipText = _current == "geral" ? "" : "Só a aba Geral tem valores padrão.";
        foreach (var section in Sections) PaintItem(section);
    }

    private void RestoreDefaults()
    {
        Ask("Voltar categoria, classificação, idioma, capacidade e visibilidade aos valores padrão? O nome, a descrição e a imagem não mudam, e nada é salvo até você clicar em “Salvar Alterações”.", "Restaurar", () => _general.RestoreDefaults());
    }

    /// <summary>Saves what is pending: the General tab first, then the access mode. Success is announced only after the server accepted everything.</summary>
    public async Task SaveAsync()
    {
        if (_saving || _info is null || !Dirty) return;
        _saving = true; RefreshFooter();
        string? renamedTo = null;
        if (_general.IsDirty)
        {
            var reply = await _api!.SaveGeneral(_info.Id, _general.Draft(), _info.UpdatedAt);
            if (reply.Ok && IslandInfo.Parse(reply.Body) is { } saved) { if (saved.Name != _info.Name) renamedTo = saved.Name; Apply(saved, preserveAccessDraft: true); }
            else { _saving = false; RefreshFooter(); Refuse(reply, "as configurações da ilha"); return; }
        }
        if (_access.IsDirty)
        {
            var mode = _access.DraftMode;
            var reply = await _api!.AccessMode(_info.Id, mode, _info.UpdatedAt);
            if (!reply.Ok) { _saving = false; RefreshFooter(); Refuse(reply, "o modo de acesso"); if (renamedTo is not null) IslandRenamed?.Invoke(_info.Id, renamedTo); return; }
            var fresh = await _api.Get(_info.Id);
            if (fresh.Ok && IslandInfo.Parse(fresh.Body) is { } info) Apply(info);
            else _access.MarkSaved(mode);
            _ = _access.LoadAsync();
        }
        _saving = false; RefreshFooter(); Notify("Configurações da ilha atualizadas com sucesso.");
        if (renamedTo is not null) IslandRenamed?.Invoke(_info.Id, renamedTo);
    }

    /// <summary>Closes or reopens the island from the Administração tab: the same access mode as the Acesso tab, so it is audited and enforced the same way.</summary>
    private async Task<string?> SetClosedAsync(bool close)
    {
        if (_info is null) return "A ilha ainda não carregou.";
        if (Dirty) return "Salve ou descarte as alterações pendentes antes de fechar ou reabrir a ilha.";
        var reply = await _api!.AccessMode(_info.Id, close ? "closed" : (_info.IsPublic ? "public" : "invite"), _info.UpdatedAt);
        if (!reply.Ok) return reply.Message;
        var fresh = await _api.Get(_info.Id); if (fresh.Ok && IslandInfo.Parse(fresh.Body) is { } info) Apply(info);
        return null;
    }

    private void Refuse(IslandReply reply, string what)
    {
        if (reply.Conflict) { Ask("Outra pessoa alterou a ilha antes de você. Recarregar mostra o que ela mudou, mas descarta as suas alterações. Quer recarregar agora?", "Recarregar", () => _ = LoadAsync()); return; }
        if (reply.Fields.Count > 0) _general.ShowErrors(reply.Fields);
        Notify(reply.Ok ? "O servidor respondeu algo que não deu para ler. Tente de novo." : $"Não foi possível salvar {what}. {reply.Message}", error: true);
    }

    public void Notify(string text, bool error = false)
    {
        _toast.Text = text; _toast.AddThemeColorOverride("font_color", error ? new Color("#FFC2C5") : IslandUi.TextPrimary);
        _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(error ? new Color("#3A1A1F") : new Color("#0E3A47"), error ? IslandUi.Error : IslandUi.AccentPrimary, 10, 1, 16, 9));
        _toast.Visible = true; _toastLeft = error ? 6 : 3.5;
        _toast.ResetSize(); var panel = _panel.GetGlobalRect(); _toast.GlobalPosition = new Vector2(panel.Position.X + (panel.Size.X - _toast.Size.X) / 2, panel.End.Y - 110);
    }
}
