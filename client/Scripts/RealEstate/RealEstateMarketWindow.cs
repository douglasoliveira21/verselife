using System.Globalization;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.RealEstate;

/// <summary>
/// "Mercado Imobiliário": the land that is for sale or for rent on the public islands. Tabs on top, the search filters on the left, the listings as cards (or a list) with pages, and the buying, renting and visiting from each card.
/// The server decides what is listed and charges the money; this only asks and shows.
/// </summary>
public partial class RealEstateMarketWindow : Node
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly (string Id, string Text, bool Ready)[] Tabs = [("ilhas_venda", "Ilhas à Venda", true), ("ilhas_aluguel", "Ilhas para Alugar", true), ("homesteads", "Homesteads", true), ("terrenos", "Terrenos", true), ("comerciais", "Comerciais", true), ("residenciais", "Residenciais", true), ("todos", "Todos", true)];

    private Control _root = null!;
    private PanelContainer _panel = null!;
    private Control _grip = null!;
    private Label _toast = null!, _resultsTitle = null!, _note = null!;
    private Button _minimize = null!, _gridButton = null!, _listButton = null!, _search = null!;
    private Control _body = null!;
    private AcceptDialog _help = null!;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private OptionButton _type = null!, _deal = null!, _category = null!, _region = null!, _sort = null!;
    private LineEdit _query = null!, _minArea = null!, _maxArea = null!, _minPrice = null!, _maxPrice = null!;
    private CheckBox _onlyAvailable = null!, _withVisit = null!;
    private GridContainer _cards = null!;
    private HBoxContainer _pagination = null!;
    private ScrollContainer _scroll = null!;
    private MarketApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private MarketPageData _data = new();
    private readonly List<MarketListing> _shown = [];
    private string _tab = "terrenos";
    private bool _asList, _busy, _dragging, _resizing, _moved, _minimized;
    private int _page = 1;
    private Vector2 _size = new(1360, 880);
    private Vector2? _viewportOverride;
    private double _toastLeft;
    private string _lastQuery = "";

    /// <summary>Goes to an island (set by the main UI).</summary>
    public Action<Guid>? Visit { get; set; }

    public bool IsOpen => _root is not null && _root.Visible;
    public MarketPageData Data => _data;
    public string LastQueryForTesting => _lastQuery;
    public string TabForTesting => _tab;
    public IReadOnlyList<string> TabTextsForTesting => [.. Tabs.Select(t => _tabButtons[t.Id].Text.Trim())];
    public bool TabEnabledForTesting(string id) => !_tabButtons[id].Disabled;
    public IReadOnlyList<string> ListingsForTesting => [.. _shown.Select(l => l.Title)];
    public string TotalTextForTesting => _resultsTitle.Text;
    public string NoteForTesting => _note.Text;
    public string ToastForTesting => _toast.Text;
    public IReadOnlyList<string> PagesForTesting => [.. _pagination.GetChildren().OfType<Button>().Select(b => b.Text.Trim())];
    public bool AsListForTesting => _asList;
    public Vector2 PanelSizeForTesting => _panel.Size;
    public int ColumnsForTesting => _cards.Columns;
    public string BadgeForTesting(int index) => _cards.GetChildCount() > index ? string.Join("|", _cards.GetChild(index).FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)) : "";
    public (string Text, bool Disabled) PrimaryForTesting(int index) => PrimaryButton(index) is { } b ? (b.Text.Trim(), b.Disabled) : ("", true);
    public bool VisitEnabledForTesting(int index) => FindButton(index, "Visitar") is { Disabled: false };
    public void SetViewportForTesting(Vector2 size) { _viewportOverride = size; Layout(); }
    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new MarketApi(new IslandManagementApi(transport)); }
    public void PressCloseForTesting() => RequestClose();
    public async Task ClickTabForTesting(string id) => await SelectTab(id);
    public async Task SearchForTesting() => await SearchAsync(1);
    public async Task GoToPageForTesting(int page) => await SearchAsync(page);
    public async Task SortForTesting(int index) { _sort.Select(index); await SearchAsync(1); }
    public void SetFiltersForTesting(string? query = null, int? deal = null, int? category = null, string? minPrice = null, string? maxPrice = null, string? minArea = null, string? maxArea = null, bool? onlyAvailable = null, bool? withVisit = null)
    {
        if (query is not null) _query.Text = query; if (deal is { } d) _deal.Select(d); if (category is { } c) _category.Select(c); if (minPrice is not null) _minPrice.Text = minPrice; if (maxPrice is not null) _maxPrice.Text = maxPrice;
        if (minArea is not null) _minArea.Text = minArea; if (maxArea is not null) _maxArea.Text = maxArea; if (onlyAvailable is { } o) _onlyAvailable.ButtonPressed = o; if (withVisit is { } v) _withVisit.ButtonPressed = v;
    }
    public async Task ClearForTesting() { Clear(); await Task.CompletedTask; }
    public void ToggleViewForTesting(bool list) { _asList = list; Render(); }
    public async Task<bool> BuyForTesting(int index) => await BuyAsync(_shown[index]);
    public async Task<bool> RentForTesting(int index, int periods) => await RentAsync(_shown[index], periods);
    public void VisitForTesting(int index) => VisitListing(_shown[index]);

    private Button? PrimaryButton(int index) => FindButton(index, "Comprar") ?? FindButton(index, "Alugar") ?? FindButton(index, "Alugado");
    private Button? FindButton(int index, string text) => _cards.GetChildCount() > index ? _cards.GetChild(index).FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) : null;

    public override void _Ready()
    {
        _api ??= new MarketApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));
        _root = new Control { Name = "RealEstateMarketRoot", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Theme = ViewerTheme.Create() }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _panel = new PanelContainer { Name = "RealEstateMarketPanel", ClipContents = true }; _panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(IslandUi.MainPanel, .97f), IslandUi.BorderDefault, IslandUi.RadiusWindow, 1, 0, 0, 28)); _root.AddChild(_panel);
        var shell = new VBoxContainer(); shell.AddThemeConstantOverride("separation", 0); _panel.AddChild(shell);
        shell.AddChild(BuildTitleBar());
        var body = IslandUi.Column(12); body.SizeFlagsVertical = Control.SizeFlags.ExpandFill; var margin = IslandUi.Padded(body, 16, 12, 16, 14); margin.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _body = margin; shell.AddChild(margin);
        body.AddChild(BuildTabs());
        var row = IslandUi.Row(14); row.SizeFlagsVertical = Control.SizeFlags.ExpandFill; body.AddChild(row);
        row.AddChild(BuildFilters()); row.AddChild(BuildResults());

        _grip = new Control { CustomMinimumSize = new Vector2(22, 22), Size = new Vector2(22, 22), MouseDefaultCursorShape = Control.CursorShape.Fdiagsize, TooltipText = "Arraste para redimensionar" };
        _grip.Draw += () => { for (var i = 0; i < 3; i++) _grip.DrawLine(new Vector2(20, 8 + i * 5), new Vector2(8 + i * 5, 20), new Color(IslandUi.TextMuted, .8f), 1.5f); };
        _grip.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _resizing = button.Pressed; };
        _root.AddChild(_grip);
        _toast = IslandUi.Text("", 15, IslandUi.TextPrimary, true); _toast.Visible = false; _toast.HorizontalAlignment = HorizontalAlignment.Center; _toast.AddThemeStyleboxOverride("normal", IslandUi.Box(new Color("#0E3A47"), IslandUi.AccentPrimary, 10, 1, 16, 9)); _toast.MouseFilter = Control.MouseFilterEnum.Ignore; _root.AddChild(_toast);
        _help = new AcceptDialog { Title = "Ajuda", OkButtonText = "Entendi", Unresizable = true, DialogText = "Aqui você acha terrenos à venda ou para alugar nas ilhas públicas.\n\n• As abas e os filtros mudam a lista; “Buscar” aplica.\n• “Visitar” leva você até a ilha do terreno.\n• “Comprar” e “Alugar” pedem a sua confirmação e cobram da sua carteira.\n• Terrenos seus e ofertas feitas para outra pessoa não aparecem.\n• Ilhas inteiras e Homesteads chegam com a venda de ilhas." };
        _panel.AddChild(_help);
        GetViewport().SizeChanged += Layout;
        ApplyPreset(_tab);
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
        var title = IslandUi.Text("Mercado Imobiliário", 21, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(title);
        var help = IslandUi.IconButton("help", "Ajuda", 36); help.Pressed += () => _help.PopupCentered(new Vector2I(560, 0)); row.AddChild(help);
        _minimize = IslandUi.IconButton("minimize", "Minimizar", 36); _minimize.Pressed += () => { _minimized = !_minimized; _body.Visible = !_minimized; Layout(); }; row.AddChild(_minimize);
        var close = IslandUi.IconButton("close", "Fechar", 36); close.Pressed += RequestClose; row.AddChild(close);
        bar.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } button) _dragging = button.Pressed;
            else if (input is InputEventMouseMotion motion && _dragging) { _panel.Position += motion.Relative; _moved = true; KeepOnScreen(); }
        };
        return bar;
    }

    private Control BuildTabs()
    {
        var row = IslandUi.Row(8);
        foreach (var (id, text, ready) in Tabs)
        {
            var button = IslandUi.Action(text, null, IslandUi.Kind.Secondary, 38); button.ToggleMode = true; button.Disabled = !ready; button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; 
            var tab = id; button.Pressed += () => _ = SelectTab(tab); _tabButtons[id] = button; row.AddChild(button);
        }
        _tabButtons[_tab].ButtonPressed = true;
        return row;
    }

    private Control BuildFilters()
    {
        var (card, body) = IslandUi.Card("Filtros de Busca", "search"); card.CustomMinimumSize = new Vector2(310, 0); card.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; ((Control)body.GetParent()).SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddChild(scroll);
        var form = IslandUi.Column(8); form.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; scroll.AddChild(form);
        Label Caption(string text) => IslandUi.Text(text, 13, IslandUi.TextSecondary, true);
        form.AddChild(Caption("Buscar")); _query = IslandUi.Input("Nome, código ou ilha", 80); _query.TextSubmitted += text => _ = SearchAsync(1); form.AddChild(_query);
        form.AddChild(Caption("Tipo de Imóvel")); _type = IslandUi.Dropdown([("Todos", "all"), ("Terrenos", "parcel"), ("Ilhas", "island")]); form.AddChild(_type);
        form.AddChild(Caption("Negociação")); _deal = IslandUi.Dropdown([("Todos", "any"), ("Venda", "sale"), ("Aluguel", "rent")]); form.AddChild(_deal);
        form.AddChild(Caption("Categoria")); _category = IslandUi.Dropdown([("Todas", ""), ("Residencial", "residencial"), ("Comercial", "comercial"), ("Mista", "mista"), ("Social", "social"), ("Área Comum", "comum")]); form.AddChild(_category);
        form.AddChild(Caption("Tipo de Região")); _region = IslandUi.Dropdown([("Todos", "all"), ("Full Region", "full"), ("Homestead", "homestead")]); form.AddChild(_region);
        form.AddChild(Caption("Área (m²)")); var area = IslandUi.Row(8); _minArea = Range("Mín."); _maxArea = Range("Máx."); area.AddChild(_minArea); area.AddChild(IslandUi.Text("–", 14, IslandUi.TextMuted)); area.AddChild(_maxArea); form.AddChild(area);
        form.AddChild(Caption("Preço (L$)")); var price = IslandUi.Row(8); _minPrice = Range("Mín."); _maxPrice = Range("Máx."); price.AddChild(_minPrice); price.AddChild(IslandUi.Text("–", 14, IslandUi.TextMuted)); price.AddChild(_maxPrice); form.AddChild(price);
        _onlyAvailable = new CheckBox { Text = "Somente disponíveis", ButtonPressed = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Desligado, mostra também o que já está alugado." }; form.AddChild(_onlyAvailable);
        _withVisit = new CheckBox { Text = "Com visitação", FocusMode = Control.FocusModeEnum.None, TooltipText = "Só terrenos em que qualquer pessoa pode entrar." }; form.AddChild(_withVisit);
        body.AddChild(IslandUi.Spacer());
        var clear = IslandUi.Action("Limpar Filtros", null, IslandUi.Kind.Secondary, 38); clear.Pressed += () => { Clear(); _ = SearchAsync(1); }; body.AddChild(clear);
        _search = IslandUi.Action("Buscar", "search", IslandUi.Kind.Primary, 44); _search.Pressed += () => _ = SearchAsync(1); body.AddChild(_search);
        return card;
    }

    private static LineEdit Range(string placeholder) { var edit = IslandUi.Input(placeholder, 12); edit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; return edit; }

    private Control BuildResults()
    {
        var column = IslandUi.Column(10); column.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; column.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        var head = IslandUi.Row(10); column.AddChild(head);
        _resultsTitle = IslandUi.Text("Resultados", 20, IslandUi.TextPrimary, true); _resultsTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; head.AddChild(_resultsTitle);
        head.AddChild(IslandUi.Text("Ordenar por:", 14, IslandUi.TextSecondary));
        _sort = IslandUi.Dropdown([("Mais Recentes", "new"), ("Menor preço", "price"), ("Maior preço", "price_desc"), ("Maior área", "area"), ("Mais prims", "prims")]); _sort.CustomMinimumSize = new Vector2(170, 36); _sort.ItemSelected += index => _ = SearchAsync(1); head.AddChild(_sort);
        _gridButton = IslandUi.IconButton("grid", "Ver em grade", 36, true); _gridButton.ToggleMode = true; _gridButton.ButtonPressed = true; _gridButton.Pressed += () => { _asList = false; Render(); }; head.AddChild(_gridButton);
        _listButton = IslandUi.IconButton("list", "Ver em lista", 36, true); _listButton.ToggleMode = true; _listButton.Pressed += () => { _asList = true; Render(); }; head.AddChild(_listButton);
        _note = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); column.AddChild(_note);
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddChild(_scroll);
        _cards = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _cards.AddThemeConstantOverride("h_separation", 12); _cards.AddThemeConstantOverride("v_separation", 12); _scroll.AddChild(_cards);
        _pagination = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; _pagination.AddThemeConstantOverride("separation", 6); column.AddChild(_pagination);
        return column;
    }

    // ---- window behaviour -------------------------------------------------------------------------------------------------------

    private void Layout()
    {
        if (_panel is null) return;
        var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; var margin = 16f;
        var minWidth = Math.Min(1000f, view.X - 2 * margin); var minHeight = Math.Min(640f, view.Y - 2 * margin);
        var size = new Vector2(Math.Clamp(_size.X, minWidth, Math.Max(minWidth, view.X - 2 * margin)), Math.Clamp(_size.Y, minHeight, Math.Max(minHeight, view.Y - 2 * margin)));
        _panel.CustomMinimumSize = new Vector2(minWidth, _minimized ? 60 : minHeight); _panel.Size = _minimized ? new Vector2(size.X, 60) : size;
        if (!_moved) _panel.Position = ((view - _panel.Size) / 2).Floor(); else KeepOnScreen();
        _grip.Visible = !_minimized; _grip.Position = _panel.Position + _panel.Size - new Vector2(24, 24);
        var columns = _asList ? 1 : Math.Clamp((int)((_panel.Size.X - 310 - 70) / 300), 1, 4);
        if (_cards is not null && _cards.Columns != columns) _cards.Columns = columns;
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

    public void Toggle() { if (IsOpen) RequestClose(); else _ = OpenAsync(); }
    public void RequestClose() { _root.Visible = false; }
    public async Task OpenAsync() { _root.Visible = true; Layout(); await SearchAsync(1); }

    private void Notify(string text) { _toast.Text = text; _toast.Visible = true; _toastLeft = 3.5; var view = _viewportOverride ?? GetViewport().GetVisibleRect().Size; _toast.Position = new Vector2((view.X - Math.Max(260, _toast.GetCombinedMinimumSize().X)) / 2, view.Y - 90); }

    // ---- searching --------------------------------------------------------------------------------------------------------------------

    /// What each tab asks for: the kind of property, the deal, the region and the category the filters are set to.
    private void ApplyPreset(string tab)
    {
        var (type, deal, region, category) = tab switch
        {
            "ilhas_venda" => ("island", "sale", "all", ""), "ilhas_aluguel" => ("island", "rent", "all", ""), "homesteads" => ("island", "any", "homestead", ""),
            "comerciais" => ("parcel", "any", "all", "comercial"), "residenciais" => ("parcel", "any", "all", "residencial"), "todos" => ("all", "any", "all", ""), _ => ("parcel", "any", "all", ""),
        };
        IslandUi.Select(_type, type); IslandUi.Select(_deal, deal); IslandUi.Select(_region, region); IslandUi.Select(_category, category);
    }

    private async Task SelectTab(string id)
    {
        if (!Tabs.Any(t => t.Id == id && t.Ready)) return;
        _tab = id; foreach (var (key, button) in _tabButtons) button.ButtonPressed = key == id;
        ApplyPreset(id); await SearchAsync(1);
    }

    private void Clear()
    {
        _query.Text = ""; _minArea.Text = _maxArea.Text = _minPrice.Text = _maxPrice.Text = ""; _onlyAvailable.ButtonPressed = true; _withVisit.ButtonPressed = false; ApplyPreset(_tab);
    }

    private static long? Money(string text) => long.TryParse(text.Trim().Replace(".", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;
    private static double? Metres(string text) => double.TryParse(text.Trim().Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n >= 0 ? n : null;

    private MarketFilters Filters(int page)
    {
        var type = IslandUi.IdOf(_type); var region = IslandUi.IdOf(_region);
        return new MarketFilters(_query.Text, IslandUi.IdOf(_deal), IslandUi.IdOf(_category), Money(_minPrice.Text), Money(_maxPrice.Text), Metres(_minArea.Text), Metres(_maxArea.Text), _onlyAvailable.ButtonPressed, _withVisit.ButtonPressed, IslandUi.IdOf(_sort), page, 9, type == "all" ? "" : type, region == "all" ? "" : region);
    }

    private async Task SearchAsync(int page)
    {
        if (_api is null || _busy) return;
        _busy = true; _page = page; _note.Text = "Buscando…"; var filters = Filters(page); _lastQuery = filters.ToQuery();
        var reply = await _api.Search(filters); _busy = false;
        if (!reply.Ok)
        {
            _shown.Clear(); _data = new MarketPageData(); Render();
            _note.Text = reply.Status == 404 ? "O servidor ainda não tem o mercado imobiliário. Ele chega com a próxima publicação da API." : reply.Status == 0 ? "Sem conexão com o servidor." : LandManagementApi.Words(reply); return;
        }
        var data = MarketPageData.Parse(reply.Body);
        if (data is null) { _note.Text = "O servidor mandou uma resposta que não entendi."; return; }
        _data = data; _page = data.Page; _shown.Clear(); _shown.AddRange(data.Items); _note.Text = ""; Render();
    }

    // ---- showing ---------------------------------------------------------------------------------------------------------------------

    private void Render()
    {
        _resultsTitle.Text = $"Resultados ({_data.Total.ToString("N0", Pt)} {(_data.Total == 1 ? "imóvel" : "imóveis")})";
        _gridButton.ButtonPressed = !_asList; _listButton.ButtonPressed = _asList; Layout();
        foreach (var child in _cards.GetChildren()) child.QueueFree();
        foreach (var listing in _shown) _cards.AddChild(_asList ? BuildRow(listing) : BuildCard(listing));
        if (_shown.Count == 0 && _note.Text.Length == 0) _note.Text = "Nenhum imóvel encontrado com esses filtros.";
        BuildPagination();
    }

    private static Color StateColor(MarketListing l) => l.State switch { "sale" => IslandUi.AccentSecondary, "rent" => IslandUi.RentPurple, "rented" => IslandUi.Disabled, _ => IslandUi.TextSecondary };
    private static string StateText(MarketListing l) => l.State switch { "sale" => "À Venda", "rent" => "Para Alugar", "rented" => "Alugado", _ => l.State };

    private Control Image(MarketListing l, Vector2 size)
    {
        var frame = new PanelContainer { CustomMinimumSize = size, ClipContents = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; frame.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(StateColor(l), .18f), IslandUi.BorderDefault, 8, 1));
        var picture = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepCentered, Texture = UiStyle.Icon("map", new Color(StateColor(l), .8f), 44), MouseFilter = Control.MouseFilterEnum.Ignore }; frame.AddChild(picture);
        if (l.IslandCover.StartsWith("asset:", StringComparison.Ordinal) && Guid.TryParse(l.IslandCover[6..], out var asset) && GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is { } cache)
            cache.Request(asset, texture => { if (texture is not null && IsInstanceValid(picture)) { picture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; picture.Texture = texture; } }, l.IslandId);
        return frame;
    }

    private Control BuildCard(MarketListing l)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 10, 10));
        var column = IslandUi.Column(6); card.AddChild(column);
        column.AddChild(Image(l, new Vector2(0, 118)));
        column.AddChild(IslandUi.Text(l.Title, 16, IslandUi.TextPrimary, true));
        var type = IslandUi.Row(8); type.AddChild(IslandUi.Text(l.TypeText, 13, IslandUi.TextSecondary)); column.AddChild(type);
        var island = IslandUi.Row(8); var name = IslandUi.Text(l.IslandName, 13, IslandUi.TextMuted); name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; name.ClipText = true; island.AddChild(name); island.AddChild(IslandUi.Pill(StateText(l), StateColor(l))); column.AddChild(island);
        var facts = IslandUi.Row(10); facts.AddChild(IslandUi.Text(LandMath.Area(l.Area), 13, IslandUi.TextPrimary, true)); facts.AddChild(IslandUi.Text($"{LandMath.Number(l.MaxObjects)} prims", 13, IslandUi.TextSecondary)); column.AddChild(facts);
        column.AddChild(IslandUi.Text(l.PriceText, 19, StateColor(l) == IslandUi.Disabled ? IslandUi.TextSecondary : StateColor(l), true));
        column.AddChild(Buttons(l));
        return card;
    }

    private Control BuildRow(MarketListing l)
    {
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusCard, 1, 10, 8));
        var row = IslandUi.Row(14); card.AddChild(row);
        var picture = Image(l, new Vector2(150, 84)); picture.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; row.AddChild(picture);
        var info = IslandUi.Column(3); info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; info.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(info);
        info.AddChild(IslandUi.Text(l.Title, 16, IslandUi.TextPrimary, true)); info.AddChild(IslandUi.Text($"{l.TypeText} · {l.IslandName} · {LandMath.Area(l.Area)} · {LandMath.Number(l.MaxObjects)} prims · {l.OwnerName}", 13, IslandUi.TextSecondary));
        var side = IslandUi.Column(4); side.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(side);
        var price = IslandUi.Text(l.PriceText, 19, StateColor(l) == IslandUi.Disabled ? IslandUi.TextSecondary : StateColor(l), true); price.HorizontalAlignment = HorizontalAlignment.Right; side.AddChild(price);
        var line = IslandUi.Row(8); line.AddChild(IslandUi.Pill(StateText(l), StateColor(l))); side.AddChild(line); row.AddChild(Buttons(l));
        return card;
    }

    private Control Buttons(MarketListing l)
    {
        var row = IslandUi.Row(8); row.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var visit = IslandUi.Action("Visitar", null, IslandUi.Kind.Secondary, 36); visit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; visit.Disabled = !l.CanVisit; visit.TooltipText = l.CanVisit ? "Vai até a ilha deste terreno." : "Este terreno não recebe visitas."; visit.Pressed += () => VisitListing(l); row.AddChild(visit);
        var take = l.State == "rented" ? IslandUi.Action("Alugado", null, IslandUi.Kind.Quiet, 36) : l.Offer == "sale" ? IslandUi.Action("Comprar", null, IslandUi.Kind.Primary, 36) : IslandUi.Action("Alugar", null, IslandUi.Kind.Purple, 36);
        take.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; take.Disabled = l.State == "rented"; take.TooltipText = l.State == "rented" ? "Este terreno já está alugado." : "";
        take.Pressed += () => { if (l.Offer == "sale") OpenBuy(l); else OpenRent(l); }; row.AddChild(take);
        return row;
    }

    private void BuildPagination()
    {
        foreach (var child in _pagination.GetChildren()) child.QueueFree();
        var pages = _data.Pages; if (_data.Total == 0) return;
        Button Page(string text, int target, bool current = false, bool enabled = true)
        {
            var b = IslandUi.Action(text, null, current ? IslandUi.Kind.Primary : IslandUi.Kind.Secondary, 34); b.CustomMinimumSize = new Vector2(38, 34); b.Disabled = !enabled; b.Pressed += () => _ = SearchAsync(target); _pagination.AddChild(b); return b;
        }
        Page("‹", _page - 1, false, _page > 1);
        var shown = new SortedSet<int> { 1, pages, _page - 1, _page, _page + 1 }; shown.RemoveWhere(p => p < 1 || p > pages); var previous = 0;
        foreach (var p in shown) { if (p - previous > 1) { var gap = IslandUi.Text("…", 16, IslandUi.TextMuted); _pagination.AddChild(gap); } Page(p.ToString(Pt), p, p == _page); previous = p; }
        Page("›", _page + 1, false, _page < pages);
    }

    // ---- buying, renting, visiting ----------------------------------------------------------------------------------------------

    private void VisitListing(MarketListing l) { if (!l.CanVisit) return; Visit?.Invoke(l.IslandId); RequestClose(); }

    private void Ask(string title, Control content, Func<Task<bool>> accept, string okText)
    {
        var dialog = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Cancelar", Exclusive = true, Theme = ViewerTheme.Create() };
        dialog.AddChild(IslandUi.Padded(content, 6, 6, 6, 6)); _panel.AddChild(dialog);
        dialog.Confirmed += async () => { await accept(); dialog.QueueFree(); }; dialog.Canceled += () => dialog.QueueFree();
        dialog.PopupCentered(new Vector2I(480, 0));
    }

    private void OpenBuy(MarketListing l)
    {
        var content = IslandUi.Column(6);
        content.AddChild(IslandUi.Text($"{l.Title}", 17, IslandUi.TextPrimary, true)); content.AddChild(IslandUi.Text($"{l.TypeText} na ilha {l.IslandName}", 14, IslandUi.TextSecondary));
        content.AddChild(IslandUi.Text($"{LandMath.Area(l.Area)} · {LandMath.Number(l.MaxObjects)} prims · vendedor: {l.OwnerName}", 14, IslandUi.TextSecondary));
        content.AddChild(IslandUi.Text($"Preço: {LandMath.Money(l.Price)}", 18, IslandUi.AccentSecondary, true)); content.AddChild(IslandUi.Text("O valor sai da sua carteira agora e o terreno passa a ser seu.", 13, IslandUi.TextMuted, false, true));
        if (l.IsIsland) content.AddChild(IslandUi.Text("Você vira dono da ilha inteira, com os terrenos e objetos que o vendedor ainda tem nela. Terrenos de outras pessoas continuam sendo delas.", 13, IslandUi.TextMuted, false, true));
        Ask(l.IsIsland ? "Comprar ilha" : "Comprar terreno", content, async () => await BuyAsync(l), "Comprar");
    }

    private async Task<bool> BuyAsync(MarketListing l)
    {
        var reply = await _api!.Buy(l);
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); await SearchAsync(_page); return false; }
        Notify($"{(l.IsIsland ? "Ilha comprada" : "Terreno comprado")}: {l.Title}."); await SearchAsync(_page); return true;
    }

    private void OpenRent(MarketListing l)
    {
        var content = IslandUi.Column(6);
        content.AddChild(IslandUi.Text(l.Title, 17, IslandUi.TextPrimary, true)); content.AddChild(IslandUi.Text($"{l.TypeText} na ilha {l.IslandName} · {LandMath.Area(l.Area)}", 14, IslandUi.TextSecondary));
        var periods = IslandUi.Number(l.RentMinPeriods, Math.Max(l.RentMinPeriods, l.RentMaxPeriods), 1, $" período(s) de {l.RentPeriodDays} dias"); periods.Value = l.RentMinPeriods; content.AddChild(periods);
        var total = IslandUi.Text("", 16, IslandUi.RentPurple, true); content.AddChild(total);
        void Update() => total.Text = $"Total agora: {LandMath.Money(l.Price * (long)periods.Value)}" + (l.RentDeposit > 0 ? $" + caução {LandMath.Money(l.RentDeposit)} (volta no fim)" : "");
        periods.ValueChanged += _ => Update(); Update();
        if (l.IsIsland) content.AddChild(IslandUi.Text("Você cuida da ilha durante o contrato, mas ela continua sendo de quem a alugou para você.", 13, IslandUi.TextMuted, false, true));
        Ask(l.IsIsland ? "Alugar ilha" : "Alugar terreno", content, async () => await RentAsync(l, (int)periods.Value), "Alugar");
    }

    private async Task<bool> RentAsync(MarketListing l, int periods)
    {
        var reply = await _api!.Rent(l, periods);
        if (!reply.Ok) { Notify(LandManagementApi.Words(reply)); await SearchAsync(_page); return false; }
        Notify($"{(l.IsIsland ? "Ilha alugada" : "Terreno alugado")}: {l.Title}."); await SearchAsync(_page); return true;
    }
}
