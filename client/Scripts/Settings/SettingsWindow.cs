using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Scripts.Settings;

/// <summary>
/// The settings window (client/Assets/ui reference): header, a side bar with the 18 categories, search and profiles, the page of the category with its tabs,
/// and a footer with Restaurar padrão / Descartar / Aplicar alterações. Every control reads and writes a <see cref="SettingsStore"/>: nothing here keeps
/// a value of its own, so what is shown is what the store holds (pending or applied).
/// </summary>
public partial class SettingsWindow : Control
{
    private const int HeaderHeight = 64, FooterHeight = 80;
    /// <summary>The page needs this much width (interface units) to show two columns of cards; narrower, they stack.</summary>
    public const float TwoColumnWidth = 1400f;

    private static readonly string[] CategoryIcons = ["user", "monitor", "volume", "mic", "move", "camera", "avatar", "chat", "shield", "bell", "bag", "hammer", "globe", "code", "wifi", "grid", "eye", "chart"];

    private static readonly string[] Subtitles =
    [
        "Idioma, conta, janela e inicialização do LifeVerse", "Qualidade visual, renderização e otimização do LifeVerse", "Volume, saída e comportamento do som", "Microfone, voz e indicadores de fala",
        "Teclas, movimentação e sensibilidade", "Câmera, campo de visão e modo foto", "Animações, nomes e simplificação de avatares", "Conversas, amigos, grupos e convites",
        "Visibilidade, bloqueios e permissões", "Alertas, sons e modo não perturbe", "Ordenação, prévias e cache do inventário", "Grade, encaixe, eixos e histórico de construção",
        "Mapa, favoritos, teleportes e carregamento de ilhas", "Console Lua, editor e permissões de scripts", "Conexão, reconexão e diagnóstico de rede", "Escala, transparência, barras e layout da HUD",
        "Texto, contraste, movimento e navegação por teclado", "Versão, logs, cache e diagnóstico"
    ];

    private static readonly string[] Planned =
    [
        "Idioma, região, formato de data, inicialização, ilha inicial, atualizações, informações da conta e sessões.", "", "Áudio por tipo (ambiente, efeitos, música, mídia, interface), saída de áudio, áudio espacial e silenciar em segundo plano.",
        "Dispositivos de entrada e saída, teste do microfone, push-to-talk, sensibilidade, volume de usuários e indicadores de fala.", "Remapear teclas com detecção de conflitos, sensibilidade do mouse e inversão de eixos.",
        "Primeira e terceira pessoa, distância, campo de visão, suavização, câmera livre e modo foto.", "Exibição de nomes, títulos, indicadores de voz, animações e simplificação visual.",
        "Transparência, histórico, fontes, notificações de chat, amigos, grupos e convites.", "Visibilidade online, mensagens privadas, convites, bloqueios e permissões de scripts e mídia.",
        "Alertas de mensagens, grupos, amizades, teleportes, transações e scripts; sons, duração e não perturbe.", "Ordenação, visualização, miniaturas, filtros, confirmações de exclusão e cache.",
        "Grade, encaixe, unidades, eixos, duplicação, histórico e indicadores de impacto da ilha.", "Mapa, minimapa, bússola, favoritos, coordenadas e convites de teleporte.",
        "Tema e fonte do editor, autocompletar, erros e avisos, e revogação de autorizações.", "Estado da conexão, reconexão automática e diagnóstico.",
        "Escala, modo, opacidades, barras, minimapa, chat e perfis de HUD (hoje em Eu › Interface…).", "Escala de texto, alto contraste, redução de movimento e de flashes, navegação por teclado.",
        "Versão do cliente, logs, limpeza de cache, exportação de diagnóstico e restauração de padrões."
    ];

    private static readonly string[] GraphicsTabs = ["Geral", "Qualidade", "Mundo e avatares", "Otimização", "Estatísticas"];

    private static readonly Dictionary<string, string> PresetText = new()
    {
        ["verylow"] = "Sem sombras nem efeitos de tela. Para computadores bem simples: máxima fluidez com imagem básica.",
        ["low"] = "Sombras duras e curtas, sem efeitos de tela. Leve, para computadores modestos.",
        ["medium"] = "Suavização das bordas, oclusão ambiente e brilho leves. Bom equilíbrio em placas intermediárias.",
        ["high"] = "Ótimo equilíbrio entre qualidade visual e desempenho: sombras suaves e os principais efeitos.",
        ["ultra"] = "Qualidade máxima: sombras longas e suaves, iluminação indireta e suavização 4x. Pede uma placa de vídeo potente."
    };

    private readonly SettingsStore _store;
    private readonly PanelContainer _frame = new();
    private readonly SettingsDialog _dialog = new();
    private readonly Dictionary<SettingCategory, Button> _categoryButtons = [];
    private readonly Dictionary<string, (Control Row, Action Refresh)> _rows = [];
    private PanelContainer _header = null!, _sidebar = null!, _footer = null!;
    private HBoxContainer _bodyRow = null!;
    private ScrollContainer _categoryScroll = null!, _pageScroll = null!;
    private VBoxContainer _pageHost = null!;
    private LineEdit _search = null!;
    private UiTabs _tabs = null!;
    private Label _title = null!, _subtitle = null!, _footerNote = null!;
    private TextureRect _categoryIcon = null!;
    private Button _apply = null!, _discard = null!, _restore = null!, _maximizeButton = null!;
    private OptionButton _profileChoice = null!, _scope = null!;
    private MenuButton _profileMenu = null!;
    private BoxContainer? _columns;
    private SettingCategory _category = SettingCategory.Graphics;
    private string _tab = "Geral";
    private bool _maximized, _minimized, _dragging, _syncing, _searching;
    private Vector2 _dragOffset;
    private double _flash;
    private MicrophoneTest _mic = null!;
    private LevelMeter? _micMeter;
    private Button? _micButton;
    private Label? _micState;

    public event Action? Opened, Closed;
    public SettingCategory Category => _category;
    public string Tab => _tab;
    public SettingsStore Store => _store;
    public SettingsDialog Dialog => _dialog;
    public bool Maximized => _maximized;
    public bool Minimized => _minimized;
    public Rect2 FrameRect => new(_frame.Position, _frame.Size);
    public bool SearchActive => _searching;
    public LineEdit SearchField => _search;
    public Button ApplyButton => _apply;
    public Button DiscardButton => _discard;
    public Button RestoreButton => _restore;
    public Label FooterNote => _footerNote;
    public IReadOnlyDictionary<SettingCategory, Button> CategoryButtons => _categoryButtons;
    public UiTabs Tabs => _tabs;
    public IReadOnlyCollection<string> RowIds => _rows.Keys;
    public Control? RowOf(string id) => _rows.TryGetValue(id, out var r) ? r.Row : null;
    public Control PageHost => _pageHost;
    public OptionButton ProfileChoice => _profileChoice;
    public Control SidebarForTesting => _sidebar;
    public bool TwoColumns => _columns is { Vertical: false };
    public double ScrollOfCategoriesForTesting => _categoryScroll.ScrollVertical;

    public SettingsWindow(SettingsStore store) { _store = store; }

