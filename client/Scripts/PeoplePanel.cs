using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;

namespace LifeVerse.Client.Scripts;

/// The People window: find players by name, answer friend requests, manage the friends list and groups.
/// Requests go out one at a time; every change reloads the lists so the window always shows what the server holds.
public partial class PeoplePanel : Node
{
    [Signal] public delegate void MessageEventHandler(string text);
    private enum Kind { Search, Requests, Friends, Groups, Invites, Action }
    private sealed record Pending(Kind Kind, Godot.HttpClient.Method Method, string Path, string? Body, string Success);

    private readonly Queue<Pending> _queue = new();
    private Pending? _current;
    private HttpRequest? _http;
    private FloatingWindow _window = null!;
    private LineEdit _query = null!, _groupName = null!;
    private ItemList _results = null!, _requests = null!, _friends = null!, _invites = null!, _groups = null!;
    private Button _searchAction = null!, _requestAccept = null!, _requestDecline = null!, _friendRemove = null!, _inviteAccept = null!, _inviteDecline = null!, _groupLeave = null!;
    private readonly List<(Guid Id, string Relation)> _resultRows = [];
    private readonly List<(Guid Id, string Direction)> _requestRows = [];
    private readonly List<Guid> _friendRows = [], _inviteRows = [], _groupRows = [];

    /// Tests set this to capture requests instead of sending them.
    public Func<Godot.HttpClient.Method, string, string?, bool>? RequestSink { get; set; }
    public bool Visible => _window.Visible;
    public ItemList ResultsForTesting => _results;
    public ItemList RequestsForTesting => _requests;
    public ItemList FriendsForTesting => _friends;
    public ItemList InvitesForTesting => _invites;
    public ItemList GroupsForTesting => _groups;
    public LineEdit QueryForTesting => _query;

    public override void _Ready() => Build();

    public void AttachTo(Control root) { root.AddChild(_window); }

    public void Toggle()
    {
        _window.Visible = !_window.Visible;
        if (_window.Visible) { _window.MoveToFront(); Reload(); }
    }

    private void Build()
    {
        _window = new FloatingWindow("Pessoas") { Visible = false };
        _window.Place(.5f, .5f, -250, -230, 250, 230);
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(480, 380) };
        _window.Content.AddChild(tabs);

        var search = Tab(tabs, "Buscar");
        _query = new LineEdit { PlaceholderText = "Nome do jogador (2 letras ou mais)", ClearButtonEnabled = true };
        _query.TextSubmitted += _ => Search();
        search.AddChild(_query);
        var go = new Button { Text = "Buscar" }; go.Pressed += Search; search.AddChild(go);
        _results = List(search, 190);
        _results.ItemSelected += _ => UpdateButtons();
        _searchAction = new Button { Text = "Adicionar amigo", Disabled = true }; _searchAction.Pressed += SearchAction; search.AddChild(_searchAction);

        var friends = Tab(tabs, "Amigos");
        friends.AddChild(new Label { Text = "Pedidos" });
        _requests = List(friends, 90); _requests.ItemSelected += _ => UpdateButtons();
        var requestButtons = Row(friends);
        _requestAccept = AddButton(requestButtons, "Aceitar", AcceptRequest); _requestDecline = AddButton(requestButtons, "Recusar / cancelar", DeclineRequest);
        friends.AddChild(new Label { Text = "Amigos" });
        _friends = List(friends, 110); _friends.ItemSelected += _ => UpdateButtons();
        _friendRemove = AddButton(friends, "Remover amigo", RemoveFriend);

        var groups = Tab(tabs, "Grupos");
        groups.AddChild(new Label { Text = "Convites" });
        _invites = List(groups, 70); _invites.ItemSelected += _ => UpdateButtons();
        var inviteButtons = Row(groups);
        _inviteAccept = AddButton(inviteButtons, "Entrar", () => AnswerInvite(true)); _inviteDecline = AddButton(inviteButtons, "Recusar", () => AnswerInvite(false));
        groups.AddChild(new Label { Text = "Meus grupos" });
        _groups = List(groups, 100); _groups.ItemSelected += _ => UpdateButtons();
        _groupLeave = AddButton(groups, "Sair do grupo", LeaveGroup);
        var create = Row(groups);
        _groupName = new LineEdit { PlaceholderText = "Nome do novo grupo", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _groupName.TextSubmitted += _ => CreateGroup();
        create.AddChild(_groupName);
        AddButton(create, "Criar", CreateGroup);
        UpdateButtons();
    }

    private static VBoxContainer Tab(TabContainer tabs, string title)
    {
        var box = new VBoxContainer { Name = title };
        box.AddThemeConstantOverride("separation", 6);
        tabs.AddChild(box);
        return box;
    }

    private static ItemList List(Control parent, float height)
    {
        var list = new ItemList { CustomMinimumSize = new Vector2(0, height), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        parent.AddChild(list);
        return list;
    }

    private static HBoxContainer Row(Control parent)
    {
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 6);
        parent.AddChild(row);
        return row;
    }

