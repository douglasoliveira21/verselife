using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// People and conversations: Amigos (the friends, with a message and a remove button each, and a way to ask someone by their identifier), Pedidos (friend requests to accept or
    /// decline), Mensagens (the private conversation with a friend: the last 50 messages, a field to answer) and Grupos (the groups the person belongs to, with a leave button).
    /// Everything is the platform's own social API (/api/v1/social/...); the server decides who may do what and rate-limits the messages.
    /// </summary>
    public sealed class SocialWindow : FloatingWindow
    {
        private readonly ApiContext _c; private readonly Dictionary<string, VisualElement> _tabs = new Dictionary<string, VisualElement>(); private readonly Dictionary<string, Button> _buttons = new Dictionary<string, Button>();
        private readonly ScrollView _friends, _requests, _groups, _log; private readonly TextField _chatInput, _addId; private readonly Label _talkingTo, _status;
        private Guid _with;
        public string CurrentTab { get; private set; }
        public ConversationPane IslandChat { get; }
        public Guid Conversation => _with;
        public int LiveMessages { get; private set; }
        public int FriendCount => _friends.contentContainer.childCount;
        public int MessageCount => _log.contentContainer.childCount;

        public SocialWindow(ApiContext context) : base(context.Ui, "social", "Social", 440f, 470f, new Vector2(460, 90))
        {
            _c = context; var bar = UiKit.Row(); bar.style.marginBottom = 6;
            foreach (var name in new[] { "Amigos", "Pedidos", "Mensagens", "Grupos", "Ilha" }) { var captured = name; var button = UiKit.Button(name, () => Show(captured)); _buttons[name] = button; bar.AddToClassList("lv-tabs"); bar.Add(button); var tab = new VisualElement(); tab.style.flexGrow = 1; tab.style.display = DisplayStyle.None; _tabs[name] = tab; }
            Body.Add(bar); _status = UiKit.Text("", "lv-small", "lv-muted"); Body.Add(_status);
            _friends = new ScrollView(); _friends.style.flexGrow = 1; _addId = UiKit.Field("Pedir amizade (identificador da pessoa)", false, 40); var ask = UiKit.Button("Pedir amizade", AskFriend); _tabs["Amigos"].Add(_friends); _tabs["Amigos"].Add(_addId); _tabs["Amigos"].Add(ask);
            _requests = new ScrollView(); _requests.style.flexGrow = 1; _tabs["Pedidos"].Add(_requests);
            _talkingTo = UiKit.Text("Escolha um amigo na aba Amigos.", "lv-muted"); _log = new ScrollView(); _log.style.flexGrow = 1; _chatInput = UiKit.Field("", false, 600); _chatInput.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) SendMessage(); }, TrickleDown.TrickleDown);
            _tabs["Mensagens"].Add(_talkingTo); _tabs["Mensagens"].Add(_log); _tabs["Mensagens"].Add(_chatInput);
            _groups = new ScrollView(); _groups.style.flexGrow = 1; _tabs["Grupos"].Add(_groups);
            IslandChat = new ConversationPane(context, () => "/api/v1/social/messages?channel=local&page=1&pageSize=50", text => JsonSerializer.Serialize(new { content = text, islandId = context.CurrentIsland(), clientMessageId = Guid.NewGuid().ToString("N") }));
            _tabs["Ilha"].Add(UiKit.Text("As mensagens da ilha na plataforma (o histórico fica guardado; o chat ao vivo é o da barra de baixo).", "lv-muted", "lv-small")); _tabs["Ilha"].Add(IslandChat);
            foreach (var tab in _tabs.Values) Body.Add(tab);
            Show("Amigos");
            // live: a message from the person on screen shows at once; a new or accepted friendship refreshes the lists
            _c.FollowWhileShown(this, (type, data) => { if (type == "message" && _with != Guid.Empty && ApiContext.Field(data, "from") == _with.ToString()) { LiveMessages++; LoadMessages(); } else if (type == "friend_request") LoadRequests(); else if (type == "friend_accepted") LoadFriends(); });
        }

        public void Show(string tab)
        {
            CurrentTab = tab; foreach (var pair in _tabs) pair.Value.style.display = pair.Key == tab ? DisplayStyle.Flex : DisplayStyle.None; foreach (var pair in _buttons) pair.Value.EnableInClassList("lv-btn-selected", pair.Key == tab);
            if (tab == "Amigos") LoadFriends(); else if (tab == "Pedidos") LoadRequests(); else if (tab == "Mensagens") LoadMessages(); else LoadGroups();
        }

        private void Say(string text, bool error = false) { _status.text = text; _status.EnableInClassList("lv-bad", error); }

        private void Call(string method, string path, string json, Action<int, string> done) => this.Then(_c.Api.ApiAsync(method, path, json), r => done(r.Status, r.Body));

        public void LoadFriends() => Call("GET", "/api/v1/social/friends", null, (status, body) =>
        {
            _friends.Clear(); if (status != 200) { Say(UiTasks.Explain(status), true); return; }
            var rows = Json.Rows(body); Say(rows.Count == 0 ? "Você ainda não tem amigos." : "");
            foreach (var row in rows)
            {
                var id = row.Id(); var name = row.First("username", "displayName", "name"); var line = UiKit.Row(UiKit.Text(name, "lv-bold"), UiKit.Spacer(), UiKit.Button("Mensagem", () => Talk(id, name)), UiKit.Button("Remover", () => Remove(id)));
                line.AddToClassList("lv-row"); _friends.Add(line);
            }
        });

        private void Remove(Guid id) => Call("DELETE", "/api/v1/social/friends/" + id, null, (status, _) => { if (status >= 200 && status < 300) { _c.Notices.Show("Amizade desfeita.", NoticeKind.Info); LoadFriends(); } else Say(UiTasks.Explain(status), true); });

        private void AskFriend()
        {
            if (!Guid.TryParse(_addId.value.Trim(), out var id)) { Say("Digite o identificador da pessoa (um código como 3f2b…).", true); return; }
            Call("POST", "/api/v1/social/friends/requests", JsonSerializer.Serialize(new { userId = id }), (status, _) => { if (status >= 200 && status < 300) { _c.Notices.Show("Pedido de amizade enviado.", NoticeKind.Good); _addId.value = ""; Say(""); } else Say(UiTasks.Explain(status), true); });
        }

        public void LoadRequests() => Call("GET", "/api/v1/social/friends/requests", null, (status, body) =>
        {
            _requests.Clear(); if (status != 200) { Say(UiTasks.Explain(status), true); return; }
            var rows = Json.Rows(body); Say(rows.Count == 0 ? "Nenhum pedido de amizade." : "");
            foreach (var row in rows)
            {
                var id = row.Id("userId", "fromUserId", "requesterId", "id"); var name = row.First("username", "displayName", "name", "fromUsername");
                var line = UiKit.Row(UiKit.Text(name.Length > 0 ? name : id.ToString().Substring(0, 8), "lv-bold"), UiKit.Spacer(),
                    UiKit.Button("Aceitar", () => Call("POST", "/api/v1/social/friends/" + id + "/accept", null, (s, _) => { if (s >= 200 && s < 300) { _c.Notices.Show("Vocês agora são amigos.", NoticeKind.Good); LoadRequests(); } else Say(UiTasks.Explain(s), true); }), "lv-btn-primary"),
                    UiKit.Button("Recusar", () => Call("POST", "/api/v1/social/friends/" + id + "/decline", null, (s, _) => LoadRequests())));
                line.AddToClassList("lv-row"); _requests.Add(line);
            }
        });

        /// <summary>Opens the conversation with a friend.</summary>
        public void Talk(Guid id, string name) { _with = id; _talkingTo.text = "Conversa com " + name; Show("Mensagens"); }

        public void LoadMessages()
        {
            _log.Clear(); if (_with == Guid.Empty) return;
            Call("GET", "/api/v1/social/messages?channel=private&conversationWith=" + _with + "&page=1&pageSize=50", null, (status, body) =>
            {
                _log.Clear(); if (status != 200) { Say(UiTasks.Explain(status), true); return; }
                var mine = _c.Api.UserId;
                foreach (var row in Json.Rows(body).OrderBy(r => r.Text("createdAt")))
                {
                    var fromMe = Guid.TryParse(row.Text("senderUserId"), out var sender) && sender == mine; var line = UiKit.Text((fromMe ? "Você: " : "") + row.Text("content"), fromMe ? "lv-accent" : "lv-bold"); line.style.whiteSpace = WhiteSpace.Normal; line.style.marginBottom = 3; _log.Add(line);
                }
                _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue / 2f)).StartingIn(30);
            });
        }

        public bool SendMessage()
        {
            var text = ChatPanel.Clean(_chatInput.value); if (text == null || _with == Guid.Empty) return false;
            Call("POST", "/api/v1/social/messages", JsonSerializer.Serialize(new { content = text, recipientUserId = _with, clientMessageId = Guid.NewGuid().ToString("N") }), (status, _) => { if (status >= 200 && status < 300) { _chatInput.value = ""; LoadMessages(); } else Say(UiTasks.Explain(status), true); });
            return true;
        }

        public TextField MessageField => _chatInput;

        public void LoadGroups() => Call("GET", "/api/v1/social/groups", null, (status, body) =>
        {
            _groups.Clear(); if (status != 200) { Say(UiTasks.Explain(status), true); return; }
            var rows = Json.Rows(body); Say(rows.Count == 0 ? "Você não está em nenhum grupo." : "");
            foreach (var row in rows)
            {
                var id = row.Id("groupId", "id"); var line = UiKit.Row(UiKit.Text(row.First("name", "title"), "lv-bold"), UiKit.Spacer(), UiKit.Text(row.First("role", "memberRole"), "lv-muted"), UiKit.Button("Sair", () => Call("POST", "/api/v1/social/groups/" + id + "/leave", null, (s, _) => { if (s >= 200 && s < 300) LoadGroups(); else Say(UiTasks.Explain(s), true); })));
                line.AddToClassList("lv-row"); _groups.Add(line);
            }
        });
    }
}
