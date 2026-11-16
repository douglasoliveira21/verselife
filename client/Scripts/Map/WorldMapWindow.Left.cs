using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>The left column of the world map: the search for places (by name, region, owner, coordinates or "Name/x/z", with results in pages and the searches made before) and the categories, which are real filters.</summary>
public partial class WorldMapWindow
{
    private sealed record Result(string Title, string Subtitle, AtlasIsland? Island, double X, double Y, string Kind);

    private const int ResultsPerPage = 8;
    private PanelContainer _left = null!; private ScrollContainer _leftScroll = null!;
    private Button _backFromFilters = null!, _prevResults = null!, _nextResults = null!;
    private LineEdit _query = null!;
    private VBoxContainer _resultsBox = null!, _categoryBox = null!;
    private HFlowContainer _recentBox = null!;
    private Control _pager = null!;
    private Label _resultsNote = null!;
    private Godot.Timer _typing = null!;
    private readonly Dictionary<string, Button> _categoryButtons = [];
    private List<Result> _results = [];
    private int _resultPage;
    private SearchHistory _history = new();

    private static string HistoryFile => ProjectSettings.GlobalizePath("user://map_search_history.json");

    private void BuildLeft(Control body)
    {
        _left = new PanelContainer { CustomMinimumSize = new Vector2(280, 0), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _left.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .8f), new Color(UiStyle.BorderSoft, .5f), 12, 1, 10, 10)); body.AddChild(_left);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _left.AddChild(scroll); _leftScroll = scroll;
        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; column.AddThemeConstantOverride("separation", 10); scroll.AddChild(column);
        _backFromFilters = Btn("‹ Voltar ao mapa", UiStyle.ButtonKind.Secondary, () => { _filtersOpen = false; Adapt(); }, null, 34); _backFromFilters.Visible = false; column.AddChild(_backFromFilters);
        _history = SearchHistory.Load(HistoryFile);

        var search = Section(column, "Busca de Lugares", out var searchBody);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6); searchBody.AddChild(row);
        _query = new LineEdit { PlaceholderText = "Buscar ilha, região ou local...", ClearButtonEnabled = true, MaxLength = 80, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 38) }; _query.AddThemeFontSizeOverride("font_size", 15);
        _query.TextChanged += _ => _typing.Start(); _query.TextSubmitted += _ => ChooseFirst(); _query.FocusEntered += () => UpdateResults(); row.AddChild(_query);
        var go = new Button { Icon = UiStyle.Icon("search", UiStyle.Text, 20), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 38), TooltipText = "Buscar", ExpandIcon = false }; UiStyle.StyleButton(go, UiStyle.ButtonKind.Tool, 6, 6, 10); go.Pressed += ChooseFirst; row.AddChild(go);
        _typing = new Godot.Timer { OneShot = true, WaitTime = .2 }; _typing.Timeout += () => UpdateResults(); AddChild(_typing);
        _resultsBox = new VBoxContainer(); _resultsBox.AddThemeConstantOverride("separation", 4); searchBody.AddChild(_resultsBox);
        _pager = new HBoxContainer { Visible = false }; ((HBoxContainer)_pager).AddThemeConstantOverride("separation", 6); searchBody.AddChild(_pager);
        _prevResults = Btn("‹", UiStyle.ButtonKind.Tool, () => { _resultPage = Math.Max(0, _resultPage - 1); ShowResults(); }, "Resultados anteriores", 30); _pager.AddChild(_prevResults);
        _resultsNote = Lbl("", 13, false, UiStyle.TextMuted); _resultsNote.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; _resultsNote.HorizontalAlignment = HorizontalAlignment.Center; _pager.AddChild(_resultsNote);
        _nextResults = Btn("›", UiStyle.ButtonKind.Tool, () => { _resultPage = Math.Min((_results.Count - 1) / ResultsPerPage, _resultPage + 1); ShowResults(); }, "Mais resultados", 30); _pager.AddChild(_nextResults);
        _recentBox = new HFlowContainer(); _recentBox.AddThemeConstantOverride("h_separation", 6); _recentBox.AddThemeConstantOverride("v_separation", 4); searchBody.AddChild(_recentBox);
        _ = search;

        Section(column, "Categorias", out _categoryBox);
        foreach (var (key, label, icon) in Categories)
        {
            var captured = key; var button = new Button { Text = "  " + label, Icon = UiStyle.Icon(icon, UiStyle.TextMuted, 20), Alignment = HorizontalAlignment.Left, ExpandIcon = false, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), TooltipText = "Mostra só estas ilhas. Clique de novo para ver todas." };
            button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += () => SelectCategory(captured); _categoryBox.AddChild(button); _categoryButtons[key] = button;
        }
        _category = Hub.Settings.Category; if (Categories.All(c => c.Key != _category) || (_category == "adulto" && !Hub.Settings.ShowAdult)) _category = string.Empty; StyleCategories();
        BuildPlaces(column);
    }

    private Control Section(Container parent, string title, out VBoxContainer body)
    {
        var holder = new VBoxContainer(); holder.AddThemeConstantOverride("separation", 6); parent.AddChild(holder);
        var header = new Button { Text = title, Icon = UiStyle.Icon("chevron-down", UiStyle.TextMuted, 18), Alignment = HorizontalAlignment.Left, Flat = true, ExpandIcon = false, IconAlignment = HorizontalAlignment.Right, FocusMode = Control.FocusModeEnum.None }; header.AddThemeFontOverride("font", UiStyle.Bold); header.AddThemeFontSizeOverride("font_size", 16); header.AddThemeColorOverride("font_color", UiStyle.Text); holder.AddChild(header);
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 6); holder.AddChild(inner);
        header.Pressed += () => { inner.Visible = !inner.Visible; header.Icon = UiStyle.Icon(inner.Visible ? "chevron-down" : "chevron-right", UiStyle.TextMuted, 18); };
        body = inner; return holder;
    }

    private void StyleCategories()
    {
        foreach (var (key, button) in _categoryButtons)
        {
            var selected = key == _category; var icon = Categories.First(c => c.Key == key).Icon; button.Icon = UiStyle.Icon(icon, selected ? UiStyle.AccentText : UiStyle.TextMuted, 20); button.AddThemeColorOverride("font_color", selected ? UiStyle.AccentText : UiStyle.Text);
            foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed" }) button.AddThemeStyleboxOverride(state, UiStyle.Box(selected ? UiStyle.FieldFill : state == "normal" ? Colors.Transparent : new Color(UiStyle.FieldFill, .6f), selected ? new Color(UiStyle.Accent, .8f) : Colors.Transparent, 10, selected ? 1 : 0, 10, 6));
            button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        }
    }

    /// <summary>Chooses a category (or, choosing the one that is on, none). Adult islands first ask the player to say they are 18 or older.</summary>
    public void SelectCategory(string key)
    {
        if (key == _category) key = string.Empty;
        if (key == "adulto" && !Hub.Settings.ShowAdult) { AskAdult(); return; }
        var serverSide = (string k) => k is "residencial" or "comercial" or "adulto"; var needsReload = serverSide(_category) || serverSide(key);
        _category = key; Hub.Settings.Category = key; Hub.Settings.Save(); StyleCategories(); HideCard();
        if (needsReload || _items.Count == 0) Reload(); else Refresh(false);
        _canvas.FitAll(glide: true);
    }

    private void AskAdult()
    {
        if (_adultDialog is not null && GodotObject.IsInstanceValid(_adultDialog)) _adultDialog.QueueFree();
        _adultDialog = new FloatingWindow("Conteúdo adulto", icon: "alert") { Name = "WorldMapAdult" }; _adultDialog.Place(.5f, .5f, -230, -110, 230, 110);
        _adultDialog.Content.AddChild(Lbl("Estas ilhas têm conteúdo para maiores de 18 anos. Ao continuar, você confirma que tem 18 anos ou mais. Você pode desfazer isso no menu do mapa.", 15, false, UiStyle.Text, wrap: true));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 8); _adultDialog.Content.AddChild(row);
        var yes = Btn("Tenho 18 anos ou mais", UiStyle.ButtonKind.Primary, () => { Hub.Settings.ShowAdult = true; Hub.Settings.Save(); _adultDialog?.QueueFree(); SelectCategory("adulto"); }); yes.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(yes);
        var no = Btn("Cancelar", UiStyle.ButtonKind.Secondary, () => _adultDialog?.QueueFree()); no.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(no);
        AddChild(_adultDialog); _adultDialog.MoveToFront();
    }

    // ---- searching --------------------------------------------------------------------------------------------------------------------------

    private static string Norm(string? text) => SearchText.Normalize(text);

    private List<Result> Search(string text)
    {
        var list = new List<Result>(); var normalized = Norm(text); if (normalized.Length == 0) return list;
        if (MapCoordinates.TryParsePair(text, out var gx, out var gy)) { var under = MapCoordinates.IslandAt(_items, gx, gy); list.Add(new Result($"Ir para {gx:0}, {gy:0}", under is null ? "Coordenadas do mapa" : $"Dentro de {under.Name}", under, gx, gy, "coords")); }
        if (MapCoordinates.TryParsePlace(text, out var name, out var lx, out var lz) && _items.FirstOrDefault(i => Norm(i.Name) == Norm(name)) is { } place) { var (px, py) = MapCoordinates.ToGlobal(place, lx, lz); list.Add(new Result($"{place.Name} ({lx:0}, {lz:0})", "Lugar dentro da ilha", place, px, py, "place")); }
        var matches = _items.Select(i => (Island: i, Score: Score(i, normalized))).Where(m => m.Score > 0).OrderByDescending(m => m.Score).ThenBy(m => m.Island.Name, StringComparer.CurrentCultureIgnoreCase).Take(100);
        foreach (var (island, _) in matches) list.Add(new Result(island.Name, $"{(island.Category.Length > 0 ? island.Category : "ilha")} · {island.AccessWords}" + (island.OwnerName.Length > 0 ? $" · de {island.OwnerName}" : string.Empty), island, island.X, island.Y, "island"));
        return list;
    }

    private static int Score(AtlasIsland island, string text)
    {
        var name = Norm(island.Name); if (name == text) return 100; if (name.StartsWith(text, StringComparison.Ordinal)) return 80; if (name.Contains(text, StringComparison.Ordinal)) return 60;
        if (Norm(island.OwnerName).Contains(text, StringComparison.Ordinal)) return 40; if (Norm(island.Category).Contains(text, StringComparison.Ordinal)) return 30; return Norm(island.Description).Contains(text, StringComparison.Ordinal) ? 20 : 0;
    }

    private void UpdateResults()
    {
        var text = _query.Text.Trim(); _results = Search(text); _resultPage = 0; ShowResults(); if (_tabs.Selected == 1) FillRegions();
    }

    private void ShowResults()
    {
        foreach (var child in _resultsBox.GetChildren()) { _resultsBox.RemoveChild(child); child.QueueFree(); } foreach (var child in _recentBox.GetChildren()) { _recentBox.RemoveChild(child); child.QueueFree(); }
        var typed = _query.Text.Trim();
        if (typed.Length == 0)
        {
            _pager.Visible = false; if (!SearchWindow.KeepHistory || _history.Items.Count == 0) return;
            var title = Lbl("Pesquisas recentes", 13, false, UiStyle.TextMuted); _resultsBox.AddChild(title);
            foreach (var past in _history.Items.Take(6)) { var captured = past; var chip = new Button { Text = "↺ " + past, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 30) }; UiStyle.StyleButton(chip, UiStyle.ButtonKind.Secondary, 10, 4); chip.AddThemeFontSizeOverride("font_size", 13); chip.Pressed += () => { _query.Text = captured; UpdateResults(); }; _recentBox.AddChild(chip); }
            var clear = new Button { Text = "Limpar histórico", Flat = true, FocusMode = Control.FocusModeEnum.None }; clear.AddThemeColorOverride("font_color", UiStyle.AccentText); clear.AddThemeFontSizeOverride("font_size", 13); clear.Pressed += () => { _history.Clear(); _history.Save(HistoryFile); ShowResults(); }; _recentBox.AddChild(clear); return;
        }
        if (_results.Count == 0) { _pager.Visible = false; _resultsBox.AddChild(Lbl("Nada encontrado. Tente outro nome, uma região, “x, y” ou “Nome/x/z”.", 13, false, UiStyle.TextMuted, wrap: true)); return; }
        foreach (var result in _results.Skip(_resultPage * ResultsPerPage).Take(ResultsPerPage))
        {
            var captured = result; var row = new Button { Text = result.Title + "\n" + result.Subtitle, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 52), TooltipText = result.Subtitle };
            UiStyle.StyleButton(row, UiStyle.ButtonKind.Field, 10, 6, 10); row.AddThemeFontSizeOverride("font_size", 14); row.Pressed += () => Choose(captured); _resultsBox.AddChild(row);
        }
        var pages = (_results.Count - 1) / ResultsPerPage + 1; _pager.Visible = pages > 1; _resultsNote.Text = $"{_resultPage + 1} de {pages} · {_results.Count}"; _prevResults.Disabled = _resultPage == 0; _nextResults.Disabled = _resultPage >= pages - 1;
    }

    private void ChooseFirst() { if (_query.Text.Trim().Length > 0) UpdateResults(); if (_results.Count > 0) Choose(_results[0]); else if (_query.Text.Trim().Length > 0) Say("Nada encontrado para essa busca."); }

    /// <summary>Takes the map to a result: an island (chosen, with its card) or a point of the atlas.</summary>
    private void Choose(Result result)
    {
        if (SearchWindow.KeepHistory && _query.Text.Trim().Length >= 2) { _history.Add(_query.Text.Trim()); _history.Save(HistoryFile); }
        if (_tabs.Selected != 0) _tabs.Select(0); if (_narrow) { _filtersOpen = false; Adapt(); }
        if (result.Island is { } island && result.Kind == "island") { FocusIsland(island); return; }
        _canvas.GlideTo(result.X, result.Y, Math.Max(_canvas.Zoom, .7)); if (result.Island is { } under) _canvas.Select(under.Id);
    }

    // ---- tests --------------------------------------------------------------------------------------------------------------------------------

    public void ScrollLeftForTesting(int y) => _leftScroll.ScrollVertical = y;
    public void SetQueryForTesting(string text) { _query.Text = text; UpdateResults(); }
    public int ResultCountForTesting => _results.Count;
    public IReadOnlyList<string> ResultTitlesForTesting => [.. _results.Select(r => r.Title)];
    public IReadOnlyList<string> ResultsShownForTesting => [.. _resultsBox.GetChildren().OfType<Button>().Select(b => b.Text.Split('\n')[0])];
    public bool PagerShownForTesting => _pager.Visible;
    public void NextResultsForTesting() => _nextResults.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressResultForTesting(int index) => _resultsBox.GetChildren().OfType<Button>().ElementAt(index).EmitSignal(BaseButton.SignalName.Pressed);
    public void SubmitQueryForTesting() => ChooseFirst();
    public IReadOnlyList<string> RecentForTesting => [.. _recentBox.GetChildren().OfType<Button>().Select(b => b.Text)];
    public IReadOnlyList<string> CategoryLabelsForTesting => [.. _categoryButtons.Values.Select(b => b.Text.Trim())];
    public void PressCategoryForTesting(string key) => SelectCategory(key);
    public void ResetHistoryForTesting() { _history.Clear(); _history.Save(HistoryFile); }
}
