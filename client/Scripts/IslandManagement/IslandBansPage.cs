using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Banimentos tab: counters, a form to ban (name, reason, duration) and the list with every ban of the island, including the ones that ended. Banning and lifting ask first. The ban
/// itself is applied by the server where people come in; this only sends the request and shows the answer.
/// </summary>
public partial class IslandBansPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Text, TimeSpan? Span)[] Durations = [("1 hora", TimeSpan.FromHours(1)), ("1 dia", TimeSpan.FromDays(1)), ("7 dias", TimeSpan.FromDays(7)), ("30 dias", TimeSpan.FromDays(30)), ("Permanente", null)];
    private static readonly (string Text, string Id)[] Filters = [("Todos", ""), ("Ativos", "active"), ("Expirados", "expired"), ("Desbanidos", "lifted")];

    private readonly Label[] _counters = new Label[4];
    private LineEdit _name = null!, _reason = null!, _search = null!;
    private OptionButton _duration = null!, _filter = null!;
    private Button _ban = null!, _previous = null!, _next = null!;
    private Label _status = null!, _count = null!, _pageText = null!, _empty = null!;
    private VBoxContainer _rows = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private int _page = 1, _pages = 1, _total;
    private List<Guid> _ids = [];

    public event Action<string>? Notify;
    public event Action<Guid>? ViewProfile;
    /// <summary>Asks the person to confirm (text, confirm button, what to do).</summary>
    public Action<string, string, Action>? Ask { get; set; }

    public string CountersForTesting => string.Join("|", _counters.Select(c => c.Text));
    public string StatusForTesting => _status.Text;
    public int TotalForTesting => _total;
    public int RowsForTesting => _rows.GetChildCount();
    public string PageTextForTesting => _pageText.Text;
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public string ReasonForTesting { get => _reason.Text; set => _reason.Text = value; }
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public void SelectDurationForTesting(int index) => _duration.Select(index);
    public void SelectFilterForTesting(int index) => _filter.Select(index);
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public void PressBanForTesting() => _ban.EmitSignal(BaseButton.SignalName.Pressed);
    public Task LiftFirstForTesting() => _ids.Count == 0 ? Task.CompletedTask : LiftAsync(_ids[0], "teste");
    public Task ChangeFirstForTesting(TimeSpan? span) => _ids.Count == 0 ? Task.CompletedTask : ChangeAsync(_ids[0], span);

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 205); column.AddChild(top);
        top.AddChild(BuildSummary()); top.AddChild(BuildForm());
        column.AddChild(BuildList());
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildSummary()
    {
        var (card, body) = IslandUi.Card("Resumo dos Banimentos", "ban"); card.SizeFlagsStretchRatio = 40;
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 10); body.AddChild(grid);
        var specs = new (string Text, Color Color)[] { ("Banimentos ativos", IslandUi.Error), ("Temporários", IslandUi.AccentSecondary), ("Permanentes", IslandUi.Error), ("Expirados", IslandUi.TextMuted) };
        for (var i = 0; i < 4; i++)
        {
            var tile = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 62) }; tile.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.InputBackground, IslandUi.BorderDefault, IslandUi.RadiusInput, 1, 14, 6));
            var inside = IslandUi.Column(0); inside.AddChild(IslandUi.Text(specs[i].Text, 13, IslandUi.TextSecondary)); _counters[i] = IslandUi.Text("—", 22, specs[i].Color, true); inside.AddChild(_counters[i]); tile.AddChild(inside); grid.AddChild(tile);
        }
        return card;
    }

    private Control BuildForm()
    {
        var (card, body) = IslandUi.Card("Adicionar Banimento", "alert"); card.SizeFlagsStretchRatio = 60;
        var first = IslandUi.Row(10); body.AddChild(first);
        _name = IslandUi.Input("Nome da pessoa"); first.AddChild(_name);
        _duration = IslandUi.Dropdown(Durations.Select((d, i) => (d.Text, i.ToString()))); _duration.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _duration.CustomMinimumSize = new Vector2(170, 40); _duration.Select(2); first.AddChild(_duration);
        _reason = IslandUi.Input("Motivo do banimento (obrigatório)", 200); body.AddChild(_reason);
        var footer = IslandUi.Row(10); body.AddChild(footer);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); footer.AddChild(_status);
        _ban = IslandUi.Action("Banir Usuário", "ban", IslandUi.Kind.Danger, 42); _ban.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _ban.CustomMinimumSize = new Vector2(190, 42); _ban.Pressed += OnBanPressed; footer.AddChild(_ban);
        return card;
    }

    private Control BuildList()
    {
        var (card, body) = IslandUi.Card("Lista de Banimentos", "list"); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var tools = IslandUi.Row(10); body.AddChild(tools);
        _search = IslandUi.Input("Pesquisar por pessoa ou motivo…"); _search.ClearButtonEnabled = true; _search.TextSubmitted += _text => Search(); tools.AddChild(_search);
        _filter = IslandUi.Dropdown(Filters.Select(f => (f.Text, f.Id))); _filter.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _filter.CustomMinimumSize = new Vector2(170, 40); _filter.ItemSelected += _index => Search(); tools.AddChild(_filter);
        var find = IslandUi.Action("Pesquisar", "search", IslandUi.Kind.Secondary, 40); find.Pressed += Search; tools.AddChild(find);

        var head = IslandUi.Row(10); head.AddChild(Head("Pessoa", 3)); head.AddChild(Head("Motivo", 3)); head.AddChild(Head("Responsável", 2)); head.AddChild(Head("Data", 2)); head.AddChild(Head("Término", 2)); head.AddChild(Head("Estado", 2)); head.AddChild(Head("Ações", 2, true));
        body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Nenhum banimento por aqui.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager);
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false)
    {
        var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label;
    }

    private void Search() { _page = 1; _ = LoadAsync(); }

    // ---- data -------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var filter = _filter.Selected >= 0 ? Filters[_filter.Selected].Id : "";
        var reply = await _api.BansGet(_island, _search.Text.Trim(), filter, _page);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement body)
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        if (body.TryGetProperty("counts", out var counts))
            for (var i = 0; i < 4; i++) _counters[i].Text = counts.TryGetProperty(new[] { "active", "temporary", "permanent", "expired" }[i], out var n) && n.TryGetInt32(out var value) ? value.ToString(Pt) : "—";
        _total = body.TryGetProperty("total", out var t) && t.TryGetInt32(out var total) ? total : 0;
        _page = body.TryGetProperty("page", out var p) && p.TryGetInt32(out var page) ? page : 1;
        var size = body.TryGetProperty("pageSize", out var s) && s.TryGetInt32(out var ps) && ps > 0 ? ps : 10; _pages = Math.Max(1, (_total + size - 1) / size);
        var items = body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _ids = [.. items.Select(i => i.GetProperty("banId").GetGuid())];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; _empty.Text = _search.Text.Trim().Length > 0 || _filter.Selected > 0 ? "Nenhum banimento com esse filtro." : "Nenhum banimento por aqui.";
        _count.Text = _total == 1 ? "1 registro" : $"{_total} registros"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages;
    }

    private static string Date(JsonElement item, string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "";
    private static string Str(JsonElement item, string name) => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private Control BuildRow(JsonElement item)
    {
        var id = item.GetProperty("banId").GetGuid(); var user = Guid.TryParse(Str(item, "userId"), out var u) ? u : Guid.Empty;
        var name = Str(item, "name"); var state = Str(item, "status"); var ends = Date(item, "expiresAt");
        var (stateText, stateColor) = state switch { "active" => ("Ativo", IslandUi.Error), "expired" => ("Expirado", IslandUi.TextMuted), _ => ("Desbanido", IslandUi.Success) };
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) }; box.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 0, 0));
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(10); row.CustomMinimumSize = new Vector2(0, 49); column.AddChild(row);
        var who = IslandUi.Row(9); who.SizeFlagsHorizontal = SizeFlags.ExpandFill; who.SizeFlagsStretchRatio = 3; who.AddChild(UiStyle.IconRect("avatar", IslandUi.TextSecondary, 22)); who.AddChild(IslandUi.Text(name, 15, IslandUi.TextPrimary, true)); row.AddChild(who);
        var reason = IslandUi.Text(Str(item, "reason"), 14, IslandUi.TextSecondary, false, true); reason.SizeFlagsStretchRatio = 3; reason.TooltipText = Str(item, "reason"); reason.MouseFilter = MouseFilterEnum.Pass; row.AddChild(reason);
        foreach (var (text, ratio) in new[] { (Str(item, "bannedBy"), 2), (Date(item, "createdAt"), 2), (ends.Length > 0 ? ends : "Permanente", 2) })
        { var label = IslandUi.Text(text, 14, IslandUi.TextSecondary); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; row.AddChild(label); }
        var stateHolder = IslandUi.Row(0); stateHolder.SizeFlagsHorizontal = SizeFlags.ExpandFill; stateHolder.SizeFlagsStretchRatio = 2; var pill = IslandUi.Pill(stateText, stateColor); pill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; stateHolder.AddChild(pill); row.AddChild(stateHolder);

        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 2; actions.Alignment = BoxContainer.AlignmentMode.End; row.AddChild(actions);
        var profile = IslandUi.IconButton("user", "Ver perfil", 34); profile.Pressed += () => { if (user != Guid.Empty) ViewProfile?.Invoke(user); }; actions.AddChild(profile);
        if (state == "active")
        {
            var change = new MenuButton { Icon = UiStyle.Icon("calendar", IslandUi.TextPrimary, 20), TooltipText = "Alterar a duração", CustomMinimumSize = new Vector2(34, 34), Flat = true, FocusMode = FocusModeEnum.None };
            var popup = change.GetPopup(); popup.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusInput, 1, 6, 6)); popup.AddThemeStyleboxOverride("hover", IslandUi.Box(IslandUi.AccentSelected, null, 5, 0, 8, 4)); popup.AddThemeFontSizeOverride("font_size", 14);
            foreach (var (text, _) in Durations) popup.AddItem(text == "Permanente" ? text : $"Daqui a {text}");
            popup.IdPressed += index => _ = ChangeAsync(id, Durations[(int)index].Span); actions.AddChild(change);
            var lift = IslandUi.IconButton("unlock", "Desbanir", 34); lift.Pressed += () => Confirm($"Desbanir {name}? A pessoa poderá entrar de novo na ilha.", "Desbanir", () => _ = LiftAsync(id, name)); actions.AddChild(lift);
        }
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    private void Confirm(string text, string ok, Action action) { if (Ask is { } ask) ask(text, ok, action); else action(); }

    private void OnBanPressed()
    {
        var who = _name.Text.Trim(); if (who.Length == 0) { Say("Digite o nome de quem você quer banir.", true); return; }
        if (_reason.Text.Trim().Length == 0) { Say("Escreva o motivo do banimento.", true); return; }
        var length = Durations[Math.Max(0, _duration.Selected)].Text;
        Confirm($"Banir {who} ({(length == "Permanente" ? "permanente" : "por " + length)})? A pessoa não poderá mais entrar na ilha.", "Banir", () => _ = BanAsync());
    }

    public async Task BanAsync()
    {
        if (_api is null) return;
        var who = _name.Text.Trim(); _ban.Disabled = true;
        var span = Durations[Math.Max(0, _duration.Selected)].Span;
        var reply = await _api.BanAdd(_island, who, _reason.Text.Trim(), span is { } s ? DateTimeOffset.UtcNow + s : null); _ban.Disabled = false;
        if (!reply.Ok) { Say(reply.Message, true); return; }
        _name.Text = ""; _reason.Text = ""; Say($"{who} foi banida.", false); Notify?.Invoke($"{who} foi banido da ilha."); _page = 1; await LoadAsync();
    }

    public async Task ChangeAsync(Guid ban, TimeSpan? span)
    {
        if (_api is null) return;
        var reply = await _api.BanChange(_island, ban, span is { } s ? DateTimeOffset.UtcNow + s : null);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say("Duração alterada.", false); await LoadAsync();
    }

    public async Task LiftAsync(Guid ban, string name)
    {
        if (_api is null) return;
        var reply = await _api.BanLift(_island, ban);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say($"{name} foi desbanida.", false); Notify?.Invoke($"{name} foi desbanido."); await LoadAsync();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
