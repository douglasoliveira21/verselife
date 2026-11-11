using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// "Buscar no LifeVerse": one window to find islands, plots, people, groups, stores, products, events and points of interest. Three tabs (search, favorites,
/// recommended for you), filters by kind and category, ordering, suggestions while typing mixed with the searches made before (kept on this computer only),
/// thumbnails, a preview of where the result is, and a button that takes the avatar there. Results can be favorited and reported.
/// </summary>
public partial class SearchWindow : Node
{
    private const string Pending = "Buscando…";
    private static readonly string[] KindIcons = ["map", "grid", "user", "people", "bag", "cube", "sun", "pin"]; // same order as SearchKinds.All

    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private Control _searchRow = null!, _filterRow = null!, _interestRow = null!, _pager = null!, _reportRow = null!;
    private LineEdit _query = null!, _interests = null!, _reason = null!;
    private HFlowContainer _hints = null!;
    private OptionButton _kind = null!, _sort = null!, _category = null!;
    private ItemList _list = null!;
    private Label _note = null!, _pageLabel = null!, _pTitle = null!, _pMeta = null!, _pDescription = null!, _pWhere = null!;
    private Button _prev = null!, _next = null!, _go = null!, _favorite = null!, _report = null!, _profile = null!;
    /// <summary>"Ver perfil" on a person: opens the profile window for that user.</summary>
    public Action<Guid>? OpenProfile { get; set; }
    public bool ProfileVisibleForTesting => _profile.Visible;
    public void PressProfileForTesting() => _profile.EmitSignal(BaseButton.SignalName.Pressed);
    private TextureRect _thumb = null!;
    private Godot.Timer _typing = null!;

    private readonly List<SearchHit> _hits = [];
    private readonly Dictionary<string, Texture2D> _thumbnails = [];
    private readonly Queue<(string Url, int Row)> _thumbQueue = new();
    private SearchPageData _page = SearchPageData.Empty;
    private SearchHistory _history = new();
    private bool _building, _fetching; private int _seq, _suggestSeq; private Guid _opened;
    private string _lastQuery = string.Empty;

    /// <summary>Where the search history is kept (this computer only). Tests change it.</summary>
    public static string HistoryPath { get; set; } = "user://search_history.json";
    private static string HistoryFile => ProjectSettings.GlobalizePath(HistoryPath);
    /// <summary>Set by the main UI: join an island, move to a spot, move to a plot, and which island the avatar is on.</summary>
    public Action<Guid>? JoinIsland { get; set; }
    public Action<float, float>? TeleportToPoint { get; set; }
    public Func<Guid, bool>? TeleportToParcel { get; set; }
    public Func<Guid>? CurrentIsland { get; set; }
    /// <summary>Tests: answers the requests without a network: (method, path, body) -> (status, body).</summary>
    public Func<string, string, string?, (int Status, string Body)>? Transport { get; set; }

    public bool Visible => _window.Visible;
    public IReadOnlyList<SearchHit> Hits => _hits;
    public SearchHit? Selected => _list.GetSelectedItems() is { Length: > 0 } rows && rows[0] < _hits.Count ? _hits[rows[0]] : null;

    public override void _Ready() { _history = SearchHistory.Load(HistoryFile); Build(); }

    public void Toggle() { if (_window.Visible) _window.Visible = false; else Open(); }

    public void Open()
    {
        _window.Visible = true; _window.MoveToFront(); _query.GrabFocus(); ShowHints();
        if (_tabs.Selected == 0 && _hits.Count == 0) Explore();
    }

    // ---- building -----------------------------------------------------------------------------------------------------------------

