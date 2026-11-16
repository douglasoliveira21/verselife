using Godot;
using LifeVerse.Shared.Search;

namespace LifeVerse.Client.Scripts.Map;

/// <summary>
/// "Mapa do Mundo": a floating window with the atlas of the islands. On the left the search for places and the categories; in the middle the map (pan, zoom, markers, the card of the chosen island with Teletransportar
/// and Info, a legend and the controls), or, on the Regiões tab, the same islands as a list. Everything is read from the server through <see cref="MapHub"/>; entering an island goes through the game's own teleport.
/// Split in files: this one (shell, data, states), Left (search and categories), Card (the island card, info, actions, menu) and Regions (the list tab).
/// </summary>
public partial class WorldMapWindow : Node
{
    public static readonly (string Key, string Label, string Icon)[] Categories =
    [("destaques", "Destaques", "star"), ("populares", "Populares", "chart"), ("novas", "Novas Ilhas", "plus"), ("residencial", "Residenciais", "home"), ("comercial", "Comerciais", "bag"), ("eventos", "Eventos", "calendar"), ("adulto", "Adulto 18+", "lock")];
    private static readonly string[] TabTitles = ["MUNDO", "REGIÕES", "TERRENOS", "PESSOAS", "GRUPOS", "EVENTOS"], TabIcons = ["map", "grid", "home", "user", "people", "calendar"];
    private const int NarrowWidth = 760;

    public MapHub Hub { get; set; } = null!;
    /// <summary>The game's own search window (opens the link of an island there).</summary>
    public Action<string>? OpenLink { get; set; }

    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private HBoxContainer _body = null!;
    private Control _stack = null!;
    private WorldMapCanvas _canvas = null!;
    private Label _loading = null!, _status = null!;
    private VBoxContainer _stateBox = null!;
    private Label _stateText = null!;
    private Button _stateAction = null!;
    private Button _filtersToggle = null!;
    private List<AtlasIsland> _items = [];
    private string _category = string.Empty;
    private bool _narrow, _filtersOpen, _dirty = true;
    private DateTimeOffset _loadedAt = DateTimeOffset.MinValue;
    private readonly HashSet<Guid> _eventIslands = [], _featured = [];
    private double _sinceSave; private bool _saveDue;

    public bool Visible => _window.Visible;
    public IReadOnlyList<AtlasIsland> Items => _items;
    public IReadOnlyList<AtlasIsland> Shown => Filter();
    public WorldMapCanvas Canvas => _canvas;

    public override void _Ready() { Build(); Hub.Atlas.Changed += OnAtlas; Hub.Atlas.Updated += OnUpdated; Hub.Atlas.Failed += OnFailed; Hub.DestinationChanged += () => _canvas.QueueRedraw(); Hub.FriendsChanged += OnFriends; Hub.SettingsApplied += OnSettings; Hub.Watch(() => _window.Visible); }

    public override void _ExitTree() { Hub.Atlas.Changed -= OnAtlas; Hub.Atlas.Updated -= OnUpdated; Hub.Atlas.Failed -= OnFailed; Hub.FriendsChanged -= OnFriends; Hub.SettingsApplied -= OnSettings; }

    public void Toggle() { if (_window.Visible) Close(); else Open(); }

    public void Close() { _window.Visible = false; HideCard(); Hub.Atlas.Cancel(); SaveView(); }

    /// <summary>Opens the window in front of the others. The map is read again when it is older than a minute or something changed; otherwise what is there is shown at once.</summary>
    public void Open()
    {
        _window.Visible = true; _window.MoveToFront(); _canvas.GrabFocus();
        LoadPanels(); LoadPeople(); if (_dirty || DateTimeOffset.UtcNow - _loadedAt > TimeSpan.FromMinutes(1)) Reload(); else _canvas.QueueRedraw();
    }

