using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Objetos e Prims tab: the budget of the island (prims used, free, objects), the list of objects with who owns them and what they cost, the consumption by owner, the details of one object and
/// returning objects. Returning never deletes: the server puts the objects back in the inventory of their owners. Everything is asked of the server; nothing is computed or made up here.
/// </summary>
public partial class IslandObjectsPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, string Id)[] ScriptFilters = [("Todos os scripts", ""), ("Com scripts", "with"), ("Sem scripts", "without")];
    private static readonly (string Text, string Id)[] Sorts = [("Mais prims", "cost"), ("Mais recentes", "recent"), ("Nome", "name")];
    private static readonly Dictionary<string, string> TypeNames = new() { ["cube"] = "Cubo", ["sphere"] = "Esfera", ["cylinder"] = "Cilindro", ["cone"] = "Cone", ["torus"] = "Toro", ["prism"] = "Prisma", ["mesh"] = "Modelo", ["plane"] = "Plano" };

    private readonly Label[] _stats = new Label[4];
    private readonly Label[] _statNotes = new Label[4];
    private ProgressBar _usage = null!;
    private Label _usageText = null!, _status = null!, _count = null!, _pageText = null!, _empty = null!, _ownersEmpty = null!;
    private LineEdit _search = null!;
    private OptionButton _owner = null!, _type = null!, _scripts = null!, _sort = null!;
    private Button _viewObjects = null!, _viewOwners = null!, _previous = null!, _next = null!;
    private VBoxContainer _rows = null!, _ownerRows = null!, _detail = null!;
    private Control _objectsView = null!, _ownersView = null!;
    private IslandManagementApi? _api;
    private Guid _island, _selected;
    private int _page = 1, _pages = 1, _total;
    private bool _loadingFilters;
    private List<Guid> _ids = [];
    private List<Guid> _ownerIds = [];

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }

    public string StatusForTesting => _status.Text;
    public string StatsForTesting => string.Join("|", _stats.Select(s => s.Text));
    public double UsageBarForTesting => _usage.Value;
    public int TotalForTesting => _total;
    public int RowsForTesting => _rows.GetChildCount();
    public int OwnerRowsForTesting => _ownerRows.GetChildCount();
    public string PageTextForTesting => _pageText.Text;
    public string DetailForTesting => string.Join("|", _detail.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public Guid SelectedForTesting => _selected;
    public bool OwnersViewForTesting => _ownersView.Visible;
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public IReadOnlyList<string> OwnerRowTextsForTesting => [.. _ownerRows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public IReadOnlyList<string> OwnerOptionsForTesting => [.. Enumerable.Range(0, _owner.ItemCount).Select(_owner.GetItemText)];
    public void SelectOwnerForTesting(int index) => _owner.Select(index);
    public void SelectTypeForTesting(int index) => _type.Select(index);
    public void SelectScriptsForTesting(int index) => _scripts.Select(index);
    public void SelectSortForTesting(int index) => _sort.Select(index);
    public void ShowOwnersForTesting(bool on) => ShowView(on);
    public Task SelectFirstForTesting() => _ids.Count == 0 ? Task.CompletedTask : SelectAsync(_ids[0]);
    public Task ReturnSelectedForTesting() => _selected == Guid.Empty ? Task.CompletedTask : ReturnAsync([_selected]);
    public Task ReturnFirstOwnerForTesting() => _ownerIds.Count == 0 ? Task.CompletedTask : ReturnOwnerAsync(_ownerIds[0], "teste");

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        column.AddChild(BuildStats()); column.AddChild(BuildUsage());
        var switcher = IslandUi.Row(8); column.AddChild(switcher);
        _viewObjects = IslandUi.Action("Objetos", "cube", IslandUi.Kind.Secondary, 38); _viewObjects.Pressed += () => ShowView(false); switcher.AddChild(_viewObjects);
        _viewOwners = IslandUi.Action("Consumo por Proprietário", "people", IslandUi.Kind.Secondary, 38); _viewOwners.Pressed += () => ShowView(true); switcher.AddChild(_viewOwners);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; switcher.AddChild(_status);
        _objectsView = BuildObjectsView(); column.AddChild(_objectsView);
        _ownersView = BuildOwnersView(); _ownersView.Visible = false; column.AddChild(_ownersView);
        PaintSwitch();
    }

    // ---- structure --------------------------------------------------------------------------------------------------------

    private Control BuildStats()
    {
        var row = IslandUi.Row(14); row.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.CustomMinimumSize = new Vector2(0, 96);
        var specs = new (string Title, string Icon)[] { ("Prims Totais", "cube"), ("Prims Utilizadas", "chart"), ("Prims Disponíveis", "ok"), ("Objetos na Ilha", "layers") };
        for (var i = 0; i < 4; i++)
        {
            var (card, body) = IslandUi.Card(specs[i].Title, specs[i].Icon); body.AddThemeConstantOverride("separation", 0);
            _stats[i] = IslandUi.Text("—", 24, IslandUi.TextPrimary, true); body.AddChild(_stats[i]);
            _statNotes[i] = IslandUi.Text("", 13, IslandUi.TextMuted); body.AddChild(_statNotes[i]); row.AddChild(card);
        }
        return row;
    }

    private Control BuildUsage()
    {
        var box = IslandUi.Column(6); box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _usageText = IslandUi.Text("Uso da ilha", 14, IslandUi.TextSecondary); box.AddChild(_usageText);
        _usage = IslandUi.Progress(0, 16); box.AddChild(_usage);
        return box;
    }

    private Control BuildObjectsView()
    {
        var wrap = IslandUi.Row(14); wrap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var (card, body) = IslandUi.Card("Objetos da Ilha", "cube"); card.SizeFlagsStretchRatio = 68; card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var tools = IslandUi.Row(8); body.AddChild(tools);
        _search = IslandUi.Input("Pesquisar objeto ou dono…"); _search.ClearButtonEnabled = true; _search.TextSubmitted += _text => Search(); tools.AddChild(_search);
        var refresh = IslandUi.IconButton("reset", "Atualizar", 40); refresh.Pressed += () => _ = ReloadAllAsync(); tools.AddChild(refresh);
        var filters = IslandUi.Row(8); body.AddChild(filters);
        _owner = IslandUi.Dropdown([("Todos os donos", "")]); _owner.ItemSelected += _i => Filtered(); filters.AddChild(_owner);
        _type = IslandUi.Dropdown([("Todos os tipos", "")]); _type.ItemSelected += _i => Filtered(); filters.AddChild(_type);
        _scripts = IslandUi.Dropdown(ScriptFilters); _scripts.ItemSelected += _i => Filtered(); filters.AddChild(_scripts);
        _sort = IslandUi.Dropdown(Sorts); _sort.ItemSelected += _i => Filtered(); filters.AddChild(_sort);

        var head = IslandUi.Row(8); head.AddChild(Head("Objeto", 3)); head.AddChild(Head("Dono", 2)); head.AddChild(Head("Tipo", 1)); head.AddChild(Head("Prims", 1)); head.AddChild(Head("Scripts", 1)); head.AddChild(Head("Física", 2)); head.AddChild(Head("Ações", 1, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhum objeto por aqui.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager); wrap.AddChild(card);

        var (panel, panelBody) = IslandUi.Card("Detalhes do Objeto", "info"); panel.SizeFlagsStretchRatio = 32; panel.CustomMinimumSize = new Vector2(320, 0); panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        _detail = panelBody; ShowNoDetail(); wrap.AddChild(panel);
        return wrap;
    }

    private Control BuildOwnersView()
    {
        var (card, body) = IslandUi.Card("Consumo por Proprietário", "people"); card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var head = IslandUi.Row(8); head.AddChild(Head("Proprietário", 3)); head.AddChild(Head("Objetos", 1)); head.AddChild(Head("Prims", 1)); head.AddChild(Head("Scripts", 1)); head.AddChild(Head("Ações", 2, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _ownerRows = IslandUi.Column(0); body.AddChild(_ownerRows);
        _ownersEmpty = IslandUi.Text("Ninguém tem objetos na ilha.", 15, IslandUi.TextMuted); _ownersEmpty.CustomMinimumSize = new Vector2(0, 60); _ownersEmpty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_ownersEmpty);
        body.AddChild(IslandUi.Text("Devolver manda cada objeto de volta ao inventário do dono; nada é apagado.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false)
    {
        var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label;
    }

    private void ShowView(bool owners) { _objectsView.Visible = !owners; _ownersView.Visible = owners; PaintSwitch(); }

    private void PaintSwitch()
    {
        foreach (var (button, on) in new[] { (_viewObjects, _objectsView.Visible), (_viewOwners, !_objectsView.Visible) })
        {
            var box = IslandUi.Box(on ? IslandUi.AccentSelected : new Color(0, 0, 0, .1f), on ? IslandUi.AccentPrimary : new Color("#4FA9C5"), IslandUi.RadiusButton, 1, 16, 6);
            foreach (var name in new[] { "normal", "hover", "pressed", "focus" }) button.AddThemeStyleboxOverride(name, box);
        }
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private void Search() { _page = 1; _ = LoadAsync(); }
    private void Filtered() { if (!_loadingFilters) { _page = 1; _ = LoadAsync(); } }

    public async Task ReloadAllAsync() { await LoadOwnersAsync(); await LoadAsync(); }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static long Long(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static string Number(long n) => n.ToString("N0", Pt);
    private static string TypeText(string type) => TypeNames.TryGetValue(type, out var text) ? text : type;
    private static string PhysicsText(string physics) => physics switch { "physical" => "Físico", "none" => "Sem colisão", _ => "Estático" };

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var ownerId = _owner.Selected > 0 && Guid.TryParse(_owner.GetItemMetadata(_owner.Selected).AsString(), out var o) ? o : (Guid?)null;
        var type = _type.Selected > 0 ? _type.GetItemMetadata(_type.Selected).AsString() : "";
        var reply = await _api.ObjectsGet(_island, _search.Text.Trim(), ownerId, type, ScriptFilters[Math.Max(0, _scripts.Selected)].Id, Sorts[Math.Max(0, _sort.Selected)].Id, _page);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement body)
    {
        if (body.TryGetProperty("usage", out var usage))
        {
            var cost = Long(usage, "cost"); var max = Long(usage, "maxCost"); var objects = Int(usage, "objects"); var maxObjects = Int(usage, "maxObjects");
            _stats[0].Text = Number(max); _stats[1].Text = Number(cost); _stats[2].Text = Number(Math.Max(0, max - cost)); _stats[3].Text = Number(objects);
            var percent = max <= 0 ? 0 : Math.Clamp(cost * 100.0 / max, 0, 100);
            _statNotes[0].Text = "orçamento da ilha"; _statNotes[1].Text = percent.ToString("0.0", Pt) + "% do orçamento"; _statNotes[2].Text = "ainda para construir"; _statNotes[3].Text = $"de {Number(maxObjects)} permitidos";
            _usage.Value = percent; IslandUi.SetProgressColor(_usage, percent); _usageText.Text = $"Uso da ilha: {percent.ToString("0.0", Pt)}%";
            _stats[1].AddThemeColorOverride("font_color", percent >= 90 ? IslandUi.Error : percent >= 75 ? IslandUi.AccentSecondary : IslandUi.TextPrimary);
        }
        if (body.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            _loadingFilters = true; var keep = _type.Selected > 0 ? _type.GetItemMetadata(_type.Selected).AsString() : "";
            _type.Clear(); _type.AddItem("Todos os tipos"); _type.SetItemMetadata(0, "");
            foreach (var t in types.EnumerateArray()) { _type.AddItem(TypeText(t.GetString() ?? "")); _type.SetItemMetadata(_type.ItemCount - 1, t.GetString() ?? ""); }
            IslandUi.Select(_type, keep); if (_type.Selected < 0) _type.Select(0); _loadingFilters = false;
        }
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _total = Int(body, "total"); _page = Math.Max(1, Int(body, "page")); var size = Int(body, "pageSize") is > 0 and var ps ? ps : 10; _pages = Math.Max(1, (_total + size - 1) / size);
        var items = body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _ids = [.. items.Select(i => Guid.Parse(Str(i, "id")))];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; _count.Text = _total == 1 ? "1 objeto" : $"{Number(_total)} objetos"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages;
    }

    private Control BuildRow(JsonElement item)
    {
        var id = Guid.Parse(Str(item, "id")); var name = Str(item, "name"); var chosen = id == _selected;
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 48), MouseDefaultCursorShape = CursorShape.PointingHand };
        box.AddThemeStyleboxOverride("panel", IslandUi.Box(chosen ? new Color(IslandUi.AccentSelected, .55f) : new Color(0, 0, 0, 0), chosen ? IslandUi.AccentPrimary : null, 6, chosen ? 1 : 0, 4, 0));
        box.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) _ = SelectAsync(id); };
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 47); column.AddChild(row);
        var title = IslandUi.Text(name, 15, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.SizeFlagsStretchRatio = 3; title.ClipText = true; title.TooltipText = name; title.MouseFilter = MouseFilterEnum.Pass; row.AddChild(title);
        void Cell(string text, int ratio, Color? color = null) { var label = IslandUi.Text(text, 14, color ?? IslandUi.TextSecondary); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; label.ClipText = true; row.AddChild(label); }
        Cell(Str(item, "ownerName"), 2); Cell(TypeText(Str(item, "type")), 1); Cell(Number(Long(item, "cost")), 1, IslandUi.TextPrimary);
        Cell(Int(item, "scripts") == 0 ? "—" : Int(item, "scripts").ToString(), 1, Int(item, "scripts") > 0 ? IslandUi.AccentSecondary : IslandUi.TextMuted); Cell(PhysicsText(Str(item, "physics")), 2);
        var actions = IslandUi.Row(4); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 1; actions.Alignment = BoxContainer.AlignmentMode.End;
        var details = IslandUi.IconButton("eye", "Ver detalhes", 32); details.Pressed += () => _ = SelectAsync(id); actions.AddChild(details);
        var back = IslandUi.IconButton("undo", "Devolver ao dono", 32); back.Pressed += () => ConfirmReturn([id], name); actions.AddChild(back); row.AddChild(actions);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- owners -----------------------------------------------------------------------------------------------------------

    public async Task LoadOwnersAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.ObjectsOwners(_island);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        foreach (var child in _ownerRows.GetChildren()) child.QueueFree();
        var items = reply.Body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        _ownerIds = [.. items.Select(i => Guid.Parse(Str(i, "userId")))];
        var keep = _owner.Selected > 0 ? _owner.GetItemMetadata(_owner.Selected).AsString() : "";
        _loadingFilters = true; _owner.Clear(); _owner.AddItem("Todos os donos"); _owner.SetItemMetadata(0, "");
        foreach (var item in items) { _owner.AddItem(Str(item, "name")); _owner.SetItemMetadata(_owner.ItemCount - 1, Str(item, "userId")); }
        IslandUi.Select(_owner, keep); if (_owner.Selected < 0) _owner.Select(0); _loadingFilters = false;
        foreach (var item in items)
        {
            var id = Guid.Parse(Str(item, "userId")); var name = Str(item, "name");
            var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 46);
            void Cell(string text, int ratio, bool bold = false) { var label = IslandUi.Text(text, 14, bold ? IslandUi.TextPrimary : IslandUi.TextSecondary, bold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; row.AddChild(label); }
            Cell(name, 3, true); Cell(Number(Long(item, "objects")), 1); Cell(Number(Long(item, "cost")), 1); Cell(Number(Long(item, "scripts")), 1);
            var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 2; actions.Alignment = BoxContainer.AlignmentMode.End;
            var show = IslandUi.Action("Ver objetos", "cube", IslandUi.Kind.Secondary, 34); show.Pressed += () => { IslandUi.Select(_owner, id.ToString()); ShowView(false); _page = 1; _ = LoadAsync(); }; actions.AddChild(show);
            var give = IslandUi.Action("Devolver tudo", "undo", IslandUi.Kind.Danger, 34); give.Pressed += () => Confirm($"Devolver todos os objetos de {name} ao inventário dela? Ela perde eles da ilha, mas nada é apagado.", "Devolver tudo", () => _ = ReturnOwnerAsync(id, name)); actions.AddChild(give);
            row.AddChild(actions); _ownerRows.AddChild(row); _ownerRows.AddChild(IslandUi.Divider());
        }
        _ownersEmpty.Visible = items.Count == 0;
    }

    // ---- details and returning --------------------------------------------------------------------------------------------

    private void ShowNoDetail()
    {
        foreach (var child in _detail.GetChildren()) child.QueueFree();
        _detail.AddChild(IslandUi.Text("Escolha um objeto na lista para ver os detalhes.", 14, IslandUi.TextMuted, false, true));
    }

    public async Task SelectAsync(Guid id)
    {
        if (_api is null) return;
        _selected = id; var reply = await _api.ObjectDetail(_island, id);
        if (!reply.Ok) { Say(reply.Message, true); _selected = Guid.Empty; ShowNoDetail(); return; }
        var d = reply.Body;
        foreach (var child in _detail.GetChildren()) child.QueueFree();
        _detail.AddChild(IslandUi.Text(Str(d, "name"), 18, IslandUi.TextPrimary, true, true));
        if (Str(d, "description").Length > 0) _detail.AddChild(IslandUi.Text(Str(d, "description"), 13, IslandUi.TextSecondary, false, true));
        void Line(string caption, string value) { var row = IslandUi.Row(8); var c = IslandUi.Text(caption, 13, IslandUi.TextMuted); c.CustomMinimumSize = new Vector2(110, 0); row.AddChild(c); var v = IslandUi.Text(value, 14, IslandUi.TextPrimary, false, true); row.AddChild(v); _detail.AddChild(row); }
        string Vec(string name) => d.TryGetProperty(name, out var v) ? string.Join(", ", new[] { "x", "y", "z" }.Select(k => (v.TryGetProperty(k, out var n) && n.TryGetDouble(out var x) ? x : 0).ToString("0.##", Pt))) : "";
        Line("ID", Str(d, "id")); Line("Proprietário", Str(d, "ownerName")); Line("Criador", Str(d, "creatorName")); if (Str(d, "lastOwnerName").Length > 0) Line("Dono anterior", Str(d, "lastOwnerName"));
        Line("Tipo", TypeText(Str(d, "type"))); Line("Prims", Number(Long(d, "cost"))); Line("Scripts", Int(d, "scripts").ToString()); Line("Física", PhysicsText(Str(d, "physics")));
        Line("Posição", Vec("position")); Line("Rotação", Vec("rotation")); Line("Dimensões", Vec("scale"));
        if (d.TryGetProperty("locked", out var locked) && locked.ValueKind == JsonValueKind.True) Line("Proteção", "Protegido contra edição");
        if (d.TryGetProperty("temporaryUntil", out var until) && until.ValueKind == JsonValueKind.String && until.TryGetDateTimeOffset(out var when)) Line("Some em", when.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt));
        if (d.TryGetProperty("createdAt", out var created) && created.TryGetDateTimeOffset(out var made)) Line("Criado em", made.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt));
        if (d.TryGetProperty("permissions", out var rights) && rights.ValueKind == JsonValueKind.Object) { var allowed = rights.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.True).Select(p => p.Name); Line("Permissões", string.Join(", ", allowed)); }
        var back = IslandUi.Action("Devolver ao dono", "undo", IslandUi.Kind.Danger, 40); var name = Str(d, "name"); back.Pressed += () => ConfirmReturn([id], name); _detail.AddChild(back);
        await LoadAsync();
    }

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }
    private void ConfirmReturn(Guid[] ids, string name) => Confirm($"Devolver {name} ao inventário do dono? O objeto sai da ilha, mas nada é apagado.", "Devolver", () => _ = ReturnAsync(ids));

    public async Task ReturnAsync(Guid[] ids)
    {
        if (_api is null) return;
        var reply = await _api.ObjectsReturn(_island, ids);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        var returned = Int(reply.Body, "returned"); Say($"{returned} objeto(s) devolvido(s).", false); Notify?.Invoke($"{returned} objeto(s) devolvido(s) ao inventário.");
        if (ids.Contains(_selected)) { _selected = Guid.Empty; ShowNoDetail(); }
        await ReloadAllAsync();
    }

    public async Task ReturnOwnerAsync(Guid owner, string name)
    {
        if (_api is null) return;
        var reply = await _api.ObjectsReturnOwner(_island, owner);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        var returned = Int(reply.Body, "returned"); var left = Int(reply.Body, "remaining");
        Say(left == 0 ? $"{returned} objeto(s) de {name} devolvido(s)." : $"{returned} devolvido(s); ainda restam {left}. Repita para devolver o resto.", left != 0); Notify?.Invoke($"Objetos de {name} devolvidos.");
        _selected = Guid.Empty; ShowNoDetail(); await ReloadAllAsync();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
