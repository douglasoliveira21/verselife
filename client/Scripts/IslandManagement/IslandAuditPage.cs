using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Histórico e Auditoria tab: what was done to the island through the management window and what was built on it, with who did it, when, and the values before and after. It is read-only:
/// nothing here can be changed or deleted, and the server only lets the owner and administrators read it.
/// </summary>
public partial class IslandAuditPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, int Hours)[] Periods = [("Todo o período", 0), ("Últimas 24 horas", 24), ("Últimos 7 dias", 168), ("Últimos 30 dias", 720)];
    private static readonly (string Text, string Id)[] Categories = [("Todos os tipos", ""), ("Configurações gerais", "geral"), ("Acesso", "acesso"), ("Banimentos", "banimento"), ("Permissões", "permissao"), ("Objetos devolvidos", "objeto"), ("Construção", "construcao")];
    private static readonly Dictionary<string, string> CategoryNames = new() { ["geral"] = "Geral", ["acesso"] = "Acesso", ["banimento"] = "Banimento", ["permissao"] = "Permissão", ["objeto"] = "Objetos", ["construcao"] = "Construção" };
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["name"] = "Nome", ["description"] = "Descrição", ["coverImageUrl"] = "Imagem", ["category"] = "Categoria", ["contentRating"] = "Classificação", ["language"] = "Idioma", ["maxPlayers"] = "Capacidade", ["showInSearch"] = "Aparece no buscador",
        ["showOnMap"] = "Aparece no mapa", ["spawnX"] = "Chegada X", ["spawnZ"] = "Chegada Z", ["mode"] = "Modo", ["expiresAt"] = "Expira em", ["reason"] = "Motivo", ["permissions"] = "Permissões", ["count"] = "Quantidade", ["names"] = "Objetos", ["returned"] = "Devolvidos", ["remaining"] = "Restantes",
    };

    private LineEdit _user = null!, _search = null!;
    private OptionButton _period = null!, _category = null!;
    private Label _status = null!, _count = null!, _pageText = null!, _empty = null!;
    private VBoxContainer _rows = null!, _detail = null!;
    private Button _previous = null!, _next = null!;
    private IslandManagementApi? _api;
    private Guid _island, _selected;
    private int _page = 1, _pages = 1, _total;
    private List<Guid> _ids = [];

    public string StatusForTesting => _status.Text;
    public int TotalForTesting => _total;
    public int RowsForTesting => _rows.GetChildCount();
    public string PageTextForTesting => _pageText.Text;
    public Guid SelectedForTesting => _selected;
    public string DetailForTesting => string.Join("|", _detail.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public string UserForTesting { get => _user.Text; set => _user.Text = value; }
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public void SelectPeriodForTesting(int index) => _period.Select(index);
    public void SelectCategoryForTesting(int index) => _category.Select(index);
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public Task SelectFirstForTesting() => _ids.Count == 0 ? Task.CompletedTask : SelectAsync(_ids[0]);
    public Task ApplyForTesting() { _page = 1; return LoadAsync(); }

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        column.AddChild(BuildFilters());
        var wrap = IslandUi.Row(14); wrap.SizeFlagsHorizontal = SizeFlags.ExpandFill; column.AddChild(wrap);
        wrap.AddChild(BuildTable()); wrap.AddChild(BuildDetail());
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildFilters()
    {
        var (card, body) = IslandUi.Card("Filtros", "search");
        var first = IslandUi.Row(10); body.AddChild(first);
        _period = IslandUi.Dropdown(Periods.Select((p, i) => (p.Text, i.ToString()))); _period.ItemSelected += _i => Apply(); first.AddChild(_period);
        _category = IslandUi.Dropdown(Categories); _category.ItemSelected += _i => Apply(); first.AddChild(_category);
        _user = IslandUi.Input("Quem fez (nome)"); _user.TextSubmitted += _t => Apply(); first.AddChild(_user);
        _search = IslandUi.Input("Pesquisar no texto…"); _search.TextSubmitted += _t => Apply(); first.AddChild(_search);
        var go = IslandUi.Action("Filtrar", "search", IslandUi.Kind.Primary, 40); go.Pressed += Apply; first.AddChild(go);
        var clear = IslandUi.Action("Limpar", null, IslandUi.Kind.Secondary, 40); clear.Pressed += () => { _period.Select(0); _category.Select(0); _user.Text = ""; _search.Text = ""; Apply(); }; first.AddChild(clear);
        _status = IslandUi.Text("Só entram aqui as ações que deram certo; o que o servidor recusou não vira registro.", 13, IslandUi.TextMuted, false, true); body.AddChild(_status);
        return card;
    }

    private Control BuildTable()
    {
        var (card, body) = IslandUi.Card("Eventos", "history"); card.SizeFlagsStretchRatio = 66; card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var head = IslandUi.Row(8); head.AddChild(Head("Data/Hora", 3)); head.AddChild(Head("Pessoa", 2)); head.AddChild(Head("Tipo", 2)); head.AddChild(Head("Evento", 5)); head.AddChild(Head("Resultado", 2));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhum evento com esses filtros.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager);
        return card;
    }

    private Control BuildDetail()
    {
        var (card, body) = IslandUi.Card("Detalhes do Evento", "info"); card.SizeFlagsStretchRatio = 34; card.CustomMinimumSize = new Vector2(340, 0); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        _detail = body; ShowNoDetail(); return card;
    }

    private static Label Head(string text, int ratio) { var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; return label; }

    // ---- data -------------------------------------------------------------------------------------------------------------

    private void Apply() { _page = 1; _ = LoadAsync(); }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string When(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Pt) : "";

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.AuditGet(_island, Periods[Math.Max(0, _period.Selected)].Hours, _user.Text.Trim(), Categories[Math.Max(0, _category.Selected)].Id, _search.Text.Trim(), _page);
        if (!reply.Ok) { _status.Text = reply.Message; _status.AddThemeColorOverride("font_color", IslandUi.Error); return; }
        _status.Text = "Só entram aqui as ações que deram certo; o que o servidor recusou não vira registro."; _status.AddThemeColorOverride("font_color", IslandUi.TextMuted);
        var body = reply.Body;
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _total = body.TryGetProperty("total", out var t) && t.TryGetInt32(out var total) ? total : 0;
        _page = body.TryGetProperty("page", out var p) && p.TryGetInt32(out var page) ? page : 1;
        var size = body.TryGetProperty("pageSize", out var s) && s.TryGetInt32(out var ps) && ps > 0 ? ps : 10; _pages = Math.Max(1, (_total + size - 1) / size);
        var items = body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _ids = [.. items.Select(i => Guid.Parse(Str(i, "id")))];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; _count.Text = _total == 1 ? "1 evento" : $"{_total.ToString("N0", Pt)} eventos"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages;
    }

    private Control BuildRow(JsonElement item)
    {
        var id = Guid.Parse(Str(item, "id")); var chosen = id == _selected;
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 48), MouseDefaultCursorShape = CursorShape.PointingHand };
        box.AddThemeStyleboxOverride("panel", IslandUi.Box(chosen ? new Color(IslandUi.AccentSelected, .55f) : new Color(0, 0, 0, 0), chosen ? IslandUi.AccentPrimary : null, 6, chosen ? 1 : 0, 4, 0));
        box.GuiInput += input => { if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) _ = SelectAsync(id); };
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(8); row.CustomMinimumSize = new Vector2(0, 47); column.AddChild(row);
        void Cell(string text, int ratio, Color? color = null, bool bold = false, bool wrap = false) { var label = IslandUi.Text(text, 14, color ?? IslandUi.TextSecondary, bold, wrap); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (!wrap) label.ClipText = true; row.AddChild(label); }
        Cell(When(item, "at"), 3); Cell(Str(item, "actorName"), 2, IslandUi.TextPrimary, true);
        var category = Str(item, "category"); Cell(CategoryNames.TryGetValue(category, out var title) ? title : category, 2);
        var summary = Str(item, "summary"); var line = IslandUi.Text(summary, 14, IslandUi.TextPrimary, false, true); line.SizeFlagsHorizontal = SizeFlags.ExpandFill; line.SizeFlagsStretchRatio = 5; line.TooltipText = summary; line.MouseFilter = MouseFilterEnum.Pass; row.AddChild(line);
        var result = IslandUi.Row(0); result.SizeFlagsHorizontal = SizeFlags.ExpandFill; result.SizeFlagsStretchRatio = 2; var pill = IslandUi.Pill("Sucesso", IslandUi.Success); pill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; result.AddChild(pill); row.AddChild(result);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- detail -----------------------------------------------------------------------------------------------------------

    private void ShowNoDetail()
    {
        foreach (var child in _detail.GetChildren()) child.QueueFree();
        _detail.AddChild(IslandUi.Text("Escolha um evento para ver os detalhes.", 14, IslandUi.TextMuted, false, true));
    }

    private static IEnumerable<string> Lines(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                yield return $"{(FieldNames.TryGetValue(property.Name, out var title) ? title : property.Name)}: {Plain(property.Value)}";
        else if (value.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) yield return Plain(value);
    }

    private static string Plain(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.TryGetDateTimeOffset(out var d) && v.GetString()!.Length > 18 && v.GetString()![10] == 'T' ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : v.GetString() ?? "",
        JsonValueKind.True => "sim", JsonValueKind.False => "não", JsonValueKind.Null => "—", JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(Plain)), JsonValueKind.Number => v.GetRawText().Replace('.', ','), _ => v.GetRawText(),
    };

    public async Task SelectAsync(Guid id)
    {
        if (_api is null) return;
        _selected = id; var reply = await _api.AuditDetail(_island, id);
        if (!reply.Ok) { _selected = Guid.Empty; ShowNoDetail(); _status.Text = reply.Message; return; }
        var d = reply.Body;
        foreach (var child in _detail.GetChildren()) child.QueueFree();
        _detail.AddChild(IslandUi.Text(Str(d, "summary"), 16, IslandUi.TextPrimary, true, true));
        void Line(string caption, string value) { if (value.Length == 0) return; var row = IslandUi.Row(8); var c = IslandUi.Text(caption, 13, IslandUi.TextMuted); c.CustomMinimumSize = new Vector2(104, 0); row.AddChild(c); row.AddChild(IslandUi.Text(value, 14, IslandUi.TextPrimary, false, true)); _detail.AddChild(row); }
        Line("ID do evento", Str(d, "id")); Line("Responsável", Str(d, "actorName")); Line("Ilha", Str(d, "island")); Line("Data", When(d, "at"));
        var category = Str(d, "category"); Line("Operação", CategoryNames.TryGetValue(category, out var title) ? $"{title} · {Str(d, "action")}" : Str(d, "action")); Line("Recurso", Str(d, "resource")); Line("Resultado", "Sucesso");
        void Block(string caption, string key)
        {
            if (!d.TryGetProperty(key, out var value)) return; var lines = Lines(value).ToList(); if (lines.Count == 0) return;
            _detail.AddChild(IslandUi.Text(caption, 14, IslandUi.AccentPrimary, true));
            var box = new PanelContainer(); box.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, IslandUi.RadiusInput, 1, 10, 8));
            var inside = IslandUi.Column(2); foreach (var line in lines) inside.AddChild(IslandUi.Text(line, 13, IslandUi.TextSecondary, false, true)); box.AddChild(inside); _detail.AddChild(box);
        }
        Block("Valores anteriores", "before"); Block("Valores novos", "after");
        Line("ID de correlação", Str(d, "correlationId"));
        await LoadAsync();
    }
}
