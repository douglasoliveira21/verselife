using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The People window (client/Assets/ui/04): tabs Buscar / Amigos / Grupos, friends as rows with avatar and status dot, friend
/// requests as cards with Aceitar / Recusar. Requests go out one at a time; every change reloads the lists so the window
/// always shows what the server holds.
public partial class PeoplePanel : Node
{
    [Signal] public delegate void MessageEventHandler(string text);
    private enum Kind { Search, Requests, Friends, Groups, Invites, Action }
    private sealed record Pending(Kind Kind, Godot.HttpClient.Method Method, string Path, string? Body, string Success);

    private readonly Queue<Pending> _queue = new();
    private Pending? _current;
    private HttpRequest? _http;
    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!;
    private LineEdit _query = null!, _groupName = null!;
    private RowList _results = null!, _requests = null!, _friends = null!, _invites = null!, _groups = null!;
    private Label _friendsTitle = null!, _requestsTitle = null!;
    private Button _searchAction = null!, _friendRemove = null!, _groupLeave = null!;
    private readonly List<Control> _pages = [];
    private readonly List<(Guid Id, string Relation)> _resultRows = [];
    private readonly List<(Guid Id, string Direction)> _requestRows = [];
    private readonly List<Guid> _friendRows = [], _inviteRows = [], _groupRows = [];

    /// Tests set this to capture requests instead of sending them.
    public Func<Godot.HttpClient.Method, string, string?, bool>? RequestSink { get; set; }
    /// Raised by the chat button on a friend row.
    public event Action<Guid, string>? ChatRequested;
    public bool Visible => _window.Visible;
    public RowList ResultsForTesting => _results;
    public RowList RequestsForTesting => _requests;
    public RowList FriendsForTesting => _friends;
    public RowList InvitesForTesting => _invites;
    public RowList GroupsForTesting => _groups;
    public LineEdit QueryForTesting => _query;

    public override void _Ready() => Build();

    public void AttachTo(Control root) { root.AddChild(_window); }

    /// Opens the window on the search tab with a name already searched (the radial menu's "Adicionar amigo").
    public void SearchFor(string name)
    {
        _window.Visible = true; _window.MoveToFront(); _tabs.Select(0); _query.Text = name; Reload(); Search();
    }

    public void Toggle()
    {
        _window.Visible = !_window.Visible;
        if (_window.Visible) { _window.MoveToFront(); Reload(); }
    }

    // ---- layout ----------------------------------------------------------------------------------------------

