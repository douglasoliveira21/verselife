using System;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The groups: the ones the person belongs to (open, leave), a search over all groups (join, or ask to join), a form to create one, and the invitations received (accept, decline).
    /// A group is opened in its own window: the description, the members (kick, with a second touch), the requests to join (approve, deny) and a field to invite someone by identifier.
    /// What each person may do in a group is decided by the API from their role; a refusal is shown as it came.
    /// </summary>
    public sealed class GroupsWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly ApiList _mine, _invites, _found; private readonly TextField _search, _name, _about; private readonly Label _status;
        private readonly System.Collections.Generic.Dictionary<string, VisualElement> _tabs = new System.Collections.Generic.Dictionary<string, VisualElement>();
        public string CurrentTab { get; private set; }
        public int MineCount => _mine.Count;
        public int InviteCount => _invites.Count;

        public GroupsWindow(ApiContext context) : base(context.Ui, "groups", "Grupos", 460f, 500f, new Vector2(440, 90))
        {
            _c = context; var bar = UiKit.Row(); _status = UiKit.Text("", "lv-small", "lv-muted");
            foreach (var name in new[] { "Meus", "Buscar", "Criar", "Convites" }) { var captured = name; bar.Add(UiKit.Button(name, () => Show(captured))); var tab = new VisualElement(); tab.style.flexGrow = 1; tab.style.display = DisplayStyle.None; _tabs[name] = tab; }
            Body.Add(bar); Body.Add(_status);
            _mine = new ApiList(context, () => "/api/v1/groups/mine", row => { var id = row.Id("id", "groupId"); return UiKit.Row(UiKit.Text(row.First("name"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("role"), "lv-muted"), UiKit.Button("Abrir", () => new GroupDetailWindow(_c, id, row.First("name")).Open()), Controls.Confirm("Sair", () => "Confirmar saída", () => Post("DELETE", "/api/v1/groups/" + id + "/join", null, "Você saiu do grupo.", () => _mine.Reload()))); }, "Você não está em nenhum grupo.");
            _tabs["Meus"].Add(_mine);
            _search = UiKit.Field("Buscar grupos", false, 60); _search.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) _found.Reload(); }, TrickleDown.TrickleDown);
            _found = new ApiList(context, () => string.IsNullOrWhiteSpace(_search.value) ? null : "/api/v1/groups/search?q=" + Uri.EscapeDataString(_search.value.Trim()), row => { var id = row.Id("id", "groupId"); return UiKit.Row(UiKit.Text(row.First("name"), "lv-bold"), UiKit.Spacer(), UiKit.Button("Entrar", () => Post("POST", "/api/v1/groups/" + id + "/join", null, "Pedido enviado ou você entrou.", () => { }), "lv-btn-primary")); }, "Digite o nome do grupo.", false);
            _tabs["Buscar"].Add(_search); _tabs["Buscar"].Add(_found);
            _name = UiKit.Field("Nome do grupo", false, 60); _about = UiKit.Field("Descrição", false, 300); _tabs["Criar"].Add(_name); _tabs["Criar"].Add(_about); _tabs["Criar"].Add(UiKit.Button("Criar grupo", Create, "lv-btn-primary"));
            _invites = new ApiList(context, () => "/api/v1/social/groups/invites", row => { var id = row.Id("inviteId", "id"); return UiKit.Row(UiKit.Text(row.First("groupName") + " (de " + row.First("fromUsername") + ")", "lv-bold"), UiKit.Spacer(), UiKit.Button("Aceitar", () => Post("POST", "/api/v1/social/groups/invites/" + id + "/accept", null, "Você entrou no grupo.", () => _invites.Reload()), "lv-btn-primary"), UiKit.Button("Recusar", () => Post("POST", "/api/v1/social/groups/invites/" + id + "/decline", null, null, () => _invites.Reload()))); }, "Nenhum convite.");
            _tabs["Convites"].Add(_invites); foreach (var tab in _tabs.Values) Body.Add(tab); Show("Meus");
        }

        public void Show(string tab) { CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None; if (tab == "Meus") _mine.Reload(); else if (tab == "Convites") _invites.Reload(); }

        private void Post(string method, string path, string json, string success, Action then) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { if (success != null) _c.Notices.Show(success, NoticeKind.Good); _status.text = ""; then(); } else { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); } });

        public void Create()
        {
            var name = _name.value.Trim(); if (name.Length < 3) { _status.text = "O nome do grupo precisa ter pelo menos 3 letras."; _status.AddToClassList("lv-bad"); return; }
            Post("POST", "/api/v1/groups", JsonSerializer.Serialize(new { name, description = _about.value.Trim(), imageUrl = (string)null }), "Grupo criado.", () => { _name.value = ""; _about.value = ""; Show("Meus"); });
        }

        public TextField NameField => _name;
    }

    public sealed class GroupDetailWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Guid _group; private readonly ApiList _members, _requests; private readonly Label _status;
        public int MemberCount => _members.Count;
        public int RequestCount => _requests.Count;
        public ConversationPane Chat { get; private set; }

        public GroupDetailWindow(ApiContext context, Guid group, string name) : base(context.Ui, "group-" + group.ToString("N").Substring(0, 8), name.Length == 0 ? "Grupo" : name, 420f, 700f, new Vector2(500, 20))
        {
            _c = context; _group = group; var about = UiKit.Text("", "lv-muted"); about.style.whiteSpace = WhiteSpace.Normal; Body.Add(about); _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status);
            this.Then(context.Api.ApiAsync("GET", "/api/v1/groups/" + group), r => { if (r.Status == 200) { var row = Json.One(r.Body); var g = row.Has("group") ? new Row(row.Element.GetProperty("group")) : row; about.text = g.First("description"); } });
            Body.Add(UiKit.Text("Membros", "lv-bold", "lv-accent"));
            _members = new ApiList(context, () => "/api/v1/groups/" + group + "/members", row => { var id = row.Id("userId", "id"); return UiKit.Row(UiKit.Text(row.First("username", "displayName", "name"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("role"), "lv-muted"), Controls.Confirm("Expulsar", () => "Confirmar expulsão", () => Act("POST", "/api/v1/groups/" + group + "/members/" + id + "/kick", null, () => _members.Reload()))); }, "Sem membros.");
            _members.style.maxHeight = 170; Body.Add(_members);
            Body.Add(UiKit.Text("Pedidos para entrar", "lv-bold", "lv-accent"));
            _requests = new ApiList(context, () => "/api/v1/groups/" + group + "/requests", row => { var id = row.Id("id", "requestId"); return UiKit.Row(UiKit.Text(row.First("username", "displayName", "userId"), "lv-bold"), UiKit.Spacer(), UiKit.Button("Aprovar", () => Act("POST", "/api/v1/groups/" + group + "/requests/" + id + "/approve", null, () => { _requests.Reload(); _members.Reload(); }), "lv-btn-primary"), UiKit.Button("Negar", () => Act("POST", "/api/v1/groups/" + group + "/requests/" + id + "/deny", null, () => _requests.Reload()))); }, "Nenhum pedido.");
            _requests.style.maxHeight = 110; Body.Add(_requests);
            Body.Add(UiKit.Text("Conversa do grupo", "lv-bold", "lv-accent")); if (context.OpenConversation != null) Body.Add(UiKit.Button("Abrir na conversa", () => context.OpenConversation(group, "Grupo", true))); Chat = new ConversationPane(context, () => "/api/v1/social/messages?groupId=" + group + "&page=1&pageSize=50", text => JsonSerializer.Serialize(new { content = text, groupId = group, clientMessageId = Guid.NewGuid().ToString("N") })); Chat.style.minHeight = 150; Body.Add(Chat); Chat.FollowLive((type, data) => type == "group_message" && ApiContext.Field(data, "groupId") == group.ToString());
            var invite = UiKit.Field("Convidar (identificador da pessoa)", false, 40); Body.Add(invite);
            Body.Add(UiKit.Button("Convidar", () => { if (!Guid.TryParse(invite.value.Trim(), out var id)) { Say("Digite o identificador da pessoa.", true); return; } Act("POST", "/api/v1/groups/" + group + "/invites", JsonSerializer.Serialize(new { userId = id }), () => { invite.value = ""; _c.Notices.Show("Convite enviado.", NoticeKind.Good); }); }));
        }

        private void Say(string text, bool error) { _status.text = text; _status.EnableInClassList("lv-bad", error); }
        private void Act(string method, string path, string json, Action then) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { Say("", false); then(); } else Say(UiTasks.Explain(r.Status), true); });
    }

    /// <summary>What can be done to a person: see the profile, ask for friendship, write, block or unblock, and report (with a reason, after a second touch). Their presence (online, away, busy) is shown when the API tells it.</summary>
    public sealed class PersonActionsWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _status, _presence; private readonly TextField _reason;
        public Guid User { get; }

        public PersonActionsWindow(ApiContext context, Guid user, string name, Action<Guid> openProfile = null, Action<Guid, string> openChat = null) : base(context.Ui, "person-" + user.ToString("N").Substring(0, 8), name, 340f, -1f, new Vector2(560, 150))
        {
            _c = context; User = user; _presence = UiKit.Text("…", "lv-muted"); Body.Add(_presence); _status = UiKit.Text("", "lv-small", "lv-muted"); _status.style.whiteSpace = WhiteSpace.Normal; Body.Add(_status);
            this.Then(context.Api.ApiAsync("GET", "/api/v1/social/presence/" + user), r => _presence.text = r.Status == 200 ? "Está " + PresenceWords(Json.One(r.Body).First("status")) : "Presença desconhecida");
            Body.Add(UiKit.Button("Ver o perfil", () => openProfile?.Invoke(user))); Body.Add(UiKit.Button("Escrever", () => openChat?.Invoke(user, name)));
            Body.Add(UiKit.Row(UiKit.Button("Trazer para cá", () => Invite("offer")), UiKit.Button("Pedir para ir até", () => Invite("request"))));
            Body.Add(UiKit.Button("Pedir amizade", () => Act("POST", "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = user }), "Pedido de amizade enviado.")));
            Body.Add(Controls.Confirm("Bloquear", () => "Confirmar o bloqueio", () => Act("POST", "/api/v1/social/blocks/" + user, null, "Pessoa bloqueada.")));
            Body.Add(UiKit.Button("Desbloquear", () => Act("DELETE", "/api/v1/social/blocks/" + user, null, "Pessoa desbloqueada.")));
            Body.Add(UiKit.Divider()); _reason = UiKit.Field("Motivo da denúncia", false, 300); Body.Add(_reason);
            Body.Add(Controls.Confirm("Denunciar", () => "Confirmar a denúncia", Report, "lv-btn-secondary"));
        }

        public TextField ReasonField => _reason;
        public void Block() => Act("POST", "/api/v1/social/blocks/" + User, null, "Pessoa bloqueada.");

        public static string PresenceWords(string status) => status switch { "online" => "online", "away" => "ausente", "busy" => "ocupada", "offline" => "offline", "invisible" => "offline", _ => "—" };

        public void Report()
        {
            var reason = _reason.value.Trim(); if (reason.Length < 3) { _status.text = "Escreva o motivo da denúncia."; _status.AddToClassList("lv-bad"); return; }
            Act("POST", "/api/v1/social/reports", JsonSerializer.Serialize(new { reportedUserId = User, messageId = (Guid?)null, islandId = (Guid?)null, reason }), "Denúncia enviada. Obrigado.");
        }

        private void Act(string method, string path, string json, string done) => this.Then(_c.Api.ApiAsync(method, path, json), r => { if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show(done, NoticeKind.Good); _status.text = ""; } else { _status.text = UiTasks.Explain(r.Status); _status.AddToClassList("lv-bad"); } });

        /// <summary>Invites the person to be brought here ("offer") or asks to go to them ("request"); both must be on the same island. The answer comes back through the live channel.</summary>
        public void Invite(string kind) => this.Then(_c.Api.ApiAsync("POST", "/api/v1/social/teleports", JsonSerializer.Serialize(new { targetUserId = User, kind, islandId = _c.CurrentIsland() })), r =>
        {
            if (r.Status >= 200 && r.Status < 300) { _c.Notices.Show(kind == "offer" ? "Convite enviado: esperando a resposta." : "Pedido enviado: esperando a resposta.", NoticeKind.Good); _status.text = ""; }
            else { _status.text = LiveEvents.InviteError(r.Status, r.Body); _status.AddToClassList("lv-bad"); }
        });
        public string Notice => _status.text;
    }

    /// <summary>A person's profile: the name and the short text; for one's own, the text can be edited and a post written; the feed of posts below.</summary>
    public sealed class ProfileWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Label _name, _status; private readonly TextField _who, _bio, _post; private readonly ApiList _feed; private Guid _shown; private readonly VisualElement _mine;
        public string ShownName => _name.text;
        public string Bio { get => _bio.value; set => _bio.value = value; }
        public string PostText { get => _post.value; set => _post.value = value; }
        public bool Editable { get; private set; }

        public ProfileWindow(ApiContext context) : base(context.Ui, "profile", "Perfil", 400f, 560f, new Vector2(540, 120))
        {
            _c = context; _name = UiKit.Text("…", "lv-title-text", "lv-accent"); _status = UiKit.Text("", "lv-small", "lv-bad"); Body.Add(_name); Body.Add(_status);
            _bio = UiKit.Field("Sobre", false, 1000); _bio.multiline = true; _bio.style.minHeight = 70; Body.Add(_bio);
            _mine = new VisualElement(); _mine.Add(UiKit.Button("Salvar o texto", SaveBio, "lv-btn-primary")); _post = UiKit.Field("Escrever uma publicação", false, 500); _mine.Add(_post); _mine.Add(UiKit.Button("Publicar", Publish)); Body.Add(_mine);
            Body.Add(UiKit.Text("Publicações", "lv-bold", "lv-accent")); _feed = new ApiList(context, () => _shown == Guid.Empty ? null : "/api/v1/users/" + _shown + "/profile/feed", row => { var line = new VisualElement(); line.Add(UiKit.Text(row.First("text", "content"), "lv-muted")); return line; }, "Nenhuma publicação.", false); _feed.style.maxHeight = 150; Body.Add(_feed);
            _mine.Add(UiKit.Button("Galeria e capa…", () => new GalleryWindow(_c).Open())); _mine.Add(UiKit.Button("Casamento…", () => new MarriageWindow(_c).Open()));
            _who = UiKit.Field("Ver o perfil de (identificador)", false, 40); Body.Add(_who); Body.Add(UiKit.Button("Ver", () => Load(Guid.TryParse(_who.value.Trim(), out var id) ? id : _c.Api.UserId))); Load(_c.Api.UserId);
        }

        public void Load(Guid user)
        {
            if (user == Guid.Empty) { _status.text = "Entre para ver o perfil."; return; }
            _shown = user; Editable = user == _c.Api.UserId; _mine.style.display = Editable ? DisplayStyle.Flex : DisplayStyle.None; _bio.isReadOnly = !Editable;
            this.Then(_c.Api.ApiAsync("GET", "/api/v1/users/" + user + "/profile/full"), r =>
            {
                if (r.Status != 200) { _status.text = UiTasks.Explain(r.Status); return; }
                var row = Json.One(r.Body); _status.text = ""; var profile = row.Has("profile") ? new Row(row.Element.GetProperty("profile")) : row;
                _name.text = profile.First("displayName", "username", "name"); _bio.value = profile.First("bio", "about", "description", "aboutMe"); _feed.Reload();
            });
        }

        public void SaveBio() => this.Then(_c.Api.ApiAsync("PUT", "/api/v1/users/me/profile/details", JsonSerializer.Serialize(new { bio = _bio.value, profilePhotoUrl = (string)null, privacyJson = (string)null })), r => { if (r.Status >= 200 && r.Status < 300) _c.Notices.Show("Perfil salvo.", NoticeKind.Good); else { _status.text = UiTasks.Explain(r.Status); } });

        public void Publish()
        {
            var text = ChatPanel.Clean(_post.value); if (text == null) return;
            this.Then(_c.Api.ApiAsync("POST", "/api/v1/users/me/profile/posts", JsonSerializer.Serialize(new { text, galleryImageId = (Guid?)null, visibility = "public" })), r => { if (r.Status >= 200 && r.Status < 300) { _post.value = ""; _feed.Reload(); } else _status.text = UiTasks.Explain(r.Status); });
        }
    }
}
