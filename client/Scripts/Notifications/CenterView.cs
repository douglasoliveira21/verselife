using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;

namespace LifeVerse.Client.Scripts.Notifications;

/// <summary>
/// The list of the notification center with its tabs (Todas, Grupos, Convites, Sistema): it fetches pages from the server, draws a row for each notification, lets the person answer an invitation
/// with one click and mark things as read. The bell panel uses it compact; the "Ver todas" window uses it with search and the unread filter.
/// </summary>
public partial class CenterView : VBoxContainer
{
    public static readonly (string Id, string Text)[] Tabs = [("todas", "Todas"), ("grupos", "Grupos"), ("convites", "Convites"), ("sistema", "Sistema")];
    /// <summary>What "now" is for the relative times (tests set it).</summary>
    public static Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    private readonly bool _full;
    private NotificationsApi? _api;
    private readonly Dictionary<string, Button> _tabButtons = [];
    private VBoxContainer _list = null!;
    private Label _note = null!, _unreadText = null!;
    private LineEdit? _search; private CheckBox? _unreadOnly;
    private Button _more = null!;
    private string _tab = "todas";
    private readonly List<CenterEntry> _items = [];
    private CenterPage _last = new();
    private bool _busy;
    private int _rows;

    /// <summary>Raised with every page the server sent, so the bell can show the counters.</summary>
    public event Action<CenterPage>? Loaded;
    /// <summary>Raised when the person clicks a notification that belongs to a place (a group, the properties).</summary>
    public Action<CenterEntry>? Open { get; set; }
    /// <summary>Raised with a short sentence about what was done (the main UI shows it as a toast).</summary>
    public Action<string>? Said { get; set; }

    public CenterView(bool full) { _full = full; }