    private static Button Action(string text, UiStyle.ButtonKind kind, System.Action action, int height = 38)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        UiStyle.StyleButton(button, kind, 10, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private static OptionButton Choice(params string[] items) { var option = new OptionButton { CustomMinimumSize = new Vector2(0, 36), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var item in items) option.AddItem(item); return option; }

    private static Label Text(string text = "", bool muted = false, int size = 15)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) };
        label.AddThemeFontSizeOverride("font_size", size); if (muted) label.AddThemeColorOverride("font_color", UiStyle.TextMuted); return label;
    }

    private void Build()
    {
        _window = new FloatingWindow("Buscar no LifeVerse", icon: "search") { Name = "SearchWindow", Visible = false };
        _window.Place(.5f, .5f, -430, -310, 430, 310);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 16 }; _tabs.AddTab("Buscar", "search"); _tabs.AddTab("Favoritos", "star"); _tabs.AddTab("Para você", "bulb"); content.AddChild(_tabs);

        _searchRow = new HBoxContainer(); ((HBoxContainer)_searchRow).AddThemeConstantOverride("separation", 8); content.AddChild(_searchRow);
        _query = new LineEdit { PlaceholderText = "Ilhas, terrenos, pessoas, grupos, lojas, eventos…", CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true, MaxLength = SearchText.MaxQueryLength };
        _query.AddThemeFontSizeOverride("font_size", 16); _query.TextSubmitted += _ => Search(); _query.TextChanged += _ => { _typing.Start(); }; _searchRow.AddChild(_query);
        var go = Action("Buscar", UiStyle.ButtonKind.Primary, () => Search()); go.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; go.CustomMinimumSize = new Vector2(110, 38); _searchRow.AddChild(go);
        _typing = new Godot.Timer { OneShot = true, WaitTime = .25 }; _typing.Timeout += OnTyped; AddChild(_typing);

        _hints = new HFlowContainer(); _hints.AddThemeConstantOverride("h_separation", 6); _hints.AddThemeConstantOverride("v_separation", 4); content.AddChild(_hints);

        _filterRow = new HBoxContainer(); ((HBoxContainer)_filterRow).AddThemeConstantOverride("separation", 8); content.AddChild(_filterRow);
        _kind = Choice(["Tudo", .. SearchKinds.All.Select(SearchKinds.Label)]); _kind.ItemSelected += _ => Search(); _filterRow.AddChild(_kind);
        _category = Choice("Todas as categorias"); _category.ItemSelected += _ => { if (!_building) Search(); }; _filterRow.AddChild(_category);
        _sort = Choice(SearchApi.SortLabels); _sort.ItemSelected += _ => Search(); _filterRow.AddChild(_sort);

        _interestRow = new HBoxContainer(); ((HBoxContainer)_interestRow).AddThemeConstantOverride("separation", 8); _interestRow.Visible = false; content.AddChild(_interestRow);
        _interests = new LineEdit { PlaceholderText = "O que você gosta? música, pesca, arte… (separe por vírgula)", CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 400 }; _interests.AddThemeFontSizeOverride("font_size", 16); _interestRow.AddChild(_interests);
        var save = Action("Salvar interesses", UiStyle.ButtonKind.Primary, SaveInterests); save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; save.CustomMinimumSize = new Vector2(170, 38); _interestRow.AddChild(save);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); content.AddChild(body);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(430, 0) }; left.AddThemeConstantOverride("separation", 6); body.AddChild(left);
        _list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, FixedIconSize = new Vector2I(44, 44), IconMode = ItemList.IconModeEnum.Left, MaxTextLines = 2, AllowReselect = true, CustomMinimumSize = new Vector2(0, 300) };
        _list.AddThemeFontSizeOverride("font_size", 15); _list.ItemSelected += row => ShowPreview(row >= 0 && row < _hits.Count ? _hits[(int)row] : null); _list.ItemActivated += _ => GoToResult(); left.AddChild(_list);
        _pager = new HBoxContainer(); ((HBoxContainer)_pager).AddThemeConstantOverride("separation", 8); left.AddChild(_pager);
        _prev = Action("‹ Anterior", UiStyle.ButtonKind.Secondary, () => Move(-1), 34); _pager.AddChild(_prev);
        _pageLabel = Text("", true); _pageLabel.HorizontalAlignment = HorizontalAlignment.Center; _pageLabel.CustomMinimumSize = new Vector2(120, 0); _pager.AddChild(_pageLabel);
        _next = Action("Próxima ›", UiStyle.ButtonKind.Secondary, () => Move(1), 34); _pager.AddChild(_next);
        _note = Text("", true, 14); left.AddChild(_note);

        var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 0) }; side.AddThemeConstantOverride("separation", 6); body.AddChild(side);
        _thumb = new TextureRect { CustomMinimumSize = new Vector2(0, 130), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered }; side.AddChild(_thumb);
        _pTitle = Text("Escolha um resultado", size: 19); _pTitle.AddThemeFontOverride("font", UiStyle.Bold); side.AddChild(_pTitle);
        _pMeta = Text("", true); side.AddChild(_pMeta); _pDescription = Text(); side.AddChild(_pDescription); _pWhere = Text("", true); side.AddChild(_pWhere);
        _go = Action("Ir até lá", UiStyle.ButtonKind.Primary, GoToResult); side.AddChild(_go);
        _profile = Action("Ver perfil", UiStyle.ButtonKind.Secondary, () => { if (Selected is { Kind: SearchKinds.Person } person) OpenProfile?.Invoke(person.Id); }); side.AddChild(_profile);
        _favorite = Action("Favoritar", UiStyle.ButtonKind.Secondary, ToggleFavorite); side.AddChild(_favorite);
        _report = Action("Denunciar resultado", UiStyle.ButtonKind.Secondary, () => { _reportRow.Visible = !_reportRow.Visible; if (_reportRow.Visible) _reason.GrabFocus(); }); side.AddChild(_report);
        _reportRow = new VBoxContainer { Visible = false }; side.AddChild(_reportRow);
        _reason = new LineEdit { PlaceholderText = "Por que você está denunciando?", CustomMinimumSize = new Vector2(0, 36), MaxLength = SearchModerationLimits.MaxReason }; _reason.AddThemeFontSizeOverride("font_size", 15); _reportRow.AddChild(_reason);
        _reportRow.AddChild(Action("Enviar denúncia", UiStyle.ButtonKind.Secondary, SendReport, 34));

        _tabs.TabChanged += OnTab; ShowPreview(null); UpdateChrome();
        AddChild(_window);
    }

    // ---- network ------------------------------------------------------------------------------------------------------------------

    private void Send(Godot.HttpClient.Method method, string path, string? body, Action<int, string> done)
    {
        if (Transport is not null) { var (status, text) = Transport(method.ToString().ToUpperInvariant(), path, body); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        string[] headers = body is null ? [$"Authorization: Bearer {session.AccessToken}"] : [$"Authorization: Bearer {session.AccessToken}", "Content-Type: application/json"];
        if (http.Request($"{app.ApiBaseUrl}{path}", headers, method, body ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    private static string Failure(int status) => status switch { 0 => "Sem conexão com o servidor.", 401 => "Entre na sua conta para buscar.", 429 => "Calma! Muitas tentativas. Tente de novo daqui a pouco.", _ => $"Algo deu errado (HTTP {status})." };

    // ---- searching ----------------------------------------------------------------------------------------------------------------

    private string KindName => _kind.Selected <= 0 ? string.Empty : SearchKinds.All[_kind.Selected - 1];
    private string SortName => SearchApi.SortNames[Math.Clamp(_sort.Selected, 0, SearchApi.SortNames.Length - 1)];
    private string? CategoryName => _category.Selected <= 0 ? null : _category.GetItemText(_category.Selected);

    /// <summary>Searches for what is typed, from the first page. With nothing typed, shows the places people go to.</summary>
    public void Search(int page = 1)
    {
        _hints.Visible = false; var text = _query.Text.Trim();
        if (text.Length == 0) { Explore(); return; }
        if (SearchText.Normalize(text).Length < GlobalSearch2.MinLength) { _note.Text = "Digite pelo menos 2 letras."; return; }
        if (page == 1) { _history.Add(text); _history.Save(HistoryFile); _lastQuery = text; }
        var seq = ++_seq; _note.Text = Pending;
        Send(Godot.HttpClient.Method.Get, SearchApi.SearchPath(_lastQuery.Length > 0 && page > 1 ? _lastQuery : text, KindName, CategoryName, SortName, page), null, (status, body) =>
        {
            if (seq != _seq) return;
            if (status is < 200 or >= 300) { Fill([], SearchPageData.Empty); _note.Text = Failure(status); return; }
            var data = SearchApi.ParsePage(body); Fill(data.Items, data); RefreshCategories(data);
            _note.Text = data.Total == 0 ? "Nada encontrado. Tente outras palavras ou tire um filtro." : $"{data.Total} resultado(s). Duplo clique para ir até lá.";
        });
    }

    private void Move(int by) { if (_tabs.Selected == 1) LoadFavorites(Math.Clamp(_page.Page + by, 1, _page.Pages)); else Search(Math.Clamp(_page.Page + by, 1, _page.Pages)); }

    /// <summary>Nothing typed yet: the popular places, then the newest ones.</summary>
    public void Explore()
    {
        var seq = ++_seq; _note.Text = Pending;
        Send(Godot.HttpClient.Method.Get, SearchApi.PopularPath, null, (status, body) =>
        {
            if (seq != _seq) return;
            if (status is < 200 or >= 300) { Fill([], SearchPageData.Empty); _note.Text = Failure(status); return; }
            var popular = SearchApi.ParseList(body).ToList();
            Send(Godot.HttpClient.Method.Get, SearchApi.RecentPath, null, (s2, b2) =>
            {
                if (seq != _seq) return;
                var seen = popular.Select(h => (h.Kind, h.Id)).ToHashSet(); if (s2 is >= 200 and < 300) popular.AddRange(SearchApi.ParseList(b2).Where(h => seen.Add((h.Kind, h.Id))));
                Fill(popular, SearchPageData.Empty); _note.Text = popular.Count == 0 ? "Ainda não há lugares para mostrar. Escreva algo para buscar." : "Lugares populares e novidades. Escreva algo para buscar.";
            });
        });
    }

    private void LoadFavorites(int page = 1)
    {
        var seq = ++_seq; _note.Text = Pending;
        Send(Godot.HttpClient.Method.Get, SearchApi.FavoritesPath(null, page), null, (status, body) =>
        {
            if (seq != _seq) return;
            if (status is < 200 or >= 300) { Fill([], SearchPageData.Empty); _note.Text = Failure(status); return; }
            var data = SearchApi.ParsePage(body); Fill(data.Items, data);
            _note.Text = data.Total == 0 ? "Você ainda não favoritou nada. Use “Favoritar” em um resultado." : $"{data.Total} favorito(s).";
        });
    }

    private void LoadRecommended()
    {
        var seq = ++_seq; _note.Text = Pending;
        Send(Godot.HttpClient.Method.Get, SearchApi.InterestsPath, null, (s, b) => { if (s is >= 200 and < 300) _interests.Text = string.Join(", ", SearchApi.ParseStrings(b)); });
        Send(Godot.HttpClient.Method.Get, SearchApi.RecommendedPath, null, (status, body) =>
        {
            if (seq != _seq) return;
            if (status is < 200 or >= 300) { Fill([], SearchPageData.Empty); _note.Text = Failure(status); return; }
            var items = SearchApi.ParseList(body); Fill(items, SearchPageData.Empty);
            _note.Text = items.Count == 0 ? "Conte do que você gosta (ex.: música, pesca, arte) e salve para ver sugestões." : $"{items.Count} sugestão(ões) pelos seus interesses.";
        });
    }

    private void SaveInterests()
    {
        var list = _interests.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Send(Godot.HttpClient.Method.Put, SearchApi.InterestsPath, SearchApi.InterestsBody(list), (status, body) =>
        {
            if (status is < 200 or >= 300) { _note.Text = Failure(status); return; }
            _interests.Text = string.Join(", ", SearchApi.ParseStrings(body)); LoadRecommended();
        });
    }

    private void OnTab(int index)
    {
        UpdateChrome(); _page = SearchPageData.Empty; _hits.Clear(); _list.Clear(); ShowPreview(null);
        if (index == 0) { if (_query.Text.Trim().Length == 0) Explore(); else Search(); } else if (index == 1) LoadFavorites(); else LoadRecommended();
    }

    private void UpdateChrome()
    {
        var tab = _tabs.Selected; _searchRow.Visible = _filterRow.Visible = tab == 0; _interestRow.Visible = tab == 2; _hints.Visible = tab == 0 && _hints.GetChildCount() > 0;
        _pager.Visible = tab != 2 && _page.Pages > 1; _pageLabel.Text = $"Página {_page.Page} de {_page.Pages}"; _prev.Disabled = _page.Page <= 1; _next.Disabled = _page.Page >= _page.Pages;
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
        foreach (var past in _history.Matching(typed, 4)) if (seen.Add(SearchText.Normalize(past)) && SearchText.Normalize(past) != SearchText.Normalize(typed)) shown.Add(past);
        foreach (var s in fromServer ?? []) if (shown.Count < 8 && seen.Add(SearchText.Normalize(s))) shown.Add(s);
        foreach (var text in shown)
        {
            var past = _history.Items.Any(h => SearchText.Normalize(h) == SearchText.Normalize(text)); var captured = text;
            var button = new Button { Text = (past ? "↺ " : "") + text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) }; UiStyle.StyleButton(button, UiStyle.ButtonKind.Secondary, 10, 4); button.AddThemeFontSizeOverride("font_size", 14);
            button.Pressed += () => { _query.Text = captured; Search(); }; _hints.AddChild(button);
        }
        _hints.Visible = _tabs?.Selected == 0 && shown.Count > 0; _hintTexts = shown;
    }

    private IReadOnlyList<string> _hintTexts = [];
    public void ClearHistory() { _history.Clear(); _history.Save(HistoryFile); ShowHints(); }

    // ---- results ------------------------------------------------------------------------------------------------------------------

    private void Fill(IReadOnlyList<SearchHit> hits, SearchPageData page)
    {
        _hits.Clear(); _hits.AddRange(hits); _page = page; _list.Clear(); _thumbQueue.Clear(); ShowPreview(null);
        for (var i = 0; i < _hits.Count; i++)
        {
            var hit = _hits[i]; var row = _list.AddItem(RowText(hit), IconFor(hit)); _list.SetItemTooltip(row, hit.Description);
            if (hit.ThumbnailUrl is not null && SearchApi.IsLoadableImageUrl(hit.ThumbnailUrl)) _thumbQueue.Enqueue((hit.ThumbnailUrl, row));
        }
        UpdateChrome(); NextThumbnail();
    }

    private static string RowText(SearchHit hit) => (hit.Favorite ? "★ " : "") + hit.Title + "\n" + hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "");
    private Texture2D IconFor(SearchHit hit) => hit.ThumbnailUrl is not null && _thumbnails.TryGetValue(hit.ThumbnailUrl, out var t) ? t : UiStyle.Icon(KindIcons[Math.Max(0, Array.IndexOf(SearchKinds.All, hit.Kind))], UiStyle.TextMuted, 36);

    private void RefreshCategories(SearchPageData data)
    {
        _building = true; var current = CategoryName; _category.Clear(); _category.AddItem("Todas as categorias");
        foreach (var (name, count) in data.Categories) _category.AddItem(name);
        for (var i = 1; i < _category.ItemCount; i++) if (string.Equals(_category.GetItemText(i), current, StringComparison.OrdinalIgnoreCase)) _category.Select(i);
        _building = false;
    }

    /// <summary>Thumbnails come one at a time so a long page does not open twenty connections at once.</summary>
    private void NextThumbnail()
    {
        if (_fetching || Transport is not null) return;
        while (_thumbQueue.Count > 0)
        {
            var (url, row) = _thumbQueue.Dequeue(); var mine = _seq;
            if (_thumbnails.TryGetValue(url, out var cached)) { if (row < _list.ItemCount) _list.SetItemIcon(row, cached); continue; }
            _fetching = true; var http = new HttpRequest { Timeout = 10, BodySizeLimit = 2_000_000 }; AddChild(http);
            http.RequestCompleted += (result, code, _, bytes) =>
            {
                http.QueueFree(); _fetching = false;
                if (result == (long)HttpRequest.Result.Success && code == 200 && DecodeImage(bytes) is { } texture) { _thumbnails[url] = texture; if (mine == _seq && row < _list.ItemCount) _list.SetItemIcon(row, texture); }
                NextThumbnail();
            };
            if (http.Request(url) != Error.Ok) { http.QueueFree(); _fetching = false; continue; }
            return;
        }
    }

    private static Texture2D? DecodeImage(byte[] bytes)
    {
        if (bytes.Length < 12) return null; var image = new Image();
        var error = bytes[0] == 0x89 && bytes[1] == 0x50 ? image.LoadPngFromBuffer(bytes) : bytes[0] == 0xFF && bytes[1] == 0xD8 ? image.LoadJpgFromBuffer(bytes) : bytes[0] == 0x52 && bytes[1] == 0x49 ? image.LoadWebpFromBuffer(bytes) : Error.FileUnrecognized;
        return error == Error.Ok && image.GetWidth() > 0 && image.GetWidth() <= 4096 && image.GetHeight() <= 4096 ? ImageTexture.CreateFromImage(image) : null;
    }

    // ---- preview and actions ------------------------------------------------------------------------------------------------------

    private void ShowPreview(SearchHit? hit)
    {
        _reportRow.Visible = false; _reason.Text = string.Empty;
        _go.Visible = _favorite.Visible = _report.Visible = hit is not null;
        _profile.Visible = hit is { Kind: SearchKinds.Person };
        if (hit is null) { _pTitle.Text = "Escolha um resultado"; _pMeta.Text = _pDescription.Text = _pWhere.Text = string.Empty; _thumb.Texture = null; _opened = Guid.Empty; return; }
        _pTitle.Text = hit.Title; _pMeta.Text = hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "") + (hit.Category.Length > 0 && !hit.Subtitle.Contains(hit.Category) ? " · " + hit.Category : "");
        _pDescription.Text = hit.Description.Length > 0 ? hit.Description : "Sem descrição."; _pWhere.Text = WhereText(hit); _thumb.Texture = IconOrThumb(hit);
        _go.Disabled = !hit.HasPlace; _go.Text = !hit.HasPlace ? "Sem lugar para visitar" : IsHere(hit) ? "Teleportar até lá" : "Ir para a ilha";
        _favorite.Text = hit.Favorite ? "Remover dos favoritos" : "Favoritar"; _report.Disabled = false;
        if (_opened != hit.Id) { _opened = hit.Id; SendEvent("open"); }
    }

    private Texture2D IconOrThumb(SearchHit hit) => hit.ThumbnailUrl is not null && _thumbnails.TryGetValue(hit.ThumbnailUrl, out var t) ? t : UiStyle.Icon(KindIcons[Math.Max(0, Array.IndexOf(SearchKinds.All, hit.Kind))], UiStyle.TextMuted, 72);
    private bool IsHere(SearchHit hit) => hit.IslandId is { } island && island != Guid.Empty && island == (CurrentIsland?.Invoke() ?? Guid.Empty);

    /// <summary>Where the result is, in words.</summary>
    public string WhereText(SearchHit hit)
    {
        if (!hit.HasPlace) return "Este resultado não tem um lugar para visitar.";
        var spot = hit.X is { } x && hit.Z is { } z ? $"x {x:0}, z {z:0}" : "sem ponto marcado";
        return IsHere(hit) ? $"Nesta ilha · {spot}." : $"Em outra ilha · {spot}. Você entra na ilha e é levado até o ponto.";
    }

    private void SendEvent(string type) => Send(Godot.HttpClient.Method.Post, SearchApi.EventsPath, SearchApi.EventBody(type, _lastQuery), (_, _) => { });

    /// <summary>Takes the avatar to the chosen result: a spot or plot on this island, or into the island and then to the spot.</summary>
    public void GoToResult()
    {
        if (Selected is not { } hit) return;
        if (!hit.HasPlace || hit.IslandId is not { } island) { _note.Text = "Este resultado não tem um lugar para visitar."; return; }
        SendEvent("teleport");
        if (IsHere(hit))
        {
            if (hit.Kind == SearchKinds.Parcel && TeleportToParcel?.Invoke(hit.Id) == true) { _note.Text = $"Indo para {hit.Title}…"; return; }
            if (hit.X is { } x && hit.Z is { } z && TeleportToPoint is not null) { TeleportToPoint(x, z); _note.Text = $"Indo para {hit.Title}…"; return; }
            _note.Text = "Você já está nesta ilha."; return;
        }
        if (hit.X is { } px && hit.Z is { } pz) SearchTravel.Set(island, px, pz); else SearchTravel.Clear();
        _note.Text = $"Entrando na ilha de {hit.Title}…"; JoinIsland?.Invoke(island);
    }

    private void ToggleFavorite()
    {
        if (Selected is not { } hit) return; var row = _list.GetSelectedItems()[0]; var wanted = !hit.Favorite;
        Send(wanted ? Godot.HttpClient.Method.Put : Godot.HttpClient.Method.Delete, SearchApi.FavoritePath(hit.Kind, hit.Id), null, (status, _) =>
        {
            if (status is < 200 or >= 300) { _note.Text = status == 404 ? "Este resultado não está mais disponível." : status == 409 ? "Sua lista de favoritos está cheia." : Failure(status); return; }
            if (_tabs.Selected == 1 && !wanted) { LoadFavorites(_page.Page); return; }
            var updated = hit with { Favorite = wanted }; _hits[row] = updated; _list.SetItemText(row, RowText(updated)); ShowPreview(updated); _list.Select(row);
            _note.Text = wanted ? "Adicionado aos favoritos." : "Removido dos favoritos.";
        });
    }

    private void SendReport()
    {
        if (Selected is not { } hit) return; var reason = _reason.Text.Trim();
        if (reason.Length < 3) { _note.Text = "Conte em poucas palavras o motivo da denúncia."; return; }
        Send(Godot.HttpClient.Method.Post, SearchApi.ReportPath(hit.Kind, hit.Id), SearchApi.ReportBody(reason), (status, _) =>
        {
            _reportRow.Visible = false; _reason.Text = string.Empty;
            _note.Text = status is >= 200 and < 300 ? "Denúncia enviada. Obrigado por ajudar a manter o LifeVerse seguro." : status == 404 ? "Este resultado não está mais disponível." : status == 400 ? "Você não pode denunciar o seu próprio conteúdo." : Failure(status);
        });
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------

    public void SetQueryForTesting(string text) { _query.Text = text; }
    public void SelectForTesting(int row) { _list.Select(row); ShowPreview(row >= 0 && row < _hits.Count ? _hits[row] : null); }
    public void SelectTabForTesting(int tab) => _tabs.Select(tab);
    public void SetFiltersForTesting(int kind, int sort) { _kind.Selected = kind; _sort.Selected = sort; }
    public void SetCategoryForTesting(string name) { for (var i = 0; i < _category.ItemCount; i++) if (_category.GetItemText(i) == name) _category.Select(i); }
    public void FavoriteForTesting() => ToggleFavorite();
    public void ReportForTesting(string reason) { _reason.Text = reason; SendReport(); }
    public void SetInterestsForTesting(string text) { _interests.Text = text; SaveInterests(); }
    public void NextPageForTesting() => Move(1);
    public string NoteForTesting => _note.Text;
    public string PreviewTitleForTesting => _pTitle.Text;
    public string PreviewWhereForTesting => _pWhere.Text;
    public string GoTextForTesting => _go.Text;
    public bool GoEnabledForTesting => _go.Visible && !_go.Disabled;
    public string FavoriteTextForTesting => _favorite.Text;
    public IReadOnlyList<string> HintsForTesting => _hintTexts;
    public IReadOnlyList<string> CategoriesForTesting => [.. Enumerable.Range(0, _category.ItemCount).Select(_category.GetItemText)];
    public string InterestsForTesting => _interests.Text;
    public string RowTextForTesting(int row) => _list.GetItemText(row);
    public bool PagerVisibleForTesting => _pager.Visible;
    public int TabForTesting => _tabs.Selected;
    public Control WindowForTesting => _window;
}

/// <summary>The limits the server applies, repeated here so the window stops typing at the same point.</summary>
internal static class GlobalSearch2 { public const int MinLength = 2; }
internal static class SearchModerationLimits { public const int MaxReason = 500; }
