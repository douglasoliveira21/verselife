using System.Globalization;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Shared.Groups;

namespace LifeVerse.Client.Scripts;

public sealed record GroupChatLine(Guid Id, Guid UserId, string Username, string Text, DateTimeOffset At);
public sealed record GroupAnnouncementData(Guid Id, string Title, string Body, bool Pinned, string Author, DateTimeOffset At);
public sealed record GroupEventData(Guid Id, string Name, string Description, string IslandName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, int Attending, bool Joined, Guid OrganizerUserId);
public sealed record GroupNoticeData(Guid Id, Guid GroupId, string Text, bool Read);

/// <summary>The talking part of the groups window: the chat (live through the notification socket), announcements, events only for members, and the notices.</summary>
public sealed partial class GroupsWindow
{
    private readonly List<GroupChatLine> _chat = [];
    private readonly List<GroupAnnouncementData> _announcements = [];
    private readonly List<GroupEventData> _events = [];
    private readonly List<GroupNoticeData> _notices = [];
    private readonly List<(Guid Id, string Name)> _islands = [];
    private readonly Dictionary<Guid, int> _unreadChat = [];
    private DateTimeOffset? _mutedUntil; private bool _notifyMuted; private Guid? _testUser;

    private Control _noticesPage = null!;
    private ItemList _chatList = null!, _announceList = null!, _eventList = null!, _noticeList = null!;
    private LineEdit _chatInput = null!, _annTitle = null!, _evName = null!, _evStart = null!, _evMinutes = null!, _evCapacity = null!;
    private TextEdit _annText = null!; private Label _announceBody = null!, _eventBody = null!;
    private CheckBox _annPinned = null!, _notifyOff = null!;
    private OptionButton _evIsland = null!;
    private Button _chatSend = null!, _chatDelete = null!, _annPost = null!, _annPin = null!, _annDelete = null!, _evCreate = null!, _evJoin = null!, _evCancel = null!, _noticesRead = null!;

    public IReadOnlyList<GroupChatLine> Chat => _chat;
    public IReadOnlyList<GroupAnnouncementData> Announcements => _announcements;
    public IReadOnlyList<GroupEventData> Events => _events;
    public IReadOnlyList<GroupNoticeData> Notices => _notices;

    private Guid MyId => _testUser ?? GetNodeOrNull<SessionManager>("/root/SessionManager")?.UserId ?? Guid.Empty;
    private bool Muted(DateTimeOffset now) => _mutedUntil is { } until && until > now;

    // ---- chat -------------------------------------------------------------------------------------------------------------------------------------

