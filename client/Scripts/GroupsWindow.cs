using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts;

public sealed record GroupCardData(Guid Id, string Name, string Tag, string Description, string AccentColor, string Privacy, string JoinPolicy, int Members, int MemberLimit, bool IsMember, bool RequestPending, bool Invited, bool Banned);
public sealed record GroupMemberData(Guid UserId, string Username, Guid RoleId, string RoleName, int Rank, DateTimeOffset? MutedUntil);
public sealed record GroupRoleData(Guid Id, string Name, string SystemKey, int Rank, string[] Permissions, int Members, bool CanEdit);

/// <summary>
/// "Grupos": the window to find, make and run groups. It shows only what the server says and offers only what the person's cargo allows (the same
/// hierarchy rules as the server, so buttons that would be refused are off); the server checks everything again. Tabs for my groups, the search
/// and creating a group; for the group chosen: overview, members, cargos, requests and bans, history and settings.
/// </summary>
public sealed partial class GroupsWindow : Node
{
    private static readonly string[] ActionNames = ["Parado"];
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private FloatingWindow _window = null!;
    private UiTabs _tabs = null!, _inner = null!;
    private Control _minePage = null!, _searchPage = null!, _createPage = null!, _detail = null!;
    private readonly List<Control> _innerPages = [];
    private Label _status = null!;

    // data
    private readonly List<GroupCardData> _mine = [], _found = [];
    private GroupCardData? _current;
    private string _myRole = string.Empty; private int _myRank = -1; private HashSet<string> _myPermissions = []; private bool _isLeader;
    private readonly List<GroupMemberData> _members = [];
    private readonly List<GroupRoleData> _roles = [];
    private readonly List<(Guid Id, Guid UserId, string Username)> _requests = [];
    private readonly List<(Guid UserId, string Username)> _bans = [];

    // controls
    private ItemList _mineList = null!, _foundList = null!, _memberList = null!, _roleList = null!, _requestList = null!, _banList = null!, _auditList = null!;
    private LineEdit _query = null!, _newName = null!, _newTag = null!, _newDescription = null!, _inviteName = null!;
    private OptionButton _newPrivacy = null!, _newPolicy = null!, _roleChoice = null!;
    private ColorPickerButton _newColour = null!;
    private Label _overview = null!;
    private Button _join = null!, _leave = null!, _kick = null!, _ban = null!, _mute = null!, _assign = null!, _invite = null!, _approve = null!, _deny = null!, _unban = null!;
    private LineEdit _roleName = null!, _roleRank = null!, _setName = null!, _setTag = null!, _setDescription = null!, _setLimit = null!, _confirm = null!;
    private OptionButton _setPrivacy = null!, _setPolicy = null!, _leaderChoice = null!;
    private ColorPickerButton _setColour = null!;
    private readonly Dictionary<string, CheckBox> _permissionBoxes = [];
    private Button _roleCreate = null!, _roleSave = null!, _roleDelete = null!, _saveSettings = null!, _transfer = null!, _dissolve = null!;

    /// Tests set this to answer requests without a network: (method, path, body) -> (status, body).
    public Func<Godot.HttpClient.Method, string, string?, (int Status, string Body)>? Transport { get; set; }
    public bool Visible => _window.Visible;
    public IReadOnlyList<GroupCardData> Mine => _mine;
    public IReadOnlyList<GroupCardData> Found => _found;
    public GroupCardData? Current => _current;
    public IReadOnlyList<GroupMemberData> Members => _members;
    public IReadOnlyList<GroupRoleData> Roles => _roles;
    public string StatusText => _status.Text;

    public override void _Ready() => Build();

    public void Toggle() { if (_window.Visible) _window.Visible = false; else Open(); }
    public void Open() { _window.Visible = true; _window.MoveToFront(); LoadMine(); LoadNotices(); }

    // ---- talking to the API -------------------------------------------------------------------------------------------------------------------------

