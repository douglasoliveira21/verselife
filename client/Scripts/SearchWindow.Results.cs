using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts;

/// <summary>The left column of the search window: searching (every tab, the start screen, favorites), the list of result cards with their pictures, paging, and the loading, empty and error states.</summary>
public partial class SearchWindow
{
    private static readonly string[] KindIcons = ["map", "grid", "user", "people", "bag", "cube", "sun", "pin"]; // same order as SearchKinds.All
    private static readonly (string Title, string[] Kinds)[] Sections = [("Pessoas", [SearchKinds.Person]), ("Lugares", [SearchKinds.Island, SearchKinds.Parcel, SearchKinds.Place]), ("Eventos", [SearchKinds.Event]), ("Grupos", [SearchKinds.Group]), ("Itens", [SearchKinds.Product]), ("Lojas", [SearchKinds.Store])];

    private ScrollContainer _resultScroll = null!;
    private VBoxContainer _resultBox = null!;
    private LineEdit? _interests;
    private string _interestsText = string.Empty;
    private readonly List<SearchHit> _hits = [];
    private readonly List<PanelContainer> _cards = [];
    private readonly List<string> _cardTexts = [];
    private readonly Dictionary<string, Texture2D> _thumbnails = [];
    private readonly Queue<(string Url, TextureRect Target)> _thumbQueue = new();
    private bool _fetching;
    private int _selectedIndex = -1;

    public IReadOnlyList<SearchHit> Hits => _hits;
    public SearchHit? Selected => _selectedIndex >= 0 && _selectedIndex < _hits.Count ? _hits[_selectedIndex] : null;