    private static Button AddButton(Control parent, string text, Action pressed)
    {
        var button = new Button { Text = text, Disabled = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

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

    private void AcceptRequest() { if (Selected(_requests) is { } i && _requestRows[i].Direction == "in") Act(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{_requestRows[i].Id}/accept", null, "Amizade aceita."); }
    private void DeclineRequest() { if (Selected(_requests) is { } i) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/friends/{_requestRows[i].Id}/decline", null, "Pedido removido."); }
    private void RemoveFriend() { if (Selected(_friends) is { } i) Act(Godot.HttpClient.Method.Delete, $"/api/v1/social/friends/{_friendRows[i]}", null, "Amigo removido."); }
    private void AnswerInvite(bool accept) { if (Selected(_invites) is { } i) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/groups/invites/{_inviteRows[i]}/{(accept ? "accept" : "decline")}", null, accept ? "Você entrou no grupo." : "Convite recusado."); }
    private void LeaveGroup() { if (Selected(_groups) is { } i) Act(Godot.HttpClient.Method.Post, $"/api/v1/social/groups/{_groupRows[i]}/leave", null, "Você saiu do grupo."); }

    private void CreateGroup()
    {
        var name = _groupName.Text.Trim();
        if (name.Length is < 1 or > 120) { Say("O nome do grupo deve ter de 1 a 120 letras."); return; }
        Act(Godot.HttpClient.Method.Post, "/api/v1/social/groups", JsonSerializer.Serialize(new { name, description = string.Empty, imageUrl = (string?)null }), "Grupo criado.");
        _groupName.Clear();
    }

    private void Act(Godot.HttpClient.Method method, string path, string? body, string success) => Send(Kind.Action, method, path, body, success);
    private static int? Selected(ItemList list) { var items = list.GetSelectedItems(); return items.Length > 0 ? items[0] : null; }

    private void UpdateButtons()
    {
        var result = Selected(_results);
        _searchAction.Disabled = result is null || _resultRows[result.Value].Relation is not ("none" or "pending_in");
        _searchAction.Text = result is not null && _resultRows[result.Value].Relation == "pending_in" ? "Aceitar pedido" : "Adicionar amigo";
        var request = Selected(_requests);
        _requestAccept.Disabled = request is null || _requestRows[request.Value].Direction != "in";
        _requestDecline.Disabled = request is null;
        _friendRemove.Disabled = Selected(_friends) is null;
        _inviteAccept.Disabled = _inviteDecline.Disabled = Selected(_invites) is null;
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
                Fill(_results, _resultRows, root, e => (Guid.Parse(e.GetProperty("id").GetString()!), e.GetProperty("relation").GetString() ?? "none"),
                    e => $"{e.GetProperty("displayName").GetString()} (@{e.GetProperty("username").GetString()}) — {RelationText(e.GetProperty("relation").GetString())}");
                if (_resultRows.Count == 0) _results.AddItem("Ninguém encontrado.", null, false);
                break;
            case Kind.Requests:
                Fill(_requests, _requestRows, root, e => (Guid.Parse(e.GetProperty("userId").GetString()!), e.GetProperty("direction").GetString() ?? "in"),
                    e => $"{e.GetProperty("username").GetString()} — {(e.GetProperty("direction").GetString() == "in" ? "quer ser seu amigo" : "aguardando resposta")}");
                break;
            case Kind.Friends:
                _friendRows.Clear(); _friends.Clear();
                foreach (var e in root.EnumerateArray()) { _friendRows.Add(Guid.Parse(e.GetProperty("id").GetString()!)); _friends.AddItem(e.GetProperty("username").GetString() ?? "?"); }
                break;
            case Kind.Invites:
                _inviteRows.Clear(); _invites.Clear();
                foreach (var e in root.EnumerateArray()) { _inviteRows.Add(Guid.Parse(e.GetProperty("inviteId").GetString()!)); _invites.AddItem($"{e.GetProperty("groupName").GetString()} — convite de {e.GetProperty("fromUsername").GetString()}"); }
                break;
            case Kind.Groups:
                _groupRows.Clear(); _groups.Clear();
                foreach (var e in root.EnumerateArray()) { _groupRows.Add(Guid.Parse(e.GetProperty("id").GetString()!)); _groups.AddItem($"{e.GetProperty("name").GetString()} · {e.GetProperty("members").GetInt32()} membros · {e.GetProperty("role").GetString()}"); }
                break;
        }
        UpdateButtons();
    }

    private static void Fill<T>(ItemList list, List<T> rows, JsonElement array, Func<JsonElement, T> row, Func<JsonElement, string> text)
    {
        rows.Clear(); list.Clear();
        foreach (var element in array.EnumerateArray()) { rows.Add(row(element)); list.AddItem(text(element)); }
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
            case "accept": AcceptRequest(); break;
            case "decline": DeclineRequest(); break;
            case "remove": RemoveFriend(); break;
            case "inviteAccept": AnswerInvite(true); break;
            case "leave": LeaveGroup(); break;
            case "create": CreateGroup(); break;
        }
    }
}