    private void Build()
    {
        _window = new FloatingWindow("Pessoas", icon: "people", minimizable: true) { Visible = false };
        _window.Place(.5f, .5f, -280, -300, 280, 300);
        _window.Content.AddThemeConstantOverride("separation", 10);
        var tabs = _tabs = new UiTabs { EqualWidth = true };
        tabs.AddTab("Buscar"); tabs.AddTab("Amigos"); tabs.AddTab("Grupos");
        _window.Content.AddChild(tabs);
        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 470) };
        _window.Content.AddChild(stack);

        _pages.Add(Page(stack, BuildSearch())); _pages.Add(Page(stack, BuildFriends())); _pages.Add(Page(stack, BuildGroups()));
        tabs.TabChanged += index => { for (var i = 0; i < _pages.Count; i++) _pages[i].Visible = i == index; };
        tabs.Select(1);
        UpdateButtons();
    }

    private static Control Page(Control stack, Control content)
    {
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, Visible = false };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);
        stack.AddChild(scroll);
        return scroll;
    }

    private Control BuildSearch()
    {
        var box = Column();
        _query = new LineEdit { PlaceholderText = "Nome do jogador (2 letras ou mais)", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(0, 44) };
        _query.TextSubmitted += _ => Search();
        box.AddChild(_query);
        var go = new Button { Text = "Buscar", Icon = UiStyle.Icon("search", UiStyle.Text, 20), CustomMinimumSize = new Vector2(0, 44) };
        UiStyle.StyleButton(go, UiStyle.ButtonKind.Primary); go.Pressed += Search; box.AddChild(go);
        _results = new RowList(); box.AddChild(_results);
        _results.SelectionChanged += _ => UpdateButtons();
        _searchAction = new Button { Text = "Adicionar amigo", Disabled = true, CustomMinimumSize = new Vector2(0, 44) };
        UiStyle.StyleButton(_searchAction, UiStyle.ButtonKind.Secondary); _searchAction.Pressed += SearchAction; box.AddChild(_searchAction);
        return box;
    }

    private Control BuildFriends()
    {
        var box = Column();
        var friends = Section(box, "Meus Amigos", out _friendsTitle);
        _friends = new RowList(); friends.AddChild(_friends);
        _friends.SelectionChanged += _ => UpdateButtons();
        _friendRemove = new Button { Text = "Remover amigo", Disabled = true, CustomMinimumSize = new Vector2(0, 40) };
        UiStyle.StyleButton(_friendRemove, UiStyle.ButtonKind.Secondary); _friendRemove.Pressed += RemoveFriend;
        friends.AddChild(_friendRemove);
        var requests = Section(box, "Solicitações de amizade", out _requestsTitle);
        _requests = new RowList { Gap = 8 }; requests.AddChild(_requests);
        return box;
    }

    private Control BuildGroups()
    {
        var box = Column();
        var invites = Section(box, "Convites", out _);
        _invites = new RowList { Gap = 8 }; invites.AddChild(_invites);
        var groups = Section(box, "Meus grupos", out _);
        _groups = new RowList(); groups.AddChild(_groups);
        _groups.SelectionChanged += _ => UpdateButtons();
        _groupLeave = new Button { Text = "Sair do grupo", Disabled = true, CustomMinimumSize = new Vector2(0, 40) };
        UiStyle.StyleButton(_groupLeave, UiStyle.ButtonKind.Secondary); _groupLeave.Pressed += LeaveGroup;
        groups.AddChild(_groupLeave);
        var create = new HBoxContainer(); create.AddThemeConstantOverride("separation", 8);
        _groupName = new LineEdit { PlaceholderText = "Nome do novo grupo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 44) };
        _groupName.TextSubmitted += _ => CreateGroup();
        create.AddChild(_groupName);
        var make = new Button { Text = "Criar", CustomMinimumSize = new Vector2(90, 44) };
        UiStyle.StyleButton(make, UiStyle.ButtonKind.Primary); make.Pressed += CreateGroup; create.AddChild(make);
        box.AddChild(create);
        return box;
    }

    private static VBoxContainer Column()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        return box;
    }

    /// A collapsible section: a caret and a bold title that fold the content below it.
    private static VBoxContainer Section(Control parent, string title, out Label label)
    {
        var holder = new VBoxContainer(); holder.AddThemeConstantOverride("separation", 4);
        var head = new Button { Flat = true, Alignment = HorizontalAlignment.Left, Text = "  " + title, Icon = UiStyle.Icon("caret-down", UiStyle.Text, 16), FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 36) };
        head.AddThemeFontOverride("font", UiStyle.Bold); head.AddThemeFontSizeOverride("font_size", 20);
        foreach (var name in new[] { "normal", "hover", "pressed", "hover_pressed", "focus" }) head.AddThemeStyleboxOverride(name, new StyleBoxEmpty());
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color" }) head.AddThemeColorOverride(name, UiStyle.Text);
        var body = new VBoxContainer(); body.AddThemeConstantOverride("separation", 6);
        head.Pressed += () => { body.Visible = !body.Visible; head.Icon = UiStyle.Icon(body.Visible ? "caret-down" : "caret-right", UiStyle.Text, 16); };
        holder.AddChild(head); holder.AddChild(body);
        parent.AddChild(holder);
        label = new Label { Text = title }; // the title text lives on the button; this keeps the base text for counters
        head.AddChild(label); label.Visible = false;
        return body;
    }

    private static void SetSectionTitle(Label label, string text)
    {
        label.Text = text;
        if (label.GetParent() is Button head) head.Text = "  " + text;
    }

    // ---- rows ------------------------------------------------------------------------------------------------

    /// A vertical list of selectable rows; the test API mirrors the old ItemList (ItemCount, GetItemText, Select).
    public partial class RowList : VBoxContainer
    {
        private readonly List<(PanelContainer Row, string Text)> _rows = [];
        private int _selected = -1;
        public int Gap { set => AddThemeConstantOverride("separation", value); }
        public event Action<int>? SelectionChanged;
        public int ItemCount => _rows.Count;
        public int SelectedIndex => _selected;
        public string GetItemText(int index) => _rows[index].Text;

        public RowList() { AddThemeConstantOverride("separation", 0); }

        public void Clear()
        {
            foreach (var (row, _) in _rows) { RemoveChild(row); row.QueueFree(); }
            _rows.Clear(); _selected = -1;
        }

        public PanelContainer Add(Control content, string text, bool selectable = true, bool card = false)
        {
            var row = new PanelContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeStyleboxOverride("panel", card ? UiStyle.Box(UiStyle.RowFill, new Color(UiStyle.Border, .7f), UiStyle.Radius, 1, 12, 10) : RowBox(false));
            row.AddChild(content);
            var index = _rows.Count;
            if (selectable) row.GuiInput += input => { if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Select(index); };
            _rows.Add((row, text)); AddChild(row);
            return row;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _rows.Count) return;
            _selected = index;
            for (var i = 0; i < _rows.Count; i++) _rows[i].Row.AddThemeStyleboxOverride("panel", RowBox(i == index));
            SelectionChanged?.Invoke(index);
        }

        private static StyleBoxFlat RowBox(bool selected) => new()
        {
            BgColor = selected ? new Color(UiStyle.AccentFill, .65f) : new Color(0, 0, 0, 0),
            BorderColor = selected ? UiStyle.Accent : new Color(UiStyle.Divider, .7f),
            BorderWidthBottom = 1, BorderWidthLeft = selected ? 1 : 0, BorderWidthRight = selected ? 1 : 0, BorderWidthTop = selected ? 1 : 0,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusBottomLeft = selected ? 10 : 0, CornerRadiusBottomRight = selected ? 10 : 0, CornerRadiusTopLeft = selected ? 10 : 0, CornerRadiusTopRight = selected ? 10 : 0,
        };
    }

    /// A round avatar placeholder: the player's initial on a colour derived from the name.
    private static Control Avatar(string name, int size)
    {
        var hue = (Math.Abs(name.GetHashCode()) % 360) / 360f;
        var holder = new Panel { CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        holder.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Color.FromHsv(hue, .45f, .62f), BorderColor = new Color(1, 1, 1, .55f), BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, CornerRadiusBottomLeft = size / 2, CornerRadiusBottomRight = size / 2, CornerRadiusTopLeft = size / 2, CornerRadiusTopRight = size / 2, AntiAliasing = true });
        var letter = new Label { Text = name.Length > 0 ? name[..1].ToUpperInvariant() : "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        letter.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        letter.AddThemeFontOverride("font", UiStyle.Bold); letter.AddThemeFontSizeOverride("font_size", size / 2);
        holder.AddChild(letter);
        return holder;
    }

    private sealed partial class Dot : Control
    {
        public Color Color { get; set; }
        public Dot(Color color) { Color = color; CustomMinimumSize = new Vector2(14, 14); SizeFlagsVertical = SizeFlags.ShrinkCenter; MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Draw() => DrawCircle(Size / 2, 6, Color);
    }

    private static Label Text(string text, int size, Color color, bool bold = false)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", bold ? UiStyle.Bold : UiStyle.Regular); label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
        label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return label;
    }

    private static Button IconButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Icon = UiStyle.Icon(icon, UiStyle.Text, 24), Flat = true, TooltipText = tooltip, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var hover = UiStyle.Box(new Color(1, 1, 1, .10f), new Color(0, 0, 0, 0), UiStyle.RadiusSmall, 0, 0, 0);
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty()); button.AddThemeStyleboxOverride("hover", hover); button.AddThemeStyleboxOverride("pressed", hover); button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        button.Pressed += action;
        return button;
    }

    /// avatar · dot · name and subtitle · [action buttons]
    private static HBoxContainer PersonLine(string name, string subtitle, Color? dot, int avatar = 52)
    {
        var line = new HBoxContainer(); line.AddThemeConstantOverride("separation", 12);
        line.AddChild(Avatar(name, avatar));
        if (dot is { } color) line.AddChild(new Dot(color));
        var texts = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        texts.AddThemeConstantOverride("separation", 0);
        texts.AddChild(Text(name, 20, UiStyle.Text, bold: true)); texts.AddChild(Text(subtitle, 17, UiStyle.Text));
        line.AddChild(texts);
        return line;
    }

    private static string FriendSubtitle(JsonElement e)
    {
        if (!e.TryGetProperty("online", out var online) || online.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "Amigo";
        var place = e.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.String ? location.GetString() : null;
        if (online.GetBoolean()) return string.IsNullOrWhiteSpace(place) ? "Online" : $"Online — {place}";
        if (e.TryGetProperty("lastSeen", out var seen) && seen.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(seen.GetString(), out var when)) return $"Offline — {Ago(DateTimeOffset.UtcNow - when)}";
        return "Offline";
    }

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "agora há pouco" : span.TotalHours < 1 ? $"há {(int)span.TotalMinutes} min" : span.TotalDays < 1 ? $"há {(int)span.TotalHours} horas" : $"há {(int)span.TotalDays} dias";

    // ---- actions ---------------------------------------------------------------------------------------------

    public void Reload()
    {
        Send(Kind.Requests, Godot.HttpClient.Method.Get, "/api/v1/social/friends/requests");
        Send(Kind.Friends, Godot.HttpClient.Method.Get, "/api/v1/social/friends");
        Send(Kind.Invites, Godot.HttpClient.Method.Get, "/api/v1/social/groups/invites");
        Send(Kind.Groups, Godot.HttpClient.Method.Get, "/api/v1/social/groups");
    }

    public void Search()
    {
        var text = _query.Text.Trim();
        if (text.Length < 2) { _results.Clear(); _resultRows.Clear(); UpdateButtons(); Say($"Digite pelo menos 2 letras."); return; }
        Send(Kind.Search, Godot.HttpClient.Method.Get, "/api/v1/social/people?q=" + Uri.EscapeDataString(text));
    }

    private void SearchAction()
    {
        if (Selected(_results) is not { } index) return;
        var (id, relation) = _resultRows[index];
        if (relation == "none") Act(Godot.HttpClient.Method.Post, "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = id }), "Pedido de amizade enviado.");
        else if (relation == "pending_in") Act(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{id}/accept", null, "Amizade aceita.");
    }

    private void AcceptRequest(int i) { if (i < _requestRows.Count && _requestRows[i].Direction == "in") Act(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{_requestRows[i].Id}/accept", null, "Amizade aceita."); }
    private void DeclineRequest(int i) { if (i < _requestRows.Count) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{_requestRows[i].Id}/decline", null, "Pedido removido."); }
    private void RemoveFriend() { if (Selected(_friends) is { } i) Act(Godot.HttpClient.Method.Delete, $"/api/v1/social/friends/{_friendRows[i]}", null, "Amigo removido."); }
    private void AnswerInvite(int i, bool accept) { if (i < _inviteRows.Count) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/groups/invites/{_inviteRows[i]}/{(accept ? "accept" : "decline")}", null, accept ? "Você entrou no grupo." : "Convite recusado."); }
    private void LeaveGroup() { if (Selected(_groups) is { } i) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/groups/{_groupRows[i]}/leave", null, "Você saiu do grupo."); }

    private void CreateGroup()
    {
        var name = _groupName.Text.Trim();
        if (name.Length is < 1 or > 120) { Say("O nome do grupo deve ter de 1 a 120 letras."); return; }
        Act(Godot.HttpClient.Method.Post, "/api/v1/social/groups", JsonSerializer.Serialize(new { name, description = string.Empty, imageUrl = (string?)null }), "Grupo criado.");
        _groupName.Clear();
    }

    private void Act(Godot.HttpClient.Method method, string path, string? body, string success) => Send(Kind.Action, method, path, body, success);
    private static int? Selected(RowList list) => list.SelectedIndex >= 0 ? list.SelectedIndex : null;

    private void UpdateButtons()
    {
        var result = Selected(_results);
        _searchAction.Disabled = result is null || result.Value >= _resultRows.Count || _resultRows[result.Value].Relation is not ("none" or "pending_in");
        _searchAction.Text = result is not null && result.Value < _resultRows.Count && _resultRows[result.Value].Relation == "pending_in" ? "Aceitar pedido" : "Adicionar amigo";
        _friendRemove.Disabled = Selected(_friends) is null;
        _groupLeave.Disabled = Selected(_groups) is null;
    }

    // ---- requests --------------------------------------------------------------------------------------------

    private void Send(Kind kind, Godot.HttpClient.Method method, string path, string? body = null, string success = "")
    {
        _queue.Enqueue(new Pending(kind, method, path, body, success));
        Next();
    }

    private void Next()
    {
        if (_current is not null || _queue.Count == 0) return;
        _current = _queue.Dequeue();
        if (RequestSink is not null) { RequestSink(_current.Method, _current.Path, _current.Body); _current = null; Next(); return; }
        var session = GetNodeOrNull<SessionManager>("/root/SessionManager"); var app = GetNodeOrNull<AppManager>("/root/AppManager");
        if (session is null || app is null || !session.IsAuthenticated) { _current = null; _queue.Clear(); Say("Entre na sua conta para usar esta janela."); return; }
        if (_http is null) { _http = new HttpRequest { Timeout = 15 }; AddChild(_http); _http.RequestCompleted += OnCompleted; }
        var headers = new List<string> { $"Authorization: Bearer {session.AccessToken}" };
        if (_current.Body is not null) headers.Add("Content-Type: application/json");
        if (_http.Request($"{app.ApiBaseUrl}{_current.Path}", [.. headers], _current.Method, _current.Body ?? string.Empty) != Error.Ok) { _current = null; Say("Não foi possível falar com o servidor."); Next(); }
    }

    private void OnCompleted(long result, long code, string[] headers, byte[] body)
    {
        var request = _current; _current = null;
        if (request is not null) Deliver(request.Kind, (int)code, result == (long)HttpRequest.Result.Success, body.GetStringFromUtf8(), request.Success);
        Next();
    }

    private void Deliver(Kind kind, int code, bool reachable, string body, string success)
    {
        if (!reachable) { Say("Sem conexão com o servidor."); return; }
        if (code is < 200 or >= 300)
        {
            if (kind == Kind.Action) Say(code switch { 409 => "O dono não pode sair do grupo.", 404 => "Isso não existe mais.", _ => $"Não deu certo ({code})." });
            return;
        }
        if (kind == Kind.Action) { if (success.Length > 0) Say(success); Reload(); if (_query.Text.Trim().Length >= 2) Search(); return; }
        Apply(kind, body);
    }

    private void Apply(Kind kind, string body)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(body).RootElement.Clone(); } catch (JsonException) { return; }
        if (root.ValueKind != JsonValueKind.Array) return;
        switch (kind)
        {
            case Kind.Search:
                _resultRows.Clear(); _results.Clear();
                foreach (var e in root.EnumerateArray())
                {
                    var relation = e.GetProperty("relation").GetString() ?? "none";
                    var name = e.GetProperty("displayName").GetString() ?? "?";
                    var text = $"{name} (@{e.GetProperty("username").GetString()}) — {RelationText(relation)}";
                    _resultRows.Add((Guid.Parse(e.GetProperty("id").GetString()!), relation));
                    _results.Add(PersonLine(name, $"@{e.GetProperty("username").GetString()} {RelationText(relation)}".Trim(), null, 44), text);
                }
                if (_resultRows.Count == 0) _results.Add(Text("Ninguém encontrado.", 17, UiStyle.TextMuted), "Ninguém encontrado.", selectable: false);
                break;
            case Kind.Requests:
                _requestRows.Clear(); _requests.Clear();
                foreach (var e in root.EnumerateArray())
                {
                    var direction = e.GetProperty("direction").GetString() ?? "in";
                    var name = e.GetProperty("username").GetString() ?? "?";
                    var index = _requestRows.Count;
                    _requestRows.Add((Guid.Parse(e.GetProperty("userId").GetString()!), direction));
                    _requests.Add(RequestCard(name, direction, index), $"{name} — {(direction == "in" ? "quer ser seu amigo" : "aguardando resposta")}", selectable: true, card: true);
                }
                SetSectionTitle(_requestsTitle, $"Solicitações de amizade ({_requestRows.Count})");
                break;
            case Kind.Friends:
                _friendRows.Clear(); _friends.Clear();
                var online = 0;
                foreach (var e in root.EnumerateArray())
                {
                    var id = Guid.Parse(e.GetProperty("id").GetString()!);
                    var name = e.GetProperty("username").GetString() ?? "?";
                    var isOnline = e.TryGetProperty("online", out var flag) && flag.ValueKind == JsonValueKind.True;
                    var known = e.TryGetProperty("online", out var any) && any.ValueKind is JsonValueKind.True or JsonValueKind.False;
                    if (isOnline) online++;
                    _friendRows.Add(id);
                    var line = PersonLine(name, FriendSubtitle(e), known ? (isOnline ? UiStyle.SuccessBright : UiStyle.TextMuted) : null);
                    if (isOnline || !known) line.AddChild(IconButton("chat", "Enviar mensagem", () => ChatRequested?.Invoke(id, name)));
                    var more = IconButton("dots", "Mais opções", () => { });
                    var captured = _friendRows.Count - 1;
                    more.Pressed += () => OpenFriendMenu(more, captured);
                    line.AddChild(more);
                    _friends.Add(line, name);
                }
                SetSectionTitle(_friendsTitle, $"Meus Amigos ({online} online)");
                break;
            case Kind.Invites:
                _inviteRows.Clear(); _invites.Clear();
                foreach (var e in root.EnumerateArray())
                {
                    var group = e.GetProperty("groupName").GetString() ?? "?";
                    var from = e.GetProperty("fromUsername").GetString() ?? "?";
                    var index = _inviteRows.Count;
                    _inviteRows.Add(Guid.Parse(e.GetProperty("inviteId").GetString()!));
                    _invites.Add(InviteCard(group, from, index), $"{group} — convite de {from}", selectable: false, card: true);
                }
                break;
            case Kind.Groups:
                _groupRows.Clear(); _groups.Clear();
                foreach (var e in root.EnumerateArray())
                {
                    var name = e.GetProperty("name").GetString() ?? "?";
                    var members = e.GetProperty("members").GetInt32();
                    var role = e.GetProperty("role").GetString() ?? "member";
                    _groupRows.Add(Guid.Parse(e.GetProperty("id").GetString()!));
                    _groups.Add(PersonLine(name, $"{members} membros · {role}", null, 44), $"{name} · {members} membros · {role}");
                }
                break;
        }
        UpdateButtons();
    }

    private Control RequestCard(string name, string direction, int index)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        box.AddChild(PersonLine(name, direction == "in" ? "quer adicionar você aos amigos." : "aguardando resposta.", null, 52));
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        if (direction == "in") buttons.AddChild(ActionButton("Aceitar", "check", UiStyle.ButtonKind.Primary, () => AcceptRequest(index)));
        buttons.AddChild(ActionButton(direction == "in" ? "Recusar" : "Cancelar", "close", UiStyle.ButtonKind.ToolSelected, () => DeclineRequest(index), orange: true));
        box.AddChild(buttons);
        return box;
    }

    private Control InviteCard(string group, string from, int index)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 8);
        box.AddChild(PersonLine(group, $"convite de {from}", null, 44));
        var buttons = new HBoxContainer(); buttons.AddThemeConstantOverride("separation", 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        buttons.AddChild(ActionButton("Entrar", "check", UiStyle.ButtonKind.Primary, () => AnswerInvite(index, true)));
        buttons.AddChild(ActionButton("Recusar", "close", UiStyle.ButtonKind.ToolSelected, () => AnswerInvite(index, false), orange: true));
        box.AddChild(buttons);
        return box;
    }

    private static Button ActionButton(string text, string icon, UiStyle.ButtonKind kind, Action action, bool orange = false)
    {
        var button = new Button { Text = text, Icon = UiStyle.Icon(icon, orange ? UiStyle.Orange : UiStyle.Accent, 26), CustomMinimumSize = new Vector2(150, 48), FocusMode = Control.FocusModeEnum.None, ExpandIcon = false };
        UiStyle.StyleButton(button, orange ? UiStyle.ButtonKind.Secondary : kind);
        if (orange)
        {
            foreach (var (state, st) in new[] { ("normal", UiStyle.ButtonState.Normal), ("hover", UiStyle.ButtonState.Hover), ("pressed", UiStyle.ButtonState.Pressed), ("hover_pressed", UiStyle.ButtonState.Pressed) })
                button.AddThemeStyleboxOverride(state, UiStyle.Box(st == UiStyle.ButtonState.Hover ? UiStyle.OrangeFillHover : st == UiStyle.ButtonState.Pressed ? UiStyle.OrangeFillPressed : UiStyle.OrangeFill, UiStyle.Orange, UiStyle.Radius, 1, 12, 7));
        }
        button.AddThemeFontSizeOverride("font_size", 20);
        button.Pressed += action;
        return button;
    }

    private void OpenFriendMenu(Control anchor, int index)
    {
        _friends.Select(index);
        var menu = new PopupMenu();
        menu.AddItem("Remover amigo", 0);
        menu.IdPressed += _ => { RemoveFriend(); menu.QueueFree(); };
        menu.PopupHide += () => menu.QueueFree();
        anchor.AddChild(menu);
        menu.Position = (Vector2I)anchor.GetScreenPosition() + new Vector2I(0, (int)anchor.Size.Y);
        menu.Popup();
    }

    private static string RelationText(string? relation) => relation switch { "friend" => "amigo", "pending_out" => "pedido enviado", "pending_in" => "quer ser seu amigo", _ => "" };
    private void Say(string text) => EmitSignal(SignalName.Message, text);

    public void DeliverForTesting(string kind, string json) => Apply(Enum.Parse<Kind>(kind), json);
    public void PressForTesting(string what)
    {
        switch (what)
        {
            case "search": Search(); break;
            case "searchAction": SearchAction(); break;
            case "accept": if (Selected(_requests) is { } a) AcceptRequest(a); break;
            case "decline": if (Selected(_requests) is { } d) DeclineRequest(d); break;
            case "remove": RemoveFriend(); break;
            case "inviteAccept": AnswerInvite(0, true); break;
            case "leave": LeaveGroup(); break;
            case "create": CreateGroup(); break;
        }
    }
}
