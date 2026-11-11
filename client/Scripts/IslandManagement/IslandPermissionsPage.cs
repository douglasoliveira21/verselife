using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts.IslandManagement;

/// <summary>
/// The Permissões tab: what each person may do on the island besides coming in. The permissions offered and the roles come from the server (only what it really checks). Each switch is its own
/// call: it sends the whole set the person should have, and if the server refuses the switch goes back and the reason is shown. Only the owner can touch "administrador".
/// </summary>
public partial class IslandPermissionsPage : ScrollContainer
{
    private sealed record Definition(string Key, string Title, string Text);
    private static readonly (string Text, string[] Permissions)[] AddRoles = [("Moderador", ["ban"]), ("Construtor", ["build", "run_scripts"]), ("Administrador", ["admin"])];

    private List<Definition> _definitions = [];
    private LineEdit _name = null!, _search = null!;
    private OptionButton _role = null!;
    private Button _add = null!, _previous = null!, _next = null!;
    private Label _status = null!, _count = null!, _pageText = null!, _empty = null!;
    private VBoxContainer _rows = null!, _definitionBox = null!, _roleRows = null!;
    private HBoxContainer _head = null!;
    private IslandManagementApi? _api;
    private Guid _island;
    private int _page = 1, _pages = 1, _total;
    private bool _isOwner;
    private List<Guid> _ids = [];
    private readonly Dictionary<Guid, List<string>> _sets = [];

    public event Action<string>? Notify;
    public event Action<Guid>? ViewProfile;
    public Action<string, string, Action>? Ask { get; set; }