    public CenterPage Last => _last;
    public string TabForTesting => _tab;
    public int RowsForTesting => _rows;
    public IReadOnlyList<CenterEntry> ItemsForTesting => _items;
    public string NoteForTesting => _note.Text;
    public bool MoreVisibleForTesting => _more.Visible;
    public string TextForTesting => string.Join("|", FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public bool HasButtonForTesting(string text) => FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text || b.TooltipText == text);
    public void PressForTesting(string textOrTip) { if (FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == textOrTip || b.TooltipText == textOrTip) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public void SearchForTesting(string text) { if (_search is not null) { _search.Text = text; _ = ReloadAsync(); } }

    public void Bind(NotificationsApi api) => _api = api;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 2); AddChild(tabs);
        foreach (var (id, text) in Tabs)
        {
            var tab = id; var button = new Button { Text = text, Flat = true, ToggleMode = true, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30) };
            button.AddThemeFontSizeOverride("font_size", 13); button.Pressed += () => { _tab = tab; Highlight(); _ = ReloadAsync(); };
            tabs.AddChild(button); _tabButtons[id] = button;
        }
        if (_full)
        {
            var filters = new HBoxContainer(); filters.AddThemeConstantOverride("separation", 10); AddChild(filters);
            _search = new LineEdit { PlaceholderText = "Pesquisar notificações…", ClearButtonEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 32) };
            _search.TextSubmitted += typed => _ = ReloadAsync(); filters.AddChild(_search);
            _unreadOnly = new CheckBox { Text = "Só não lidas", FocusMode = FocusModeEnum.None }; _unreadOnly.Toggled += on => _ = ReloadAsync(); filters.AddChild(_unreadOnly);
        }
        _unreadText = new Label { Visible = false }; _unreadText.AddThemeFontSizeOverride("font_size", 12); _unreadText.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_unreadText);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 120) }; AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; _list.AddThemeConstantOverride("separation", 6); scroll.AddChild(_list);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(260, 0) }; _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); AddChild(_note);
        _more = new Button { Text = "Carregar mais", Visible = false, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 32) }; _more.Pressed += () => _ = LoadMoreAsync(); AddChild(_more);
        Highlight();
    }

    private void Highlight()
    {
        foreach (var (id, button) in _tabButtons)
        {
            var on = id == _tab; button.ButtonPressed = on;
            var unread = id == "todas" ? 0 : _last.UnreadOf(id);
            button.Text = Tabs.First(t => t.Id == id).Text + (unread > 0 ? $" ({unread})" : "");
            var line = UiStyle.Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 0, 6, 4); line.BorderWidthBottom = on ? 2 : 0; line.BorderColor = UiStyle.Accent;
            foreach (var state in new[] { "normal", "pressed", "hover", "focus", "hover_pressed" }) button.AddThemeStyleboxOverride(state, line);
            button.AddThemeColorOverride("font_color", on ? UiStyle.Accent : UiStyle.TextMuted); button.AddThemeColorOverride("font_pressed_color", UiStyle.Accent); button.AddThemeColorOverride("font_hover_color", UiStyle.Text);
        }
    }

    public async Task ReloadAsync()
    {
        if (_api is null || _busy) return;
        _busy = true; _note.Text = _items.Count == 0 ? "Carregando…" : "";
        var reply = await _api.List(_tab, _unreadOnly?.ButtonPressed ?? false, _search?.Text, 0, 20); _busy = false;
        if (!reply.Ok || CenterPage.Parse(reply.Body) is not { } page) { if (_items.Count == 0) _note.Text = reply.Status == 0 ? "Sem conexão com o servidor." : reply.Status == 404 ? "O servidor ainda não tem a central de notificações." : LandManagementApi.Words(reply); return; }
        _items.Clear(); _items.AddRange(page.Items); Apply(page);
    }

    public async Task LoadMoreAsync()
    {
        if (_api is null || _busy || !_last.HasMore) return;
        _busy = true; var reply = await _api.List(_tab, _unreadOnly?.ButtonPressed ?? false, _search?.Text, _items.Count, 20); _busy = false;
        if (!reply.Ok || CenterPage.Parse(reply.Body) is not { } page) { Said?.Invoke(LandManagementApi.Words(reply)); return; }
        _items.AddRange(page.Items.Where(i => !_items.Any(o => o.Source == i.Source && o.Id == i.Id))); Apply(page);
    }

    private void Apply(CenterPage page)
    {
        _last = page; Highlight(); Render();
        var unread = page.UnreadOf(_tab); _unreadText.Visible = unread > 0; _unreadText.Text = unread == 1 ? "1 não lida" : $"{unread} não lidas";
        Loaded?.Invoke(page);
    }

    private void Render()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        _rows = _items.Count; var now = Now();
        foreach (var entry in _items) _list.AddChild(Row(entry, now));
        _note.Text = _items.Count == 0 ? (_tab == "convites" ? "Nenhum convite esperando." : "Nenhuma notificação.") : "";
        _more.Visible = _last.HasMore;
    }

    private static (string Icon, Color Color) Look(CenterEntry e) => e.Category switch
    {
        "convites" => ("mail", UiStyle.Accent), "grupos" => ("people", UiStyle.Accent), _ => (e.Kind.Contains("fail", StringComparison.Ordinal) || e.Kind.Contains("grace", StringComparison.Ordinal) ? "alert" : "bell", e.Kind.Contains("fail", StringComparison.Ordinal) || e.Kind.Contains("grace", StringComparison.Ordinal) ? UiStyle.Warning : UiStyle.Orange)
    };

    private Control Row(CenterEntry e, DateTimeOffset now)
    {
        var (icon, color) = Look(e);
        var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = e.CanAnswer ? CursorShape.Arrow : CursorShape.PointingHand };
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(e.Read ? new Color(UiStyle.RowFill, .5f) : new Color(UiStyle.AccentFill, .35f), e.Read ? UiStyle.BorderSoft : new Color(color, .6f), 10, 1, 10, 8));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        row.AddChild(UiStyle.IconRect(icon, color, 22));
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 2); row.AddChild(text);
        var title = new Label { Text = e.Title, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(150, 0) }; title.AddThemeFontSizeOverride("font_size", 14); title.AddThemeFontOverride("font", UiStyle.Medium);
        title.AddThemeColorOverride("font_color", e.Read ? UiStyle.TextMuted : UiStyle.Text); text.AddChild(title);
        if (e.Body.Length > 0) { var body = new Label { Text = e.Body, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(150, 0) }; body.AddThemeFontSizeOverride("font_size", 12); body.AddThemeColorOverride("font_color", UiStyle.TextMuted); text.AddChild(body); }
        var when = new Label { Text = RelativeTime.Of(e.At, now) }; when.AddThemeFontSizeOverride("font_size", 11); when.AddThemeColorOverride("font_color", UiStyle.TextDisabled); text.AddChild(when);
        if (!e.Read && !e.CanAnswer) { var dot = new Panel { CustomMinimumSize = new Vector2(8, 8), SizeFlagsVertical = SizeFlags.ShrinkCenter }; dot.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Orange, UiStyle.Orange, 4, 0, 0, 0)); row.AddChild(dot); }
        if (e.CanAnswer)
        {
            row.AddChild(RoundButton("check", UiStyle.SuccessBright, "Aceitar", () => _ = AnswerAsync(e, true)));
            row.AddChild(RoundButton("close", UiStyle.Danger, "Recusar", () => _ = AnswerAsync(e, false)));
        }
        else card.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _ = ClickAsync(e); };
        return card;
    }

    private static Button RoundButton(string icon, Color color, string tip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, color, 18), TooltipText = tip, CustomMinimumSize = new Vector2(34, 34), SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None, ExpandIcon = false };
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(new Color(color, .14f), color, 17, 1, 0, 0)); button.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(color, .3f), color, 17, 1, 0, 0)); button.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color(color, .45f), color, 17, 1, 0, 0));
        button.Pressed += action; return button;
    }

    /// <summary>Accepting goes through the same checks as any other way into a group; the server may refuse (banned, full…) and then the invitation stays.</summary>
    public async Task<bool> AnswerAsync(CenterEntry entry, bool accept)
    {
        if (_api is null || !entry.CanAnswer) return false;
        var reply = await _api.AnswerInvite(entry.Id, accept);
        if (!reply.Ok) { Said?.Invoke(GroupWords(reply)); return false; }
        Said?.Invoke(accept ? $"Você entrou em {(entry.GroupName.Length > 0 ? entry.GroupName : "o grupo")}." : "Convite recusado.");
        await ReloadAsync(); return true;
    }

    private static string GroupWords(IslandReply reply)
    {
        var code = reply.Body.ValueKind == System.Text.Json.JsonValueKind.Object && reply.Body.TryGetProperty("error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() ?? "" : "";
        return code switch
        {
            "not_found" => "Esse convite não existe mais.", "banned" => "Você foi banido desse grupo.", "group_full" => "O grupo está cheio.", "too_many_groups" => "Você já está em grupos demais.",
            "already_member" => "Você já faz parte desse grupo.", _ => reply.Message.Length > 0 ? reply.Message : "Não foi possível responder ao convite."
        };
    }

    private async Task ClickAsync(CenterEntry entry)
    {
        if (!entry.Read && _api is not null && (await _api.MarkRead([entry], false)).Ok) { var at = _items.FindIndex(i => i.Source == entry.Source && i.Id == entry.Id); if (at >= 0) _items[at] = entry with { Read = true }; await ReloadAsync(); }
        Open?.Invoke(entry);
    }

    public async Task MarkAllReadAsync()
    {
        if (_api is null) return;
        var reply = await _api.MarkRead(null, true, _tab); if (!reply.Ok) { Said?.Invoke(LandManagementApi.Words(reply)); return; }
        await ReloadAsync();
    }

    public Task ClickForTesting(CenterEntry entry) => ClickAsync(entry);
}
