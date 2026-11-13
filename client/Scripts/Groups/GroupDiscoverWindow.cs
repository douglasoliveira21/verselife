using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>
/// "Descobrir Grupos": search the public groups by name, sigla or description, narrowed by category, language, how one enters and "só gratuitos". Each result opens the profile of the group, where joining happens.
/// </summary>
public sealed partial class GroupDiscoverWindow : Node
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private FloatingWindow _window = null!;
    private LineEdit _query = null!;
    private OptionButton _category = null!, _language = null!, _policy = null!;
    private CheckBox _free = null!;
    private VBoxContainer _results = null!;
    private Label _note = null!;
    private Button _more = null!;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private GroupProfileApi? _api;
    private int _page, _generation;
    private bool _busy;

    /// <summary>Opens the profile of a group.</summary>
    public Action<Guid>? OpenGroup { get; set; }

    public bool Visible => _window.Visible;
    public Vector2 SizeForTesting => _window.Size;
    public string TextForTesting => string.Join("|", _results.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public string NoteForTesting => _note.Text;
    public bool HasButtonForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text && b.Visible);
    public void PressForTesting(string text) { if (_window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text && b.Visible) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public void FilterForTesting(string query, string category = "", string language = "", string policy = "", bool free = false)
    { _query.Text = query; IslandUi.Select(_category, category); IslandUi.Select(_language, language); IslandUi.Select(_policy, policy); _free.ButtonPressed = free; }
    public void SetTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new GroupProfileApi(new IslandManagementApi(transport)); }
    private GroupProfileApi Api => _api ??= new GroupProfileApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    public override void _Ready() => Build();

    public void Open() { _window.Visible = true; _window.MoveToFront(); _ = SearchAsync(); }
    public async Task OpenAsync() { _window.Visible = true; _window.MoveToFront(); await SearchAsync(); }

    private void Build()
    {
        _window = new FloatingWindow("Descobrir Grupos", icon: "search", minimizable: true) { Visible = false }; _window.Place(.5f, .5f, -330, -310, 330, 310); _window.MakeResizable(); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var column = IslandUi.Column(8); column.SizeFlagsVertical = Control.SizeFlags.ExpandFill; _window.Content.AddChild(column);
        var line = IslandUi.Row(8); column.AddChild(line);
        _query = IslandUi.Input("Nome, sigla ou descrição"); _query.TextSubmitted += x => _ = SearchAsync(); line.AddChild(_query);
        var go = IslandUi.Action("Buscar", "search", IslandUi.Kind.Primary, 40, 16); go.Pressed += () => _ = SearchAsync(); line.AddChild(go);
        var filters = IslandUi.Row(8); column.AddChild(filters);
        _category = IslandUi.Dropdown([("Todas as categorias", ""), .. GroupCreateWindow.CategoryOptions.Select(c => (c.Text, c.Id))]); filters.AddChild(_category);
        _language = IslandUi.Dropdown([("Qualquer idioma", ""), .. GroupCreateWindow.LanguageOptions.Select(c => (c.Text, c.Id))]); filters.AddChild(_language);
        _policy = IslandUi.Dropdown([("Qualquer entrada", ""), ("Entrada livre", "open"), ("Por aprovação", "request"), ("Só por convite", "invite")]); filters.AddChild(_policy);
        _free = new CheckBox { Text = "Só gratuitos", FocusMode = Control.FocusModeEnum.None }; filters.AddChild(_free);
        foreach (var drop in new[] { _category, _language, _policy }) drop.ItemSelected += x => _ = SearchAsync();
        _free.Toggled += x => _ = SearchAsync();
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); column.AddChild(_note);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(560, 300) }; column.AddChild(scroll);
        _results = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _results.AddThemeConstantOverride("separation", 6); scroll.AddChild(_results);
        _more = IslandUi.Action("Mais resultados", null, IslandUi.Kind.Secondary, 36, 16); _more.Visible = false; _more.Pressed += () => _ = MoreAsync(); column.AddChild(_more);
    }

    private async Task SearchAsync() { _page = 0; foreach (var child in _results.GetChildren()) child.QueueFree(); await LoadPageAsync(); }
    private async Task MoreAsync() { _page++; await LoadPageAsync(); }

    private async Task LoadPageAsync()
    {
        var generation = ++_generation; _busy = true; _note.Text = "Buscando…"; _more.Visible = false;
        var reply = await Api.Search(_query.Text.Trim(), _page, IslandUi.IdOf(_category), IslandUi.IdOf(_language), IslandUi.IdOf(_policy), _free.ButtonPressed);
        if (generation != _generation) return; _busy = false;
        if (!reply.Ok) { _note.Text = reply.Status == 0 ? "Sem conexão com o servidor." : GroupProfileWindow.GroupWords(reply); return; }
        var found = reply.Body.ValueKind == JsonValueKind.Array ? reply.Body.EnumerateArray().Select(e => e.Clone()).ToList() : [];
        foreach (var group in found) _results.AddChild(Card(group));
        _more.Visible = found.Count >= 20;
        _note.Text = _results.GetChildCount() == 0 ? "Nenhum grupo encontrado com esses filtros." : $"{_results.GetChildCount()} grupo(s)";
    }

    private static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Long(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var x) ? x : 0;
    private static bool Flag(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;

    private Control Card(JsonElement group)
    {
        var id = Guid.TryParse(Str(group, "id"), out var parsed) ? parsed : Guid.Empty; var tag = Str(group, "tag"); var fee = Long(group, "joinFee"); var policy = Str(group, "joinPolicy");
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; panel.AddThemeStyleboxOverride("panel", IslandUi.Box(new Color(UiStyle.RowFill, .55f), UiStyle.BorderSoft, 12, 1, 12, 8)); panel.SetMeta("group_name", Str(group, "name"));
        var row = IslandUi.Row(10); panel.AddChild(row);
        var text = IslandUi.Column(2); text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(text);
        var title = new Label { Text = tag.Length > 0 ? $"{Str(group, "name")}  [{tag}]" : Str(group, "name") }; title.AddThemeFontSizeOverride("font_size", 15); title.AddThemeFontOverride("font", UiStyle.Medium); text.AddChild(title);
        var category = GroupCreateWindow.CategoryOptions.FirstOrDefault(c => c.Id == Str(group, "category")).Text ?? Str(group, "category");
        var entry = policy switch { "open" => fee > 0 ? $"Entrada livre · L$ {fee.ToString("N0", Pt)}" : "Entrada livre · grátis", "invite" => "Só por convite", _ => "Por aprovação" };
        var meta = new Label { Text = $"{category} · {Long(group, "members")} membro(s) · {entry}", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }; meta.AddThemeFontSizeOverride("font_size", 12); meta.AddThemeColorOverride("font_color", UiStyle.TextMuted); text.AddChild(meta);
        var about = Str(group, "description"); if (about.Length > 0) { var d = new Label { Text = about.Length > 140 ? about[..140] + "…" : about, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) }; d.AddThemeFontSizeOverride("font_size", 12); text.AddChild(d); }
        var view = IslandUi.Action(Flag(group, "isMember") ? "Abrir" : "Ver grupo", null, IslandUi.Kind.Secondary, 34, 16); view.Pressed += () => OpenGroup?.Invoke(id); row.AddChild(view);
        return panel;
    }
}