    public string StatusForTesting => _status.Text;
    public int TotalForTesting => _total;
    public int RowsForTesting => _rows.GetChildCount();
    public string PageTextForTesting => _pageText.Text;
    public int DefinitionsForTesting => _definitions.Count;
    public string RolesForTesting => string.Join("|", _roleRows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text))));
    public string NameForTesting { get => _name.Text; set => _name.Text = value; }
    public string SearchForTesting { get => _search.Text; set => _search.Text = value; }
    public void SelectRoleForTesting(int index) => _role.Select(index);
    public IReadOnlyList<string> RowTextsForTesting => [.. _rows.GetChildren().OfType<Control>().Select(r => string.Join(" ", r.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text)))];
    public IReadOnlyList<string> SetOfFirstForTesting => _ids.Count == 0 ? [] : _sets[_ids[0]];
    public bool AdminSwitchEnabledForTesting => _rows.GetChildCount() > 0 && _rows.GetChild(0).FindChildren("*", "Button", true, false).OfType<IslandToggle>().Last().Disabled == false;
    public Task ToggleFirstForTesting(string permission, bool on) => _ids.Count == 0 ? Task.CompletedTask : ToggleAsync(_ids[0], permission, on);
    public Task ClearFirstForTesting() => _ids.Count == 0 ? Task.CompletedTask : ClearAsync(_ids[0], "teste");

    public void Bind(IslandManagementApi api) => _api = api;
    public void SetIsland(Guid island) => _island = island;

    public override void _Ready()
    {
        HorizontalScrollMode = ScrollMode.Auto; SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        var column = IslandUi.Column(14); column.SizeFlagsHorizontal = SizeFlags.ExpandFill; AddChild(column);
        var top = IslandUi.Row(14); top.SizeFlagsHorizontal = SizeFlags.ExpandFill; top.CustomMinimumSize = new Vector2(0, 250); column.AddChild(top);
        top.AddChild(BuildDefinitions()); top.AddChild(BuildRoles());
        column.AddChild(BuildList());
    }

    // ---- cards ------------------------------------------------------------------------------------------------------------

    private Control BuildDefinitions()
    {
        var (card, body) = IslandUi.Card("O que se pode permitir", "shield"); card.SizeFlagsStretchRatio = 45; body.AddThemeConstantOverride("separation", 7);
        _definitionBox = IslandUi.Column(7); body.AddChild(_definitionBox);
        body.AddChild(IslandUi.Text("Entrar na ilha é a aba Acesso; banir, a aba Banimentos.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildRoles()
    {
        var (card, body) = IslandUi.Card("Papéis", "people"); card.SizeFlagsStretchRatio = 55;
        var head = IslandUi.Row(10); head.AddChild(Head("Papel", 3)); head.AddChild(Head("Pessoas", 1)); head.AddChild(Head("Permissões", 5)); body.AddChild(head); body.AddChild(IslandUi.Divider());
        _roleRows = IslandUi.Column(0); body.AddChild(_roleRows);
        body.AddChild(IslandUi.Text("Papéis são conjuntos prontos: ao escolher um, a pessoa recebe essas permissões. Papéis personalizados ainda não existem.", 13, IslandUi.TextMuted, false, true));
        return card;
    }

    private Control BuildList()
    {
        var (card, body) = IslandUi.Card("Pessoas com Permissões", "user"); card.SizeFlagsVertical = SizeFlags.ExpandFill;
        var add = IslandUi.Row(10); body.AddChild(add);
        _name = IslandUi.Input("Nome da pessoa"); _name.TextSubmitted += _text => _ = AddAsync(); add.AddChild(_name);
        _role = IslandUi.Dropdown(AddRoles.Select((r, i) => (r.Text, i.ToString()))); _role.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; _role.CustomMinimumSize = new Vector2(190, 40); _role.Select(1); add.AddChild(_role);
        _add = IslandUi.Action("Dar permissões", "plus", IslandUi.Kind.Primary, 40); _add.Pressed += () => _ = AddAsync(); add.AddChild(_add);
        var tools = IslandUi.Row(10); body.AddChild(tools);
        _search = IslandUi.Input("Pesquisar na lista…"); _search.ClearButtonEnabled = true; _search.TextSubmitted += _text => Search(); tools.AddChild(_search);
        var find = IslandUi.Action("Pesquisar", "search", IslandUi.Kind.Secondary, 40); find.Pressed += Search; tools.AddChild(find);
        _status = IslandUi.Text("", 14, IslandUi.TextMuted, false, true); body.AddChild(_status);
        _head = IslandUi.Row(10); body.AddChild(_head); body.AddChild(IslandUi.Divider());
        _rows = IslandUi.Column(0); _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill; body.AddChild(_rows);
        _empty = IslandUi.Text("Ninguém além do proprietário tem permissões ainda.", 15, IslandUi.TextMuted); _empty.CustomMinimumSize = new Vector2(0, 60); _empty.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(_empty);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var pager = IslandUi.Row(10); _count = IslandUi.Text("", 14, IslandUi.TextSecondary); _count.SizeFlagsHorizontal = SizeFlags.ExpandFill; pager.AddChild(_count);
        _previous = IslandUi.Action("Anterior", null, IslandUi.Kind.Secondary, 36); _previous.Pressed += () => { if (_page > 1) { _page--; _ = LoadAsync(); } }; pager.AddChild(_previous);
        _pageText = IslandUi.Text("", 14, IslandUi.TextPrimary, true); pager.AddChild(_pageText);
        _next = IslandUi.Action("Próxima", null, IslandUi.Kind.Secondary, 36); _next.Pressed += () => { if (_page < _pages) { _page++; _ = LoadAsync(); } }; pager.AddChild(_next);
        body.AddChild(pager);
        return card;
    }

    private static Label Head(string text, int ratio, bool end = false, float width = 0)
    {
        var label = IslandUi.Text(text, 13, IslandUi.TextMuted, true); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; if (end) label.HorizontalAlignment = HorizontalAlignment.Right;
        if (width > 0) { label.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; label.CustomMinimumSize = new Vector2(width, 0); label.HorizontalAlignment = HorizontalAlignment.Center; }
        return label;
    }

    private void Search() { _page = 1; _ = LoadAsync(); }

    // ---- data -------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync()
    {
        if (_api is null || _island == Guid.Empty) return;
        var reply = await _api.PermsGet(_island, _search.Text.Trim(), _page);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Show(reply.Body);
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static List<string> Strings(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? [.. v.EnumerateArray().Select(x => x.GetString() ?? "")] : [];

    private void Show(JsonElement body)
    {
        _isOwner = body.TryGetProperty("isOwner", out var o) && o.ValueKind == JsonValueKind.True;
        _definitions = body.TryGetProperty("definitions", out var d) && d.ValueKind == JsonValueKind.Array ? [.. d.EnumerateArray().Select(x => new Definition(Str(x, "key"), Str(x, "title"), Str(x, "text")))] : [];
        foreach (var child in _definitionBox.GetChildren()) child.QueueFree();
        foreach (var def in _definitions) { var row = IslandUi.Column(0); row.AddChild(IslandUi.Text(def.Title, 15, IslandUi.TextPrimary, true)); row.AddChild(IslandUi.Text(def.Text, 13, IslandUi.TextSecondary, false, true)); _definitionBox.AddChild(row); }
        foreach (var child in _roleRows.GetChildren()) child.QueueFree();
        if (body.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            foreach (var role in roles.EnumerateArray())
            {
                var keys = Strings(role, "permissions"); var titles = keys.Select(k => _definitions.FirstOrDefault(x => x.Key == k)?.Title ?? k).ToList();
                var row = IslandUi.Row(10); row.CustomMinimumSize = new Vector2(0, 34);
                row.AddChild(Cell(Str(role, "title"), 3, true)); row.AddChild(Cell(Int(role, "people").ToString(), 1)); row.AddChild(Cell(keys.Count == 0 ? "Só visita" : keys.Count == _definitions.Count ? "Tudo" : string.Join(", ", titles), 5, false, IslandUi.TextSecondary));
                _roleRows.AddChild(row);
            }
        BuildHead();

        foreach (var child in _rows.GetChildren()) child.QueueFree();
        _sets.Clear();
        _total = Int(body, "total"); _page = Math.Max(1, Int(body, "page")); var size = Math.Max(1, Int(body, "pageSize") == 0 ? 10 : Int(body, "pageSize")); _pages = Math.Max(1, (_total + size - 1) / size);
        var items = body.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array ? array.EnumerateArray().ToList() : [];
        if (items.Count == 0 && _page > 1) { _page = _pages; _ = LoadAsync(); return; }
        _ids = [];
        foreach (var item in items) { var id = Guid.Parse(Str(item, "userId")); _ids.Add(id); _sets[id] = Strings(item, "permissions"); _rows.AddChild(BuildRow(item, id)); }
        _empty.Visible = items.Count == 0; _empty.Text = _search.Text.Trim().Length > 0 ? "Ninguém na lista com esse nome." : "Ninguém além do proprietário tem permissões ainda.";
        _count.Text = _total == 1 ? "1 pessoa" : $"{_total} pessoas"; _pageText.Text = $"Página {_page} de {_pages}";
        _previous.Disabled = _page <= 1; _next.Disabled = _page >= _pages;
        _role.SetItemDisabled(2, !_isOwner);
    }

    private static Label Cell(string text, int ratio, bool bold = false, Color? color = null)
    {
        var label = IslandUi.Text(text, 14, color ?? IslandUi.TextPrimary, bold); label.SizeFlagsHorizontal = SizeFlags.ExpandFill; label.SizeFlagsStretchRatio = ratio; return label;
    }

    private void BuildHead()
    {
        foreach (var child in _head.GetChildren()) child.QueueFree();
        _head.AddChild(Head("Pessoa", 3)); _head.AddChild(Head("Papel", 2));
        foreach (var def in _definitions) _head.AddChild(Head(def.Title, 1, false, 84));
        _head.AddChild(Head("Ações", 1, true));
    }

    private static string RoleTitle(string role) => role switch { "admin" => "Administrador", "moderator" => "Moderador", "builder" => "Construtor", "visitor" => "Visitante", _ => "Personalizado" };

    private Control BuildRow(JsonElement item, Guid id)
    {
        var name = Str(item, "name"); var role = Str(item, "role"); var held = Strings(item, "permissions");
        var lockedByAdmin = role == "admin" && !_isOwner;
        var box = new PanelContainer { CustomMinimumSize = new Vector2(0, 50) }; box.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(0, 0, 0, 0), null, 0, 0, 0, 0));
        var column = IslandUi.Column(0); box.AddChild(column);
        var row = IslandUi.Row(10); row.CustomMinimumSize = new Vector2(0, 49); column.AddChild(row);
        var who = IslandUi.Row(9); who.SizeFlagsHorizontal = SizeFlags.ExpandFill; who.SizeFlagsStretchRatio = 3; who.AddChild(UiStyle.IconRect("avatar", IslandUi.TextSecondary, 22)); who.AddChild(IslandUi.Text(name, 15, IslandUi.TextPrimary, true)); row.AddChild(who);
        var roleHolder = IslandUi.Row(0); roleHolder.SizeFlagsHorizontal = SizeFlags.ExpandFill; roleHolder.SizeFlagsStretchRatio = 2; var pill = IslandUi.Pill(RoleTitle(role), role == "admin" ? IslandUi.AccentSecondary : role == "custom" ? IslandUi.TextMuted : IslandUi.AccentPrimary); pill.SizeFlagsHorizontal = SizeFlags.ShrinkBegin; roleHolder.AddChild(pill); row.AddChild(roleHolder);
        foreach (var def in _definitions)
        {
            var toggle = new IslandToggle(); toggle.SetPressedNoSignal(held.Contains(def.Key)); toggle.TooltipText = def.Text;
            toggle.Disabled = lockedByAdmin || (def.Key == "admin" && !_isOwner);
            var key = def.Key; toggle.Toggled += on => _ = ToggleAsync(id, key, on);
            var holder = new CenterContainer { CustomMinimumSize = new Vector2(84, 0), SizeFlagsHorizontal = SizeFlags.ShrinkCenter }; holder.AddChild(toggle); row.AddChild(holder);
        }
        var actions = IslandUi.Row(6); actions.SizeFlagsHorizontal = SizeFlags.ExpandFill; actions.SizeFlagsStretchRatio = 1; actions.Alignment = BoxContainer.AlignmentMode.End;
        var profile = IslandUi.IconButton("user", "Ver perfil", 34); profile.Pressed += () => ViewProfile?.Invoke(id); actions.AddChild(profile);
        var remove = IslandUi.IconButton("trash", "Tirar todas as permissões", 34); remove.Disabled = lockedByAdmin;
        remove.Pressed += () => { if (Ask is { } ask) ask($"Tirar todas as permissões de {name}? Ela continua podendo entrar se a ilha permitir.", "Tirar", () => _ = ClearAsync(id, name)); else _ = ClearAsync(id, name); };
        actions.AddChild(remove); row.AddChild(actions);
        column.AddChild(IslandUi.Divider());
        return box;
    }

    // ---- actions ----------------------------------------------------------------------------------------------------------

    public async Task ToggleAsync(Guid user, string permission, bool on)
    {
        if (_api is null || !_sets.TryGetValue(user, out var current)) return;
        var wanted = current.Where(p => p != permission).ToList(); if (on) wanted.Add(permission);
        var reply = await _api.PermsChange(_island, user, wanted);
        if (!reply.Ok) Say(reply.Message, true); else Say("Permissões atualizadas.", false);
        await LoadAsync();
    }

    public async Task AddAsync()
    {
        if (_api is null) return;
        var who = _name.Text.Trim(); if (who.Length == 0) { Say("Digite o nome da pessoa.", true); return; }
        _add.Disabled = true; var reply = await _api.PermsAdd(_island, who, AddRoles[Math.Max(0, _role.Selected)].Permissions); _add.Disabled = false;
        if (!reply.Ok) { Say(reply.Message, true); return; }
        _name.Text = ""; Say($"{who} recebeu as permissões.", false); Notify?.Invoke($"{who} agora tem permissões na ilha."); _page = 1; await LoadAsync();
    }

    public async Task ClearAsync(Guid user, string name)
    {
        if (_api is null) return;
        var reply = await _api.PermsClear(_island, user);
        if (!reply.Ok) { Say(reply.Message, true); return; }
        Say($"{name} ficou sem permissões.", false); await LoadAsync();
    }

    private void Say(string text, bool error) { _status.Text = text; _status.AddThemeColorOverride("font_color", error ? IslandUi.Error : IslandUi.Success); }
}
