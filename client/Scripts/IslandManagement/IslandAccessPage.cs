using System.Globalization;
using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Acesso tab: how the island lets people in (public, only invited, temporarily closed) and the list of people allowed in, with an optional expiry. The mode is a draft saved with
/// "Salvar Alterações"; the list changes at once (each add, change and removal is its own call). What the server refuses is shown, not hidden.
/// </summary>
public partial class IslandAccessPage : ScrollContainer
{
    private static readonly CultureInfo Pt = new("pt-BR");
    private static readonly (string Id, string Title, string Text, string Icon)[] Modes =
    [
        ("public", "Pública", "Qualquer pessoa pode entrar pelo mapa, pelo buscador ou por teleporte.", "globe"),
        ("invite", "Somente convidados", "Só entram o proprietário, os administradores e as pessoas da lista abaixo.", "lock"),
        ("closed", "Temporariamente fechada", "Ninguém entra, só o proprietário e os administradores. Quem já está na ilha continua.", "ban"),
    ];
    private static readonly (string Text, TimeSpan? Span)[] Expiries = [("Sem expiração", null), ("Daqui a 1 hora", TimeSpan.FromHours(1)), ("Daqui a 1 dia", TimeSpan.FromDays(1)), ("Daqui a 7 dias", TimeSpan.FromDays(7)), ("Daqui a 30 dias", TimeSpan.FromDays(30))];

    private readonly Dictionary<string, Button> _modeButtons = [];
    private Label _summary = null!, _count = null!, _status = null!, _pageText = null!;
    private LineEdit _search = null!, _name = null!;
    private OptionButton _expiry = null!;
    private Button _add = null!, _previous = null!, _next = null!, _find = null!;
    private VBoxContainer _rows = null!;
    private Control _empty = null!;
    private string _savedMode = "invite", _draftMode = "invite";
    private int _page = 1, _pages = 1, _total;
    private bool _canEdit;
    private IslandManagementApi? _api;
    private Guid _island;

    public event Action? Changed;
    public event Action<string>? Notify;
    public event Action<Guid>? ViewProfile;