    public void LoadChat()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/chat?take=100", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _chat.Clear(); _chat.AddRange(ReadList(body, m => new GroupChatLine(IdOf(m, "id"), IdOf(m, "userId"), Str(m, "username"), Str(m, "content"), Time(m, "at") ?? DateTimeOffset.UtcNow)));
            RefreshChat();
        });
    }

    private void RefreshChat()
    {
        _chatList.Clear();
        foreach (var m in _chat) _chatList.AddItem($"[{m.At.ToLocalTime():HH:mm}] {m.Username}: {m.Text}");
        Callable.From(() => { var bar = _chatList.GetVScrollBar(); bar.Value = bar.MaxValue; }).CallDeferred();
        UpdateCommunicationButtons();
    }

    public void SendChat()
    {
        if (_current is not { } g || _chatInput.Text.Trim().Length == 0) return;
        if (Muted(DateTimeOffset.UtcNow)) { Say(Explain(403, "{\"error\":\"muted\"}")); return; }
        var text = _chatInput.Text;
        Call(Godot.HttpClient.Method.Post, $"/{g.Id}/chat", new { content = text, clientMessageId = Guid.NewGuid().ToString("N") }, (status, body) =>
        {
            if (status is >= 200 and < 300) { if (_chatInput.Text == text) _chatInput.Text = string.Empty; LoadChat(); }
            else { Say(Explain(status, body)); if (ErrorOf(body) == "muted") { _mutedUntil = DateTimeOffset.UtcNow.AddMinutes(1); UpdateCommunicationButtons(); } }
        });
    }

    public void DeleteChatSelected()
    {
        if (_current is { } g && _chatList.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _chat.Count)
            Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/chat/{_chat[sel[0]].Id}", null, "Mensagem apagada.", LoadChat);
    }

    public void SetNotificationsOff(bool off)
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Put, $"/{g.Id}/notification-settings", new { muted = off }, (status, body) =>
        {
            if (status is >= 200 and < 300) { _notifyMuted = off; Say(off ? "Você não será avisado sobre este grupo." : "Os avisos deste grupo voltaram."); }
            else { Say(Explain(status, body)); _notifyOff.SetPressedNoSignal(_notifyMuted); }
        });
    }

    /// <summary>An event from the notification socket: the chat of the open group grows by itself, and the other lists refresh when something new is posted.</summary>
    public void OnLiveEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Str(root, "type") is not { Length: > 0 } type || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return;
            var group = IdOf(data, "groupId"); var open = _current?.Id == group;
            switch (type)
            {
                case "group_message":
                    if (open)
                    {
                        var id = IdOf(data, "id"); if (_chat.Any(m => m.Id == id)) return;
                        _chat.Add(new GroupChatLine(id, IdOf(data, "userId"), Str(data, "username"), Str(data, "content"), Time(data, "at") ?? DateTimeOffset.UtcNow)); RefreshChat();
                    }
                    else if (group != Guid.Empty) { _unreadChat[group] = _unreadChat.GetValueOrDefault(group) + 1; RelabelMine(); }
                    break;
                case "group_announcement" when open: LoadAnnouncements(); break;
                case "group_event" or "group_event_cancelled" when open: LoadEvents(); break;
                case "group_kicked" or "group_banned" or "group_dissolved" or "group_role_changed" or "group_leader_changed": LoadMine(); if (open) OpenGroup(group); break;
            }
        }
        catch (JsonException) { }
    }

    private void RelabelMine()
    {
        for (var i = 0; i < _mine.Count && i < _mineList.ItemCount; i++)
        {
            var g = _mine[i]; var unread = _unreadChat.GetValueOrDefault(g.Id);
            _mineList.SetItemText(i, $"{(g.Tag.Length > 0 ? $"[{g.Tag}] " : string.Empty)}{g.Name} · {g.Members} membro(s){(unread > 0 ? $" · {unread} nova(s)" : string.Empty)}");
        }
    }

    // ---- announcements -----------------------------------------------------------------------------------------------------------------------------

    public void LoadAnnouncements()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/announcements", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _announcements.Clear(); _announcements.AddRange(ReadList(body, a => new GroupAnnouncementData(IdOf(a, "id"), Str(a, "title"), Str(a, "body"), Flag(a, "pinned"), Str(a, "author"), Time(a, "at") ?? DateTimeOffset.UtcNow)));
            _announceList.Clear(); foreach (var a in _announcements) _announceList.AddItem($"{(a.Pinned ? "📌 " : string.Empty)}{a.Title} · {a.Author}, {a.At.ToLocalTime():dd/MM}");
            _announceBody.Text = _announcements.Count == 0 ? "Nenhum anúncio ainda." : "Escolha um anúncio para ler.";
            UpdateCommunicationButtons();
        });
    }

    private GroupAnnouncementData? SelectedAnnouncement() { var p = _announceList.GetSelectedItems(); return p.Length == 0 || p[0] >= _announcements.Count ? null : _announcements[p[0]]; }

    public void PostAnnouncement()
    {
        if (_current is not { } g) return;
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/announcements", new { title = _annTitle.Text.Trim(), body = _annText.Text.Trim(), pinned = _annPinned.ButtonPressed }, "Anúncio publicado; os membros foram avisados.", () => { _annTitle.Text = string.Empty; _annText.Text = string.Empty; _annPinned.ButtonPressed = false; LoadAnnouncements(); });
    }

    public void TogglePinned() { if (_current is { } g && SelectedAnnouncement() is { } a) Do(Godot.HttpClient.Method.Put, $"/{g.Id}/announcements/{a.Id}", new { pinned = !a.Pinned }, a.Pinned ? "Anúncio desafixado." : "Anúncio fixado.", LoadAnnouncements); }
    public void DeleteAnnouncement() { if (_current is { } g && SelectedAnnouncement() is { } a) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/announcements/{a.Id}", null, "Anúncio apagado.", LoadAnnouncements); }

    // ---- events ---------------------------------------------------------------------------------------------------------------------------------------

    public void LoadEvents()
    {
        if (_current is not { } g) return;
        Call(Godot.HttpClient.Method.Get, $"/{g.Id}/events", null, (status, body) =>
        {
            if (status is < 200 or >= 300) return;
            _events.Clear(); _events.AddRange(ReadList(body, e => new GroupEventData(IdOf(e, "id"), Str(e, "name"), Str(e, "description"), Str(e, "islandName"), Time(e, "startsAt") ?? DateTimeOffset.UtcNow, Time(e, "endsAt") ?? DateTimeOffset.UtcNow, Num(e, "capacity"), Num(e, "attending"), Flag(e, "joined"), IdOf(e, "organizerUserId"))));
            _eventList.Clear(); foreach (var e in _events) _eventList.AddItem($"{e.StartsAt.ToLocalTime():dd/MM HH:mm} · {e.Name} · {e.Attending}/{e.Capacity}{(e.Joined ? " · você vai" : string.Empty)}");
            _eventBody.Text = _events.Count == 0 ? "Nenhum evento marcado." : "Escolha um evento para ver os detalhes.";
            UpdateCommunicationButtons();
        });
        if (Has(GroupPermissions.ManageEvents) && _islands.Count == 0)
            Call(Godot.HttpClient.Method.Get, "/api/v1/discovery/islands?pageSize=50", null, (status, body) =>
            {
                if (status is < 200 or >= 300) return;
                try
                {
                    using var d = JsonDocument.Parse(body); _islands.Clear(); _evIsland.Clear();
                    if (d.RootElement.TryGetProperty("items", out var items)) foreach (var i in items.EnumerateArray()) { _islands.Add((IdOf(i, "id"), Str(i, "name"))); _evIsland.AddItem(Str(i, "name")); }
                }
                catch (JsonException) { }
            });
    }

    private GroupEventData? SelectedEvent() { var p = _eventList.GetSelectedItems(); return p.Length == 0 || p[0] >= _events.Count ? null : _events[p[0]]; }

    private void ShowEvent()
    {
        if (SelectedEvent() is { } e) _eventBody.Text = $"{e.Name}\n{e.StartsAt.ToLocalTime():dd/MM/yyyy HH:mm} até {e.EndsAt.ToLocalTime():HH:mm} · {e.IslandName}\n{e.Attending} de {e.Capacity} lugares\n\n{(e.Description.Length > 0 ? e.Description : "Sem descrição.")}";
        UpdateCommunicationButtons();
    }

    public void CreateEvent()
    {
        if (_current is not { } g) return;
        if (!DateTime.TryParseExact(_evStart.Text.Trim(), ["dd/MM/yyyy HH:mm", "d/M/yyyy H:mm"], Pt, DateTimeStyles.None, out var local)) { Say("Data inválida. Use dia/mês/ano hora:minuto, por exemplo 05/11/2026 20:00."); return; }
        if (_islands.Count == 0 || _evIsland.Selected < 0) { Say(Explain(400, "{\"error\":\"bad_island\"}")); return; }
        var starts = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)); var minutes = int.TryParse(_evMinutes.Text, out var m) ? m : 120;
        Do(Godot.HttpClient.Method.Post, $"/{g.Id}/events", new { name = _evName.Text.Trim(), description = string.Empty, islandId = _islands[_evIsland.Selected].Id, startsAt = starts.UtcDateTime, endsAt = starts.UtcDateTime.AddMinutes(Math.Max(1, minutes)), capacity = int.TryParse(_evCapacity.Text, out var c) ? c : 50 },
            "Evento criado; os membros foram avisados.", () => { _evName.Text = string.Empty; LoadEvents(); });
    }

    public void ToggleEvent() { if (_current is { } g && SelectedEvent() is { } e) Do(e.Joined ? Godot.HttpClient.Method.Delete : Godot.HttpClient.Method.Post, $"/{g.Id}/events/{e.Id}/join", e.Joined ? null : new { }, e.Joined ? "Você saiu do evento." : "Você está no evento.", LoadEvents); }
    public void CancelEvent() { if (_current is { } g && SelectedEvent() is { } e) Do(Godot.HttpClient.Method.Delete, $"/{g.Id}/events/{e.Id}", null, "Evento cancelado; quem ia foi avisado.", LoadEvents); }

    // ---- notices --------------------------------------------------------------------------------------------------------------------------------------

    public void LoadNotices() => Call(Godot.HttpClient.Method.Get, "/notifications?take=50", null, (status, body) =>
    {
        if (status is < 200 or >= 300) return;
        try
        {
            using var d = JsonDocument.Parse(body); _notices.Clear(); _noticeList.Clear(); var unread = Num(d.RootElement, "unread");
            if (d.RootElement.TryGetProperty("items", out var items))
                foreach (var n in items.EnumerateArray())
                {
                    var read = Flag(n, "read"); var text = $"{(read ? "  " : "● ")}{Str(n, "groupName")} — {Str(n, "title")} · {(Time(n, "at") ?? DateTimeOffset.UtcNow).ToLocalTime():dd/MM HH:mm}";
                    _notices.Add(new GroupNoticeData(IdOf(n, "id"), IdOf(n, "groupId"), text, read)); _noticeList.AddItem(text);
                }
            _noticesRead.Disabled = unread == 0;
            if (unread > 0) Say($"Você tem {unread} aviso(s) de grupos não lido(s).");
        }
        catch (JsonException) { }
    });

    public void MarkNoticesRead() => Do(Godot.HttpClient.Method.Post, "/notifications/read", new { }, "Avisos marcados como lidos.", LoadNotices);

    // ---- what the buttons may do --------------------------------------------------------------------------------------------------------------------------

    private void UpdateCommunicationButtons()
    {
        var member = _current?.IsMember == true; var muted = Muted(DateTimeOffset.UtcNow);
        _chatInput.Editable = member && !muted; _chatInput.PlaceholderText = muted ? $"Você está silenciado até {_mutedUntil!.Value.ToLocalTime():HH:mm}." : "Escreva uma mensagem para o grupo";
        _chatSend.Disabled = !member || muted;
        var selected = _chatList.GetSelectedItems(); var line = selected.Length > 0 && selected[0] < _chat.Count ? _chat[selected[0]] : null;
        _chatDelete.Disabled = line is null || !(line.UserId == MyId || Has(GroupPermissions.ModerateChat));
        _notifyOff.SetPressedNoSignal(_notifyMuted);
        var announce = Has(GroupPermissions.Announce); var ann = SelectedAnnouncement();
        _annPost.Disabled = !announce; _annTitle.Editable = announce; _annText.Editable = announce; _annPinned.Disabled = !announce;
        _annPin.Disabled = !(announce && ann is not null); _annDelete.Disabled = !(announce && ann is not null); _annPin.Text = ann?.Pinned == true ? "Desafixar" : "Fixar";
        if (ann is not null) _announceBody.Text = $"{ann.Title}\n{ann.Author} · {ann.At.ToLocalTime():dd/MM/yyyy HH:mm}\n\n{ann.Body}";
        var manage = Has(GroupPermissions.ManageEvents); var ev = SelectedEvent();
        _evCreate.Disabled = !manage; _evName.Editable = manage; _evStart.Editable = manage; _evMinutes.Editable = manage; _evCapacity.Editable = manage; _evIsland.Disabled = !manage;
        _evJoin.Disabled = ev is null; _evJoin.Text = ev?.Joined == true ? "Sair do evento" : "Participar";
        _evCancel.Disabled = !(ev is not null && (ev.OrganizerUserId == MyId || manage));
    }

    // ---- building the pages ----------------------------------------------------------------------------------------------------------------------------------

    private Control BuildChat(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        _chatList = List(190); _chatList.ItemSelected += _ => UpdateCommunicationButtons(); page.AddChild(_chatList);
        _chatInput = Field("Escreva uma mensagem para o grupo", 1000); _chatSend = Act("Enviar", UiStyle.ButtonKind.Primary, SendChat); _chatSend.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd; _chatInput.TextSubmitted += _ => SendChat();
        page.AddChild(Row(_chatInput, _chatSend));
        _chatDelete = Act("Apagar mensagem escolhida", UiStyle.ButtonKind.Secondary, DeleteChatSelected);
        _notifyOff = new CheckBox { Text = "Silenciar avisos deste grupo", FocusMode = Control.FocusModeEnum.None }; _notifyOff.AddThemeFontSizeOverride("font_size", 14); _notifyOff.Toggled += SetNotificationsOff;
        page.AddChild(Row(_chatDelete, _notifyOff));
        return page;
    }

    private Control BuildAnnouncements(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        _announceList = List(80); _announceList.ItemSelected += _ => UpdateCommunicationButtons(); page.AddChild(_announceList);
        _announceBody = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 60) }; _announceBody.AddThemeFontSizeOverride("font_size", 15); page.AddChild(_announceBody);
        _annPin = Act("Fixar", UiStyle.ButtonKind.Secondary, TogglePinned); _annDelete = Act("Apagar", UiStyle.ButtonKind.Secondary, DeleteAnnouncement); page.AddChild(Row(_annPin, _annDelete));
        _annTitle = Field("Título do anúncio (até 80 caracteres)", 80); page.AddChild(_annTitle);
        _annText = new TextEdit { PlaceholderText = "Texto do anúncio (até 2000 caracteres)", CustomMinimumSize = new Vector2(0, 60), WrapMode = TextEdit.LineWrappingMode.Boundary }; _annText.AddThemeFontSizeOverride("font_size", 15); page.AddChild(_annText);
        _annPinned = new CheckBox { Text = "Fixar no topo", FocusMode = Control.FocusModeEnum.None }; _annPost = Act("Publicar para todos", UiStyle.ButtonKind.Primary, PostAnnouncement); page.AddChild(Row(_annPinned, _annPost));
        return page;
    }

    private Control BuildEvents(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 6); stack.AddChild(page);
        page.AddChild(Note("Eventos que só os membros do grupo veem e podem participar."));
        _eventList = List(70); _eventList.ItemSelected += _ => ShowEvent(); page.AddChild(_eventList);
        _eventBody = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 50) }; _eventBody.AddThemeFontSizeOverride("font_size", 15); page.AddChild(_eventBody);
        _evJoin = Act("Participar", UiStyle.ButtonKind.Primary, ToggleEvent); _evCancel = Act("Cancelar evento", UiStyle.ButtonKind.Secondary, CancelEvent); page.AddChild(Row(_evJoin, _evCancel));
        _evName = Field("Nome do evento", 160); _evIsland = Choice(); page.AddChild(Row(_evName, _evIsland));
        var now = DateTime.Now.AddHours(1); _evStart = Field("dd/MM/aaaa hh:mm", 16); _evStart.Text = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).ToString("dd/MM/yyyy HH:mm", Pt);
        _evMinutes = Field("Duração (min)", 5); _evMinutes.Text = "120"; _evCapacity = Field("Vagas", 4); _evCapacity.Text = "50";
        _evCreate = Act("Criar evento", UiStyle.ButtonKind.Primary, CreateEvent); page.AddChild(Row(_evStart, _evMinutes, _evCapacity, _evCreate));
        return page;
    }

    private Control BuildNotices(Control stack)
    {
        var page = new VBoxContainer { Visible = false }; page.SetAnchorsPreset(Control.LayoutPreset.FullRect); page.AddThemeConstantOverride("separation", 8); stack.AddChild(page);
        page.AddChild(Note("Anúncios e eventos dos seus grupos, guardados até você ler. ● marca o que ainda não foi lido."));
        _noticeList = List(320); page.AddChild(_noticeList);
        _noticesRead = Act("Marcar tudo como lido", UiStyle.ButtonKind.Primary, MarkNoticesRead); page.AddChild(_noticesRead);
        return page;
    }

    // ---- hooks for tests --------------------------------------------------------------------------------------------------------------------------------------

    public void SetUserForTesting(Guid user) => _testUser = user;
    public void SetChatInputForTesting(string text) => _chatInput.Text = text;
    public void SelectChatForTesting(int index) { _chatList.Select(index); UpdateCommunicationButtons(); }
    public void SelectAnnouncementForTesting(int index) { _announceList.Select(index); UpdateCommunicationButtons(); }
    public void SelectEventForTesting(int index) { _eventList.Select(index); ShowEvent(); }
    public void SetAnnouncementFormForTesting(string title, string body, bool pinned) { _annTitle.Text = title; _annText.Text = body; _annPinned.ButtonPressed = pinned; }
    public void SetEventFormForTesting(string name, string start, int minutes, int capacity) { _evName.Text = name; _evStart.Text = start; _evMinutes.Text = minutes.ToString(CultureInfo.InvariantCulture); _evCapacity.Text = capacity.ToString(CultureInfo.InvariantCulture); }
    public void ToggleNotificationsOffForTesting(bool off) { _notifyOff.ButtonPressed = off; }
    public bool CommButtonEnabledForTesting(string which) => which switch { "send" => !_chatSend.Disabled, "chatDelete" => !_chatDelete.Disabled, "post" => !_annPost.Disabled, "pin" => !_annPin.Disabled, "annDelete" => !_annDelete.Disabled, "evCreate" => !_evCreate.Disabled, "evJoin" => !_evJoin.Disabled, "evCancel" => !_evCancel.Disabled, "chatInput" => _chatInput.Editable, _ => false };
    public string ChatPlaceholderForTesting => _chatInput.PlaceholderText;
    public string EventJoinTextForTesting => _evJoin.Text;
    public string AnnouncementBodyForTesting => _announceBody.Text;
    public int UnreadChatForTesting(Guid group) => _unreadChat.GetValueOrDefault(group);
    public string MineLabelForTesting(int index) => _mineList.GetItemText(index);
}