    private void Call(Godot.HttpClient.Method method, string path, object? body, Action<int, string> done)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body);
        if (Transport is not null) { var (status, text) = Transport(method, path, json); done(status, text); return; }
        var app = GetNodeOrNull<AppManager>("/root/AppManager"); var session = GetNodeOrNull<SessionManager>("/root/SessionManager");
        if (app is null || session is null || !session.IsAuthenticated) { done(401, string.Empty); return; }
        var http = new HttpRequest { Timeout = 15 }; AddChild(http);
        http.RequestCompleted += (result, code, _, bytes) => { http.QueueFree(); done(result == (long)HttpRequest.Result.Success ? (int)code : 0, bytes.GetStringFromUtf8()); };
        var headers = new List<string> { $"Authorization: Bearer {session.AccessToken}" }; if (json is not null) headers.Add("Content-Type: application/json");
        if (http.Request($"{app.ApiBaseUrl}{(path.StartsWith("/api/", StringComparison.Ordinal) ? path : "/api/v1/groups" + path)}", [.. headers], method, json ?? string.Empty) != Error.Ok) { http.QueueFree(); done(0, string.Empty); }
    }

    private static string ErrorOf(string body) { try { using var d = JsonDocument.Parse(body); return d.RootElement.TryGetProperty("error", out var e) ? e.GetString() ?? string.Empty : string.Empty; } catch (JsonException) { return string.Empty; } }

    /// <summary>The reasons the server gives, in words.</summary>
    public static string Explain(int status, string body) => ErrorOf(body) switch
    {
        "forbidden" => "Você não tem permissão para isso neste grupo.",
        "outranked" => "Só dá para fazer isso com quem tem um cargo abaixo do seu.",
        "cannot_grant" => "Você só pode dar permissões que você mesmo tem.",
        "rate_limited" => "Muitas ações de moderação em pouco tempo. Espere alguns minutos.",
        "not_found" => "Não encontrado (o grupo, a pessoa ou o pedido pode ter mudado).",
        "name_taken" => "Já existe algo com esse nome.",
        "tag_taken" => "Essa sigla já é de outro grupo.",
        "bad_name" => "O nome precisa ter de 3 a 40 caracteres (cargos: de 2 a 32).",
        "bad_tag" => "A sigla tem de 2 a 5 letras ou números.",
        "bad_description" => "A descrição passa de 600 caracteres.",
        "bad_identity" => "Cor, emblema ou imagem inválidos (a imagem precisa ser https).",
        "bad_policy" => "Um grupo privado só recebe gente por convite.",
        "bad_rank" => "O nível do cargo vai de 1 a 99.",
        "bad_permissions" => "Permissão desconhecida.",
        "bad_limit" => "O limite de membros não pode ser menor que o número atual de membros.",
        "bad_duration" => "Duração inválida.",
        "bad_target" => "Isso não pode ser feito com essa pessoa.",
        "bad_message" => "A mensagem passa de 300 caracteres.",
        "already_member" => "Essa pessoa já está no grupo.",
        "banned" => "Essa pessoa está banida deste grupo.",
        "group_full" => "O grupo está cheio.",
        "invite_only" => "Este grupo só recebe gente por convite.",
        "too_many_memberships" => "Você já está em grupos demais (o limite é 30).",
        "too_many_groups_created" => "Você já criou grupos demais (o limite é 5).",
        "too_many_requests" => "Você já tem pedidos demais esperando resposta.",
        "too_many_roles" => "O grupo já tem cargos demais (o limite é 20).",
        "too_many_invites" => "O grupo já tem convites demais esperando resposta.",
        "system_role" => "Os cargos iniciais não mudam de nome nem de nível e não podem ser apagados.",
        "leader_cannot_leave" => "O líder precisa passar a liderança antes de sair.",
        "confirmation_needed" => "Escreva o nome do grupo, igualzinho, para confirmar.",
        "blocked" => "O grupo ainda tem terrenos. Passe-os adiante antes de dissolver.",
        "muted" => "Você está silenciado neste grupo.",
        "bad_announcement" => "O anúncio precisa de título (até 80) e texto (até 2000).",
        "too_many_pinned" => "Já há 3 anúncios fixados. Desafixe um antes.",
        "bad_time" => "Datas inválidas: o fim vem depois do início, no futuro, e dura até 7 dias.",
        "bad_capacity" => "A capacidade vai de 1 a 5000.",
        "bad_island" => "Escolha uma ilha onde você pode entrar.",
        "too_many_events" => "O grupo já tem eventos demais marcados (o limite é 20).",
        "event_full" => "O evento está lotado.",
        _ => status == 0 ? "Sem conexão com o servidor." : status == 401 ? "Entre na sua conta." : $"Não foi possível concluir (HTTP {status})."
    };

    private void Say(string text) => _status.Text = text;

    private void Do(Godot.HttpClient.Method method, string path, object? body, string ok, Action? after = null) =>
        Call(method, path, body, (status, text) => { if (status is >= 200 and < 300) { Say(ok); after?.Invoke(); } else Say(Explain(status, text)); });

    // ---- reading ---------------------------------------------------------------------------------------------------------------------------------------

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
    private static int Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static Guid IdOf(JsonElement e, string name) => Guid.TryParse(Str(e, name), out var id) ? id : Guid.Empty;
    private static DateTimeOffset? Time(JsonElement e, string name) => DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    private static GroupCardData Card(JsonElement c) => new(IdOf(c, "id"), Str(c, "name"), Str(c, "tag"), Str(c, "description"), Str(c, "accentColor"), Str(c, "privacy"), Str(c, "joinPolicy"), Num(c, "members"), Num(c, "memberLimit"), Flag(c, "isMember"), Flag(c, "requestPending"), Flag(c, "invited"), Flag(c, "banned"));

    private static List<T> ReadList<T>(string body, Func<JsonElement, T> read)
    {
        try { using var d = JsonDocument.Parse(body); return [.. d.RootElement.EnumerateArray().Select(read)]; }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return []; }
    }

    // ---- the tabs -----------------------------------------------------------------------------------------------------------------------------------------

    public void LoadMine() => Call(Godot.HttpClient.Method.Get, "/mine", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { Say(Explain(status, body)); return; }
        _mine.Clear(); _mine.AddRange(ReadList(body, Card)); _mineList.Clear();
        foreach (var g in _mine) _mineList.AddItem($"{(g.Tag.Length > 0 ? $"[{g.Tag}] " : string.Empty)}{g.Name} · {g.Members} membro(s){(_unreadChat.GetValueOrDefault(g.Id) is > 0 and var n ? $" · {n} nova(s)" : string.Empty)}");
        if (_current is { } current) { var again = _mine.FindIndex(g => g.Id == current.Id); if (again >= 0) _mineList.Select(again); else { _current = null; _detail.Visible = false; } }
    });

    public void Search(string? query = null) => Call(Godot.HttpClient.Method.Get, $"/search?q={Uri.EscapeDataString(query ?? _query.Text.Trim())}", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { Say(Explain(status, body)); return; }
        _found.Clear(); _found.AddRange(ReadList(body, Card)); _foundList.Clear();
        foreach (var g in _found) _foundList.AddItem($"{g.Name} · {g.Members} membro(s) · {PolicyName(g)}{(g.IsMember ? " · você é membro" : g.RequestPending ? " · pedido enviado" : g.Banned ? " · banido" : string.Empty)}");
        Say(_found.Count == 0 ? "Nenhum grupo público encontrado." : $"{_found.Count} grupo(s) encontrado(s).");
        UpdateSearchButtons();
    });

    private static string PolicyName(GroupCardData g) => g.JoinPolicy switch { "open" => "entrada livre", "request" => "entra com aprovação", _ => "só por convite" };

    public void SelectMine(int index) { if (index < 0 || index >= _mine.Count) return; _mineList.Select(index); OpenGroup(_mine[index].Id); }

    /// <summary>Shows a group: its profile and what this person may do in it, then loads the lists the cargo allows.</summary>
    public void OpenGroup(Guid id) => Call(Godot.HttpClient.Method.Get, $"/{id}", null, (status, body) =>
    {
        if (status is < 200 or >= 300) { Say(Explain(status, body)); return; }
        try
        {
            using var d = JsonDocument.Parse(body); var root = d.RootElement;
            _current = Card(root.GetProperty("card")); _myRole = Str(root, "myRole"); _myRank = Num(root, "myRank"); _isLeader = Flag(root, "isLeader");
            _mutedUntil = Time(root, "mutedUntil"); _notifyMuted = Flag(root, "notificationsMuted"); _unreadChat.Remove(_current.Id);
            _myPermissions = root.TryGetProperty("myPermissions", out var p) ? [.. p.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : [];
            _overview.Text = $"{_current.Name}{(_current.Tag.Length > 0 ? $" [{_current.Tag}]" : string.Empty)}\n{(_current.Description.Length > 0 ? _current.Description : "Sem descrição.")}\n\nLíder: {Str(root, "leaderName")} · {_current.Members}/{_current.MemberLimit} membros\nPrivacidade: {(_current.Privacy == "public" ? "público" : "privado")} · Entrada: {PolicyName(_current)}\nSeu cargo: {(string.IsNullOrEmpty(_myRole) ? "(você não é membro)" : _myRole)}";
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { Say("Não foi possível ler o grupo."); return; }
        _detail.Visible = true; var member = _current.IsMember;
        _inner.Visible = member;
        if (member) { LoadChat(); LoadAnnouncements(); LoadEvents(); LoadMembers(); LoadRoles(); if (Has(GroupPermissions.ApproveRequests)) LoadRequests(); if (Has(GroupPermissions.Ban)) LoadBans(); if (Has(GroupPermissions.ViewAudit)) LoadAudit(); FillSettings(); }
        UpdateButtons();
    });

    private bool Has(string permission) => _isLeader || _myPermissions.Contains(permission);

    private void LoadMembers() => Call(Godot.HttpClient.Method.Get, $"/{_current!.Id}/members", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _members.Clear(); _members.AddRange(ReadList(body, m => new GroupMemberData(IdOf(m, "userId"), Str(m, "username"), IdOf(m, "roleId"), Str(m, "roleName"), Num(m, "rank"), Time(m, "mutedUntil"))));
        _memberList.Clear(); foreach (var m in _members) _memberList.AddItem($"{m.Username} · {m.RoleName}{(m.MutedUntil is { } until ? $" · silenciado até {until.ToLocalTime():HH:mm}" : string.Empty)}");
        _leaderChoice.Clear(); foreach (var m in _members.Where(x => x.Rank < GroupRules.LeaderRank)) { _leaderChoice.AddItem(m.Username); _leaderChoice.SetItemMetadata(_leaderChoice.ItemCount - 1, m.UserId.ToString()); }
        UpdateButtons();
    });

    private void LoadRoles() => Call(Godot.HttpClient.Method.Get, $"/{_current!.Id}/roles", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _roles.Clear(); _roles.AddRange(ReadList(body, r => new GroupRoleData(IdOf(r, "id"), Str(r, "name"), Str(r, "systemKey"), Num(r, "rank"), r.TryGetProperty("permissions", out var p) ? [.. p.EnumerateArray().Select(x => x.GetString() ?? string.Empty)] : [], Num(r, "members"), Flag(r, "canEdit"))));
        _roleList.Clear(); foreach (var r in _roles) _roleList.AddItem($"{r.Name} · nível {r.Rank} · {r.Members} membro(s)");
        _roleChoice.Clear(); foreach (var r in _roles.Where(x => x.SystemKey != "owner")) { _roleChoice.AddItem(r.Name); _roleChoice.SetItemMetadata(_roleChoice.ItemCount - 1, r.Id.ToString()); }
        UpdateButtons();
    });

    private void LoadRequests() => Call(Godot.HttpClient.Method.Get, $"/{_current!.Id}/requests", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _requests.Clear(); _requests.AddRange(ReadList(body, r => (IdOf(r, "id"), IdOf(r, "userId"), Str(r, "username")))); var messages = ReadList(body, r => Str(r, "message"));
        _requestList.Clear(); for (var i = 0; i < _requests.Count; i++) _requestList.AddItem($"{_requests[i].Username}{(messages[i].Length > 0 ? $" — “{messages[i]}”" : string.Empty)}");
        UpdateButtons();
    });

    private void LoadBans() => Call(Godot.HttpClient.Method.Get, $"/{_current!.Id}/bans", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _bans.Clear(); _bans.AddRange(ReadList(body, b => (IdOf(b, "userId"), Str(b, "username")))); var reasons = ReadList(body, b => Str(b, "reason")); var until = ReadList(body, b => Time(b, "expiresAt"));
        _banList.Clear(); for (var i = 0; i < _bans.Count; i++) _banList.AddItem($"{_bans[i].Username} · {(until[i] is { } t ? $"até {t.ToLocalTime():dd/MM}" : "para sempre")}{(reasons[i].Length > 0 ? $" — {reasons[i]}" : string.Empty)}");
        UpdateButtons();
    });

    private static string ActionText(string action) => action switch
    {
        "group_created" => "criou o grupo", "profile_updated" => "mudou o perfil do grupo", "invited" => "convidou", "member_joined" => "entrou no grupo", "member_left" => "saiu do grupo",
        "request_approved" => "aprovou a entrada de", "request_denied" => "recusou a entrada de", "member_kicked" => "expulsou", "member_banned" => "baniu", "member_unbanned" => "desbaniu", "member_muted" => "silenciou",
        "role_assigned" => "mudou o cargo de", "role_created" => "criou um cargo", "role_updated" => "mudou um cargo", "role_deleted" => "apagou um cargo", "leadership_transferred" => "passou a liderança para", "group_dissolved" => "dissolveu o grupo", _ => action
    };

    private void LoadAudit() => Call(Godot.HttpClient.Method.Get, $"/{_current!.Id}/audit?take=100", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        _auditList.Clear();
        foreach (var e in ReadList(body, e => e.Clone()))
            _auditList.AddItem($"{(Time(e, "at") ?? DateTimeOffset.UtcNow).ToLocalTime().ToString("dd/MM HH:mm", Pt)} · {Str(e, "actor")} {ActionText(Str(e, "action"))}{(Str(e, "target").Length > 0 ? " " + Str(e, "target") : string.Empty)}");
    });

    // ---- what the buttons may do (the server decides again) --------------------------------------------------------------------------------------------------

    private GroupMemberData? SelectedMember() { var p = _memberList.GetSelectedItems(); return p.Length == 0 || p[0] >= _members.Count ? null : _members[p[0]]; }
    private GroupRoleData? SelectedRole() { var p = _roleList.GetSelectedItems(); return p.Length == 0 || p[0] >= _roles.Count ? null : _roles[p[0]]; }
    private GroupCardData? SelectedFound() { var p = _foundList.GetSelectedItems(); return p.Length == 0 || p[0] >= _found.Count ? null : _found[p[0]]; }

    private bool Above(GroupMemberData? m) => m is not null && m.Rank < GroupRules.LeaderRank && (_isLeader || _myRank > m.Rank);

    private void UpdateButtons()
    {
        var m = SelectedMember(); var own = m is not null && Guid.TryParse(GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId.ToString(), out var me) && m.UserId == me;
        _kick.Disabled = !(Has(GroupPermissions.Kick) && Above(m) && !own); _ban.Disabled = !(Has(GroupPermissions.Ban) && Above(m) && !own); _mute.Disabled = !(Has(GroupPermissions.Mute) && Above(m) && !own);
        _assign.Disabled = !(Has(GroupPermissions.AssignRoles) && Above(m) && !own); _roleChoice.Disabled = _assign.Disabled; _invite.Disabled = !Has(GroupPermissions.Invite); _inviteName.Editable = Has(GroupPermissions.Invite);
        _approve.Disabled = _deny.Disabled = !Has(GroupPermissions.ApproveRequests) || _requestList.GetSelectedItems().Length == 0; _unban.Disabled = !Has(GroupPermissions.Ban) || _banList.GetSelectedItems().Length == 0;
        var manage = Has(GroupPermissions.ManageRoles); var role = SelectedRole();
        _roleCreate.Disabled = !manage; _roleSave.Disabled = !(manage && role is { CanEdit: true }); _roleDelete.Disabled = !(manage && role is { CanEdit: true, SystemKey: "" });
        foreach (var (key, box) in _permissionBoxes) box.Disabled = !(manage && (_isLeader || _myPermissions.Contains(key) || box.ButtonPressed && role is { CanEdit: true }));
        _saveSettings.Disabled = !Has(GroupPermissions.EditProfile); _transfer.Disabled = _dissolve.Disabled = !_isLeader; _confirm.Editable = _isLeader;
        _leave.Disabled = _current is null || !_current.IsMember || _isLeader; _leave.Visible = _current?.IsMember == true;
        UpdateCommunicationButtons();
        UpdateSearchButtons();
    }

    private void UpdateSearchButtons()
    {
        var g = SelectedFound(); _join.Disabled = g is null || g.IsMember || g.Banned || g.RequestPending || g.JoinPolicy == "invite";
        _join.Text = g is null ? "Entrar" : g.JoinPolicy == "open" ? "Entrar no grupo" : g.JoinPolicy == "request" ? (g.RequestPending ? "Pedido enviado" : "Pedir para entrar") : "Só por convite";
    }

    // ---- the actions -----------------------------------------------------------------------------------------------------------------------------------------

    public void Create()
    {
        Call(Godot.HttpClient.Method.Post, string.Empty, new
        {
            name = _newName.Text.Trim(), tag = _newTag.Text.Trim(), description = _newDescription.Text.Trim(),
            privacy = _newPrivacy.Selected == 1 ? "private" : "public", joinPolicy = _newPrivacy.Selected == 1 ? "invite" : _newPolicy.Selected switch { 0 => "open", 1 => "request", _ => "invite" },
            accentColor = "#" + _newColour.Color.ToHtml(false).ToUpperInvariant()
        }, (status, body) =>
        {
            if (status is < 200 or >= 300) { Say(Explain(status, body)); return; }
            Say($"Grupo “{_newName.Text.Trim()}” criado."); _newName.Text = _newTag.Text = _newDescription.Text = string.Empty; _tabs.Select(0); LoadMine();
        });
    }

    public void JoinSelected() { if (SelectedFound() is { } g) Call(Godot.HttpClient.Method.Post, $"/{g.Id}/join", new { message = string.Empty }, (status, body) => { if (status is >= 200 and < 300) { Say(g.JoinPolicy == "open" ? $"Você entrou em “{g.Name}”." : $"Pedido enviado para “{g.Name}”."); Search(); LoadMine(); } else Say(Explain(status, body)); }); }
    public void LeaveCurrent() { if (_current is { } g) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/leave", new { }, $"Você saiu de “{g.Name}”.", () => { _current = null; _detail.Visible = false; LoadMine(); }); }
    public void InviteByName() { if (_current is { } g && _inviteName.Text.Trim().Length > 0) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/invites", new { username = _inviteName.Text.Trim() }, $"Convite enviado para {_inviteName.Text.Trim()}.", () => _inviteName.Text = string.Empty); }
    public void KickSelected() { if (_current is { } g && SelectedMember() is { } m) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/members/{m.UserId}/kick", new { reason = string.Empty }, $"{m.Username} foi expulso.", () => OpenGroup(g.Id)); }
    public void BanSelected(int? days = null) { if (_current is { } g && SelectedMember() is { } m) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/members/{m.UserId}/ban", new { reason = string.Empty, days }, $"{m.Username} foi banido.", () => OpenGroup(g.Id)); }
    public void MuteSelected(int minutes = 30) { if (_current is { } g && SelectedMember() is { } m) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/members/{m.UserId}/mute", new { minutes = m.MutedUntil is null ? minutes : 0, reason = string.Empty }, m.MutedUntil is null ? $"{m.Username} foi silenciado por {minutes} minutos." : $"{m.Username} pode falar de novo.", () => LoadMembers()); }
    public void AssignSelected() { if (_current is { } g && SelectedMember() is { } m && _roleChoice.ItemCount > 0 && Guid.TryParse(_roleChoice.GetItemMetadata(_roleChoice.Selected).AsString(), out var role)) Do(Godot.HttpClient.Method.Put, $"/{g.Id}/members/{m.UserId}/role", new { roleId = role }, $"{m.Username} agora é {_roleChoice.GetItemText(_roleChoice.Selected)}.", () => { LoadMembers(); LoadRoles(); }); }
    public void DecideSelected(bool approve) { if (_current is { } g && _requestList.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _requests.Count) { var r = _requests[sel[0]]; Do(Godot.HttpClient.Method.Post, $"/{g.Id}/requests/{r.Id}/{(approve ? "approve" : "deny")}", new { }, approve ? $"{r.Username} entrou no grupo." : $"Pedido de {r.Username} recusado.", () => { LoadRequests(); LoadMembers(); }); } }
    public void UnbanSelected() { if (_current is { } g && _banList.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _bans.Count) { var b = _bans[sel[0]]; Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/bans/{b.UserId}", null, $"{b.Username} foi desbanido.", LoadBans); } }

    /// <summary>The permissions ticked in the editor of cargos.</summary>
    public string[] TickedPermissions() => [.. GroupPermissions.All.Select(p => p.Key).Where(k => _permissionBoxes[k].ButtonPressed)];

    private void ShowRole()
    {
        if (SelectedRole() is not { } r) return;
        _roleName.Text = r.Name; _roleRank.Text = r.Rank.ToString(CultureInfo.InvariantCulture);
        foreach (var (key, box) in _permissionBoxes) box.SetPressedNoSignal(r.Permissions.Contains(key));
        UpdateButtons();
    }

    public void CreateRole() { if (_current is { } g) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/roles", new { name = _roleName.Text.Trim(), rank = int.TryParse(_roleRank.Text, out var rank) ? rank : 10, permissions = TickedPermissions() }, $"Cargo “{_roleName.Text.Trim()}” criado.", LoadRoles); }
    public void SaveRole() { if (_current is { } g && SelectedRole() is { } r) Do(Godot.HttpClient.Method.Put, $"/{g.Id}/roles/{r.Id}", new { name = r.SystemKey.Length == 0 ? _roleName.Text.Trim() : null, rank = r.SystemKey.Length == 0 && int.TryParse(_roleRank.Text, out var rank) ? rank : (int?)null, permissions = TickedPermissions() }, $"Cargo “{r.Name}” salvo.", () => { LoadRoles(); LoadMembers(); }); }
    public void DeleteRole() { if (_current is { } g && SelectedRole() is { } r) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/roles/{r.Id}", null, $"Cargo “{r.Name}” apagado; seus membros viraram Membro.", () => { LoadRoles(); LoadMembers(); }); }

    private void FillSettings()
    {
        if (_current is not { } g) return;
        _setName.Text = g.Name; _setTag.Text = g.Tag; _setDescription.Text = g.Description; _setLimit.Text = g.MemberLimit.ToString(CultureInfo.InvariantCulture);
        _setPrivacy.Select(g.Privacy == "private" ? 1 : 0); _setPolicy.Select(g.JoinPolicy switch { "open" => 0, "request" => 1, _ => 2 });
        if (GroupRules.ValidColour(g.AccentColor) && g.AccentColor.Length == 7) _setColour.Color = new Color(g.AccentColor);
    }

    public void SaveSettings()
    {
        if (_current is not { } g) return;
        Do(Godot.HttpClient.Method.Put, $"/{g.Id}", new
        {
            name = _setName.Text.Trim(), tag = _setTag.Text.Trim(), description = _setDescription.Text.Trim(), privacy = _setPrivacy.Selected == 1 ? "private" : "public",
            joinPolicy = _setPrivacy.Selected == 1 ? "invite" : _setPolicy.Selected switch { 0 => "open", 1 => "request", _ => "invite" }, accentColor = "#" + _setColour.Color.ToHtml(false).ToUpperInvariant(), memberLimit = int.TryParse(_setLimit.Text, out var limit) ? limit : (int?)null
        }, "Perfil do grupo atualizado.", () => { OpenGroup(g.Id); LoadMine(); });
    }

    public void TransferLeadership() { if (_current is { } g && _leaderChoice.ItemCount > 0 && Guid.TryParse(_leaderChoice.GetItemMetadata(_leaderChoice.Selected).AsString(), out var target)) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/transfer", new { userId = target, confirm = _confirm.Text }, $"{_leaderChoice.GetItemText(_leaderChoice.Selected)} é o novo líder.", () => { _confirm.Text = string.Empty; OpenGroup(g.Id); }); }
    public void Dissolve() { if (_current is { } g) Do(Godot.HttpClient.Method.Post, $"/{g.Id}/dissolve", new { confirm = _confirm.Text }, $"O grupo “{g.Name}” foi dissolvido.", () => { _confirm.Text = string.Empty; _current = null; _detail.Visible = false; LoadMine(); }); }

    // ---- building the window ------------------------------------------------------------------------------------------------------------------------------------

    private static Button Act(string text, UiStyle.ButtonKind kind, Action action) { var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None }; UiStyle.StyleButton(b, kind, 10, 6); b.AddThemeFontSizeOverride("font_size", 16); b.Pressed += action; return b; }
    private static LineEdit Field(string placeholder, int max = 120) { var e = new LineEdit { PlaceholderText = placeholder, MaxLength = max, CustomMinimumSize = new Vector2(0, 38), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; e.AddThemeFontSizeOverride("font_size", 16); return e; }
    private static OptionButton Choice(params string[] items) { var o = new OptionButton { CustomMinimumSize = new Vector2(0, 38), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; foreach (var i in items) o.AddItem(i); return o; }
    private static ItemList List(int height) { var l = new ItemList { CustomMinimumSize = new Vector2(0, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; l.AddThemeFontSizeOverride("font_size", 16); return l; }
    private static Label Heading(string text) { var l = new Label { Text = text }; l.AddThemeFontOverride("font", UiStyle.Bold); l.AddThemeFontSizeOverride("font_size", 18); return l; }
    private static Label Note(string text) { var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) }; l.AddThemeColorOverride("font_color", UiStyle.TextMuted); l.AddThemeFontSizeOverride("font_size", 15); return l; }
    private static HBoxContainer Row(params Control[] children) { var r = new HBoxContainer(); r.AddThemeConstantOverride("separation", 8); foreach (var c in children) r.AddChild(c); return r; }
    private static ScrollContainer Scroll(VBoxContainer box) { var s = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; s.AddChild(box); return s; }

    private void Build()
    {
        _window = new FloatingWindow("Grupos", icon: "people") { Name = "GroupsWindow", Visible = false };
        _window.Place(.5f, .5f, -440, -300, 440, 300);
        var content = _window.Content; content.AddThemeConstantOverride("separation", 8);
        _tabs = new UiTabs { EqualWidth = true, Flat = true, FontSize = 16 }; _tabs.AddTab("Meus grupos", "people"); _tabs.AddTab("Procurar", "search"); _tabs.AddTab("Criar grupo", "plus"); _tabs.AddTab("Avisos"); content.AddChild(_tabs);
        var stack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(860, 470) }; content.AddChild(stack);

        // my groups: the list on the left, the group on the right
        var mine = new HBoxContainer { Visible = true }; mine.SetAnchorsPreset(Control.LayoutPreset.FullRect); mine.AddThemeConstantOverride("separation", 10); stack.AddChild(mine); _minePage = mine;
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(230, 0) }; mine.AddChild(left); left.AddChild(Heading("Seus grupos"));
        _mineList = List(380); _mineList.ItemSelected += i => { if (i >= 0 && i < _mine.Count) OpenGroup(_mine[(int)i].Id); }; left.AddChild(_mineList);
        _detail = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Visible = false }; mine.AddChild(_detail);
        _overview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) }; _overview.AddThemeFontSizeOverride("font_size", 16); _detail.AddChild(_overview);
        _leave = Act("Sair do grupo", UiStyle.ButtonKind.Secondary, LeaveCurrent); _detail.AddChild(_leave);
        _inner = new UiTabs { EqualWidth = true, Flat = true, FontSize = 13 }; foreach (var t in new[] { "Chat", "Anúncios", "Eventos", "Membros", "Cargos", "Pedidos", "Histórico", "Ajustes" }) _inner.AddTab(t); _detail.AddChild(_inner);
        var innerStack = new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 280) }; _detail.AddChild(innerStack);
        _innerPages.Add(BuildChat(innerStack)); _innerPages.Add(BuildAnnouncements(innerStack)); _innerPages.Add(BuildEvents(innerStack)); _innerPages.Add(BuildMembers(innerStack)); _innerPages.Add(BuildRoles(innerStack)); _innerPages.Add(BuildRequests(innerStack)); _innerPages.Add(BuildAudit(innerStack)); _innerPages.Add(BuildSettings(innerStack));
        _inner.TabChanged += i => { for (var p = 0; p < _innerPages.Count; p++) _innerPages[p].Visible = p == i; };
        _innerPages[0].Visible = true;

        // search
        var search = new VBoxContainer { Visible = false }; search.SetAnchorsPreset(Control.LayoutPreset.FullRect); search.AddThemeConstantOverride("separation", 8); stack.AddChild(search); _searchPage = search;
        _query = Field("Nome, sigla ou assunto"); _query.TextSubmitted += _ => Search(); search.AddChild(Row(_query, Act("Procurar", UiStyle.ButtonKind.Primary, () => Search())));
        _foundList = List(300); _foundList.ItemSelected += _ => UpdateSearchButtons(); search.AddChild(_foundList);
        _join = Act("Entrar", UiStyle.ButtonKind.Primary, JoinSelected); search.AddChild(_join);

        // create
        var create = new VBoxContainer { Visible = false }; create.SetAnchorsPreset(Control.LayoutPreset.FullRect); create.AddThemeConstantOverride("separation", 8); stack.AddChild(create); _createPage = create;
        _newName = Field("Nome do grupo (3 a 40 caracteres)", 40); _newTag = Field("Sigla (2 a 5 letras ou números, opcional)", 5); _newDescription = Field("Descrição (até 600 caracteres)", 600);
        _newPrivacy = Choice("Público (aparece na busca)", "Privado (só por convite)"); _newPolicy = Choice("Entrada livre", "Entra com aprovação", "Só por convite"); _newPolicy.Select(1);
        _newPrivacy.ItemSelected += i => { _newPolicy.Disabled = i == 1; if (i == 1) _newPolicy.Select(2); };
        _newColour = new ColorPickerButton { Color = new Color("#12F2E0"), CustomMinimumSize = new Vector2(120, 36), EditAlpha = false, FocusMode = Control.FocusModeEnum.None };
        create.AddChild(_newName); create.AddChild(_newTag); create.AddChild(_newDescription); create.AddChild(_newPrivacy); create.AddChild(_newPolicy); create.AddChild(Row(new Label { Text = "Cor do grupo" }, _newColour));
        create.AddChild(Note("Você será o líder. Depois dá para criar cargos, definir permissões, convidar e aprovar pedidos."));
        create.AddChild(Act("Criar grupo", UiStyle.ButtonKind.Primary, Create));

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(500, 0) }; _status.AddThemeColorOverride("font_color", UiStyle.AccentText); _status.AddThemeFontSizeOverride("font_size", 16); content.AddChild(_status);
        _noticesPage = BuildNotices(stack);
        _tabs.TabChanged += i => { _minePage.Visible = i == 0; _searchPage.Visible = i == 1; _createPage.Visible = i == 2; _noticesPage.Visible = i == 3; if (i == 1) Search(); if (i == 3) LoadNotices(); };
        AddChild(_window);
    }

    private Control BuildMembers(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        _memberList = List(150); _memberList.ItemSelected += _ => UpdateButtons(); page.AddChild(_memberList);
        _kick = Act("Expulsar", UiStyle.ButtonKind.Secondary, KickSelected); _ban = Act("Banir", UiStyle.ButtonKind.Secondary, () => BanSelected()); _mute = Act("Silenciar 30 min / liberar", UiStyle.ButtonKind.Secondary, () => MuteSelected());
        page.AddChild(Row(_kick, _ban, _mute));
        _roleChoice = Choice(); _assign = Act("Dar este cargo", UiStyle.ButtonKind.Primary, AssignSelected); page.AddChild(Row(_roleChoice, _assign));
        _inviteName = Field("Nome de quem convidar"); _invite = Act("Convidar", UiStyle.ButtonKind.Primary, InviteByName); _inviteName.TextSubmitted += _ => InviteByName(); page.AddChild(Row(_inviteName, _invite));
        return page;
    }

    private Control BuildRoles(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        _roleList = List(90); _roleList.ItemSelected += _ => ShowRole(); page.AddChild(_roleList);
        _roleName = Field("Nome do cargo", 32); _roleRank = Field("Nível (1 a 99)", 2); _roleRank.CustomMinimumSize = new Vector2(120, 38); _roleRank.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; page.AddChild(Row(_roleName, _roleRank));
        var grid = new GridContainer { Columns = 2 }; foreach (var (key, label, help) in GroupPermissions.All) { var box = new CheckBox { Text = label, TooltipText = help, FocusMode = Control.FocusModeEnum.None }; box.AddThemeFontSizeOverride("font_size", 14); grid.AddChild(box); _permissionBoxes[key] = box; }
        page.AddChild(Scroll(new VBoxContainer { Name = "PermissionHolder" })); page.GetChild<ScrollContainer>(page.GetChildCount() - 1).GetChild(0).AddChild(grid); page.GetChild<ScrollContainer>(page.GetChildCount() - 1).CustomMinimumSize = new Vector2(0, 90);
        _roleCreate = Act("Criar cargo", UiStyle.ButtonKind.Primary, CreateRole); _roleSave = Act("Salvar", UiStyle.ButtonKind.Secondary, SaveRole); _roleDelete = Act("Apagar", UiStyle.ButtonKind.Secondary, DeleteRole); page.AddChild(Row(_roleCreate, _roleSave, _roleDelete));
        page.AddChild(Note("Cada cargo tem um nível: só se age sobre quem tem nível menor que o seu, e só se dá permissões que você mesmo tem."));
        return page;
    }

    private Control BuildRequests(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        page.AddChild(Heading("Pedidos de entrada")); _requestList = List(100); _requestList.ItemSelected += _ => UpdateButtons(); page.AddChild(_requestList);
        _approve = Act("Aprovar", UiStyle.ButtonKind.Primary, () => DecideSelected(true)); _deny = Act("Recusar", UiStyle.ButtonKind.Secondary, () => DecideSelected(false)); page.AddChild(Row(_approve, _deny));
        page.AddChild(Heading("Banidos")); _banList = List(80); _banList.ItemSelected += _ => UpdateButtons(); page.AddChild(_banList);
        _unban = Act("Desbanir", UiStyle.ButtonKind.Secondary, UnbanSelected); page.AddChild(_unban);
        return page;
    }

    private Control BuildAudit(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        page.AddChild(Note("O que foi feito na administração do grupo, do mais novo para o mais antigo. Só quem tem permissão enxerga.")); _auditList = List(250); page.AddChild(_auditList);
        return page;
    }

    private Control BuildSettings(Control stack)
    {
        var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 6);
        _setName = Field("Nome", 40); _setTag = Field("Sigla", 5); _setDescription = Field("Descrição", 600); _setLimit = Field("Limite de membros", 4);
        _setPrivacy = Choice("Público", "Privado"); _setPolicy = Choice("Entrada livre", "Entra com aprovação", "Só por convite"); _setColour = new ColorPickerButton { Color = new Color("#12F2E0"), CustomMinimumSize = new Vector2(100, 34), EditAlpha = false, FocusMode = Control.FocusModeEnum.None };
        box.AddChild(_setName); box.AddChild(Row(_setTag, _setLimit)); box.AddChild(_setDescription); box.AddChild(Row(_setPrivacy, _setPolicy, _setColour));
        _saveSettings = Act("Salvar perfil do grupo", UiStyle.ButtonKind.Primary, SaveSettings); box.AddChild(_saveSettings);
        box.AddChild(Heading("Liderança e fim do grupo (só o líder)"));
        _leaderChoice = Choice(); _confirm = Field("Escreva o nome do grupo para confirmar"); box.AddChild(Row(new Label { Text = "Novo líder" }, _leaderChoice)); box.AddChild(_confirm);
        _transfer = Act("Passar a liderança", UiStyle.ButtonKind.Secondary, TransferLeadership); _dissolve = Act("Dissolver o grupo", UiStyle.ButtonKind.Secondary, Dissolve); box.AddChild(Row(_transfer, _dissolve));
        box.AddChild(Note("Para dissolver, o grupo não pode ter terrenos. Todos os membros são avisados e o histórico fica guardado."));
        var scroll = Scroll(box); scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect); scroll.Visible = false; stack.AddChild(scroll);
        return scroll;
    }

    // ---- hooks for tests ------------------------------------------------------------------------------------------------------------------------------------------

    public void SetCreateForTesting(string name, string tag, string description, int privacy, int policy) { _newName.Text = name; _newTag.Text = tag; _newDescription.Text = description; _newPrivacy.Select(privacy); _newPolicy.Select(policy); }
    public void SelectMemberForTesting(int index) { _memberList.Select(index); UpdateButtons(); }
    public void SelectRoleForTesting(int index) { _roleList.Select(index); ShowRole(); }
    public void SelectFoundForTesting(int index) { _foundList.Select(index); UpdateSearchButtons(); }
    public void SelectRequestForTesting(int index) { _requestList.Select(index); UpdateButtons(); }
    public void SelectBanForTesting(int index) { _banList.Select(index); UpdateButtons(); }
    public void SetInviteForTesting(string name) => _inviteName.Text = name;
    public void SetRoleFormForTesting(string name, int rank, params string[] permissions) { _roleName.Text = name; _roleRank.Text = rank.ToString(CultureInfo.InvariantCulture); foreach (var (key, box) in _permissionBoxes) box.SetPressedNoSignal(permissions.Contains(key)); }
    public void SetConfirmForTesting(string text) => _confirm.Text = text;
    public void ChooseRoleForTesting(string name) { for (var i = 0; i < _roleChoice.ItemCount; i++) if (_roleChoice.GetItemText(i) == name) _roleChoice.Select(i); }
    public void ChooseLeaderForTesting(string name) { for (var i = 0; i < _leaderChoice.ItemCount; i++) if (_leaderChoice.GetItemText(i) == name) _leaderChoice.Select(i); }
    public void SetSettingsForTesting(string name, string description) { _setName.Text = name; _setDescription.Text = description; }
    public bool ButtonEnabledForTesting(string which) => which switch { "kick" => !_kick.Disabled, "ban" => !_ban.Disabled, "mute" => !_mute.Disabled, "assign" => !_assign.Disabled, "invite" => !_invite.Disabled, "approve" => !_approve.Disabled, "roleCreate" => !_roleCreate.Disabled, "roleSave" => !_roleSave.Disabled, "roleDelete" => !_roleDelete.Disabled, "settings" => !_saveSettings.Disabled, "transfer" => !_transfer.Disabled, "dissolve" => !_dissolve.Disabled, "join" => !_join.Disabled, "leave" => !_leave.Disabled, _ => false };
    public bool PermissionBoxEnabledForTesting(string key) => !_permissionBoxes[key].Disabled;
    public string OverviewForTesting => _overview.Text;
    public int AuditLinesForTesting => _auditList.ItemCount;
    public bool InnerTabsVisibleForTesting => _inner.Visible;
    public string JoinTextForTesting => _join.Text;
    public Control WindowForTesting => _window;
}