    public override void _Ready()
    {
        Name = "SettingsWindow"; Visible = false; MouseFilter = MouseFilterEnum.Ignore; SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _frame.Name = "Frame"; _frame.MouseFilter = MouseFilterEnum.Stop;
        var frameStyle = UiStyle.Box(new Color(SettingsUi.Frame, .96f), new Color(UiStyle.Accent, .55f), 18, 2, 0, 0); frameStyle.ShadowColor = new Color(0, 0, 0, .55f); frameStyle.ShadowSize = 24;
        _frame.AddThemeStyleboxOverride("panel", frameStyle); _frame.ClipContents = true;
        AddChild(_frame);
        var root = new VBoxContainer(); root.AddThemeConstantOverride("separation", 0); _frame.AddChild(root);
        root.AddChild(BuildHeader());
        _bodyRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill }; _bodyRow.AddThemeConstantOverride("separation", 0); root.AddChild(_bodyRow);
        _bodyRow.AddChild(BuildSidebar());
        _bodyRow.AddChild(BuildContent());
        root.AddChild(BuildFooter());
        AddChild(_dialog);
        _mic = new MicrophoneTest { Name = "MicrophoneTest" }; AddChild(_mic);
        GetViewport().SizeChanged += () => { if (Visible) CallDeferred(nameof(LayoutFrame)); };
        Resized += LayoutFrame;
        SelectCategory(SettingCategory.Graphics, "Geral", focus: false);
        _store.PendingChanged += RefreshAll;
        Sync ??= ReferenceEquals(_store, SettingsService.Store) ? AccountSyncService.Instance : null;
        if (Sync is not null) Sync.Changed += () => { RefreshScope(); RefreshAll(); };
        RefreshProfiles(); RefreshScope(); RefreshAll();
    }

    // ---- account ---------------------------------------------------------------------------------------------------------------

    /// <summary>The account sync of this window (null when the window shows a store that is not the game's own, such as a test store).</summary>
    public AccountSync? Sync { get; set; }
    public OptionButton ScopeForTesting => _scope;

    private void RefreshScope()
    {
        if (Sync is null) { _scope.SetItemDisabled(1, true); _scope.SetItemTooltip(1, "A sincronização com a conta não está disponível aqui."); return; }
        _scope.SetItemDisabled(1, !Sync.SignedIn);
        _scope.SetItemTooltip(1, Sync.SignedIn ? "Interface, chat, controles, câmera, construção e acessibilidade seguem a sua conta em qualquer dispositivo. Gráficos, tela, áudio e desempenho ficam só neste computador."
                                               : "Entre na sua conta para sincronizar as configurações.");
        _scope.Select(Sync.Enabled ? 1 : 0);
    }

    private void OnScope(long index)
    {
        if (Sync is null) { RefreshScope(); return; }
        if (index == 0) { Sync.TurnOff(); return; }
        if (!Sync.SignedIn) { RefreshScope(); _dialog.Ask("Conta", "Entre na sua conta para sincronizar as configurações.", [("Entendi", UiStyle.ButtonKind.Primary, null)]); return; }
        if (Sync.Enabled) return;
        _dialog.Ask("Sincronizar com a conta", "Interface, chat, controles, câmera, construção, acessibilidade e privacidade passam a seguir a sua conta em qualquer dispositivo. Gráficos, tela, áudio, desempenho e limites ficam só neste computador. Nenhuma senha ou dado pessoal é enviado.",
            [("Sincronizar", UiStyle.ButtonKind.Primary, EnableSync), ("Cancelar", UiStyle.ButtonKind.Tool, RefreshScope)]);
    }

    public void EnableSync()
    {
        Sync!.Fetch(snapshot =>
        {
            if (snapshot.Result != SyncResult.Ok) { RefreshScope(); _dialog.Ask("Conta", snapshot.Message.Length > 0 ? snapshot.Message : "Não foi possível falar com a conta agora.", [("Entendi", UiStyle.ButtonKind.Primary, null)]); return; }
            if (snapshot.Values.Count == 0) { Sync.PushAll(snapshot.Revision, OnPushed); return; }
            AskWhichSide(snapshot);
        });
    }

    private void AskWhichSide(AccountSnapshot snapshot) => _dialog.Ask("Configurações diferentes",
        $"A conta já tem {snapshot.Values.Count} preferência(s) salvas em outro momento. Quer usar as da conta neste dispositivo, ou substituir as da conta pelas deste dispositivo?",
        [("Usar as da conta", UiStyle.ButtonKind.Primary, () => { Sync!.UseAccount(snapshot); RefreshAll(); }), ("Substituir pelas deste dispositivo", UiStyle.ButtonKind.Tool, () => Sync!.PushAll(snapshot.Revision, OnPushed)), ("Cancelar", UiStyle.ButtonKind.Tool, RefreshScope)]);

    private void OnPushed(AccountSnapshot snapshot)
    {
        RefreshScope(); RefreshAll();
        if (snapshot.Result == SyncResult.Conflict) AskWhichSide(snapshot);
        else if (snapshot.Result is SyncResult.Invalid) _dialog.Ask("Conta", snapshot.Message, [("Entendi", UiStyle.ButtonKind.Primary, null)]);
    }

    // ---- header ---------------------------------------------------------------------------------------------------------------

    private Control BuildHeader()
    {
        _header = new PanelContainer { CustomMinimumSize = new Vector2(0, HeaderHeight), MouseDefaultCursorShape = CursorShape.Move };
        var style = UiStyle.Box(SettingsUi.HeaderFill, new Color(SettingsUi.Line, .5f), 18, 0, 22, 0); style.CornerRadiusBottomLeft = style.CornerRadiusBottomRight = 0; style.BorderWidthBottom = 1;
        _header.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); _header.AddChild(row);
        row.AddChild(UiStyle.Brand(false, 38));
        row.AddChild(SettingsUi.Text("LifeVerse", 28, bold: true));
        row.AddChild(new Slash());
        row.AddChild(UiStyle.IconRect("settings", UiStyle.Accent, 34));
        row.AddChild(SettingsUi.Text("Configurações do LifeVerse", 30, bold: true));
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var minimize = SettingsUi.IconButton("minus", "Minimizar", 44); minimize.Pressed += () => { _minimized = !_minimized; ApplyWindowState(); };
        _maximizeButton = SettingsUi.IconButton("expand", "Maximizar", 44); _maximizeButton.Pressed += () => { _maximized = !_maximized; _minimized = false; _dragOffset = Vector2.Zero; ApplyWindowState(); };
        var close = SettingsUi.IconButton("close", "Fechar", 44); close.Pressed += RequestClose;
        row.AddChild(minimize); row.AddChild(_maximizeButton); row.AddChild(close);
        _header.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) { _dragging = button.Pressed && !_maximized; if (button.DoubleClick) { _maximized = !_maximized; _dragOffset = Vector2.Zero; ApplyWindowState(); } }
            else if (input is InputEventMouseMotion motion && _dragging) { _dragOffset += motion.Relative; LayoutFrame(); }
        };
        return _header;
    }

    private sealed partial class Slash : Control
    {
        public Slash() { CustomMinimumSize = new Vector2(18, 40); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw() => DrawLine(new Vector2(Size.X - 3, 2), new Vector2(3, Size.Y - 2), new Color(SettingsUi.Line, .8f), 2f, true);
    }

    private void ApplyWindowState()
    {
        _bodyRow.Visible = !_minimized; _footer.Visible = !_minimized;
        _maximizeButton.Icon = UiStyle.Icon(_maximized ? "copy" : "expand", Colors.White, 22); _maximizeButton.TooltipText = _maximized ? "Restaurar" : "Maximizar";
        LayoutFrame();
    }

    /// <summary>The frame is about 92% of the screen, centered, and never leaves it: its header stays reachable. Maximized, it fills the screen.</summary>
    public void LayoutFrame()
    {
        if (_frame is null || !IsInsideTree()) return;
        var view = GetViewportRect().Size; if (view.X < 200 || view.Y < 200) return;
        var margin = _maximized ? 8f : 24f; var room = view - new Vector2(margin * 2, margin * 2);
        var size = _maximized ? room : new Vector2(Mathf.Min(view.X * .92f, 2300f), Mathf.Min(view.Y * .935f, 1300f)).Min(room);
        if (_minimized) size.Y = HeaderHeight;
        var position = (view - size) / 2 + (_maximized ? Vector2.Zero : _dragOffset);
        position = new Vector2(Mathf.Clamp(position.X, 120 - size.X, view.X - 120), Mathf.Clamp(position.Y, 0, view.Y - HeaderHeight));
        _dragOffset = _maximized ? Vector2.Zero : position - (view - size) / 2;
        _frame.SetAnchorsPreset(LayoutPreset.TopLeft); _frame.Position = position; _frame.Size = size;
        _wanted = size; CallDeferred(nameof(SettleFrame)); CallDeferred(nameof(SettleFrame));
        _sidebar.CustomMinimumSize = new Vector2(Mathf.Clamp(size.X * .235f, 250f, 470f), 0);
        CallDeferred(nameof(UpdateColumns));
    }

    /// <summary>Scrolls the side bar just enough to show the selected category (after the list has been laid out).</summary>
    private async void ShowCategoryButton(Button button)
    {
        if (!IsInsideTree()) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);      // the size of the list settles a frame or two after the window is built
        if (!IsInstanceValid(button) || !IsInstanceValid(_categoryScroll)) return;
        var top = (int)button.Position.Y; var bottom = top + (int)button.Size.Y; var height = (int)_categoryScroll.Size.Y;
        if (top < _categoryScroll.ScrollVertical) _categoryScroll.ScrollVertical = top;
        else if (bottom > _categoryScroll.ScrollVertical + height) _categoryScroll.ScrollVertical = bottom - height;
    }

    private Vector2 _wanted;
    /// <summary>The minimum size of the content settles a frame after the pages are built; the frame is set to its size again then, so it never stays taller than the screen.</summary>
    private void SettleFrame() { if (_frame is not null && IsInstanceValid(_frame) && _frame.Size != _wanted) _frame.Size = _wanted; }

    // ---- side bar -------------------------------------------------------------------------------------------------------------

    private Control BuildSidebar()
    {
        _sidebar = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        var style = UiStyle.Box(new Color(0, 0, 0, .12f), new Color(SettingsUi.Line, .35f), 0, 0, 16, 14); style.BorderWidthRight = 1; _sidebar.AddThemeStyleboxOverride("panel", style);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); _sidebar.AddChild(column);

        var field = new PanelContainer(); field.AddThemeStyleboxOverride("panel", UiStyle.Box(SettingsUi.Inner, new Color(SettingsUi.Line, .7f), 12, 1, 12, 4));
        var searchRow = new HBoxContainer(); searchRow.AddThemeConstantOverride("separation", 8); field.AddChild(searchRow);
        searchRow.AddChild(UiStyle.IconRect("search", SettingsUi.TextSub, 22));
        _search = new LineEdit { PlaceholderText = "Buscar configurações...", Flat = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = false, CustomMinimumSize = new Vector2(0, 40) };
        _search.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); _search.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); _search.AddThemeFontSizeOverride("font_size", 18);
        _search.TextChanged += RunSearch;
        searchRow.AddChild(_search);
        var hint = new PanelContainer(); hint.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(1, 1, 1, .07f), new Color(SettingsUi.Line, .6f), 6, 1, 8, 2)); hint.AddChild(SettingsUi.Text("Ctrl + F", 14, SettingsUi.TextSub)); hint.MouseFilter = MouseFilterEnum.Ignore;
        searchRow.AddChild(hint);
        column.AddChild(field);

        _categoryScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = false };
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 4); _categoryScroll.AddChild(list);
        var group = new ButtonGroup();
        for (var i = 0; i < SettingCategories.Labels.Length; i++)
        {
            var category = (SettingCategory)i; var button = CategoryButton(SettingCategories.Labels[i], CategoryIcons[i], group);
            button.Pressed += () => { if (_searching) ClearSearch(); SelectCategory(category, null); };
            button.FocusEntered += () => ShowCategoryButton(button);      // keyboard navigation keeps the focused category in view
            list.AddChild(button); _categoryButtons[category] = button;
        }
        column.AddChild(_categoryScroll);

        var profile = new PanelContainer(); profile.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .18f), new Color(SettingsUi.Line, .45f), 12, 1, 12, 10));
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8); profile.AddChild(box);
        box.AddChild(SettingsUi.Text("Perfil de configurações", 16, SettingsUi.TextSub));
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 8); box.AddChild(line);
        _profileChoice = SettingsUi.Option(new Vector2(0, 40)); _profileChoice.SizeFlagsHorizontal = SizeFlags.ExpandFill; _profileChoice.TooltipText = "Perfil de configurações em uso";
        _profileChoice.ItemSelected += OnProfileSelected; line.AddChild(_profileChoice);
        var add = SettingsUi.IconButton("plus", "Criar perfil"); add.Pressed += () => AskProfileName("Criar perfil", "Nome do novo perfil:", "", true, (name, copy) => _store.Create(name, copy)); line.AddChild(add);
        _profileMenu = new MenuButton { Icon = UiStyle.Icon("dots", Colors.White, 22), TooltipText = "Renomear, duplicar, excluir, exportar e importar perfis", CustomMinimumSize = new Vector2(40, 40), FocusMode = FocusModeEnum.All, ExpandIcon = false, Flat = false };
        UiStyle.StyleButton(_profileMenu, UiStyle.ButtonKind.Tool, 6, 6, 10);
        var popup = _profileMenu.GetPopup(); popup.AddItem("Renomear…", 0); popup.AddItem("Duplicar…", 1); popup.AddItem("Excluir", 2); popup.AddSeparator(); popup.AddItem("Exportar para um arquivo…", 3); popup.AddItem("Importar de um arquivo…", 4);
        _profileMenu.AboutToPopup += () => { var isDefault = _store.ActiveProfile == SettingsStore.DefaultProfile; popup.SetItemDisabled(popup.GetItemIndex(0), isDefault); popup.SetItemDisabled(popup.GetItemIndex(2), isDefault); };
        popup.IdPressed += OnProfileMenu; line.AddChild(_profileMenu);
        column.AddChild(profile);
        return _sidebar;
    }

    private static Button CategoryButton(string text, string icon, ButtonGroup group)
    {
        var button = new Button { Text = text, Icon = UiStyle.Icon(icon, Colors.White, 24), ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left, ExpandIcon = false, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 46), ClipText = true };
        StyleCategory(button); return button;
    }

    private static void StyleCategory(Button button)
    {
        StyleBoxFlat Box(Color fill, Color border, int left, int width) { var b = UiStyle.Box(fill, border, 10, 0, 14, 6); b.BorderWidthLeft = left; b.SetBorderWidthAll(width); b.BorderWidthLeft = Math.Max(left, width); return b; }
        var selected = Box(new Color(UiStyle.AccentFill, .9f), UiStyle.Accent, 4, 0); selected.ShadowColor = new Color(UiStyle.Accent, .22f); selected.ShadowSize = 6;
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, ContentMarginLeft = 14, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 });
        button.AddThemeStyleboxOverride("hover", Box(new Color(1, 1, 1, .08f), UiStyle.Accent, 0, 0));
        button.AddThemeStyleboxOverride("pressed", selected); button.AddThemeStyleboxOverride("hover_pressed", selected);
        button.AddThemeStyleboxOverride("focus", Box(new Color(1, 1, 1, .05f), UiStyle.Accent, 0, 2));
        foreach (var color in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_hover_pressed_color", "icon_focus_color" }) button.AddThemeColorOverride(color, SettingsUi.TextMain);
        button.AddThemeFontOverride("font", UiStyle.Medium); button.AddThemeFontSizeOverride("font_size", 19); button.AddThemeConstantOverride("h_separation", 14); button.AddThemeConstantOverride("icon_max_width", 24);
    }

    // ---- content: header, tabs, pages ----------------------------------------------------------------------------------------------

    private Control BuildContent()
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 14);
        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) margin.AddThemeConstantOverride(side, 22);
        margin.AddChild(column);

        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 16); column.AddChild(head);
        _categoryIcon = UiStyle.IconRect("monitor", UiStyle.Accent, 48); head.AddChild(_categoryIcon);
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; titles.AddThemeConstantOverride("separation", 2); head.AddChild(titles);
        _title = SettingsUi.Text("", 34, bold: true); _subtitle = SettingsUi.Text("", 19, SettingsUi.TextSub);
        foreach (var label in new[] { _title, _subtitle }) { label.ClipText = true; label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; label.SizeFlagsHorizontal = SizeFlags.ExpandFill; }      // a long text never widens the window
        titles.AddChild(_title); titles.AddChild(_subtitle);
        _scope = SettingsUi.Option(new Vector2(330, 46)); _scope.TooltipText = "Onde as configurações valem";
        _scope.AddIconItem(UiStyle.Icon("monitor", Colors.White, 20), "Aplicação: Local (este dispositivo)"); _scope.AddItem("Aplicação: Conta (todos os dispositivos)"); _scope.ItemSelected += OnScope;
        head.AddChild(_scope);

        _tabs = new UiTabs { Flat = false, FontSize = 19, Focusable = true }; _tabs.TabChanged += index => { if (!_syncing) SelectTab(index); };
        column.AddChild(_tabs);

        _pageScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _pageHost = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _pageHost.AddThemeConstantOverride("separation", 14);
        _pageScroll.AddChild(_pageHost); column.AddChild(_pageScroll);
        _pageScroll.Resized += UpdateColumns;
        return margin;
    }

    private IReadOnlyList<string> TabsOf(SettingCategory category)
    {
        if (category == SettingCategory.Graphics) return GraphicsTabs;
        var tabs = _store.Definitions.Where(d => d.Category == category).Select(d => d.Tab).Distinct().ToList();
        return tabs.Count > 0 ? tabs : ["Geral"];
    }

    /// <summary>Shows a category (on a tab, or its first one) and keeps its button visible in the side bar.</summary>
    public void SelectCategory(SettingCategory category, string? tab, bool focus = true)
    {
        _category = category;
        var tabs = TabsOf(category); _tab = tab is not null && tabs.Contains(tab) ? tab : tabs[0];
        _syncing = true;
        foreach (var other in _categoryButtons.Values) other.SetPressedNoSignal(false);
        if (_categoryButtons.TryGetValue(category, out var button)) { button.SetPressedNoSignal(true); CallDeferred(nameof(ShowCategoryButton), button); if (focus && IsVisibleInTree()) button.GrabFocus(); }
        _title.Text = SettingCategories.LabelOf(category); _subtitle.Text = Subtitles[(int)category];
        _categoryIcon.Texture = UiStyle.Icon(CategoryIcons[(int)category], UiStyle.Accent, 48);
        _tabs.Clear(); foreach (var name in tabs) _tabs.AddTab(name); _tabs.Select(tabs.ToList().IndexOf(_tab), notify: false);
        _tabs.Visible = tabs.Count > 1;
        _syncing = false;
        BuildPage();
    }

    private void SelectTab(int index) { var tabs = TabsOf(_category); if (index < 0 || index >= tabs.Count) return; _tab = tabs[index]; BuildPage(); }

    private void BuildPage()
    {
        foreach (var child in _pageHost.GetChildren()) { _pageHost.RemoveChild(child); child.QueueFree(); }
        _statsActive = false; _statsLabels.Clear(); RenderStats.Request("settings", false);
        _mic?.Stop(); _micMeter = null; _micButton = null; _micState = null;
        _infoRows.Clear();
        _rows.Clear(); _columns = null;
        var defs = _store.Definitions.Where(d => d.Category == _category && d.Tab == _tab).ToList();
        if (_category == SettingCategory.Graphics && _tab == "Estatísticas") { BuildStatsPage(); RefreshAll(); return; }
        if (defs.Count == 0) { _pageHost.AddChild(EmptyPage()); RefreshAll(); return; }
        _columns = new BoxContainer { Vertical = false, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _columns.AddThemeConstantOverride("separation", 18);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin }; left.AddThemeConstantOverride("separation", 18);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin }; right.AddThemeConstantOverride("separation", 18);
        _columns.AddChild(left); _columns.AddChild(right);
        foreach (var group in defs.GroupBy(d => (d.Group, d.Column)))
        {
            var target = group.Key.Column == 0 ? left : right;
            if (group.Count() == 1 && group.First().Id == "graphics.preset") { target.AddChild(BuildPresetCard(group.First())); continue; }
            var card = SettingsUi.Card(group.Key.Group.Length > 0 ? group.Key.Group : _tab, null, out var body); target.AddChild(card);
            foreach (var def in group)
            {
                if (def.Kind == SettingKind.Key) { if (KeyBindings.Parse(def.Id) is { Slot: 1 } && defs.FirstOrDefault(d => d.Id == KeyBindings.IdOf(KeyBindings.Parse(def.Id)!.Value.Action, 2)) is { } second) body.AddChild(BuildKeyRow(def, second)); continue; }
                body.AddChild(BuildRow(def));
            }
            if (group.Key.Group == "Tela e exibição") body.AddChild(BuildRefreshRateRow());
        }
        if (_category == SettingCategory.Voice && _tab == "Microfone") right.AddChild(BuildMicCard());
        if (right.GetChildCount() == 0) { _columns.RemoveChild(right); right.QueueFree(); }
        _pageHost.AddChild(_columns);
        RefreshAll(); CallDeferred(nameof(UpdateColumns));
    }

    private Control EmptyPage()
    {
        var card = SettingsUi.Card(_category == SettingCategory.Graphics ? $"{_tab}: ainda sem opções" : "Ainda sem opções nesta categoria", null, out var body);
        var message = _category == SettingCategory.Graphics
            ? "As opções desta aba chegam na próxima etapa. Nada aqui é simulado: cada opção só aparece quando o sistema que ela controla está ligado ao jogo."
            : "Esta categoria chega nas próximas etapas. Nada aqui é simulado: as opções só aparecem quando o sistema que elas controlam está ligado ao jogo.";
        var text = SettingsUi.Text(message, 18, SettingsUi.TextSub); text.AutowrapMode = TextServer.AutowrapMode.WordSmart; text.MouseFilter = MouseFilterEnum.Ignore; body.AddChild(text);
        if (Planned[(int)_category].Length > 0) { var planned = SettingsUi.Text($"Previsto: {Planned[(int)_category]}", 17, SettingsUi.TextSub); planned.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(planned); }
        return card;
    }

    /// <summary>Two columns when there is room, one when there is not.</summary>
    private void UpdateColumns()
    {
        if (_columns is null || !IsInstanceValid(_columns)) return;
        _columns.Vertical = _pageScroll.Size.X < TwoColumnWidth;
    }

    // ---- rows ---------------------------------------------------------------------------------------------------------------------

    private Control BuildRow(SettingDefinition d)
    {
        var help = d.Description + (d.RestartRequired ? "\n\nExige reiniciar o cliente para valer." : "") + (AccountSync.IsSynced(d.Id) ? "\n\nSegue a sua conta quando a sincronização está ativa." : "") + (d.Unavailable is not null ? $"\n\nIndisponível: {d.Unavailable}" : "");
        var name = SettingsUi.Text(d.Label, 18);
        Control row; Action refresh;
        switch (d.Kind)
        {
            case SettingKind.Info:
            {
                var value = SettingsUi.Text("", 18, UiStyle.Accent, true); value.HorizontalAlignment = HorizontalAlignment.Right; value.ClipText = true; value.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; value.MouseFilter = MouseFilterEnum.Stop;
                row = SettingsUi.Row(name, help, value, 360);
                refresh = () => { var text = d.InfoSource is null ? "—" : SafeInfo(d.InfoSource); value.Text = text; value.TooltipText = text; };
                _infoRows.Add(refresh);
                break;
            }
            case SettingKind.Action:
            {
                var button = SettingsUi.ActionButton(d.ButtonText, UiStyle.ButtonKind.Secondary, null, 230); button.Disabled = !d.IsAvailable;
                button.Pressed += () => RunAction(d);
                row = SettingsUi.Row(name, help, button);
                refresh = () => { };
                break;
            }
            case SettingKind.Bool:
            {
                var toggle = new CheckButton { FocusMode = FocusModeEnum.All, Disabled = !d.IsAvailable };
                toggle.Toggled += on => { if (!_syncing) _store.Set(d.Id, on); };
                row = SettingsUi.Row(name, help, toggle);
                refresh = () => toggle.SetPressedNoSignal((bool)_store.Effective(d.Id));
                break;
            }
            case SettingKind.Choice:
            {
                var choice = SettingsUi.Option(); choice.Disabled = !d.IsAvailable;
                choice.ItemSelected += index => { if (!_syncing && index >= 0 && index < d.CurrentChoices.Count) _store.Set(d.Id, d.CurrentChoices[(int)index].Id); };
                row = SettingsUi.Row(name, help, choice);
                refresh = () => FillChoice(choice, d);
                break;
            }
            case SettingKind.Int:
            case SettingKind.Float:
            {
                var box = SettingsUi.Slider(d.Min, d.Max, d.Step, v => d.Display(d.Kind == SettingKind.Int ? (int)Math.Round(v) : (float)v), out var slider, out var value);
                slider.Editable = d.IsAvailable;
                slider.ValueChanged += v => { if (!_syncing) _store.Set(d.Id, v); };
                row = SettingsUi.Row(name, help, box, 330);
                refresh = () => { slider.SetValueNoSignal(Convert.ToDouble(_store.Effective(d.Id))); value.Text = d.Display(_store.Effective(d.Id)); };
                break;
            }
            default:
            {
                var field = new LineEdit { Text = (string)_store.Effective(d.Id), Editable = d.IsAvailable, CustomMinimumSize = new Vector2(280, 40), MaxLength = d.MaxLength };
                field.TextSubmitted += text => { if (!_syncing) _store.Set(d.Id, text); }; field.FocusExited += () => { if (!_syncing) _store.Set(d.Id, field.Text); };
                row = SettingsUi.Row(name, help, field);
                refresh = () => { if (!field.HasFocus()) field.Text = (string)_store.Effective(d.Id); };
                break;
            }
        }
        var rowRefresh = refresh;
        refresh = () =>
        {
            rowRefresh();
            name.AddThemeColorOverride("font_color", _store.IsPending(d.Id) ? UiStyle.Orange : SettingsUi.TextMain);
            row.Modulate = d.IsAvailable ? Colors.White : new Color(1, 1, 1, .5f);
            if (d.Id == "display.resolution" && row is HBoxContainer r && r.GetChildren().OfType<OptionButton>().FirstOrDefault() is { } option)
            {
                var windowed = DisplayOptions.Shown("display.mode", (string)_store.Effective("display.mode")) == "window"; option.Disabled = !windowed;
                option.TooltipText = windowed ? "" : "Em tela cheia o jogo usa a resolução do monitor. Para aliviar a placa, use a escala de renderização.";
            }
        };
        if (d.Unavailable is not null) row.TooltipText = $"Indisponível: {d.Unavailable}";
        _rows[d.Id] = (row, refresh);
        return row;
    }

    private readonly List<Action> _infoRows = [];
    private double _infoTimer;
    private static string SafeInfo(Func<string> source) { try { return source(); } catch (Exception) { return "—"; } }

    /// <summary>Runs an action row: asks first when it has a question, then does it.</summary>
    public void RunAction(SettingDefinition d)
    {
        void Go() { try { SettingsActions.Run(d.Id, this); } catch (Exception exception) { Notify("Não foi possível", exception.Message); } RefreshAll(); }
        if (d.Confirm is null) { Go(); return; }
        _dialog.Ask(d.Label, d.Confirm, [(d.ButtonText, UiStyle.ButtonKind.Primary, Go), ("Cancelar", UiStyle.ButtonKind.Tool, null)]);
    }

    /// <summary>A message with an OK button (what an action reports).</summary>
    public void Notify(string title, string message) => Tell(title, message);

    private void FillChoice(OptionButton choice, SettingDefinition d)
    {
        var items = d.CurrentChoices; var stored = (string)_store.Effective(d.Id);
        var shown = d.Id.StartsWith("display.", StringComparison.Ordinal) ? DisplayOptions.Shown(d.Id, stored) : stored;
        _syncing = true;
        choice.Clear();
        foreach (var item in items) choice.AddItem(item.Label);
        var at = items.ToList().FindIndex(c => c.Id == shown);
        if (at < 0 && shown.Length > 0) { choice.AddItem(shown); at = items.Count; }
        choice.Select(at);
        _syncing = false;
    }

    private Control BuildRefreshRateRow()
    {
        var name = SettingsUi.Text("Taxa de atualização do monitor", 18);
        var value = SettingsUi.Text("", 18, SettingsUi.TextSub); value.HorizontalAlignment = HorizontalAlignment.Right;
        var rate = DisplayOptions.RefreshRate(DisplayOptions.CurrentScreen); value.Text = rate > 0 ? $"{rate} Hz" : "Não informada";
        return SettingsUi.Row(name, "Taxa de atualização que o monitor informa ao sistema. O jogo não a altera: use o limite de FPS e o V-Sync.", value, 280);
    }

    // ---- keys ----------------------------------------------------------------------------------------------------------------

    private Control BuildKeyRow(SettingDefinition first, SettingDefinition second)
    {
        var action = KeyBindings.Parse(first.Id)!.Value.Action;
        var name = SettingsUi.Text(first.Label, 18);
        var main = new KeyCaptureButton { TooltipText = "Tecla principal: clique e pressione a tecla. Esc cancela." };
        var alternative = new KeyCaptureButton { AllowClear = true, TooltipText = "Tecla alternativa: clique e pressione a tecla. Esc cancela; Backspace ou Delete remove." };
        main.Captured += key => AssignKey(first, key); alternative.Captured += key => AssignKey(second, key);
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 8); buttons.AddChild(main); buttons.AddChild(alternative);
        var row = SettingsUi.Row(name, first.Description + "\n\nPrincipal e alternativa: qualquer uma das duas faz a ação.", buttons);
        void Refresh()
        {
            main.KeyName = (string)_store.Effective(first.Id); alternative.KeyName = (string)_store.Effective(second.Id);
            var conflicts = KeyConflicts();
            foreach (var (button, id) in new[] { (main, first.Id), (alternative, second.Id) })
            {
                var clash = conflicts.TryGetValue((string)_store.Effective(id), out var users) && users.Count > 1 && (string)_store.Effective(id) is { Length: > 0 };
                button.AddThemeColorOverride("font_color", clash ? UiStyle.DangerBright : _store.IsPending(id) ? UiStyle.Orange : SettingsUi.TextMain);
                button.TooltipText = clash ? $"Esta tecla também faz: {string.Join(", ", users!.Where(u => u != action).Select(u => KeyBindings.Actions.First(a => a.Action == u).Label))}." : (id == first.Id ? "Tecla principal: clique e pressione a tecla. Esc cancela." : "Tecla alternativa: clique e pressione a tecla. Esc cancela; Backspace ou Delete remove.");
            }
            name.AddThemeColorOverride("font_color", _store.IsPending(first.Id) || _store.IsPending(second.Id) ? UiStyle.Orange : SettingsUi.TextMain);
        }
        _rows[first.Id] = (row, Refresh); _rows[second.Id] = (row, Refresh);
        return row;
    }

    /// <summary>For each key in use, which actions use it.</summary>
    public Dictionary<string, List<string>> KeyConflicts()
    {
        var map = new Dictionary<string, List<string>>();
        foreach (var d in _store.Definitions.Where(d => d.Kind == SettingKind.Key))
        {
            var key = (string)_store.Effective(d.Id); if (key.Length == 0 || KeyBindings.Parse(d.Id) is not { } parsed) continue;
            if (!map.TryGetValue(key, out var users)) map[key] = users = [];
            users.Add(parsed.Action);
        }
        return map;
    }

    /// <summary>The person pressed a key for a slot: reserved keys are refused, a key already in use asks to swap, otherwise it is a pending change.</summary>
    public void AssignKey(SettingDefinition def, string key)
    {
        var current = (string)_store.Effective(def.Id);
        if (key == current) { RefreshAll(); return; }
        if (key.Length > 0)
        {
            if (KeyBindings.Reserved.Contains(key, StringComparer.OrdinalIgnoreCase)) { Tell("Tecla reservada", $"A tecla {KeyBindings.Pretty(key)} é usada pelo próprio jogo (menus, chat, modo da interface ou apagar objetos) e não pode ser trocada."); RefreshAll(); return; }
            if (!KeyBindings.IsValid(key)) { Tell("Tecla não reconhecida", "O jogo não conhece essa tecla. Tente outra."); RefreshAll(); return; }
            var other = _store.Definitions.FirstOrDefault(d => d.Kind == SettingKind.Key && d.Id != def.Id && (string)_store.Effective(d.Id) == key);
            if (other is not null)
            {
                var label = KeyBindings.Actions.First(a => a.Action == KeyBindings.Parse(other.Id)!.Value.Action).Label;
                _dialog.Ask("Tecla já em uso", $"A tecla {KeyBindings.Pretty(key)} já faz «{label}». Trocar: «{label}» passa a usar {(current.Length > 0 ? KeyBindings.Pretty(current) : "nenhuma tecla neste espaço")} e esta ação passa a usar {KeyBindings.Pretty(key)}.",
                    [("Trocar", UiStyle.ButtonKind.Primary, () => { _store.Set(other.Id, current); _store.Set(def.Id, key); RefreshAll(); }), ("Cancelar", UiStyle.ButtonKind.Tool, () => RefreshAll())]);
                return;
            }
        }
        _store.Set(def.Id, key); RefreshAll();
    }

    // ---- the microphone test -------------------------------------------------------------------------------------------------------

    public MicrophoneTest Microphone => _mic;
    public LevelMeter? MicMeter => _micMeter;
    public Button? MicButton => _micButton;

    private Control BuildMicCard()
    {
        var card = SettingsUi.Card("Teste do microfone", "Liga o microfone só para mostrar o quanto você está falando alto. Nada é gravado nem enviado; o teste para sozinho ao sair desta aba. A linha laranja é o ponto em que o microfone aberto começa a enviar a sua voz.", out var body);
        _micState = SettingsUi.Text("Clique em Iniciar teste e fale normalmente.", 17, SettingsUi.TextSub); _micState.AutowrapMode = TextServer.AutowrapMode.WordSmart; _micState.MouseFilter = MouseFilterEnum.Ignore; body.AddChild(_micState);
        _micMeter = new LevelMeter(); body.AddChild(_micMeter);
        _micButton = SettingsUi.ActionButton("Iniciar teste", UiStyle.ButtonKind.Secondary, "mic", 200);
        _micButton.Pressed += ToggleMicTest; body.AddChild(_micButton);
        UpdateMicThreshold();
        return card;
    }

    private void UpdateMicThreshold() { if (_micMeter is not null && _store.Definition("voice.sensitivity") is not null) _micMeter.Threshold = SettingsAppliers.ThresholdFor(_store.GetEffective<int>("voice.sensitivity")) / MicScale; }
    private const float MicScale = .3f;

    public void ToggleMicTest()
    {
        if (_mic.Active) { _mic.Stop(); if (_micButton is not null) _micButton.Text = "Iniciar teste"; if (_micState is not null) _micState.Text = "Teste parado."; return; }
        if (_mic.Start()) { if (_micButton is not null) _micButton.Text = "Parar teste"; if (_micState is not null) _micState.Text = "Fale normalmente: a barra mostra o volume da sua voz."; }
        else if (_micState is not null) _micState.Text = _mic.Problem;
    }

    private Control BuildPresetCard(SettingDefinition d)

    {
        var card = SettingsUi.Card("Predefinição gráfica", d.Description, out var body);
        var choice = SettingsUi.Option(new Vector2(0, 46)); choice.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        choice.ItemSelected += index => { if (!_syncing && index >= 0 && index < d.Choices.Count) _store.Set(d.Id, d.Choices[(int)index].Id); };
        body.AddChild(choice);
        var preview = new HBoxContainer(); preview.AddThemeConstantOverride("separation", 18); body.AddChild(preview);
        var picture = new TextureRect { CustomMinimumSize = new Vector2(320, 180), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true };
        var frame = new PanelContainer(); frame.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(0, 0, 0, .3f), new Color(SettingsUi.Line, .6f), 10, 1, 2, 2)); frame.AddChild(picture); preview.AddChild(frame);
        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter }; info.AddThemeConstantOverride("separation", 6); preview.AddChild(info);
        var title = SettingsUi.Text("", 28, UiStyle.Accent, true); var text = SettingsUi.Text("", 18, SettingsUi.TextSub); text.AutowrapMode = TextServer.AutowrapMode.WordSmart; text.CustomMinimumSize = new Vector2(220, 0); text.MouseFilter = MouseFilterEnum.Ignore;
        info.AddChild(title); info.AddChild(text);
        _rows[d.Id] = (card, () =>
        {
            _syncing = true; choice.Clear();
            var effective = (string)_store.Effective(d.Id); var chosen = GraphicsQuality.Parse(effective); var resolved = GraphicsQuality.Resolve(chosen);
            for (var i = 0; i < d.Choices.Count; i++) choice.AddItem(d.Choices[i].Id == "auto" ? $"Automático (recomendada: {GraphicsQuality.Labels[(int)GraphicsQuality.Resolve(GraphicsQuality.Level.Auto)]})" : d.Choices[i].Label);
            choice.Select(Math.Max(0, d.Choices.ToList().FindIndex(c => c.Id == effective))); _syncing = false;
            var key = GraphicsQuality.Keys[(int)resolved];
            title.Text = chosen == GraphicsQuality.Level.Custom ? $"Personalizada" : GraphicsQuality.Labels[(int)resolved];
            text.Text = chosen == GraphicsQuality.Level.Custom ? $"Você ajustou opções à mão a partir de {GraphicsQuality.Labels[(int)resolved].ToLowerInvariant()}. Escolher uma predefinição de novo restaura os valores dela." : PresetText.GetValueOrDefault(key, "");
            picture.Texture = Thumbnail(key);
        });
        return card;
    }

    private static readonly Dictionary<string, Texture2D?> Thumbnails = [];
    private static Texture2D? Thumbnail(string key)
    {
        if (Thumbnails.TryGetValue(key, out var cached)) return cached;
        var path = $"res://Assets/ui/presets/{key}.jpg";
        return Thumbnails[key] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }

    /// <summary>Redraws every control of the page and the footer from the store.</summary>
    public void RefreshAll()
    {
        if (_apply is null) return;
        _syncing = true;
        foreach (var (_, refresh) in _rows.Values) refresh();
        _syncing = false;
        var pending = _store.HasPending; var count = _store.PendingIds.Count;
        _apply.Disabled = !pending; _discard.Disabled = !pending;
        var restart = _store.PendingIds.Any(id => _store.Definition(id)?.RestartRequired == true);
        _footerNote.Text = !pending ? (Sync is { Enabled: true } && Sync.Status.Length > 0 ? Sync.Status : "Algumas alterações podem exigir a reinicialização do cliente para serem aplicadas.")
            : restart ? $"{count} alteração(ões) não aplicada(s). Pelo menos uma exige reiniciar o cliente para valer."
            : $"{count} alteração(ões) não aplicada(s). Use Aplicar alterações para valer no jogo.";
        _footerNote.AddThemeColorOverride("font_color", pending ? UiStyle.Orange : SettingsUi.TextSub);
        var hasSettings = _store.Definitions.Any(d => d.Category == _category && d.IsAvailable); _restore.Disabled = !hasSettings;
        _restore.TooltipText = hasSettings ? $"Restaurar o padrão de «{SettingCategories.LabelOf(_category)}»" : "Esta categoria ainda não tem opções para restaurar.";
    }

    // ---- statistics (real readings only) ------------------------------------------------------------------------------------------

    private readonly Dictionary<string, Label> _statsLabels = [];
    private bool _statsActive;
    private double _statsTimer;
    public IReadOnlyDictionary<string, Label> StatLabels => _statsLabels;

    private void BuildStatsPage()
    {
        _statsActive = true; RenderStats.Request("settings", true);
        _columns = new BoxContainer { Vertical = false, SizeFlagsHorizontal = SizeFlags.ExpandFill }; _columns.AddThemeConstantOverride("separation", 18);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin }; left.AddThemeConstantOverride("separation", 18);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkBegin }; right.AddThemeConstantOverride("separation", 18);
        _columns.AddChild(left); _columns.AddChild(right);
        void Group(VBoxContainer into, string title, params (string Key, string Label, string Help)[] stats)
        {
            var card = SettingsUi.Card(title, null, out var body); into.AddChild(card);
            foreach (var (key, label, help) in stats)
            {
                var value = SettingsUi.Text("—", 18, UiStyle.Accent, true); value.HorizontalAlignment = HorizontalAlignment.Right; _statsLabels[key] = value;
                body.AddChild(SettingsUi.Row(SettingsUi.Text(label, 18), help, value, 260));
            }
        }
        Group(left, "Desempenho",
            ("fps", "FPS atual", "Quadros por segundo agora."), ("average", "FPS médio (5 s)", "Média dos últimos cinco segundos: mostra a tendência sem o balanço de cada quadro."),
            ("frame", "Tempo de quadro", "Quanto dura um quadro, calculado a partir dos FPS."), ("process", "Processamento (CPU)", "Tempo da lógica do jogo por quadro."),
            ("physics", "Física (CPU)", "Tempo da física por quadro."), ("render_cpu", "Renderização (CPU)", "Tempo que o processador gasta preparando o desenho de cada quadro."), ("render_gpu", "Renderização (GPU)", "Tempo que a placa de vídeo gasta desenhando cada quadro, medido pelo renderizador."));
        Group(left, "Memória",
            ("ram", "RAM do jogo", "Memória do computador usada pelo jogo."), ("vram", "VRAM em uso", "Memória de vídeo que o jogo usa (texturas, buffers e imagens de tela)."),
            ("vram_textures", "VRAM: texturas", "Parte da memória de vídeo ocupada por texturas."), ("vram_buffers", "VRAM: buffers", "Parte da memória de vídeo ocupada por geometria e dados."));
        Group(right, "Cena",
            ("objects", "Objetos desenhados", "Quantos objetos foram enviados à placa no último quadro."), ("triangles", "Triângulos", "Primitivas desenhadas no último quadro."), ("draw_calls", "Chamadas de desenho (draw calls)", "Quantas vezes o jogo pediu à placa para desenhar algo no último quadro. Muitas chamadas pesam no processador."),
            ("nodes", "Nós na árvore", "Quantos elementos o jogo mantém na cena."), ("resources", "Recursos carregados", "Texturas, malhas, materiais e outros recursos em memória."), ("orphans", "Nós órfãos", "Elementos criados e esquecidos fora da cena; devem ficar perto de zero."));
        Group(right, "Avatares",
            ("avatars", "Avatares desenhados", "Quantos avatares estão sendo desenhados (você e os outros que não estão ocultos)."), ("avatars_total", "Avatares na ilha", "Você e os outros avatares conectados à ilha."),
            ("avatars_detail", "Por nível de detalhe", "Quantos avatares estão em cada nível: completo, reduzido, básico e oculto (sem contar você)."));
        Group(right, "Sistema", ("adapter", "Placa de vídeo", "Nome da placa de vídeo informado pelo sistema."), ("renderer", "Renderizador", "Método e driver de renderização em uso."));
        _pageHost.AddChild(_columns);
        _statsTimer = 1; RefreshStats(); CallDeferred(nameof(UpdateColumns));
    }

    /// <summary>Reads the engine and writes the numbers into the page. Public so tests can drive it.</summary>
    public void RefreshStats()
    {
        if (!_statsActive || _statsLabels.Count == 0) return;
        var pt = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        var s = RenderStats.Read(GetTree().Root, GetTree().Root.GetNodeOrNull<Autoloads.NetworkManager>("NetworkManager"));
        string Ms(double v) => v > 0 ? string.Format(pt, "{0:0.0} ms", v) : "—";
        string Mb(double v) => string.Format(pt, "{0:N0} MB", v);
        string Count(long v) => v.ToString("N0", pt);
        void Put(string key, string text) { if (_statsLabels.TryGetValue(key, out var label)) label.Text = text; }
        Put("fps", string.Format(pt, "{0:0} FPS", s.Fps)); Put("average", string.Format(pt, "{0:0} FPS", s.AverageFps)); Put("frame", Ms(s.FrameMs)); Put("process", Ms(s.ProcessMs)); Put("physics", Ms(s.PhysicsMs));
        Put("render_cpu", Ms(s.CpuRenderMs)); Put("render_gpu", Ms(s.GpuRenderMs));
        Put("ram", Mb(s.RamMb)); Put("vram", Mb(s.VramMb)); Put("vram_textures", Mb(s.TextureMemoryMb)); Put("vram_buffers", Mb(s.BufferMemoryMb));
        Put("objects", Count(s.Objects)); Put("triangles", Count(s.Triangles)); Put("draw_calls", Count(s.DrawCalls)); Put("nodes", Count(s.Nodes)); Put("resources", Count(s.Resources)); Put("orphans", Count(s.OrphanNodes));
        Put("avatars", Count(s.AvatarsDrawn)); Put("avatars_total", Count(s.AvatarsTotal));
        var counts = GetTree().Root.GetNodeOrNull<Autoloads.NetworkManager>("NetworkManager")?.DetailCounts;
        string Level(Shared.Avatar.AvatarDetail d) => (counts is not null && counts.TryGetValue(d, out var n) ? n : 0).ToString(pt);
        Put("avatars_detail", $"{Level(Shared.Avatar.AvatarDetail.Full)} · {Level(Shared.Avatar.AvatarDetail.Reduced)} · {Level(Shared.Avatar.AvatarDetail.Basic)} · {Level(Shared.Avatar.AvatarDetail.Hidden)}");
        Put("adapter", s.Adapter); Put("renderer", s.Renderer);
    }

    // ---- footer ----------------------------------------------------------------------------------------------------------------


    private Control BuildFooter()
    {
        _footer = new PanelContainer { CustomMinimumSize = new Vector2(0, FooterHeight) };
        var style = UiStyle.Box(new Color(0, 0, 0, .14f), new Color(SettingsUi.Line, .45f), 18, 0, 22, 12); style.CornerRadiusTopLeft = style.CornerRadiusTopRight = 0; style.BorderWidthTop = 1; _footer.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 12); _footer.AddChild(row);
        row.AddChild(UiStyle.IconRect("info", SettingsUi.TextSub, 24));
        _footerNote = SettingsUi.Text("", 17, SettingsUi.TextSub); _footerNote.SizeFlagsHorizontal = SizeFlags.ExpandFill; _footerNote.AutowrapMode = TextServer.AutowrapMode.WordSmart; row.AddChild(_footerNote);
        _restore = SettingsUi.ActionButton("Restaurar padrão", UiStyle.ButtonKind.Tool, "reset", 190); _restore.Pressed += AskRestore;
        _discard = SettingsUi.ActionButton("Descartar", UiStyle.ButtonKind.Tool, "undo", 150); _discard.Pressed += () => { _store.Discard(); RefreshAll(); };
        _apply = SettingsUi.ActionButton("Aplicar alterações", UiStyle.ButtonKind.Primary, "check", 230); _apply.Pressed += ApplyChanges;
        row.AddChild(_restore); row.AddChild(_discard); row.AddChild(_apply);
        return _footer;
    }

    private void AskRestore()
    {
        var category = _category;
        _dialog.Ask("Restaurar padrão", $"Restaurar os valores padrão de «{SettingCategories.LabelOf(category)}»? Nada é aplicado até você usar Aplicar alterações; Descartar desfaz.",
            [("Restaurar", UiStyle.ButtonKind.Primary, () => { _store.ResetCategory(category); RefreshAll(); }), ("Cancelar", UiStyle.ButtonKind.Tool, null)]);
    }

    private static readonly string[] DisplayIds = ["display.monitor", "display.resolution", "display.mode"];

    /// <summary>Validates, saves and applies. A change to the screen is shown for 15 seconds and goes back by itself if it is not confirmed.</summary>
    public void ApplyChanges()
    {
        if (!_store.HasPending) return;
        var screenChange = _store.PendingIds.Any(DisplayIds.Contains);
        var previous = (Monitor: DisplayOptions.CurrentScreen.ToString(), Resolution: DisplayOptions.CurrentResolution(), Mode: DisplayOptions.CurrentMode());
        var result = _store.Apply();
        RefreshAll();
        Sync?.PushApplied(result.Changed, OnPushed);
        if (!screenChange) return;
        _dialog.Ask("Manter estas configurações de tela?", "A tela mudou. Se você não confirmar, as configurações anteriores voltam sozinhas.",
            [("Manter", UiStyle.ButtonKind.Primary, null), ("Reverter", UiStyle.ButtonKind.Tool, () => RevertScreen(previous))], seconds: 15, onTimeout: () => RevertScreen(previous));
    }

    private void RevertScreen((string Monitor, string Resolution, string Mode) previous)
    {
        _store.Set("display.monitor", previous.Monitor); _store.Set("display.resolution", previous.Resolution); _store.Set("display.mode", previous.Mode);
        _store.Apply(); RefreshAll();
    }

    // ---- profiles -----------------------------------------------------------------------------------------------------------------

    public void RefreshProfiles()
    {
        if (_profileChoice is null) return;
        _syncing = true; _profileChoice.Clear();
        var names = _store.Profiles(); foreach (var name in names) _profileChoice.AddItem(name);
        _profileChoice.Select(Math.Max(0, names.ToList().FindIndex(n => string.Equals(n, _store.ActiveProfile, StringComparison.CurrentCultureIgnoreCase)))); _syncing = false;
    }

    private void OnProfileSelected(long index)
    {
        if (_syncing) return;
        var name = _profileChoice.GetItemText((int)index);
        void Go() { try { _store.Switch(name); } catch (Exception exception) { Tell("Não foi possível trocar de perfil", exception.Message); } RefreshProfiles(); BuildPage(); }
        if (!_store.HasPending) { Go(); return; }
        RefreshProfiles();
        _dialog.Ask("Alterações não aplicadas", "Trocar de perfil descarta as alterações que você ainda não aplicou.", [("Trocar e descartar", UiStyle.ButtonKind.Primary, Go), ("Cancelar", UiStyle.ButtonKind.Tool, null)]);
    }

    private void Tell(string title, string message) => _dialog.Ask(title, message, [("OK", UiStyle.ButtonKind.Primary, null)]);

    private void AskProfileName(string title, string message, string initial, bool offerCopy, Func<string, bool, string> action)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 10);
        var field = new LineEdit { Text = initial, PlaceholderText = "Nome do perfil", MaxLength = 40, CustomMinimumSize = new Vector2(0, 44) }; box.AddChild(field);
        var copy = new CheckBox { Text = "Começar com os valores do perfil atual", Visible = offerCopy, ButtonPressed = true }; box.AddChild(copy);
        _dialog.Ask(title, message, [("Confirmar", UiStyle.ButtonKind.Primary, () =>
        {
            try { var created = action(field.Text, copy.ButtonPressed); if (!string.IsNullOrEmpty(created) && offerCopy) _store.Switch(created); RefreshProfiles(); BuildPage(); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException) { Tell("Não foi possível", exception.Message); }
        }), ("Cancelar", UiStyle.ButtonKind.Tool, null)], box);
        field.GrabFocus(); field.SelectAll();
    }

    private void OnProfileMenu(long id)
    {
        var active = _store.ActiveProfile;
        switch (id)
        {
            case 0: AskProfileName("Renomear perfil", $"Novo nome para «{active}»:", active, false, (name, _) => { _store.Rename(active, name); return ""; }); break;
            case 1: AskProfileName("Duplicar perfil", $"Nome da cópia de «{active}»:", $"{active} (cópia)", false, (name, _) => { _store.Duplicate(active, name); return ""; }); break;
            case 2:
                _dialog.Ask("Excluir perfil", $"Excluir o perfil «{active}»? Você volta para o perfil Padrão. Isso não pode ser desfeito.",
                    [("Excluir", UiStyle.ButtonKind.Secondary, () => { try { _store.Delete(active); } catch (Exception exception) when (exception is InvalidOperationException or ArgumentException) { Tell("Não foi possível excluir", exception.Message); } RefreshProfiles(); BuildPage(); }), ("Cancelar", UiStyle.ButtonKind.Tool, null)]);
                break;
            case 3: OpenFileDialog(true); break;
            case 4: OpenFileDialog(false); break;
        }
    }

    private void OpenFileDialog(bool save)
    {
        var dialog = new FileDialog { FileMode = save ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile, Access = FileDialog.AccessEnum.Filesystem, Title = save ? "Exportar perfil" : "Importar perfil", UseNativeDialog = true };
        dialog.AddFilter("*.json", "Perfil de configurações (JSON)"); if (save) dialog.CurrentFile = $"lifeverse-{SettingsStore.SafeFileName(_store.ActiveProfile)}.json";
        dialog.FileSelected += path =>
        {
            try { if (save) { _store.Export(_store.ActiveProfile, path); Tell("Perfil exportado", path); } else { var name = _store.Import(path); RefreshProfiles(); Tell("Perfil importado", $"O perfil «{name}» foi criado. Escolha-o na lista para usar."); } }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException) { Tell("Não foi possível", exception.Message); }
            dialog.QueueFree();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog); dialog.PopupCenteredRatio(.6f);
    }

    // ---- search -------------------------------------------------------------------------------------------------------------------

    private void RunSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { ClearSearch(); return; }
        _searching = true; _tabs.Visible = false; _columns = null; _rows.Clear(); _statsActive = false; RenderStats.Request("settings", false); _mic.Stop();
        foreach (var child in _pageHost.GetChildren()) { _pageHost.RemoveChild(child); child.QueueFree(); }
        var results = SettingsSearch.Find(_store.Definitions, query);
        _title.Text = "Resultados da busca"; _subtitle.Text = results.Count == 0 ? $"Nenhuma configuração encontrada para «{query.Trim()}»" : $"{results.Count} configuração(ões) para «{query.Trim()}»";
        _categoryIcon.Texture = UiStyle.Icon("search", UiStyle.Accent, 48);
        foreach (var button in _categoryButtons.Values) button.SetPressedNoSignal(false);
        if (results.Count == 0) { var none = SettingsUi.Text("Tente outras palavras: o nome da opção, da categoria ou o que ela faz (por exemplo «sombras», «volume», «tela cheia»).", 18, SettingsUi.TextSub); none.AutowrapMode = TextServer.AutowrapMode.WordSmart; _pageHost.AddChild(none); return; }
        foreach (var def in results) _pageHost.AddChild(ResultRow(def, query));
    }

    private Control ResultRow(SettingDefinition d, string query)
    {
        var panel = new PanelContainer { MouseDefaultCursorShape = CursorShape.PointingHand, FocusMode = FocusModeEnum.All, CustomMinimumSize = new Vector2(0, 64) };
        StyleBoxFlat Box(float fill, bool focus) => UiStyle.Box(new Color(1, 1, 1, fill), focus ? UiStyle.Accent : new Color(SettingsUi.Line, .4f), 12, focus ? 2 : 1, 16, 10);
        panel.AddThemeStyleboxOverride("panel", Box(.04f, false));
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 2); panel.AddChild(column);
        var label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, Text = Highlighted(d.Label, query) };
        label.AddThemeFontSizeOverride("normal_font_size", 19); label.AddThemeFontOverride("normal_font", UiStyle.Medium); label.AddThemeFontSizeOverride("bold_font_size", 19); label.AddThemeFontOverride("bold_font", UiStyle.Bold);
        column.AddChild(label);
        column.AddChild(SettingsUi.Text($"{SettingCategories.LabelOf(d.Category)} › {d.Tab}" + (d.IsAvailable ? "" : " · indisponível"), 15, SettingsUi.TextSub));
        panel.MouseEntered += () => panel.AddThemeStyleboxOverride("panel", Box(.09f, false)); panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", Box(.04f, false));
        panel.FocusEntered += () => panel.AddThemeStyleboxOverride("panel", Box(.09f, true)); panel.FocusExited += () => panel.AddThemeStyleboxOverride("panel", Box(.04f, false));
        panel.SetMeta("setting", d.Id);
        panel.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || input is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space }) { GoTo(d); AcceptEvent(); }
        };
        return panel;
    }

    private static string Highlighted(string text, string query)
    {
        var marks = SettingsSearch.Highlights(text, query); if (marks.Count == 0) return text.Replace("[", "[lb]");
        var builder = new System.Text.StringBuilder(); var at = 0;
        foreach (var (start, length) in marks) { if (start < at) continue; builder.Append(text[at..start].Replace("[", "[lb]")).Append("[b][color=#12F2E0]").Append(text.Substring(start, length).Replace("[", "[lb]")).Append("[/color][/b]"); at = start + length; }
        return builder.Append(text[at..].Replace("[", "[lb]")).ToString();
    }

    public void ClearSearch()
    {
        if (!_searching && _search.Text.Length == 0) return;
        _searching = false; if (_search.Text.Length > 0) _search.Text = "";
        SelectCategory(_category, _tab, focus: false);
    }

    /// <summary>Opens the page of a setting and flashes its row.</summary>
    public void GoTo(SettingDefinition d)
    {
        _searching = false; _search.Text = "";
        SelectCategory(d.Category, d.Tab, focus: false);
        CallDeferred(nameof(FlashRow), d.Id);
    }

    private void FlashRow(string id)
    {
        if (!_rows.TryGetValue(id, out var entry) || !IsInstanceValid(entry.Row)) return;
        _pageScroll.EnsureControlVisible(entry.Row);
        if (Accessibility.ReduceMotion) return;                  // no flashing when motion is reduced
        var tween = CreateTween(); entry.Row.SelfModulate = new Color(1, 1, 1, 1);
        tween.TweenProperty(entry.Row, "modulate", new Color(UiStyle.Accent.R * .6f + .4f, UiStyle.Accent.G * .6f + .4f, UiStyle.Accent.B * .6f + .4f, 1f), .18); tween.TweenProperty(entry.Row, "modulate", Colors.White, .9);
    }

    // ---- open / close ---------------------------------------------------------------------------------------------------------------

    public void Open(SettingCategory? category = null, string? tab = null)
    {
        Visible = true; _minimized = false; ApplyWindowState();
        if (category is not null) { if (_searching) ClearSearch(); SelectCategory(category.Value, tab); }
        else RefreshAll();
        RefreshProfiles(); MoveToFront(); LayoutFrame();
        Opened?.Invoke();
    }

    public new void MoveToFront() => GetParent()?.MoveChild(this, -1);

    public void Toggle(SettingCategory? category = null) { if (Visible) RequestClose(); else Open(category); }

    /// <summary>Closing with changes that were not applied asks what to do with them.</summary>
    public void RequestClose()
    {
        if (!_store.HasPending) { CloseNow(); return; }
        _dialog.Ask("Alterações não aplicadas", "Você alterou configurações e ainda não as aplicou. O que fazer com elas?",
            [("Aplicar e fechar", UiStyle.ButtonKind.Primary, () => { ApplyChanges(); if (!_dialog.IsOpen) CloseNow(); }), ("Descartar e fechar", UiStyle.ButtonKind.Secondary, () => { _store.Discard(); RefreshAll(); CloseNow(); }), ("Continuar editando", UiStyle.ButtonKind.Tool, null)]);
    }

    private void CloseNow() { _dialog.Close(); Visible = false; _dragging = false; RenderStats.Request("settings", false); _mic.Stop(); Closed?.Invoke(); }

    public override void _Input(InputEvent input)
    {
        if (!Visible || KeyCaptureButton.AnyCapturing || input is not InputEventKey { Pressed: true } key) return;
        if (key.Keycode == Key.F && key.IsCommandOrControlPressed()) { _search.GrabFocus(); _search.SelectAll(); GetViewport().SetInputAsHandled(); return; }
        if (key.Keycode != Key.Escape) return;
        GetViewport().SetInputAsHandled();
        if (_dialog.IsOpen) { _dialog.Close(); return; }
        if (_searching || _search.Text.Length > 0) { ClearSearch(); return; }
        RequestClose();
    }

    public override void _Process(double delta)
    {
        if (_flash > 0) _flash -= delta;
        if (_micMeter is not null && IsInstanceValid(_micMeter)) { _micMeter.Value = _mic.Active ? _mic.Level / MicScale : 0f; UpdateMicThreshold(); }
        if (_infoRows.Count > 0 && Visible) { _infoTimer += delta; if (_infoTimer >= 1) { _infoTimer = 0; foreach (var info in _infoRows.ToList()) info(); } }
        if (!_statsActive || !Visible) return;
        _statsTimer += delta; if (_statsTimer < .5) return; _statsTimer = 0; RefreshStats();
    }
}