    public bool IsDirty => _canEdit && _draftMode != _savedMode;
    public string DraftMode => _draftMode;
    public string SavedMode => _savedMode;
    public string SummaryForTesting => _summary.Text;
    public string StatusForTesting => _status.Text;
    public int RowsForTesting => _rows.GetChildCount();
    public int TotalForTesting => _total;
    public string PageTextForTesting => _pageText.Text;
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public void SelectExpiryForTesting(int index) => _expiry.Select(index);
    public bool AddEnabledForTesting => !_add.Disabled;
    public void PickModeForTesting(string id) => PickMode(id);
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];

    public void Bind(IslandManagementApi api) => _api = api;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);

        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 275); column.AddChild(top);
        top.AddChild(BuildModes()); top.AddChild(BuildSummary());
        column.AddChild(BuildList());
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildModes()
    {
        var (card, body) = IslandUi.Card("Configuração de Acesso", "lock"); card.SizeFlagsStretchRatio = 48; body.AddThemeConstantOverride("separation", 9);
        foreach (var (id, title, text, icon) in Modes)
        {
            var option = new Button { ToggleMode = false, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 72), SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipContents = true };
            var inside = IslandUi.Row(12); inside.MouseFilter = MouseFilterEnum.Ignore; inside.SetAnchorsPreset(LayoutPreset.FullRect); inside.AddThemeConstantOverride("separation", 12);
            var glyph = UiStyle.IconRect(icon, IslandUi.AccentPrimary, 26); glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter; var holder = IslandUi.Padded(glyph, 14, 0, 0, 0); holder.MouseFilter = MouseFilterEnum.Ignore; inside.AddChild(holder);
            var words = IslandUi.Column(1); words.SizeFlagsHorizontal = SizeFlags.ExpandFill; words.SizeFlagsVertical = SizeFlags.ShrinkCenter; words.MouseFilter = MouseFilterEnum.Ignore;
            words.AddChild(IslandUi.Text(title, 16, IslandUi.TextPrimary, true)); words.AddChild(IslandUi.Text(text, 13, IslandUi.TextSecondary, false, true)); inside.AddChild(words);
            option.AddChild(inside); var key = id; option.Pressed += () => PickMode(key); _modeButtons[id] = option; body.AddChild(option);
        }
        return card;
    }

    private Control BuildSummary()
    {
        var (card, body) = IslandUi.Card("Quem pode entrar agora", "people"); card.SizeFlagsStretchRatio = 52;
        _summary = IslandUi.Text("", 15, IslandUi.TextPrimary, false, true); body.AddChild(_summary);
        body.AddChild(IslandUi.Divider());
        body.AddChild(IslandUi.Text("O proprietário e quem tem a permissão de administrador entram sempre, em qualquer modo.", 14, IslandUi.TextSecondary, false, true));
        body.AddChild(IslandUi.Text("Mudar o modo não tira da ilha quem já está nela; vale para as próximas entradas. O modo só muda depois de “Salvar Alterações”.", 14, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildList()
    {
        var (card, body) = IslandUi.Card("Pessoas Permitidas", "user"); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var tools = IslandUi.Row(10); body.AddChild(tools);
        _search = IslandUi.Input("Pesquisar na lista…"); _search.ClearButtonEnabled = true; _search.TextSubmitted += _text => { _page = 1; _status.Text = ""; _ = LoadAsync(); }; tools.AddChild(_search);
        _find = IslandUi.Action("Pesquisar", "search", IslandUi.Kind.Secondary, 40); _find.Pressed += () => { _page = 1; _status.Text = ""; _ = LoadAsync(); }; tools.AddChild(_find);
        var add = IslandUi.Row(10); body.AddChild(add);
        _name = IslandUi.Input("Nome da pessoa para permitir"); _name.TextSubmitted += _text => _ = AddAsync(); add.AddChild(_name);
        _expiry = IslandUi.Dropdown(Expiries.Select((e, i) => (e.Text, i.ToString()))); _expiry.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _expiry.CustomMinimumSize = new Vector2(200, 40); add.AddChild(_expiry);
        _add = IslandUi.Action("Adicionar pessoa", "plus", IslandUi.Kind.Primary, 40); _add.Pressed += () => _ = AddAsync(); add.AddChild(_add);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); body.AddChild(_status);

        var head = IslandUi.Row(10); head.AddChild(Column("Nome", 3)); head.AddChild(Column("Adicionada em", 2)); head.AddChild(Column("Expira", 2)); head.AddChild(Column("Ações", 2, true)); body.AddChild(head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Ninguém na lista ainda. Adicione pelo nome.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); ((Label)_empty).HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager);
        return card;
    }

    private static Label Column(string text, int ratio, bool end = false)
    {
        var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right; return label;
    }

    // ---- mode -------------------------------------------------------------------------------------------------------------

    private void PickMode(string id)
    {
        if (!_canEdit || _draftMode == id) return;
        _draftMode = id; PaintModes(); Changed?.Invoke();
    }

    private void PaintModes()
    {
        foreach (var (id, button) in _modeButtons)
        {
            var on = id == _draftMode;
            var normal = IslandUi.Box(on ? IslandUi.AccentSelected : IslandUi.InputBackground, on ? IslandUi.AccentPrimary : IslandUi.BorderDefault, IslandUi.RadiusButton, 1, 0, 0);
            var hover = IslandUi.Box(on ? IslandUi.AccentSelected : new Color(IslandUi.AccentPrimary, .10f), IslandUi.AccentPrimary, IslandUi.RadiusButton, 1, 0, 0);
            foreach (var name in new[] { "normal", "pressed", "focus", "disabled" }) button.AddThemeStyleboxOverride(name, normal); button.AddThemeStyleboxOverride("hover", hover); button.Disabled = false;
        }
        _summary.Text = _draftMode switch
        {
            "public" => "Ilha pública: qualquer pessoa pode entrar pelo mapa, pelo buscador ou por teleporte.",
            "invite" => $"Somente convidados: entram o proprietário, os administradores e as {_total} pessoas da lista abaixo (se a lista estiver vazia, só a equipe).",
            _ => "Temporariamente fechada: só o proprietário e os administradores entram. A lista e o modo anterior ficam guardados para quando você reabrir.",
        };
    }

    // ---- data -------------------------------------------------------------------------------------------------------------

    /// <summary>Starts from what the server says about the island (mode and whether the person may change it).</summary>
    public void Fill(IslandInfo info)
    {
        _island = info.Id; _canEdit = info.CanEdit; _savedMode = _draftMode = info.AccessMode;
        _name.Editable = _canEdit; _expiry.Disabled = !_canEdit; _add.Disabled = !_canEdit;
        PaintModes(); Changed?.Invoke();
    }

    /// <summary>After the mode was saved the draft is the saved mode.</summary>
    public void MarkSaved(string mode) { _savedMode = _draftMode = mode; PaintModes(); Changed?.Invoke(); }
    public void Revert() { _draftMode = _savedMode; PaintModes(); Changed?.Invoke(); }
    public void SetDraft(string mode) { _draftMode = mode; PaintModes(); Changed?.Invoke(); }

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.AccessGet(_island, _search.Text.Trim(), _page);
        if (!reply.Ok) { _status.Text = reply.Message; _status.AddThemeColorOverride("font_color", IslandUi.Error); return; }
        Show(reply.Body);
    }

    private void Show(JsonElement body)
    {
        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _total = body.TryGetProperty("total", out var t) && t.TryGetInt32(out var total) ? total : 0;
        _page = body.TryGetProperty("page", out var p) && p.TryGetInt32(out var page) ? page : 1;
        var size = body.TryGetProperty("pageSize", out var s) && s.TryGetInt32(out var ps) && ps > 0 ? ps : 10; _pages = Math.Max(1, (_total + size - 1) / size);
        var items = body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _ids = [.. items.Select(i => i.GetProperty("permissionId").GetGuid())];
        foreach (var item in items) _rows.AddChild(BuildRow(item));
        _empty.Visible = items.Count == 0; ((Label)_empty).Text = _search.Text.Trim().Length > 0 ? "Ninguém na lista com esse nome." : "Ninguém na lista ainda. Adicione pelo nome.";
        _count.Text = _total == 1 ? "1 pessoa permitida" : $"{_total} pessoas permitidas"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages;
        PaintModes();
    }

    private Control BuildRow(JsonElement item)
    {
        var id = item.GetProperty("permissionId").GetGuid(); var user = item.TryGetProperty("userId", out var u) && u.ValueKind == JsonValueKind.String ? Guid.Parse(u.GetString()!) : Guid.Empty;
        var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""; var username = item.TryGetProperty("username", out var un) ? un.GetString() ?? "" : "";
        var added = item.TryGetProperty("addedAt", out var a) && a.TryGetDateTimeOffset(out var ad) ? ad.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "—";
        DateTimeOffset? expires = item.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.String && e.TryGetDateTimeOffset(out var ed) ? ed : null;

        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) };
        box.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 0, 0));
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(10); row.CustomMinimumSize = new Vector2(0, 49); column.AddChild(row);
        var who = IslandUi.Row(9); who.SizeFlagsHorizontal = SizeFlags.ExpandFill; who.SizeFlagsStretchRatio = 3; who.AddChild(UiStyle.IconRect("avatar", IslandUi.TextSecondary, 22));
        var names = IslandUi.Column(0); names.SizeFlagsVertical = SizeFlags.ShrinkCenter; names.AddChild(IslandUi.Text(name, 15, IslandUi.TextPrimary, true)); if (username.Length > 0 && !string.Equals(username, name, StringComparison.OrdinalIgnoreCase)) names.AddChild(IslandUi.Text("@" + username, 12, IslandUi.TextMuted)); who.AddChild(names); row.AddChild(who);
        var addedLabel = IslandUi.Text(added, 14, IslandUi.TextSecondary); addedLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill; addedLabel.SizeFlagsStretchRatio = 2; row.AddChild(addedLabel);
        var expiresLabel = IslandUi.Text(expires is { } at ? at.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Pt) : "Não expira", 14, expires is null ? IslandUi.TextMuted : IslandUi.AccentSecondary); expiresLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill; expiresLabel.SizeFlagsStretchRatio = 2; row.AddChild(expiresLabel);

        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 2; actions.Alignment = BoxContainer.AlignmentMode.End; row.AddChild(actions);
        var profile = IslandUi.IconButton("user", "Ver perfil", 34); profile.Pressed += () => { if (user != Guid.Empty) ViewProfile?.Invoke(user); }; actions.AddChild(profile);
        var change = new MenuButton { Icon = UiStyle.Icon("calendar", IslandUi.TextPrimary, 20), TooltipText = "Alterar a expiração", CustomMinimumSize = new Vector2(34, 34), Flat = true, Disabled = !_canEdit, FocusMode = FocusModeEnum.None };
        var popup = change.GetPopup(); popup.AddThemeStyleboxOverride("panel", IslandUi.Box(IslandUi.CardBackground, IslandUi.BorderDefault, IslandUi.RadiusInput, 1, 6, 6)); popup.AddThemeStyleboxOverride("hover", IslandUi.Box(IslandUi.AccentSelected, null, 5, 0, 8, 4)); popup.AddThemeFontSizeOverride("font_size", 14);
        foreach (var (text, _) in Expiries) popup.AddItem(text);
        popup.IdPressed += index => _ = ChangeAsync(id, Expiries[(int)index].Span);
        actions.AddChild(change);
        var remove = IslandUi.IconButton("trash", "Remover da lista", 34); remove.Disabled = !_canEdit; remove.Pressed += () => _ = RemoveAsync(id, name); actions.AddChild(remove);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    private DateTimeOffset? ChosenExpiry() => Expiries[Math.Max(0, _expiry.Selected)].Span is { } span ? DateTimeOffset.UtcNow + span : null;

    public async Task AddAsync()
    {
        if (_api is null || !_canEdit) return;
        var who = _name.Text.Trim(); if (who.Length == 0) { Say("Digite o nome de quem você quer permitir.", true); return; }
        _add.Disabled = true; var reply = await _api.AccessAllow(_island, who, ChosenExpiry()); _add.Disabled = !_canEdit;
        if (!reply.Ok) { Say(reply.Message, true); return; }
        _name.Text = ""; Say($"{who} agora pode entrar.", false); Notify?.Invoke($"{who} foi adicionada à lista."); _page = 1; await LoadAsync();
    }

    public async Task ChangeAsync(Guid permission, TimeSpan? span)
    {
        if (_api is null) return;
        var reply = await _api.AccessChange(_island, permission, span is { } s ? DateTimeOffset.UtcNow + s : null);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say("Expiração alterada.", false); await LoadAsync();
    }

    public async Task RemoveAsync(Guid permission, string name)
    {
        if (_api is null) return;
        var reply = await _api.AccessRemove(_island, permission);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say($"{name} saiu da lista.", false); await LoadAsync();
    }

    public Task RemoveFirstForTesting() => _ids.Count == 0 ? Task.CompletedTask : RemoveAsync(_ids[0], "teste");
    public Task ChangeFirstForTesting(TimeSpan? span) => _ids.Count == 0 ? Task.CompletedTask : ChangeAsync(_ids[0], span);
    private List<Guid> _ids = [];

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