    private void BuildResultsColumn()
    {
        _resultScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 200) };
        _resultBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _resultBox.AddThemeConstantOverride("separation", 6); _resultScroll.AddChild(_resultBox); _left.AddChild(_resultScroll);
        _pager = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; ((HBoxContainer)_pager).AddThemeConstantOverride("separation", 8); _left.AddChild(_pager);
        _prev = Action("‹ Anterior", UiStyle.ButtonKind.Secondary, () => Move(-1), 34); _prev.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; _pager.AddChild(_prev);
        _pageLabel = Text("", true); _pageLabel.HorizontalAlignment = HorizontalAlignment.Center; _pageLabel.CustomMinimumSize = new Vector2(120, 0); _pageLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; _pager.AddChild(_pageLabel);
        _next = Action("Próxima ›", UiStyle.ButtonKind.Secondary, () => Move(1), 34); _next.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; _pager.AddChild(_next);
        _note = Text("", true, 14); _left.AddChild(_note);
    }

    // ---- searching ----------------------------------------------------------------------------------------------------------------

    /// <summary>Searches for what is typed on the chosen tab, from the first page. With nothing typed: the start screen (Tudo), the most popular (the other tabs) or a request for a name (Pessoas).</summary>
    public void Search(int page = 1)
    {
        _hints.Visible = false; var text = _query.Text.Trim(); var tab = _tabs.Selected;
        if (tab == 3 && _onlyCalendar.ButtonPressed) { LoadAgenda(); return; }
        if (_onlyFavorites.ButtonPressed) { LoadFavorites(page); return; }
        if (text.Length == 0)
        {
            if (tab == 0) { Explore(); return; }
            if (tab == 1) { _page = SearchPageData.Empty; _hits.Clear(); ShowState("Digite um nome para encontrar pessoas.", icon: "user"); UpdateChrome(); _note.Text = "Digite um nome para encontrar pessoas."; return; }
        }
        else if (SearchText.Normalize(text).Length < GlobalSearch2.MinLength) { _note.Text = "Digite pelo menos 2 letras."; return; }
        if (page == 1 && text.Length > 0) { if (KeepHistory) { _history.Add(text); _history.Save(HistoryFile); } _lastQuery = text; }
        var query = text.Length > 0 && page > 1 && _lastQuery.Length > 0 ? _lastQuery : text;
        var (from, to, free, ratings, extra) = Filters(); var path = SearchApi.SearchPath(query, KindsName, CategoryName, SortName, page, tab == 0 ? 50 : 20, from, to, free, ratings, extra);
        if (!Begin(path)) return; var seq = _seq; _retry = () => Search(page);
        if (_hits.Count == 0) ShowState(Pending, icon: "search");
        Send(Godot.HttpClient.Method.Get, path, null, (status, body) =>
        {
            if (seq != _seq) return; Done();
            if (status is < 200 or >= 300) { Fail(status); return; }
            var data = SearchApi.ParsePage(body); RefreshCategories(data);
            if (tab == 0) FillGrouped(data); else Fill(data.Items, data);
            _count.Text = data.Total == 1 ? "1 resultado" : $"{data.Total} resultados";
            if (data.Total == 0) { ShowState(text.Length == 0 ? "Ainda não há nada para mostrar aqui." : "Nenhum resultado. Tente outras palavras ou tire um filtro.", icon: "search"); _note.Text = "Nada encontrado. Tente outras palavras ou tire um filtro."; }
            else Note($"{data.Total} resultado(s). Duplo clique para ir até lá.");
        });
    }

    /// <summary>The line under the results: the news of what the person just did stays once, over the usual count.</summary>
    private string _flash = string.Empty;
    private void Note(string usual) { _note.Text = _flash.Length > 0 ? _flash : usual; _flash = string.Empty; }

    private void Fail(int status) { Fill([], SearchPageData.Empty); var message = Failure(status); ShowState(message, retry: true, icon: "bell"); _note.Text = message; _count.Text = string.Empty; }

    private void Move(int by) { if (_onlyFavorites.ButtonPressed) LoadFavorites(Math.Clamp(_page.Page + by, 1, _page.Pages)); else Search(Math.Clamp(_page.Page + by, 1, _page.Pages)); }

    /// <summary>Nothing typed on Tudo: the start screen. Popular places, news and what fits the interests of the person, then the searches made before.</summary>
    public void Explore()
    {
        var paths = new[] { SearchApi.PopularPath, SearchApi.RecentPath, SearchApi.RecommendedPath };
        if (!Begin("explore")) return; var seq = _seq; _retry = Explore; if (_hits.Count == 0) ShowState(Pending, icon: "search");
        var answers = new (int Status, string Body)[3]; var left = paths.Length;
        Send(Godot.HttpClient.Method.Get, SearchApi.InterestsPath, null, (s, b) => { if (s is >= 200 and < 300) { _interestsText = string.Join(", ", SearchApi.ParseStrings(b)); if (_interests is not null && GodotObject.IsInstanceValid(_interests)) _interests.Text = _interestsText; } });
        for (var i = 0; i < paths.Length; i++)
        {
            var slot = i;
            Send(Godot.HttpClient.Method.Get, paths[i], null, (status, body) =>
            {
                if (seq != _seq) return; answers[slot] = (status, body);
                if (--left > 0) return; Done();
                if (answers[0].Status is < 200 or >= 300 && answers[1].Status is < 200 or >= 300) { Fail(answers[0].Status); return; }
                var seen = new HashSet<(string, Guid)>(); List<SearchHit> Read((int Status, string Body) a) => a.Status is >= 200 and < 300 ? [.. SearchApi.ParseList(a.Body).Where(h => seen.Add((h.Kind, h.Id)))] : [];
                var popular = Read(answers[0]); var recent = Read(answers[1]); var forYou = Read(answers[2]);
                FillStart(popular, recent, forYou);
            });
        }
    }

    private void LoadFavorites(int page = 1)
    {
        var path = SearchApi.FavoritesPath(KindsName, page);
        if (!Begin(path)) return; var seq = _seq; _retry = () => LoadFavorites(page); if (_hits.Count == 0) ShowState(Pending, icon: "star");
        Send(Godot.HttpClient.Method.Get, path, null, (status, body) =>
        {
            if (seq != _seq) return; Done();
            if (status is < 200 or >= 300) { Fail(status); return; }
            var data = SearchApi.ParsePage(body); Fill(data.Items, data); _count.Text = $"{data.Total} favorito(s)";
            if (data.Total == 0) ShowState("Você ainda não favoritou nada aqui. Use a estrela em um resultado.", icon: "star");
            _note.Text = data.Total == 0 ? "Você ainda não favoritou nada. Use “Favoritar” em um resultado." : $"{data.Total} favorito(s).";
        });
    }

    private void SaveInterests()
    {
        var list = _interestsText.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Send(Godot.HttpClient.Method.Put, SearchApi.InterestsPath, SearchApi.InterestsBody(list), (status, body) =>
        {
            if (status is < 200 or >= 300) { _note.Text = Failure(status); return; }
            _interestsText = string.Join(", ", SearchApi.ParseStrings(body)); _note.Text = "Interesses salvos."; _seq++; Done(); Explore();
        });
    }

    // ---- drawing the results ------------------------------------------------------------------------------------------------------

    private void ClearResults()
    {
        foreach (var child in _resultBox.GetChildren()) { _resultBox.RemoveChild(child); child.QueueFree(); }
        _hits.Clear(); _cards.Clear(); _cardTexts.Clear(); _metaLabels.Clear(); _thumbQueue.Clear(); _selectedIndex = -1; _detailOpen = false; Adapt();
    }

    /// <summary>A message in the place of the results: loading, nothing found, or an error with a way to try again.</summary>
    private void ShowState(string message, bool retry = false, string icon = "search")
    {
        ClearResults(); ShowPreview(null);
        var box = new VBoxContainer { Name = "State", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(0, 160) }; box.AddThemeConstantOverride("separation", 8);
        var picture = UiStyle.IconRect(icon, UiStyle.TextMuted, 44); picture.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; box.AddChild(picture);
        var text = Text(message, true, 16); text.HorizontalAlignment = HorizontalAlignment.Center; box.AddChild(text);
        if (retry) { var again = Action("Tentar novamente", UiStyle.ButtonKind.Secondary, () => _retry?.Invoke(), 36); again.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; again.CustomMinimumSize = new Vector2(180, 36); box.AddChild(again); }
        _resultBox.AddChild(box); _stateText = message; UpdateChrome();
    }

    private string _stateText = string.Empty;

    private void Fill(IReadOnlyList<SearchHit> hits, SearchPageData page)
    {
        ClearResults(); _page = page; ShowPreview(null);
        foreach (var hit in hits) AddCard(hit);
        UpdateChrome(); NextThumbnail(); DecoratePeople();
    }

    /// <summary>Tudo with a search: the results split by kind, a few of each, with "Ver todos" that opens that tab keeping the words.</summary>
    private void FillGrouped(SearchPageData data)
    {
        ClearResults(); _page = data; ShowPreview(null);
        foreach (var (title, kinds) in Sections)
        {
            var mine = data.Items.Where(h => kinds.Contains(h.Kind)).ToList(); if (mine.Count == 0) continue;
            var total = kinds.Sum(k => data.Counts.GetValueOrDefault(k)); var shown = mine.Take(3).ToList();
            var tab = Array.FindIndex(TabKinds, t => t.Length == kinds.Length && t.SequenceEqual(kinds));
            AddHeader(title, total > shown.Count && tab > 0 ? ($"Ver todos ({total})", () => _tabs.Select(tab)) : null);
            foreach (var hit in shown) AddCard(hit);
        }
        UpdateChrome(); NextThumbnail(); DecoratePeople();
    }

    /// <summary>The start screen of Tudo: the welcome text and tips, then what the server has to suggest, then the searches made before.</summary>
    private void FillStart(List<SearchHit> popular, List<SearchHit> recent, List<SearchHit> forYou)
    {
        ClearResults(); _page = SearchPageData.Empty; ShowPreview(null); _count.Text = string.Empty;
        var welcome = new VBoxContainer { Name = "Welcome", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; welcome.AddThemeConstantOverride("separation", 4);
        welcome.AddChild(Text("LifeVerse", false, 30, true)); welcome.AddChild(Text("Encontre pessoas, lugares, eventos, grupos, itens e lojas em todo o LifeVerse.", true, 16));
        welcome.AddChild(Text("Dicas de busca:", false, 15, true));
        foreach (var tip in new[] { "Use palavras-chave (ex.: praia, dança, RPG)", "Procure por nomes de usuários", "Encontre eventos que estão acontecendo agora", "Descubra lugares populares para explorar" }) welcome.AddChild(Text("•  " + tip, true, 14));
        _resultBox.AddChild(welcome);
        void Section(string title, List<SearchHit> items) { if (items.Count == 0) return; AddHeader(title, null); foreach (var hit in items) AddCard(hit); }
        Section("Lugares em destaque", popular); Section("Novidades", recent); Section("Para você", forYou);
        AddInterestsRow(); AddRecentSearches();
        _note.Text = popular.Count + recent.Count == 0 ? "Ainda não há lugares para mostrar. Escreva algo para buscar." : "Lugares populares e novidades. Escreva algo para buscar.";
        UpdateChrome(); NextThumbnail();
    }

    private void AddInterestsRow()
    {
        AddHeader("Seus interesses", null);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _resultBox.AddChild(row);

        _interests = new LineEdit { Text = _interestsText, PlaceholderText = "O que você gosta? música, pesca, arte… (separe por vírgula)", CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MaxLength = 400 }; _interests.AddThemeFontSizeOverride("font_size", 15); row.AddChild(_interests);
        var save = Action("Salvar", UiStyle.ButtonKind.Primary, () => { _interestsText = _interests!.Text; SaveInterests(); }); save.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; save.CustomMinimumSize = new Vector2(90, 38); row.AddChild(save);
    }

    private void AddRecentSearches()
    {
        if (!KeepHistory || _history.Items.Count == 0) return;
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8); _resultBox.AddChild(head);
        head.AddChild(Text("Pesquisas recentes", false, 15, true));
        var clear = new Button { Text = "Limpar", Flat = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Apaga o histórico de buscas deste computador" }; clear.AddThemeColorOverride("font_color", UiStyle.AccentText); clear.Pressed += () => { ClearHistory(); _seq++; Done(); Explore(); }; head.AddChild(clear);
        var flow = new HFlowContainer(); flow.AddThemeConstantOverride("h_separation", 6); flow.AddThemeConstantOverride("v_separation", 4); _resultBox.AddChild(flow);
        foreach (var past in _history.Items.Take(8))
        {
            var captured = past; var chip = new Button { Text = "↺ " + past, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) }; UiStyle.StyleButton(chip, UiStyle.ButtonKind.Secondary, 10, 4); chip.AddThemeFontSizeOverride("font_size", 14);
            chip.Pressed += () => { _query.Text = captured; Search(); }; flow.AddChild(chip);
        }
    }

    private void AddHeader(string title, (string Text, System.Action Action)? more)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _resultBox.AddChild(row);
        row.AddChild(Text(title, false, 16, true).WithColor(UiStyle.AccentText));
        if (more is { } link) { var button = new Button { Text = link.Text, Flat = true, FocusMode = Control.FocusModeEnum.None }; button.AddThemeColorOverride("font_color", UiStyle.AccentText); button.AddThemeFontSizeOverride("font_size", 14); button.Pressed += link.Action; row.AddChild(button); }
    }

    /// <summary>The grey line of a card: the kind and where or who; for people how I am related to them, for groups how many members and the category.</summary>
    private string MetaText(SearchHit hit)
    {
        if (hit.Kind == SearchKinds.Person) { var words = RelationWords(_relations.GetValueOrDefault(hit.Id, string.Empty)); return hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "") + (words.Length > 0 ? " · " + words : ""); }
        if (hit.Kind == SearchKinds.Event) return EventMeta(hit);
        if (hit.Kind is SearchKinds.Product or SearchKinds.Store) return MarketMeta(hit);
        if (hit.Kind == SearchKinds.Group) { var tag = hit.Subtitle.Replace("Grupo", string.Empty).Trim(); var members = (int)hit.Popularity; return "Grupo" + (tag.Length > 0 ? " " + tag : "") + $" · {members} {(members == 1 ? "membro" : "membros")}" + (hit.Category.Length > 0 ? " · " + hit.Category : ""); }
        return hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "");
    }

    private static string RowText(SearchHit hit) => (hit.Favorite ? "★ " : "") + hit.Title + "\n" + hit.KindLabel + (hit.Subtitle.Length > 0 ? " · " + hit.Subtitle : "");
    private static string ActionLabel(SearchHit hit) => hit.Kind switch { SearchKinds.Person => "Ver perfil", SearchKinds.Island or SearchKinds.Parcel or SearchKinds.Place => "Ver lugar", SearchKinds.Event => "Detalhes", SearchKinds.Group => "Ver grupo", SearchKinds.Product => "Ver item", SearchKinds.Store => "Ver loja", _ => "Detalhes" };
    private static Texture2D KindIcon(SearchHit hit, int size) => UiStyle.Icon(KindIcons[Math.Max(0, Array.IndexOf(SearchKinds.All, hit.Kind))], UiStyle.TextMuted, size);

    /// <summary>One result: picture, name, kind and where, a star when favorite, and the button of its kind. Clicking selects (the details open on the right); double click goes there.</summary>
    private void AddCard(SearchHit hit)
    {
        var index = _hits.Count; _hits.Add(hit); _cardTexts.Add(RowText(hit));
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = hit.Description, FocusMode = Control.FocusModeEnum.All }; Style(card, false); card.AddThemeStyleboxOverride("focus", UiStyle.Box(Colors.Transparent, UiStyle.Orange, 10, 2, 10, 8));
        card.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click) { Select(index); if (click.DoubleClick) GoToResult(); }
            else if (input is InputEventKey { Pressed: true, Keycode: Key.Enter or Key.KpEnter or Key.Space }) { Select(index); Primary(hit); card.AcceptEvent(); }
        };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        var thumb = new TextureRect { Name = "Thumb", CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Texture = KindIcon(hit, 40) };
        row.AddChild(thumb); if (!TryAssetThumbnail(hit.ThumbnailUrl, hit.IslandId ?? hit.Id, thumb) && hit.ThumbnailUrl is not null && (SearchApi.IsLoadableImageUrl(hit.ThumbnailUrl) || IsApiImage(hit.ThumbnailUrl))) _thumbQueue.Enqueue((hit.ThumbnailUrl, thumb));
        var text = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 0); row.AddChild(text);
        var title = Text((hit.Favorite ? "★ " : "") + hit.Title, false, 16, true, wrap: false); title.MouseFilter = Control.MouseFilterEnum.Ignore; text.AddChild(title);
        var meta = Text(MetaText(hit), true, 13, wrap: false); meta.MouseFilter = Control.MouseFilterEnum.Ignore; text.AddChild(meta); _metaLabels.Add(meta);
        var go = new Button { Text = ActionLabel(hit), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 34), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; UiStyle.StyleButton(go, UiStyle.ButtonKind.Tool, 12, 4, 8); go.AddThemeFontSizeOverride("font_size", 14);
        go.Pressed += () => { Select(index); Primary(hit); }; row.AddChild(go);
        _cards.Add(card); _resultBox.AddChild(card);
    }

    private static void Style(PanelContainer card, bool selected) => card.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.FieldFill, selected ? UiStyle.Accent : new Color(UiStyle.BorderSoft, .6f), 10, selected ? 2 : 1, 10, 8));

    /// <summary>What the button on the card does: open the profile or the group; for the rest the details are enough.</summary>
    private void Primary(SearchHit hit)
    {
        if (hit.Kind == SearchKinds.Person) OpenProfile?.Invoke(hit.Id);
        else if (hit.Kind == SearchKinds.Group) OpenGroup?.Invoke(hit.Id);
    }

    public void Select(int index)
    {
        if (index < 0 || index >= _hits.Count) return;
        if (_selectedIndex >= 0 && _selectedIndex < _cards.Count) Style(_cards[_selectedIndex], false);
        _selectedIndex = index; Style(_cards[index], true); _detailOpen = true; ShowPreview(_hits[index]); Adapt();
    }

    private void RefreshCategories(SearchPageData data)
    {
        _building = true; var current = CategoryName; _category.Clear(); _category.AddItem("Todas as categorias");
        foreach (var (name, _) in data.Categories) { _category.AddItem(_tabs.Selected == 3 ? EventCategoryLabel(name) : _tabs.Selected == 6 ? StoreCategoryLabel(name) : name); _category.SetItemMetadata(_category.ItemCount - 1, name); }
        for (var i = 1; i < _category.ItemCount; i++) if (string.Equals(_category.GetItemMetadata(i).AsString(), current, StringComparison.OrdinalIgnoreCase)) _category.Select(i);
        _building = false;
    }

    // ---- pictures -----------------------------------------------------------------------------------------------------------------

    /// <summary>The picture an owner set for an island (or a parcel) is an uploaded asset ("asset:id"), not a web address: it comes from the texture cache, which asks the server with the island it belongs to.</summary>
    private bool TryAssetThumbnail(string? reference, Guid island, TextureRect target)
    {
        if (reference is null || !reference.StartsWith("asset:", StringComparison.Ordinal) || !Guid.TryParse(reference[6..], out var asset)) return false;
        if (_thumbnails.TryGetValue(reference, out var have)) { target.Texture = have; return true; }
        if (GetParent()?.GetNodeOrNull<TextureCache>("TextureCache") is not { } cache) return true;
        cache.Request(asset, texture =>
        {
            if (texture is null) return; _thumbnails[reference] = texture;
            if (GodotObject.IsInstanceValid(target)) target.Texture = texture; if (Selected?.ThumbnailUrl == reference && GodotObject.IsInstanceValid(_pImage)) _pImage.Texture = texture;
        }, island);
        return true;
    }

    /// <summary>Thumbnails come one at a time so a long page does not open twenty connections at once.</summary>
    private void NextThumbnail()
    {
        if (_fetching || Transport is not null) return;
        while (_thumbQueue.Count > 0)
        {
            var (url, target) = _thumbQueue.Dequeue(); if (!GodotObject.IsInstanceValid(target)) continue;
            if (_thumbnails.TryGetValue(url, out var cached)) { target.Texture = cached; continue; }
            if (IsApiImage(url))
            {
                _fetching = true; var path = url; var picture = target;
                FetchBytes(path, bytes =>
                {
                    _fetching = false;
                    if (bytes is not null && DecodeImage(bytes) is { } texture) { _thumbnails[path] = texture; if (GodotObject.IsInstanceValid(picture)) picture.Texture = texture; if (Selected?.ThumbnailUrl == path) _pImage.Texture = texture; }
                    NextThumbnail();
                });
                return;
            }
            _fetching = true; var http = new HttpRequest { Timeout = 10, BodySizeLimit = 2_000_000 }; AddChild(http);
            http.RequestCompleted += (result, code, _, bytes) =>
            {
                http.QueueFree(); _fetching = false;
                if (result == (long)HttpRequest.Result.Success && code == 200 && DecodeImage(bytes) is { } texture) { _thumbnails[url] = texture; if (GodotObject.IsInstanceValid(target)) target.Texture = texture; if (Selected?.ThumbnailUrl == url) _pImage.Texture = texture; }
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
}

internal static class SearchLabelExtensions
{
    public static Label WithColor(this Label label, Color color) { label.AddThemeColorOverride("font_color", color); return label; }
}
