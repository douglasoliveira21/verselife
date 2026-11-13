using Godot;
using LifeVerse.Client.Scripts.IslandManagement;
using LifeVerse.Client.Scripts.LandManagement;
using LifeVerse.Client.Scripts.Notifications;

namespace LifeVerse.Client.Scripts.Groups;

/// <summary>
/// "Grupos": the groups of the person on the left (search, cards, Criar Grupo and Procurar Grupos) and the summary of the chosen one on the right (picture, identity, Ativar, Chat, Informações and the latest announcements).
/// It shows only what the server says. Chat and Informações open the administration window of the group; wearing a group (Ativar) is done here.
/// </summary>
public sealed partial class GroupsHubWindow : Node
{
    /// <summary>Text and icons on the cyan buttons: dark, so they read on the bright fill.</summary>
    private static readonly Color OnAccent = new("#0A1A1F");
    public static readonly string[] InfoTabs = ["Painel", "Chat", "Anúncios"];

    private FloatingWindow _window = null!;
    private GroupsHubApi? _api;
    private Func<string, string, string?, Task<(long, string)>>? _transport;
    private LineEdit _search = null!;
    private Label _count = null!, _note = null!;
    private VBoxContainer _cards = null!, _summary = null!, _noticeList = null!;
    private Control _detail = null!, _empty = null!;
    private Button _activate = null!, _chat = null!, _info = null!;
    private Label _name = null!, _meta = null!, _description = null!, _noticeNote = null!;
    private PanelContainer _cover = null!, _tile = null!;
    private TextureRect _coverIcon = null!, _tileIcon = null!;
    private readonly List<HubGroup> _groups = [];
    private IReadOnlyDictionary<Guid, int> _unread = new Dictionary<Guid, int>();
    private IReadOnlyList<HubNotice> _notices = [];
    private Guid? _selected;
    private bool _busy;
    private double _refreshIn = 30;

    /// <summary>Opens the administration window of the group on a tab (0 Painel, 1 Chat, 2 Anúncios).</summary>
    public Action<Guid, int>? OpenGroup { get; set; }
    public Action? CreateGroup { get; set; }
    public Action? FindGroups { get; set; }
    public Action<string>? Said { get; set; }

