using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// "LifeVerse — Busca": one window to find people, places, events, groups, items and stores. Seven tabs (Tudo, Pessoas, Lugares, Eventos, Grupos, Itens, Lojas), a search box with suggestions and recent searches,
/// filters, results on the left and the details of the chosen result on the right (one column when the window is narrow). Everything comes from the server; this window only draws it and asks for what the person does.
/// Split in files: this one (shell, tabs, state), Results (searching and the cards), Details (the chosen result and its actions).
/// </summary>
public partial class SearchWindow : Node
{
    private const string Pending = "Buscando…";
    /// <summary>Same order as <see cref="TabTitles"/>: the kinds each tab searches (none = everything).</summary>
    private static readonly string[][] TabKinds = [[], [SearchKinds.Person], [SearchKinds.Island, SearchKinds.Parcel, SearchKinds.Place], [SearchKinds.Event], [SearchKinds.Group], [SearchKinds.Product], [SearchKinds.Store]];
    private static readonly (string Title, string Icon)[] TabTitles = [("TUDO", "search"), ("PESSOAS", "user"), ("LUGARES", "map"), ("EVENTOS", "sun"), ("GRUPOS", "people"), ("ITENS", "cube"), ("LOJAS", "bag")];
    private const int NarrowWidth = 760;

    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private LineEdit _query = null!;
    private Button _go = null!;
    private HFlowContainer _hints = null!, _filterRow = null!;
    private OptionButton _sort = null!, _category = null!, _rating = null!, _when = null!, _price = null!;
    private CheckBox _onlyCalendar = null!;
    private Button _newEvent = null!;
    private string[] _sortNames = SearchApi.SortNames;
    private CheckBox _onlyFavorites = null!;
    private Label _count = null!, _note = null!, _pageLabel = null!;
    private Control _pager = null!;
    private Button _prev = null!, _next = null!;
    private HBoxContainer _body = null!;
    private VBoxContainer _left = null!;
    private Control _right = null!;
    private Godot.Timer _typing = null!;

    private bool _building, _pending, _narrow, _detailOpen;
    private int _seq, _suggestSeq;
    private string _lastQuery = string.Empty, _inflight = string.Empty;
    private Action? _retry;
    private SearchHistory _history = new();
    private SearchPageData _page = SearchPageData.Empty;