    /// <summary>Opens the map on one island: chosen, centred, with its card.</summary>
    public void OpenOn(Guid island)
    {
        Open(); _tabs.Select(0); _focusAfterLoad = island; if (_items.FirstOrDefault(i => i.Id == island) is { } known) { _focusAfterLoad = Guid.Empty; FocusIsland(known); }
    }

    private Guid _focusAfterLoad;

    // ---- building ---------------------------------------------------------------------------------------------------------------------

    private static Label Lbl(string text, int size = 14, bool bold = false, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text, AutowrapMode = wrap ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off };
        label.AddThemeFontSizeOverride("font_size", size); if (bold) label.AddThemeFontOverride("font", UiStyle.Bold); label.AddThemeColorOverride("font_color", color ?? UiStyle.Text); return label;
    }

    private static Button Btn(string text, UiStyle.ButtonKind kind, Action action, string? tip = null, int height = 38)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, height), TooltipText = tip ?? string.Empty }; UiStyle.StyleButton(button, kind, 12, 6); button.AddThemeFontSizeOverride("font_size", 15); button.Pressed += action; return button;
    }

    private void Build()
    {
        _window = new FloatingWindow("Mapa do Mundo", icon: "map", minimizable: true, expandable: true) { Name = "WorldMapWindow", Visible = false };
        _window.Place(.5f, .5f, -640, -400, 640, 400); var content = _window.Content; content.AddThemeConstantOverride("separation", 8);

        var tabRow = new HBoxContainer(); tabRow.AddThemeConstantOverride("separation", 8); content.AddChild(tabRow);
        _tabs = new UiTabs { EqualWidth = false, Flat = true, FontSize = 15 }; for (var t = 0; t < TabTitles.Length; t++) _tabs.AddTab(TabTitles[t], TabIcons[t]);
        var tabScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 48) };
        _tabs.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; tabScroll.AddChild(_tabs); tabRow.AddChild(tabScroll);
        var help = new Button { Text = "?", FocusMode = Control.FocusModeEnum.None, TooltipText = "Como usar o mapa", CustomMinimumSize = new Vector2(40, 40) }; UiStyle.StyleButton(help, UiStyle.ButtonKind.Tool, 8, 4); help.Pressed += ShowHelp; tabRow.AddChild(help);

        _body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; _body.AddThemeConstantOverride("separation", 10); content.AddChild(_body);
        BuildLeft(_body);

        _stack = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(320, 260), ClipContents = true }; _body.AddChild(_stack);
        _canvas = new WorldMapCanvas { Hub = Hub, Name = "Canvas" }; _canvas.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); _stack.AddChild(_canvas);
        _canvas.SelectionChanged += OnSelected; _canvas.Activated += id => { if (_items.FirstOrDefault(i => i.Id == id) is { } island) ShowInfo(island); }; _canvas.ContextRequested += ShowMenu; _canvas.ViewChanged += OnViewChanged;
        BuildControls(); BuildStates(); BuildCard(); BuildRegions(); BuildOverlays();

        _tabs.TabChanged += OnTab; _body.Resized += Adapt; AddChild(_window); SetProcess(true);
    }

    /// <summary>The zoom buttons, "fit the region" and "center on me", at the right edge of the map; the legend along the bottom.</summary>
    private void BuildControls()
    {
        var column = new VBoxContainer { Name = "Controls" }; column.AddThemeConstantOverride("separation", 6); column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterRight, Control.LayoutPresetMode.Minsize); column.OffsetLeft = -52; column.OffsetRight = -8; _stack.AddChild(column);
        Button Round(string icon, string tip, Action action)
        {
            var b = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 22), FocusMode = Control.FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(44, 44), ExpandIcon = false }; UiStyle.StyleButton(b, UiStyle.ButtonKind.Tool, 6, 6, 12); b.Pressed += action; column.AddChild(b); return b;
        }
        Round("plus", "Aproximar (+)", () => _canvas.ZoomCentered(1.35)); Round("minus", "Afastar (−)", () => _canvas.ZoomCentered(1 / 1.35));
        Round("expand", "Ajustar à região: mostra todas as ilhas da lista", () => _canvas.FitAll(glide: true)); Round("target", "Centralizar em mim (Home)", () => { if (!_canvas.CenterOnPlayer()) Say("Você não está em uma ilha do mapa agora."); });

        var legend = new HFlowContainer { Name = "Legend", MouseFilter = Control.MouseFilterEnum.Ignore }; legend.AddThemeConstantOverride("h_separation", 14); legend.AddThemeConstantOverride("v_separation", 2);
        legend.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide, Control.LayoutPresetMode.Minsize); legend.OffsetLeft = 8; legend.OffsetRight = -64; legend.OffsetTop = -30; legend.OffsetBottom = -6; _stack.AddChild(legend);
        void Entry(string icon, Color color, string text) { var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; row.AddThemeConstantOverride("separation", 4); var picture = UiStyle.IconRect(icon, color, 16); picture.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(picture); var label = Lbl(text, 13, false, UiStyle.TextMuted); label.MouseFilter = Control.MouseFilterEnum.Ignore; row.AddChild(label); legend.AddChild(row); }
        Entry("pin", UiStyle.Accent, "Você"); Entry("home", WorldMapCanvas.Online, "Residencial"); Entry("bag", UiStyle.Orange, "Comercial"); Entry("calendar", new Color("#7FA8FF"), "Evento"); Entry("star", UiStyle.Orange, "Destaque"); Entry("star", UiStyle.Accent, "Favorito"); Entry("lock", WorldMapCanvas.Error, "Sem acesso");

        _loading = Lbl("Carregando mapa…", 14, false, UiStyle.AccentText); _loading.Visible = false; _loading.Position = new Vector2(46, 8); _loading.MouseFilter = Control.MouseFilterEnum.Ignore; _stack.AddChild(_loading);
        _status = Lbl("", 14, false, UiStyle.Text, wrap: true); _status.MouseFilter = Control.MouseFilterEnum.Ignore; _status.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide, Control.LayoutPresetMode.Minsize); _status.OffsetLeft = 54; _status.OffsetRight = -64; _status.OffsetTop = -62; _status.OffsetBottom = -34; _stack.AddChild(_status);
        _filtersToggle = Btn("☰ Filtros", UiStyle.ButtonKind.Tool, () => { _filtersOpen = !_filtersOpen; Adapt(); }, "Mostra a busca e as categorias", 34); _filtersToggle.Visible = false; _filtersToggle.Position = new Vector2(46, 34); _stack.AddChild(_filtersToggle);
    }

    /// <summary>Loading, an error with a way to try again, or an empty map with a way to clear the filters: never a blank map without a word.</summary>
    private void BuildStates()
    {
        _stateBox = new VBoxContainer { Visible = false, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Pass }; _stateBox.AddThemeConstantOverride("separation", 10); _stateBox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center, Control.LayoutPresetMode.Minsize); _stateBox.OffsetLeft = -170; _stateBox.OffsetRight = 170; _stateBox.OffsetTop = -70; _stateBox.OffsetBottom = 70; _stack.AddChild(_stateBox);
        var panel = new PanelContainer(); panel.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.PanelFillSolid, .94f), new Color(UiStyle.BorderSoft, .8f), 14, 1, 18, 16)); _stateBox.AddChild(panel);
        var inner = new VBoxContainer(); inner.AddThemeConstantOverride("separation", 10); panel.AddChild(inner);
        _stateText = Lbl("", 15, false, UiStyle.Text, wrap: true); _stateText.HorizontalAlignment = HorizontalAlignment.Center; inner.AddChild(_stateText);
        _stateAction = Btn("Tentar novamente", UiStyle.ButtonKind.Secondary, () => { }, null, 38); inner.AddChild(_stateAction);
    }

    private Action _stateRetry = () => { };

    private void ShowState(string text, string? action = null, Action? retry = null)
    {
        _stateBox.Visible = true; _stateText.Text = text; _stateAction.Visible = action is not null; _stateAction.Text = action ?? string.Empty; _stateRetry = retry ?? (() => { });
        foreach (var c in _stateAction.GetSignalConnectionList(BaseButton.SignalName.Pressed)) _stateAction.Disconnect(BaseButton.SignalName.Pressed, c["callable"].AsCallable()); _stateAction.Pressed += () => _stateRetry();
    }

    private void HideState() => _stateBox.Visible = false;

    private void Say(string text) { _status.Text = text; Hub.Say?.Invoke(text); }

    // ---- data -----------------------------------------------------------------------------------------------------------------------------

    private string[]? Ratings() => _category == "adulto" ? ["adulto"] : Hub.Settings.ShowAdult ? null : ["livre", "moderado"];

    private string? ServerCategory() => _category is "residencial" or "comercial" ? _category : null;

    public void Reload()
    {
        _dirty = false; _loading.Visible = true; HideState();
        Hub.Atlas.Load(null, ServerCategory(), Ratings(), null, 500); LoadEvents();
    }

    private void LoadEvents()
    {
        Hub.Api.Send(Godot.HttpClient.Method.Get, "/api/v1/discovery/events", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return; _eventIslands.Clear();
            try { using var d = System.Text.Json.JsonDocument.Parse(body); foreach (var e in d.RootElement.EnumerateArray()) if (e.TryGetProperty("islandId", out var i) && Guid.TryParse(i.GetString(), out var id)) _eventIslands.Add(id); } catch (System.Text.Json.JsonException) { }
            ReadEvents(body); _canvas.EventIslands.Clear(); foreach (var id in _eventIslands) _canvas.EventIslands.Add(id); Refresh(false); if (_tabs.Selected == 5) FillEvents();
        });
    }

    private void OnAtlas()
    {
        _loading.Visible = false; _loadedAt = DateTimeOffset.UtcNow; _items = [.. Hub.Atlas.Page.Items]; _canvas.CellMeters = Hub.Atlas.Page.CellMeters;
        var score = _items.OrderByDescending(i => i.IsWelcome).ThenByDescending(i => (i.Visitors ?? 0) * 2 + i.Favorites).Where(i => i.IsWelcome || (i.Visitors ?? 0) * 2 + i.Favorites > 0).Take(8).Select(i => i.Id);
        _featured.Clear(); foreach (var id in score) _featured.Add(id); _canvas.FeaturedIslands.Clear(); foreach (var id in _featured) _canvas.FeaturedIslands.Add(id);
        RebuildPins(); var first = !Hub.Settings.HasView && _canvas.Zoom == 0.35 && _canvas.CenterX == 0 && _canvas.CenterY == 0; Refresh(true);
        if (first || !_viewPlaced) { _viewPlaced = true; if (Hub.Settings.HasView) _canvas.SetView(Hub.Settings.CenterX, Hub.Settings.CenterY, Hub.Settings.Zoom, announce: false); else if (!_canvas.CenterOnPlayer(glide: false)) _canvas.FitAll(); }
        if (_focusAfterLoad != Guid.Empty && _items.FirstOrDefault(i => i.Id == _focusAfterLoad) is { } target) { _focusAfterLoad = Guid.Empty; FocusIsland(target); }
        if (Hub.Atlas.Page.Truncated) Say($"Mostrando {_items.Count} de {Hub.Atlas.Page.Total} ilhas: use a busca ou as categorias para ver as outras.");
    }

    private bool _viewPlaced;

    private void OnFailed(int status)
    {
        _loading.Visible = false; if (!_window.Visible) return;
        ShowState(status == 0 ? "Sem conexão com o servidor." : status == 401 ? "Entre na sua conta para ver o mapa." : status == 403 ? "Você não tem acesso ao mapa." : status == 429 ? "Calma! Muitas tentativas. Tente de novo daqui a pouco." : $"Não foi possível carregar o mapa (HTTP {status}).", "Tentar novamente", Reload);
    }

    /// <summary>The islands the current category keeps (the rest of the filters were applied by the server).</summary>
    private List<AtlasIsland> Filter()
    {
        IEnumerable<AtlasIsland> list = _items;
        switch (_category)
        {
            case "destaques": list = list.Where(i => _featured.Contains(i.Id)); break;
            case "populares": list = list.Where(i => (i.Visitors ?? 0) + i.Favorites > 0).OrderByDescending(i => (i.Visitors ?? 0) * 2 + i.Favorites).Take(20); break;
            case "novas": list = list.OrderByDescending(i => i.CreatedAt ?? DateTimeOffset.MinValue).Take(10); break;
            case "eventos": list = list.Where(i => _eventIslands.Contains(i.Id)); break;
        }
        return [.. list];
    }

    /// <summary>Gives the canvas and the list the islands of the category, and says so when there are none.</summary>
    private void Refresh(bool fit)
    {
        var shown = Filter(); _canvas.Islands = shown; if (_tabs.Selected == 1) FillRegions(); _canvas.QueueRedraw();
        if (_selectedIsland is { } chosen && shown.All(i => i.Id != chosen.Id)) { _canvas.Select(Guid.Empty, announce: false); HideCard(); }
        if (Hub.Atlas.Loading) return;
        if (shown.Count == 0) ShowState(_items.Count == 0 && _category.Length == 0 ? "Ainda não há ilhas públicas no mapa." : "Nenhuma ilha encontrada com este filtro.", _category.Length > 0 ? "Limpar filtros" : null, () => SelectCategory(string.Empty)); else HideState();
        if (fit && shown.Count > 0 && _viewPlaced && !Hub.Settings.HasView) _canvas.FitAll();
    }

    private void OnTab(int index)
    {
        _canvas.Visible = index == 0; foreach (var child in _stack.GetChildren().OfType<Control>().Where(c => c.Name == "Controls" || c.Name == "Legend" || c.Name == "ParcelsToggle")) child.Visible = index == 0; _parcelLegend.GetParent<Control>().Visible = index == 0 && _canvas.ShowParcels;
        _regionsPage.Visible = index == 1; _parcelsPage.Visible = index == 2; _peoplePage.Visible = index == 3; _groupsPage.Visible = index == 4; _eventsPage.Visible = index == 5; var keep = _canvas.Selected; HideCard(); if (index == 2 && keep != Guid.Empty) _canvas.Select(keep, announce: false);
        switch (index) { case 1: FillRegions(); break; case 2: FillParcels(); break; case 3: LoadPeople(); FillPeople(); break; case 4: LoadPanels(); FillGroups(); break; case 5: LoadEvents(); FillEvents(); break; default: _canvas.GrabFocus(); break; }
    }

    // ---- remembering the view ---------------------------------------------------------------------------------------------------------

    private void OnViewChanged() { _saveDue = true; _sinceSave = 0; PlaceCard(); }

    private void SaveView() { Hub.Settings.CenterX = _canvas.CenterX; Hub.Settings.CenterY = _canvas.CenterY; Hub.Settings.Zoom = _canvas.Zoom; Hub.Settings.HasView = true; Hub.Settings.Category = _category; Hub.Settings.Save(); _saveDue = false; }

    public override void _Process(double delta)
    {
        if (_saveDue) { _sinceSave += delta; if (_sinceSave > 1.2) SaveView(); }
        if (_card.Visible) PlaceCard();
    }

    public override void _Input(InputEvent input)
    {
        if (!_window.Visible || input is not InputEventKey { Pressed: true, Keycode: Key.Escape } || GetViewport().GuiGetFocusOwner() is not { } focus || !_window.IsAncestorOf(focus)) return;
        if (_card.Visible) HideCard(); else Close(); GetViewport().SetInputAsHandled();
    }

    /// <summary>One column when the window is narrow: the map, or the filters on their own with a way back.</summary>
    private void Adapt()
    {
        _narrow = _body.Size.X > 0 && _body.Size.X < NarrowWidth;
        _left.Visible = !_narrow || _filtersOpen; _stack.Visible = !_narrow || !_filtersOpen; _filtersToggle.Visible = _narrow && !_filtersOpen; _backFromFilters.Visible = _narrow && _filtersOpen;
        _left.CustomMinimumSize = new Vector2(_narrow ? 0 : 280, 0); _left.SizeFlagsHorizontal = _narrow ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill; PlaceCard();
    }

    // ---- tests ---------------------------------------------------------------------------------------------------------------------------

    public string StateTextForTesting => _stateBox.Visible ? _stateText.Text : string.Empty;
    public string StatusForTesting => _status.Text;
    public bool LoadingForTesting => _loading.Visible;
    public string CategoryForTesting => _category;
    public bool NarrowForTesting => _narrow;
    public bool FiltersShownForTesting => _left.Visible;
    public bool MapShownForTesting => _stack.Visible;
    public int TabForTesting => _tabs.Selected;
    public Control WindowForTesting => _window;
    public void PressStateActionForTesting() => _stateAction.EmitSignal(BaseButton.SignalName.Pressed);
    public void SelectTabForTesting(int index) => _tabs.Select(index);
    public void ToggleFiltersForTesting() { _filtersOpen = !_filtersOpen; Adapt(); }
    public bool HelpOpenForTesting => _helpWindow is not null && GodotObject.IsInstanceValid(_helpWindow) && !_helpWindow.IsQueuedForDeletion();
    public void OpenHelpForTesting() => ShowHelp();
    public void ConfirmAdultForTesting(bool yes) { if (_adultDialog is not null && GodotObject.IsInstanceValid(_adultDialog)) _adultDialog.FindChildren("*", "Button", true, false).OfType<Button>().First(b => b.Text == (yes ? "Tenho 18 anos ou mais" : "Cancelar")).EmitSignal(BaseButton.SignalName.Pressed); }
    public bool AdultDialogOpenForTesting => _adultDialog is not null && GodotObject.IsInstanceValid(_adultDialog) && !_adultDialog.IsQueuedForDeletion();

    private FloatingWindow? _helpWindow, _adultDialog;

    private void ShowHelp()
    {
        if (_helpWindow is not null && GodotObject.IsInstanceValid(_helpWindow)) _helpWindow.QueueFree();
        _helpWindow = new FloatingWindow("Como usar o mapa", icon: "map") { Name = "WorldMapHelp" }; _helpWindow.Place(.5f, .5f, -250, -190, 250, 190);
        foreach (var line in new[] { "Arrastar com o botão esquerdo: move o mapa.", "Roda do mouse: aproxima e afasta, em volta do cursor.", "Clique em uma ilha: mostra o cartão dela. Duplo clique: informações.", "Botão direito: menu com teletransportar, favoritar, marcar destino e copiar a localização.", "Setas move o mapa; + e − dão zoom; Home centraliza em você; Esc fecha o cartão.", "Longe, as ilhas viram pontos com a quantidade: clique para aproximar.", "Categorias filtram o que aparece. Clique na categoria de novo para ver tudo.", "As ilhas escuras ou com cadeado não podem ser visitadas por você agora." })
        { var l = Lbl("•  " + line, 14, false, UiStyle.Text, wrap: true); _helpWindow.Content.AddChild(l); }
        _helpWindow.Content.AddChild(Btn("Fechar", UiStyle.ButtonKind.Secondary, () => _helpWindow?.QueueFree())); AddChild(_helpWindow); _helpWindow.MoveToFront();
    }
}