    public bool Visible => _window.Visible;
    public Guid? Selected => _selected;
    public IReadOnlyList<HubGroup> Groups => _groups;
    public string TextForTesting => string.Join("|", _window.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
    public IReadOnlyList<string> CardNamesForTesting => [.. _cards.GetChildren().OfType<Control>().Where(c => c.Visible).Select(c => c.GetMeta("group_name", "").AsString())];
    public bool ButtonEnabledForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { Disabled: false };
    public bool HasButtonForTesting(string text) => _window.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text.Trim() == text && b.Visible);
    public void PressForTesting(string text) { if (_window.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text.Trim() == text) is { } b) b.EmitSignal(BaseButton.SignalName.Pressed); }
    public void SearchForTesting(string text) { _search.Text = text; Render(); }
    public Task SelectForTesting(Guid id) => Select(id);
    public Vector2 SizeForTesting => _window.Size;

    public void UseTransportForTesting(Func<string, string, string?, Task<(long, string)>> transport) { _transport = transport; _api = new GroupsHubApi(new IslandManagementApi(transport)); }
    private GroupsHubApi Api => _api ??= new GroupsHubApi(new IslandManagementApi(_transport ?? ScriptApi.HttpTransport(this)));

    public override void _Ready() => Build();

    public void Toggle() { if (_window.Visible) _window.Visible = false; else Open(); }
    public void Open() { _window.Visible = true; _window.MoveToFront(); _ = LoadAsync(); }
    public async Task OpenAsync() { _window.Visible = true; _window.MoveToFront(); await LoadAsync(); }

    public override void _Process(double delta)
    {
        if (_window is null || !_window.Visible) return;
        _refreshIn -= delta; if (_refreshIn > 0) return; _refreshIn = 30; _ = LoadAsync(true); // what changes (a new member, an announcement) shows up while it is open
    }

    private void Build()
    {
        _window = new FloatingWindow("Grupos", icon: "people", minimizable: true) { Visible = false }; _window.Place(.5f, .5f, -360, -310, 360, 310); _window.MakeResizable(); _window.CloseRequested += () => _window.Visible = false; AddChild(_window);
        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; row.AddThemeConstantOverride("separation", 12); _window.Content.AddChild(row);

        // ---- left: my groups ----
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(236, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 36 }; left.AddThemeConstantOverride("separation", 8); row.AddChild(left);
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 6); left.AddChild(head);
        var title = new Label { Text = "Meus Grupos" }; title.AddThemeFontSizeOverride("font_size", 15); title.AddThemeFontOverride("font", UiStyle.Medium); head.AddChild(title);
        _count = new Label { Text = "" }; _count.AddThemeFontSizeOverride("font_size", 12); _count.AddThemeColorOverride("font_color", UiStyle.TextMuted); head.AddChild(_count);
        _search = new LineEdit { PlaceholderText = "Buscar nos meus grupos...", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(0, 32), RightIcon = UiStyle.Icon("search", UiStyle.TextMuted, 16) }; _search.TextChanged += _ => Render(); left.AddChild(_search);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 160) }; left.AddChild(scroll);
        _cards = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _cards.AddThemeConstantOverride("separation", 6); scroll.AddChild(_cards);
        _note = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(200, 0) }; _note.AddThemeColorOverride("font_color", UiStyle.TextMuted); left.AddChild(_note);
        var create = new Button { Text = "Criar Grupo", Icon = UiStyle.Icon("plus", OnAccent, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) }; Primary(create); create.Pressed += () => CreateGroup?.Invoke(); left.AddChild(create);
        var find = new Button { Text = "Procurar Grupos", Icon = UiStyle.Icon("search", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38) }; find.Pressed += () => FindGroups?.Invoke(); left.AddChild(find);

        // ---- right: summary ----
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsStretchRatio = 64 }; right.AddThemeConstantOverride("separation", 10); row.AddChild(right);
        _empty = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(260, 0) }; ((Label)_empty).AddThemeColorOverride("font_color", UiStyle.TextMuted); right.AddChild(_empty);
        _detail = new VBoxContainer { Visible = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; ((VBoxContainer)_detail).AddThemeConstantOverride("separation", 10); right.AddChild(_detail);
        _cover = new PanelContainer { CustomMinimumSize = new Vector2(0, 130) }; _coverIcon = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(56, 56), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; _cover.AddChild(_coverIcon); _detail.AddChild(_cover);
        var identity = new HBoxContainer(); identity.AddThemeConstantOverride("separation", 12); _detail.AddChild(identity);
        _tile = new PanelContainer { CustomMinimumSize = new Vector2(64, 64), SizeFlagsVertical = Control.SizeFlags.ShrinkBegin }; _tileIcon = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(34, 34), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; _tile.AddChild(_tileIcon); identity.AddChild(_tile);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; text.AddThemeConstantOverride("separation", 2); identity.AddChild(text);
        _name = new Label(); _name.AddThemeFontSizeOverride("font_size", 18); _name.AddThemeFontOverride("font", UiStyle.Medium); text.AddChild(_name);
        _meta = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) }; _meta.AddThemeFontSizeOverride("font_size", 12); _meta.AddThemeColorOverride("font_color", UiStyle.TextMuted); text.AddChild(_meta);
        _description = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) }; _description.AddThemeFontSizeOverride("font_size", 13); text.AddChild(_description);
        var actions = new HBoxContainer(); actions.AddThemeConstantOverride("separation", 8); _detail.AddChild(actions);
        _activate = new Button { Text = "Ativar", Icon = UiStyle.Icon("check", OnAccent, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; Primary(_activate); _activate.Pressed += () => _ = ToggleActiveAsync(); actions.AddChild(_activate);
        _chat = new Button { Text = "Chat", Icon = UiStyle.Icon("chat", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _chat.Pressed += () => { if (_selected is { } g) OpenGroup?.Invoke(g, 1); }; actions.AddChild(_chat);
        _info = new Button { Text = "Informações", Icon = UiStyle.Icon("info", UiStyle.Text, 18), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; _info.Pressed += () => { if (_selected is { } g) OpenGroup?.Invoke(g, 0); }; actions.AddChild(_info);
        var recent = new Label { Text = "Avisos recentes" }; recent.AddThemeFontSizeOverride("font_size", 14); recent.AddThemeFontOverride("font", UiStyle.Medium); _detail.AddChild(recent);
        _noticeList = new VBoxContainer(); _noticeList.AddThemeConstantOverride("separation", 4); _detail.AddChild(_noticeList);
        _noticeNote = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart }; _noticeNote.AddThemeFontSizeOverride("font_size", 12); _noticeNote.AddThemeColorOverride("font_color", UiStyle.TextMuted); _detail.AddChild(_noticeNote);
        Render();
    }

    private static void Primary(Button button)
    {
        button.AddThemeStyleboxOverride("normal", UiStyle.Box(UiStyle.Accent, UiStyle.Accent, 8, 1, 12, 6)); button.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color("#40FFF0"), new Color("#40FFF0"), 8, 1, 12, 6)); button.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color("#0DC2B4"), new Color("#0DC2B4"), 8, 1, 12, 6));
        button.AddThemeStyleboxOverride("disabled", UiStyle.Box(UiStyle.ButtonFillDisabled, UiStyle.Border, 8, 1, 12, 6)); foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" }) button.AddThemeColorOverride(c, OnAccent);
    }

    private static string IconOf(string emblem) => UiIconData.Icons.ContainsKey(emblem == "coin" ? "coins" : emblem) ? (emblem == "coin" ? "coins" : emblem) : "people";

    // ---- loading ----

    public async Task LoadAsync(bool quiet = false)
    {
        if (_busy) return; _busy = true; if (!quiet && _groups.Count == 0) _note.Text = "Carregando…";
        var mine = await Api.Mine(); var unread = await Api.Unread(); _busy = false;
        if (!mine.Ok) { if (!quiet || _groups.Count == 0) _note.Text = mine.Status == 0 ? "Sem conexão com o servidor." : mine.Status == 404 ? "O servidor ainda não tem os grupos." : LandManagementApi.Words(mine); return; }
        var list = GroupsHubParse.Groups(mine.Body).Where(g => true).ToList(); _unread = unread.Ok ? GroupsHubParse.Unread(unread.Body) : _unread;
        _groups.Clear(); _groups.AddRange(list);
        if (_selected is { } keep && !_groups.Any(g => g.Id == keep)) _selected = null;
        _selected ??= _groups.FirstOrDefault(g => g.IsActive)?.Id ?? _groups.FirstOrDefault()?.Id;
        Render(); if (_selected is { } sel) await LoadNoticesAsync(sel);
    }

    private async Task Select(Guid id) { _selected = id; Render(); await LoadNoticesAsync(id); }

    private async Task LoadNoticesAsync(Guid id)
    {
        var reply = await Api.Notices(id); if (_selected != id) return;
        _notices = reply.Ok ? [.. GroupsHubParse.Notices(reply.Body).OrderByDescending(n => n.Pinned).ThenByDescending(n => n.At).Take(3)] : [];
        _noticeNote.Text = !reply.Ok ? (reply.Status == 0 ? "Sem conexão com o servidor." : "Não foi possível ler os avisos.") : _notices.Count == 0 ? "Nenhum aviso publicado." : "";
        RenderNotices();
    }

    private async Task ToggleActiveAsync()
    {
        if (_selected is not { } id) return; var group = _groups.First(g => g.Id == id);
        var reply = group.IsActive ? await Api.Deactivate() : await Api.Activate(id);
        if (!reply.Ok) { Said?.Invoke(LandManagementApi.Words(reply)); return; }
        GetNodeOrNull<LifeVerse.Client.Autoloads.AvatarManager>("/root/AvatarManager")?.TellTheIsland(); // the island shows the new title over the avatar
        Said?.Invoke(group.IsActive ? "Você não usa mais nenhum grupo." : $"Grupo ativo: {group.Name}."); await LoadAsync(true);
    }

    // ---- drawing ----

    private void Render()
    {
        if (_cards is null) return;
        foreach (var child in _cards.GetChildren()) child.QueueFree();
        var query = _search.Text.Trim(); var shown = _groups.Where(g => query.Length == 0 || g.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || g.Tag.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _count.Text = _groups.Count == 0 ? "" : $"({_groups.Count})";
        foreach (var group in shown) _cards.AddChild(Card(group));
        _note.Text = _groups.Count == 0 ? "" : shown.Count == 0 ? "Nenhum grupo encontrado." : "";
        var any = _groups.Count > 0; _detail.Visible = any && _selected is not null; _empty.Visible = !_detail.Visible;
        ((Label)_empty).Text = !any ? "Você ainda não participa de nenhum grupo.\nCrie um ou procure um para entrar." : "Escolha um grupo na lista para ver o resumo dele.";
        if (_selected is { } id && _groups.FirstOrDefault(g => g.Id == id) is { } selected) Fill(selected);
    }

    private Control Card(HubGroup group)
    {
        var on = group.Id == _selected; var colour = new Color(group.AccentColor);
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(0, 68), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; card.SetMeta("group_name", group.Name);
        card.AddThemeStyleboxOverride("panel", UiStyle.Box(on ? new Color(UiStyle.AccentFill, .6f) : new Color(UiStyle.RowFill, .6f), on ? UiStyle.Accent : UiStyle.BorderSoft, 12, on ? 2 : 1, 10, 8));
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 10); card.AddChild(row);
        var tile = new PanelContainer { CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; tile.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(colour, .22f), new Color(colour, .7f), 10, 1, 0, 0));
        tile.AddChild(new TextureRect { Texture = UiStyle.Icon(IconOf(group.Emblem), colour, 24), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(24, 24), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }); row.AddChild(tile);
        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; text.AddThemeConstantOverride("separation", 1); row.AddChild(text);
        var name = new Label { Text = group.Name, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; name.AddThemeFontSizeOverride("font_size", 14); name.AddThemeFontOverride("font", UiStyle.Medium); text.AddChild(name);
        var role = new Label { Text = group.Title.Length > 0 ? group.Title : "Membro" }; role.AddThemeFontSizeOverride("font_size", 12); role.AddThemeColorOverride("font_color", UiStyle.Accent); text.AddChild(role);
        var members = new Label { Text = group.MembersText }; members.AddThemeFontSizeOverride("font_size", 11); members.AddThemeColorOverride("font_color", UiStyle.TextMuted); text.AddChild(members);
        var side = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; side.AddThemeConstantOverride("separation", 4); row.AddChild(side);
        if (group.IsActive) { var active = new Label { Text = "Ativo", HorizontalAlignment = HorizontalAlignment.Center }; active.AddThemeFontSizeOverride("font_size", 10); active.AddThemeColorOverride("font_color", OnAccent); var chip = new PanelContainer(); chip.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Accent, UiStyle.Accent, 8, 0, 6, 1)); chip.AddChild(active); side.AddChild(chip); }
        if (_unread.GetValueOrDefault(group.Id) is var unread and > 0) { var pill = new Label { Text = unread > 99 ? "99+" : unread.ToString(), HorizontalAlignment = HorizontalAlignment.Center, TooltipText = "Avisos novos" }; pill.AddThemeFontSizeOverride("font_size", 10); pill.AddThemeColorOverride("font_color", Colors.White); var badge = new PanelContainer(); badge.AddThemeStyleboxOverride("panel", UiStyle.Box(UiStyle.Orange, UiStyle.Orange, 8, 0, 6, 1)); badge.AddChild(pill); side.AddChild(badge); }
        card.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) _ = Select(group.Id); };
        return card;
    }

    private void Fill(HubGroup group)
    {
        var colour = new Color(group.AccentColor);
        _cover.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(colour, .18f), new Color(colour, .5f), 12, 1, 0, 0)); _coverIcon.Texture = UiStyle.Icon(IconOf(group.Emblem), new Color(colour, .85f), 56);
        _tile.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(colour, .22f), colour, 14, 2, 0, 0)); _tileIcon.Texture = UiStyle.Icon(IconOf(group.Emblem), colour, 34);
        _name.Text = group.Tag.Length > 0 ? $"{group.Name}  [{group.Tag}]" : group.Name; _meta.Text = $"{group.MembersText} · {group.PrivacyText} · {group.JoinText}" + (group.Title.Length > 0 ? $" · seu cargo: {group.Title}" : "");
        _description.Text = group.Description.Length > 0 ? group.Description : "Este grupo ainda não tem descrição.";
        _activate.Text = group.IsActive ? "Desativar" : "Ativar"; _activate.TooltipText = group.IsActive ? "Parar de usar este grupo ao lado do seu nome." : "Usar este grupo ao lado do seu nome.";
        _activate.Icon = UiStyle.Icon(group.IsActive ? "close" : "check", OnAccent, 18);
    }

    private void RenderNotices()
    {
        foreach (var child in _noticeList.GetChildren()) child.QueueFree(); var now = CenterView.Now();
        foreach (var notice in _notices)
        {
            var line = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand }; line.AddThemeStyleboxOverride("panel", UiStyle.Box(new Color(UiStyle.RowFill, .5f), UiStyle.BorderSoft, 8, 1, 10, 6));
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); line.AddChild(h); h.AddChild(UiStyle.IconRect(notice.Pinned ? "star" : "bell", UiStyle.Orange, 18));
            var title = new Label { Text = notice.Title, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis }; title.AddThemeFontSizeOverride("font_size", 13); h.AddChild(title);
            var when = new Label { Text = RelativeTime.Of(notice.At, now) }; when.AddThemeFontSizeOverride("font_size", 11); when.AddThemeColorOverride("font_color", UiStyle.TextMuted); h.AddChild(when);
            line.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _selected is { } g) OpenGroup?.Invoke(g, 2); };
            _noticeList.AddChild(line);
        }
    }
}
