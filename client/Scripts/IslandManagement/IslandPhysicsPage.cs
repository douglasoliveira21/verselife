using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Física tab: the objects of the island that are simulated (they move and fall), how much work the island server does for them, and freezing the ones that slow it down. A frozen object becomes
/// static and remembers what it was; releasing puts it back exactly. Nothing is deleted or moved, and the island server hears of it within seconds.
/// </summary>
public partial class IslandPhysicsPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, string Id)[] Filters = [("Todos", ""), ("Ativos", "active"), ("Congelados", "frozen")];

    private readonly Label[] _values = new Label[4];
    private readonly Label[] _notes = new Label[4];
    private ProgressBar _bodies = null!;
    private LineEdit _search = null!;
    private OptionButton _filter = null!;
    private Label _status = null!, _count = null!, _pageText = null!, _empty = null!;
    private Button _freezeAll = null!, _releaseAll = null!, _previous = null!, _next = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private int _page = 1, _pages = 1, _total, _active, _frozen;
    private List<(Guid Id, string Name, bool Frozen)> _items = [];

    public event Action<string>? Notify;
    public Action<string, string, Action>? Ask { get; set; }

    public string StatusForTesting => _status.Text;
    public string IndicatorsForTesting => string.Join("|", _values.Select(v => v.Text));
    public double BodiesBarForTesting => _bodies.Value;
    public int TotalForTesting => _total;
    public int RowsForTesting => _items.Count;
    public string PageTextForTesting => _pageText.Text;
    public bool FreezeAllEnabledForTesting => !_freezeAll.Disabled;
    public bool ReleaseAllEnabledForTesting => !_releaseAll.Disabled;
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public void SelectFilterForTesting(int index) => _filter.Select(index);
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text))).Where(t => t.Length > 0)];
    public Task ToggleFirstForTesting() => _items.Count == 0 ? Task.CompletedTask : _items[0].Frozen ? ReleaseAsync([_items[0].Id], false) : FreezeAsync([_items[0].Id], false);
    public Task FreezeAllForTesting() => FreezeAsync(null, true);
    public Task ReleaseAllForTesting() => ReleaseAsync(null, true);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        column.AddChild(BuildStats());
        var usage = IslandUi.Column(6); _bodies = IslandUi.Progress(0, 14); usage.AddChild(IslandUi.Text("Corpos simulados pelo servidor da ilha (o limite protege o desempenho)", 14, IslandUi.TextSecondary)); usage.AddChild(_bodies); column.AddChild(usage);
        column.AddChild(BuildList());
    }

    private Control BuildStats()
    {
        var row = IslandUi.Row(14); row.SizeFlagsHorizontal = SizeFlags.ExpandFill; row.CustomMinimumSize = new Vector2(0, 96);
        var specs = new (string Title, string Icon)[] { ("Objetos Físicos", "cube"), ("Corpos Ativos", "move"), ("Congelados", "lock"), ("Tempo de Física", "history") };
        for (var i = 0; i < 4; i++)
        {
            var (card, body) = IslandUi.Card(specs[i].Title, specs[i].Icon); body.AddThemeConstantOverride("separation", 0);
            _values[i] = IslandUi.Text("—", 24, IslandUi.TextPrimary, true); body.AddChild(_values[i]); _notes[i] = IslandUi.Text("", 13, IslandUi.TextMuted); body.AddChild(_notes[i]); row.AddChild(card);
        }
        return row;
    }

    private Control BuildList()
    {
        var (card, body) = IslandUi.Card("Objetos Simulados", "rotate"); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var tools = IslandUi.Row(8); body.AddChild(tools);
        _search = IslandUi.Input("Pesquisar objeto ou dono…"); _search.ClearButtonEnabled = true; _search.TextSubmitted += _t => Search(); tools.AddChild(_search);
        _filter = IslandUi.Dropdown(Filters); _filter.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _filter.CustomMinimumSize = new Vector2(150, 40); _filter.ItemSelected += _i => Search(); tools.AddChild(_filter);
        var refresh = IslandUi.IconButton("reset", "Atualizar", 40); refresh.Pressed += () => _ = LoadAsync(); tools.AddChild(refresh);
        var bulk = IslandUi.Row(8); body.AddChild(bulk);
        _freezeAll = IslandUi.Action("Congelar todos os ativos", "lock", IslandUi.Kind.Orange, 38); _freezeAll.Pressed += () => Confirm("Congelar todos os objetos que se movem na ilha? Eles ficam parados onde estão; você pode liberar depois e voltam a se mover como eram.", "Congelar todos", () => _ = FreezeAsync(null, true)); bulk.AddChild(_freezeAll);
        _releaseAll = IslandUi.Action("Liberar todos os congelados", "unlock", IslandUi.Kind.Secondary, 38); _releaseAll.Pressed += () => Confirm("Liberar todos os objetos congelados? Eles voltam a se mover como eram antes de congelar.", "Liberar todos", () => _ = ReleaseAsync(null, true)); bulk.AddChild(_releaseAll);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); _status.SizeFlagsHorizontal = SizeFlags.ExpandFill; bulk.AddChild(_status);
        var head = IslandUi.Row(8); head.AddChild(Head("Objeto", 3)); head.AddChild(Head("Dono", 2)); head.AddChild(Head("Estado", 2)); head.AddChild(Head("Consumo", 2)); head.AddChild(Head("Ações", 2, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhum objeto simulado com esse filtro.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager);
        body.AddChild(IslandUi.Text("Congelar não apaga nem move nada: o objeto fica parado e continua sendo do dono. Se o dono mudar a física enquanto ele está congelado, a mudança dele é mantida ao liberar. Chega à ilha em poucos segundos.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label; }

    private void Search() { _page = 1; _ = LoadAsync(); }
    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static double Num(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) ? n : 0;

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.PhysicsGet(_island, _search.Text.Trim(), Filters[Math.Max(0, _filter.Selected)].Id, _page);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement d)
    {
        var summary = d.TryGetProperty("summary", out var s) ? s : default; _active = (int)Num(summary, "active"); _frozen = (int)Num(summary, "frozen");
        var server = d.TryGetProperty("server", out var sv) && sv.ValueKind == JsonValueKind.Object ? sv : default;
        _values[0].Text = ((int)Num(summary, "simulated")).ToString(Pt); _notes[0].Text = "que se movem ou estão congelados";
        _values[2].Text = _frozen.ToString(Pt); _notes[2].Text = "parados por um administrador";
        if (server.ValueKind == JsonValueKind.Object)
        {
            var bodies = (int)Num(server, "bodies"); var max = (int)Num(server, "maxBodies");
            _values[1].Text = max > 0 ? $"{bodies} / {max}" : bodies.ToString(Pt); _notes[1].Text = $"+ {(int)Num(server, "fixedBodies")} fixos";
            _values[3].Text = Num(server, "physicsMs").ToString("0.##", Pt) + " ms"; _notes[3].Text = "por quadro no servidor";
            var percent = max > 0 ? Math.Clamp(bodies * 100.0 / max, 0, 100) : 0; _bodies.Value = percent; IslandUi.SetProgressColor(_bodies, percent);
        }
        else { _values[1].Text = "—"; _notes[1].Text = "servidor sem relatório"; _values[3].Text = "—"; _notes[3].Text = ""; _bodies.Value = 0; }
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _total = (int)Num(d, "total"); _page = Math.Max(1, (int)Num(d, "page")); var size = Num(d, "pageSize") is > 0 and var ps ? (int)ps : 10; _pages = Math.Max(1, (_total + size - 1) / size);
        var items = d.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _items = [.. items.Select(i => (Guid.Parse(Str(i, "id")), Str(i, "name"), Str(i, "state") == "frozen"))];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; _count.Text = _total == 1 ? "1 objeto" : $"{_total} objetos"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages; _freezeAll.Disabled = _active == 0; _releaseAll.Disabled = _frozen == 0;
    }

    private Control BuildRow(JsonElement item)
    {
        var id = Guid.Parse(Str(item, "id")); var name = Str(item, "name"); var frozen = Str(item, "state") == "frozen";
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) }; box.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 0, 0));
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 49); column.AddChild(row);
        var title = IslandUi.Text(name, 15, IslandUi.TextPrimary, true); title.SizeFlagsHorizontal = SizeFlags.ExpandFill; title.SizeFlagsStretchRatio = 3; title.ClipText = true; title.TooltipText = name; title.MouseFilter = MouseFilterEnum.Pass; row.AddChild(title);
        var owner = IslandUi.Text(Str(item, "ownerName"), 14, IslandUi.TextSecondary); owner.SizeFlagsHorizontal = SizeFlags.ExpandFill; owner.SizeFlagsStretchRatio = 2; owner.ClipText = true; row.AddChild(owner);
        var state = IslandUi.Row(0); state.SizeFlagsHorizontal = SizeFlags.ExpandFill; state.SizeFlagsStretchRatio = 2; var pill = IslandUi.Pill(frozen ? "Congelado" : "Ativo", frozen ? IslandUi.AccentPrimary : IslandUi.Success); pill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; state.AddChild(pill); row.AddChild(state);
        var use = IslandUi.Text($"{((int)Num(item, "cost")).ToString("N0", Pt)} prims", 14, IslandUi.TextSecondary); use.SizeFlagsHorizontal = SizeFlags.ExpandFill; use.SizeFlagsStretchRatio = 2; use.TooltipText = $"Posição {Num(item, "x").ToString("0", Pt)}, {Num(item, "y").ToString("0", Pt)}, {Num(item, "z").ToString("0", Pt)}"; use.MouseFilter = MouseFilterEnum.Pass; row.AddChild(use);
        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 2; actions.Alignment = BoxContainer.AlignmentMode.End; row.AddChild(actions);
        var toggle = IslandUi.Action(frozen ? "Liberar" : "Congelar", frozen ? "unlock" : "lock", frozen ? IslandUi.Kind.Secondary : IslandUi.Kind.Orange, 34);
        toggle.Pressed += () => { if (frozen) _ = ReleaseAsync([id], false); else Confirm($"Congelar {name}? Ele para de se mover e cair até ser liberado.", "Congelar", () => _ = FreezeAsync([id], false)); };
        actions.AddChild(toggle);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    public async Task FreezeAsync(Guid[]? ids, bool all)
    {
        if (_api is null) return;
        var reply = await _api.PhysicsFreeze(_island, ids, all);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        var changed = (int)Num(reply.Body, "changed"); Say(changed == 0 ? "Nada para congelar." : $"{changed} objeto(s) congelado(s). A ilha recebe em poucos segundos.", false); if (changed > 0) Notify?.Invoke($"{changed} objeto(s) congelado(s).");
        await LoadAsync();
    }

    public async Task ReleaseAsync(Guid[]? ids, bool all)
    {
        if (_api is null) return;
        var reply = await _api.PhysicsRelease(_island, ids, all);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        var changed = (int)Num(reply.Body, "changed"); var kept = (int)Num(reply.Body, "kept");
        Say(changed == 0 ? "Nada para liberar." : $"{changed} objeto(s) liberado(s){(kept > 0 ? $"; em {kept} o dono tinha mudado a física e a mudança dele foi mantida" : "")}.", false); if (changed > 0) Notify?.Invoke($"{changed} objeto(s) liberado(s).");
        await LoadAsync();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