    /// <summary>Where the search history is kept (this computer only). Tests change it.</summary>
    public static string HistoryPath { get; set; } = "user://search_history.json";
    private static string HistoryFile => ProjectSettings.GlobalizePath(HistoryPath);
    /// <summary>"Ver perfil" on a person: opens the profile window for that user.</summary>
    public Action<Guid>? OpenProfile { get; set; }
    /// <summary>"Ver grupo": opens the window of that group.</summary>
    public Action<Guid>? OpenGroup { get; set; }
    /// <summary>Set by the main UI: join an island, move to a spot, move to a plot, and which island the avatar is on.</summary>
    public Action<Guid>? JoinIsland { get; set; }
    public Action<float, float>? TeleportToPoint { get; set; }
    public Func<Guid, bool>? TeleportToParcel { get; set; }
    public Func<Guid>? CurrentIsland { get; set; }
    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }

    public bool Visible => _window.Visible;

    public override void _Ready() { _history = SearchHistory.Load(HistoryFile); Build(); }

    public void Toggle() { if (_window.Visible) _window.Visible = false; else Open(); }

    /// <summary>Opens the window and searches for this text right away.</summary>
    public void OpenWithQuery(string text) { Open(); _query.Text = text; Search(); }

    /// <summary>Opens the window in front of the others, on the tab and search it was left on. There is only one window.</summary>
    public void Open()
    {
        _window.Visible = true; _window.MoveToFront(); _query.GrabFocus(); ShowHints();
        if (_hits.Count == 0 && !_pending) { if (_query.Text.Trim().Length == 0) { if (_tabs.Selected == 0) Explore(); else Search(); } else Search(); }
    }

    /// <summary>Esc closes the window when it is the one being used; the player stays connected.</summary>
    public override void _Input(InputEvent input)
    {
        if (!_window.Visible || input is not InputEventKey { Pressed: true } key || GetViewport().GuiGetFocusOwner() is not { } focus || !_window.IsAncestorOf(focus)) return;
        if (key.Keycode == Key.Escape) { _window.Visible = false; GetViewport().SetInputAsHandled(); }
        else if (HandleKeys(key)) GetViewport().SetInputAsHandled();
    }

    // ---- building -----------------------------------------------------------------------------------------------------------------

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action, int height = 38)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private static OptionButton Choice(params string[] items) { var option = new OptionButton { CustomMinimumSize = new Vector2(150, 36), FocusMode = Control.FocusModeEnum.None }; foreach (var item in items) option.AddItem(item); return option; }

    private static Label Text(string text = "", bool muted = false, int size = 15, bool bold = false, bool wrap = true)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off, ClipText = !wrap, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", size); if (muted) label.AddThemeColorOverride("font_color", UiStyle.TextMuted); if (bold) label.AddThemeFontOverride("font", UiStyle.Bold); return label;
    }

    private void Build()
    {
        _window = new FloatingWindow("LifeVerse — Busca", icon: "search", minimizable: true, expandable: true) { Name = "SearchWindow", Visible = false };
        _window.Place(.5f, .5f, -560, -360, 560, 360);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);

        _tabs = new UiTabs { EqualWidth = false, Flat = true, FontSize = 15 };
        foreach (var (title, icon) in TabTitles) _tabs.AddTab(title, icon);
        var tabScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 50) };
        _tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; tabScroll.AddChild(_tabs); content.AddChild(tabScroll);

        var searchRow = new HBoxContainer(); searchRow.AddThemeConstantOverride("separation", 8); content.AddChild(searchRow);
        _query = new LineEdit { PlaceholderText = "Buscar no LifeVerse…", CustomMinimumSize = new Vector2(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true, MaxLength = SearchText.MaxQueryLength, RightIcon = UiStyle.Icon("search", UiStyle.TextMuted, 20) };
        _query.AddThemeFontSizeOverride("font_size", 16); _query.TextSubmitted += _ => Search(); _query.TextChanged += _ => _typing.Start(); searchRow.AddChild(_query);
        _go = Action("Buscar", UiStyle.ButtonKind.Primary, OnGo); _go.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; _go.CustomMinimumSize = new Vector2(110, 40); searchRow.AddChild(_go);
        _typing = new Godot.Timer { OneShot = true, WaitTime = .25 }; _typing.Timeout += OnTyped; AddChild(_typing);

        _hints = new HFlowContainer { Visible = false }; _hints.AddThemeConstantOverride("h_separation", 6); _hints.AddThemeConstantOverride("v_separation", 4); content.AddChild(_hints);

        _filterRow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _filterRow.AddThemeConstantOverride("h_separation", 8); _filterRow.AddThemeConstantOverride("v_separation", 4); content.AddChild(_filterRow);
        _category = Choice("Todas as categorias"); _category.TooltipText = "Categoria"; _category.ItemSelected += _ => { if (!_building) Search(); }; _filterRow.AddChild(_category);
        _sort = Choice(SearchApi.SortLabels); _sort.TooltipText = "Ordenar por"; _sort.ItemSelected += _ => Search(); _filterRow.AddChild(_sort);
        _rating = Choice("Qualquer classificação", "Livre", "Moderado", "Adulto"); _rating.TooltipText = "Classificação de conteúdo"; _rating.ItemSelected += _ => Search(); _filterRow.AddChild(_rating);
        _when = Choice("Qualquer data", "Acontecendo agora", "Hoje", "Próximos 7 dias", "Próximos"); _when.TooltipText = "Quando"; _when.ItemSelected += _ => Search(); _filterRow.AddChild(_when);
        _price = Choice("Gratuitos e pagos", "Gratuitos", "Pagos"); _price.TooltipText = "Valor"; _price.ItemSelected += _ => Search(); _filterRow.AddChild(_price);
        _onlyCalendar = new CheckBox { Text = " Meu calendário", FocusMode = Control.FocusModeEnum.None, TooltipText = "Mostra os eventos em que você se inscreveu ou que guardou" }; _onlyCalendar.Toggled += _ => Search(); _filterRow.AddChild(_onlyCalendar);
        _newEvent = Action("Anunciar evento", UiStyle.ButtonKind.Secondary, OpenEventForm, 36); _newEvent.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _newEvent.CustomMinimumSize = new Vector2(150, 36); _filterRow.AddChild(_newEvent);
        BuildMarketFilters(_filterRow); BuildOwnerChip(content);
        _onlyFavorites = new CheckBox { Text = " Só favoritos", FocusMode = Control.FocusModeEnum.None, TooltipText = "Mostra só o que você favoritou" }; _onlyFavorites.Toggled += _ => Search(); _filterRow.AddChild(_onlyFavorites);
        _count = Text("", true, 14, wrap: false); _count.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; _count.CustomMinimumSize = new Vector2(150, 0); _count.VerticalAlignment = VerticalAlignment.Center; _filterRow.AddChild(_count);

        _body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 12); content.AddChild(_body);
        _left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 3, CustomMinimumSize = new Vector2(0, 0) }; _left.AddThemeConstantOverride("separation", 6); _body.AddChild(_left);
        BuildResultsColumn();
        BuildDetails();

        _tabs.TabChanged += OnTab; _body.Resized += Adapt; ShowPreview(null); UpdateChrome();
        AddChild(_window);
    }

    // ---- state of the window ------------------------------------------------------------------------------------------------------

    /// <summary>The orders that make sense on the tab: places can be sorted by name, events by how soon they start.</summary>
    private void RebuildSorts(int tab)
    {
        (_sortNames, var labels) = tab switch
        {
            2 => (["relevance", "popularity", "updated", "name"], ["Relevância", "Mais populares", "Mais recentes", "Nome A–Z"]),
            3 => (["relevance", "start", "popularity"], ["Relevância", "Mais próximos", "Mais populares"]),
            5 => (["relevance", "price_asc", "price_desc", "updated", "popularity"], ["Relevância", "Menor preço", "Maior preço", "Mais recentes", "Mais populares"]),
            _ => (SearchApi.SortNames, SearchApi.SortLabels)
        };
        _building = true; _sort.Clear(); foreach (var label in labels) _sort.AddItem(label); _sort.Select(0); _building = false;
    }

    /// <summary>The filters of the tab as the search sends them: the time of an event, free or paid, and the content rating.</summary>
    private (DateTimeOffset? From, DateTimeOffset? To, bool? Free, string? Ratings, string? Extra) Filters()
    {
        var tab = _tabs.Selected; DateTimeOffset? from = null, to = null; bool? free = null; string? ratings = null;
        if (tab == 3)
        {
            var now = DateTimeOffset.Now;
            switch (_when.Selected)
            {
                case 1: from = to = now; break;
                case 2: from = now; to = new DateTimeOffset(now.Date.AddDays(1), now.Offset).AddSeconds(-1); break;
                case 3: from = now; to = now.AddDays(7); break;
                case 4: from = now; break;
            }
            free = _price.Selected switch { 1 => true, 2 => false, _ => null };
        }
        if (tab is 2 or 3 or 4 && _rating.Selected > 0) ratings = _rating.Selected switch { 1 => "livre", 2 => "moderado", _ => "adulto" };
        if (tab == 5) free = _price.Selected switch { 1 => true, 2 => false, _ => null };
        return (from, to, free, ratings, MarketExtra());
    }

    private string[] KindsOfTab => TabKinds[Math.Clamp(_tabs.Selected, 0, TabKinds.Length - 1)];
    private string KindsName => string.Join(",", KindsOfTab);
    private string SortName => _sortNames[Math.Clamp(_sort.Selected, 0, _sortNames.Length - 1)];
    private string? CategoryName => _category.Selected <= 0 ? null : _category.GetItemMetadata(_category.Selected).VariantType == Variant.Type.String ? _category.GetItemMetadata(_category.Selected).AsString() : _category.GetItemText(_category.Selected);

    private void OnTab(int index)
    {
        _seq++; Done(); _page = SearchPageData.Empty; _building = true; _category.Clear(); _category.AddItem("Todas as categorias"); _building = false;
        RebuildSorts(index); _rating.Select(0); _when.Select(0); _price.Select(0); _onlyCalendar.SetPressedNoSignal(false); ResetMarketFilters();
        ClearResults(); ShowPreview(null); UpdateChrome(); if (_keepHit is not null) { ShowKeptHit(); return; } Search();
    }

    private void UpdateChrome()
    {
        var tab = _tabs.Selected;
        _pager.Visible = _page.Pages > 1 && tab != 0; _pageLabel.Text = $"Página {_page.Page} de {_page.Pages}"; _prev.Disabled = _page.Page <= 1; _next.Disabled = _page.Page >= _page.Pages;
        _sort.Visible = _query.Text.Trim().Length > 0 || tab != 0; _category.Visible = tab != 0 && _sort.Visible;
        _rating.Visible = tab is 2 or 3 or 4; _when.Visible = _onlyCalendar.Visible = _newEvent.Visible = tab == 3; _price.Visible = tab is 3 or 5;
        _minPrice.Visible = _maxPrice.Visible = _creator.Visible = _rightCopy.Visible = _rightModify.Visible = _rightTransfer.Visible = tab == 5; _storeKind.Visible = _newStore.Visible = tab == 6;
        _ownerChip.Visible = tab == 5 && _ownerFilter != Guid.Empty; _ownerChipText.Text = $"Produtos de “{_ownerFilterName}”";
    }

    /// <summary>One column when the window is narrow: the results, or the details of the chosen one with a way back.</summary>
    private void Adapt()
    {
        _narrow = _body.Size.X > 0 && _body.Size.X < NarrowWidth;
        _right.Visible = !_narrow || _detailOpen; _left.Visible = !_narrow || !_detailOpen; if (_backInDetails is not null) _backInDetails.Visible = _narrow && _detailOpen;
        _right.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _right.SizeFlagsStretchRatio = 2;
    }

    // ---- network ------------------------------------------------------------------------------------------------------------------

    private void Send(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, body); if (status >= 0) done(status, text); return; } // tests: a negative status is a server that has not answered yet
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        string[] headers = body is null ? [$"Authorization: Bearer {session.AccessToken}"] : [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"];
        if (http.Request($"{app.ApiBaseUrl}{path}", headers, method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    private static string Failure(int status) => status switch { 0 => "Sem conexão com o servidor.", 401 => "Entre na sua conta para buscar.", 403 => "Você não tem acesso a isto.", 429 => "Calma! Muitas tentativas. Tente de novo daqui a pouco.", _ => $"Algo deu errado (HTTP {status})." };

    /// <summary>An image kept behind the login (a profile picture): a path of this API, fetched with the session.</summary>
    private static bool IsApiImage(string? url) => url is { Length: > 8 } && url.StartsWith("/api/v1/", StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal);

    /// <summary>Fetches the bytes of a picture of this API (null when it is not there). Tests answer nothing.</summary>
    private void FetchBytes(string path, Action<byte[]?> done)
    {
        if (Transport is not null) { done(null); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated || !IsApiImage(path)) { done(null); return; }
        var http = new HttpRequest { Timeout = 15, BodySizeLimit = 8 * 1024 * 1024 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success && code is >= 200 and < 300 ? bytes : null); };
        if (http.Request($"{app.ApiBaseUrl}{path}", [$"Authorization: Bearer {session.AccessToken}"]) != Error.Ok) { http.QueueFree(); done(null); }
    }

    /// <summary>A search starts: the button becomes "Cancelar" and a repeated identical request is ignored.</summary>
    private bool Begin(string path)
    {
        if (_pending && _inflight == path) return false;
        _seq++; _pending = true; _inflight = path; _go.Text = "Cancelar"; _note.Text = Pending; return true;
    }

    private void Done() { _pending = false; _inflight = string.Empty; _go.Text = "Buscar"; }

    private void OnGo()
    {
        if (_pending) { _seq++; Done(); _note.Text = "Busca cancelada."; if (_hits.Count == 0) ShowState("Busca cancelada.", retry: true); return; }
        Search();
    }

    // ---- suggestions and history --------------------------------------------------------------------------------------------------

    private void OnTyped()
    {
        var typed = _query.Text.Trim(); ShowHints();
        if (SearchText.Normalize(typed).Length < GlobalSearch2.MinLength) return;
        var seq = ++_suggestSeq;
        Send(Godot.HttpClient.Method.Get, SearchApi.SuggestPath(typed), null, (status, body) => { if (seq == _suggestSeq && status is >= 200 and < 300) ShowHints(SearchApi.ParseSuggestions(body)); });
    }

    /// <summary>Searches made before that fit what is typed, then what the server suggests.</summary>
    public void ShowHints(IReadOnlyList<string>? fromServer = null)
    {
        foreach (var child in _hints.GetChildren()) child.QueueFree();
        var typed = _query.Text.Trim(); var seen = new HashSet<string>(); var shown = new List<string>();
        foreach (var past in KeepHistory ? _history.Matching(typed, 4) : []) if (seen.Add(SearchText.Normalize(past)) && SearchText.Normalize(past) != SearchText.Normalize(typed)) shown.Add(past);
        foreach (var s in fromServer ?? []) if (shown.Count < 8 && seen.Add(SearchText.Normalize(s))) shown.Add(s);
        foreach (var text in shown)
        {
            var past = RememberedSearches.Any(h => SearchText.Normalize(h) == SearchText.Normalize(text)); var captured = text;
            var button = new Button { Text = (past ? "↺ " : "") + text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 10, 4); button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => { _query.Text = captured; Search(); }; _hints.AddChild(button);
        }
        _hints.Visible = typed.Length > 0 && shown.Count > 0; _hintTexts = shown;
    }

    private IReadOnlyList<string> _hintTexts = [];
    public void ClearHistory() { _history.Clear(); _history.Save(HistoryFile); ShowHints(); }
}

/// <summary>The limits the server applies, repeated here so the window stops typing at the same point.</summary>
internal static class GlobalSearch2 { public const int MinLength = 2; }
internal static class SearchModerationLimits { public const int MaxReason = 500; }
