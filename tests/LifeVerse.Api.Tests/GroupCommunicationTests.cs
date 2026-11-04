using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Groups;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Groups 2.0, second part: chat (mute enforced), announcements, events only for members, and the notifications about them.</summary>
public sealed class GroupCommunicationTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly List<(Guid User, string Type)> _pushed = [];
    private readonly GroupNotifier _notify;
    private readonly Guid _leader, _admin, _moderator, _member, _other, _stranger, _group, _island;

    public GroupCommunicationTests()
    {
        _notify = (user, type, _) => { _pushed.Add((user, type)); return Task.CompletedTask; };
        (_leader, _admin, _moderator, _member, _other, _stranger) = (User("lider"), User("admin"), User("moderador"), User("membro"), User("outro"), User("estranho"));
        var made = GroupService.CreateAsync(_db, _leader, new GroupProfileRequest("Os Exploradores", "", null, null, null, null, null, null, null), _now, default).Result;
        _group = JsonSerializer.SerializeToElement(made.Data).GetProperty("id").GetGuid();
        Place(_admin, "admin"); Place(_moderator, "moderator"); Place(_member, "member"); Place(_other, "member");
        _island = Guid.NewGuid(); _db.Islands.Add(new Island { Id = _island, OwnerUserId = _leader, Name = "Praça", Slug = "praca", IsPublic = true }); _db.SaveChanges();
    }

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public void Dispose() => _db.Dispose();

    private Guid User(string name) { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id; }
    private void Place(Guid user, string key) { var role = _db.GroupRoles.Single(r => r.GroupId == _group && r.SystemKey == key); _db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = _group, UserId = user, Role = key, RoleId = role.Id, JoinedAt = _now }); _db.SaveChanges(); }
    private static T Data<T>(GroupResult result) => (T)result.Data!;
    private static Guid Id(GroupResult result) => JsonSerializer.SerializeToElement(result.Data).GetProperty("id").GetGuid();
    private Task<GroupResult> Say(Guid user, string text, string? clientId = null) => GroupService.SendChatAsync(_db, user, _group, text, clientId, _now, _notify, default);
    private Task<GroupResult> Post(Guid user, string title = "Reunião", string body = "Hoje às 20h", bool pinned = false) => GroupService.PostAnnouncementAsync(_db, user, _group, new AnnouncementRequest(title, body, pinned), _now, _notify, default);
    private Task<GroupResult> MakeEvent(Guid user, int capacity = 2, int hoursAhead = 2) => GroupService.CreateEventAsync(_db, user, _group, new GroupEventRequest("Caça ao tesouro", "Venham todos", _island, _now.AddHours(hoursAhead), _now.AddHours(hoursAhead + 2), capacity), _now, _notify, default);
    private async Task<List<string>> Actions() => (await _db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == _group).OrderBy(e => e.CreatedAt).ToListAsync()).Select(e => e.Action).ToList();

    // ---- chat -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MembersChatAndOthersAreKeptOut()
    {
        Assert.True((await Say(_member, "  Olá, pessoal!  ")).Ok);
        _now = _now.AddSeconds(1); Assert.True((await Say(_admin, "Oi")).Ok);
        var history = Data<List<GroupService.ChatItem>>(await GroupService.ChatAsync(_db, _other, _group, null, 50, _now, default));
        Assert.Equal(["Olá, pessoal!", "Oi"], history.Select(m => m.Content)); Assert.Equal("membro", history[0].Username);
        Assert.Equal("forbidden", (await Say(_stranger, "psiu")).Error); Assert.Equal("forbidden", (await GroupService.ChatAsync(_db, _stranger, _group, null, 50, _now, default)).Error);
        Assert.Equal("not_found", (await GroupService.ChatAsync(_db, _member, Guid.NewGuid(), null, 50, _now, default)).Error);
        Assert.Contains(_pushed, p => p.User == _admin && p.Type == "group_message"); Assert.DoesNotContain(_pushed, p => p.User == _admin && p.Type == "group_message" && false);
        Assert.DoesNotContain(_pushed, p => p.User == _stranger);
    }

    [Fact]
    public async Task MessagesAreCheckedAndDeduplicated()
    {
        Assert.Equal("bad_message", (await Say(_member, "   ")).Error); Assert.Equal("bad_message", (await Say(_member, new string('x', 1001))).Error); Assert.Equal("bad_message", (await Say(_member, "a", new string('i', 97))).Error);
        var first = await Say(_member, "uma só vez", "abc"); var again = await Say(_member, "uma só vez", "abc");
        Assert.Equal(Id(first), Id(again)); Assert.Equal(1, await _db.SocialMessages.CountAsync(m => m.GroupId == _group));
        var cleaned = await Say(_member, "oi\u0007\n mundo"); Assert.True(cleaned.Ok);
        Assert.Equal("oi mundo", (await _db.SocialMessages.AsNoTracking().SingleAsync(m => m.Id == Id(cleaned))).Content);
    }

    [Fact]
    public async Task AMutedMemberCannotSpeakUntilItEnds()
    {
        Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, _member, 10, "spam", _now, default)).Ok);
        Assert.Equal("muted", (await Say(_member, "estou silenciado")).Error);
        Assert.True((await Say(_other, "eu posso")).Ok); // the others still talk
        _now = _now.AddMinutes(11); Assert.True((await Say(_member, "voltei")).Ok);
        Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, _member, 30, null, _now, default)).Ok); Assert.Equal("muted", (await Say(_member, "de novo")).Error);
        Assert.True((await GroupService.MuteAsync(_db, _moderator, _group, _member, 0, null, _now, default)).Ok); Assert.True((await Say(_member, "liberado")).Ok);
    }

    [Fact]
    public async Task ChatIsRateLimited()
    {
        for (var i = 0; i < GroupRules.ChatBurst; i++) Assert.True((await Say(_member, $"m{i}")).Ok);
        Assert.Equal("rate_limited", (await Say(_member, "demais")).Error);
        Assert.True((await Say(_other, "outro pode")).Ok);
        _now = _now.AddSeconds(31); Assert.True((await Say(_member, "agora sim")).Ok);
    }

    [Fact]
    public async Task OwnMessagesAndLowerRanksCanBeDeleted()
    {
        var mine = Id(await Say(_member, "apagar")); var admins = Id(await Say(_admin, "do admin")); var others = Id(await Say(_other, "do outro"));
        Assert.True((await GroupService.DeleteChatMessageAsync(_db, _member, _group, mine, _now, default)).Ok);
        Assert.Equal("forbidden", (await GroupService.DeleteChatMessageAsync(_db, _member, _group, others, _now, default)).Error); // no permission
        Assert.Equal("outranked", (await GroupService.DeleteChatMessageAsync(_db, _moderator, _group, admins, _now, default)).Error);
        Assert.True((await GroupService.DeleteChatMessageAsync(_db, _moderator, _group, others, _now, default)).Ok);
        Assert.Equal("not_found", (await GroupService.DeleteChatMessageAsync(_db, _moderator, _group, others, _now, default)).Error);
        Assert.True((await GroupService.DeleteChatMessageAsync(_db, _leader, _group, admins, _now, default)).Ok);
        Assert.Empty(Data<List<GroupService.ChatItem>>(await GroupService.ChatAsync(_db, _leader, _group, null, 50, _now, default)));
        Assert.Contains("message_deleted", await Actions());
    }

    [Fact]
    public async Task ChatHistoryPagesBackwards()
    {
        for (var i = 0; i < 5; i++) { _now = _now.AddSeconds(1); await Say(_member, $"m{i}"); }
        var page = Data<List<GroupService.ChatItem>>(await GroupService.ChatAsync(_db, _member, _group, null, 2, _now, default)); Assert.Equal(["m3", "m4"], page.Select(m => m.Content));
        var older = Data<List<GroupService.ChatItem>>(await GroupService.ChatAsync(_db, _member, _group, page[0].At, 2, _now, default)); Assert.Equal(["m1", "m2"], older.Select(m => m.Content));
    }

    // ---- announcements ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyThoseWithThePermissionAnnounceAndEveryoneIsTold()
    {
        Assert.Equal("forbidden", (await Post(_member)).Error); Assert.Equal("forbidden", (await Post(_moderator)).Error); Assert.Equal("forbidden", (await Post(_stranger)).Error);
        var posted = await Post(_admin, "Reunião", "Hoje às 20h"); Assert.True(posted.Ok);
        foreach (var user in new[] { _leader, _moderator, _member, _other }) Assert.Contains(_pushed, p => p.User == user && p.Type == "group_announcement");
        Assert.DoesNotContain(_pushed, p => p.User == _admin && p.Type == "group_announcement"); Assert.DoesNotContain(_pushed, p => p.User == _stranger);
        var list = Data<List<GroupService.AnnouncementItem>>(await GroupService.AnnouncementsAsync(_db, _member, _group, _now, default)); Assert.Single(list); Assert.Equal("admin", list[0].Author);
        Assert.Equal("forbidden", (await GroupService.AnnouncementsAsync(_db, _stranger, _group, _now, default)).Error);
        Assert.Contains("announcement_posted", await Actions());
    }

    [Fact]
    public async Task AnnouncementsAreValidatedPinnedAndLimited()
    {
        Assert.Equal("bad_announcement", (await Post(_admin, "", "x")).Error); Assert.Equal("bad_announcement", (await Post(_admin, "t", " ")).Error); Assert.Equal("bad_announcement", (await Post(_admin, new string('t', 81), "x")).Error); Assert.Equal("bad_announcement", (await Post(_admin, "t", new string('b', 2001))).Error);
        for (var i = 0; i < GroupRules.MaxPinned; i++) Assert.True((await Post(_admin, $"fixo{i}", "x", pinned: true)).Ok);
        Assert.Equal("too_many_pinned", (await Post(_admin, "mais um", "x", pinned: true)).Error);
        Assert.True((await Post(_admin, "normal", "x")).Ok);
        var list = Data<List<GroupService.AnnouncementItem>>(await GroupService.AnnouncementsAsync(_db, _member, _group, _now, default)); Assert.All(list.Take(3), a => Assert.True(a.Pinned)); Assert.False(list[3].Pinned);
        for (var i = 0; i < 6; i++) await Post(_admin, $"n{i}", "x");
        Assert.Equal("rate_limited", (await Post(_admin, "excesso", "x")).Error);
    }

    [Fact]
    public async Task AnnouncementsCanBeEditedPinnedAndDeleted()
    {
        var id = Id(await Post(_admin));
        Assert.Equal("forbidden", (await GroupService.UpdateAnnouncementAsync(_db, _member, _group, id, new AnnouncementRequest("x", null, null), _now, default)).Error);
        _now = _now.AddMinutes(1);
        Assert.True((await GroupService.UpdateAnnouncementAsync(_db, _admin, _group, id, new AnnouncementRequest("Novo título", null, true), _now, default)).Ok);
        var item = Data<List<GroupService.AnnouncementItem>>(await GroupService.AnnouncementsAsync(_db, _member, _group, _now, default)).Single(); Assert.Equal("Novo título", item.Title); Assert.True(item.Pinned); Assert.NotNull(item.EditedAt);
        Assert.Equal("bad_announcement", (await GroupService.UpdateAnnouncementAsync(_db, _admin, _group, id, new AnnouncementRequest("", null, null), _now, default)).Error);
        Assert.True((await GroupService.DeleteAnnouncementAsync(_db, _leader, _group, id, _now, default)).Ok);
        Assert.Empty(Data<List<GroupService.AnnouncementItem>>(await GroupService.AnnouncementsAsync(_db, _member, _group, _now, default)));
        Assert.Equal("not_found", (await GroupService.DeleteAnnouncementAsync(_db, _leader, _group, id, _now, default)).Error);
        Assert.Contains("announcement_deleted", await Actions());
    }

    // ---- events ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyMembersSeeJoinAndLeaveGroupEvents()
    {
        Assert.Equal("forbidden", (await MakeEvent(_member)).Error); Assert.Equal("forbidden", (await MakeEvent(_moderator)).Error);
        var made = await MakeEvent(_admin); Assert.True(made.Ok); var id = Id(made);
        var stored = await _db.DiscoveryEvents.AsNoTracking().SingleAsync(); Assert.False(stored.IsPublic); Assert.Equal(_group, stored.GroupId);
        Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_event");
        Assert.Equal("forbidden", (await GroupService.EventsAsync(_db, _stranger, _group, _now, default)).Error); Assert.Equal("forbidden", (await GroupService.JoinEventAsync(_db, _stranger, _group, id, _now, default)).Error);
        Assert.True((await GroupService.JoinEventAsync(_db, _member, _group, id, _now, default)).Ok); Assert.True((await GroupService.JoinEventAsync(_db, _member, _group, id, _now, default)).Ok); // twice is the same place
        var list = Data<List<GroupService.EventItem>>(await GroupService.EventsAsync(_db, _member, _group, _now, default)).Single(); Assert.Equal(1, list.Attending); Assert.True(list.Joined); Assert.Equal("Praça", list.IslandName);
        Assert.False(Data<List<GroupService.EventItem>>(await GroupService.EventsAsync(_db, _other, _group, _now, default)).Single().Joined);
        Assert.True((await GroupService.LeaveEventAsync(_db, _member, _group, id, _now, default)).Ok); Assert.Equal(0, await _db.EventAttendees.CountAsync());
    }

    [Fact]
    public async Task EventsHaveCapacityAndTheyAreValidated()
    {
        var id = Id(await MakeEvent(_admin, capacity: 2));
        Assert.True((await GroupService.JoinEventAsync(_db, _member, _group, id, _now, default)).Ok); Assert.True((await GroupService.JoinEventAsync(_db, _other, _group, id, _now, default)).Ok);
        Assert.Equal("event_full", (await GroupService.JoinEventAsync(_db, _moderator, _group, id, _now, default)).Error);
        Task<GroupResult> With(GroupEventRequest r) => GroupService.CreateEventAsync(_db, _admin, _group, r, _now, _notify, default);
        Assert.Equal("bad_name", (await With(new("", "", _island, _now.AddHours(1), _now.AddHours(2), 5))).Error);
        Assert.Equal("bad_time", (await With(new("x", "", _island, _now.AddHours(2), _now.AddHours(1), 5))).Error);
        Assert.Equal("bad_time", (await With(new("x", "", _island, _now.AddHours(-3), _now.AddHours(-2), 5))).Error);
        Assert.Equal("bad_time", (await With(new("x", "", _island, _now.AddHours(1), _now.AddDays(9), 5))).Error);
        Assert.Equal("bad_capacity", (await With(new("x", "", _island, _now.AddHours(1), _now.AddHours(2), 0))).Error);
        Assert.Equal("bad_island", (await With(new("x", "", Guid.NewGuid(), _now.AddHours(1), _now.AddHours(2), 5))).Error);
        var hidden = Guid.NewGuid(); _db.Islands.Add(new Island { Id = hidden, OwnerUserId = _stranger, Name = "Privada", Slug = "privada", IsPublic = false }); await _db.SaveChangesAsync();
        Assert.Equal("bad_island", (await With(new("x", "", hidden, _now.AddHours(1), _now.AddHours(2), 5))).Error); // an island the person cannot enter
        for (var i = 1; i < GroupRules.MaxUpcomingEvents; i++) Assert.True((await MakeEvent(_admin, hoursAhead: 3 + i)).Ok);
        Assert.Equal("too_many_events", (await MakeEvent(_admin)).Error);
    }

    [Fact]
    public async Task CancellingAnEventTellsThoseWhoJoined()
    {
        var id = Id(await MakeEvent(_admin)); await GroupService.JoinEventAsync(_db, _member, _group, id, _now, default); _pushed.Clear();
        Assert.Equal("forbidden", (await GroupService.CancelEventAsync(_db, _other, _group, id, _now, _notify, default)).Error);
        Assert.True((await GroupService.CancelEventAsync(_db, _admin, _group, id, _now, _notify, default)).Ok);
        Assert.Equal(0, await _db.DiscoveryEvents.CountAsync()); Assert.Equal(0, await _db.EventAttendees.CountAsync());
        Assert.Contains(_pushed, p => p.User == _member && p.Type == "group_event_cancelled"); Assert.DoesNotContain(_pushed, p => p.User == _other);
        Assert.Equal("not_found", (await GroupService.CancelEventAsync(_db, _admin, _group, id, _now, _notify, default)).Error);
        var organizers = Id(await MakeEvent(_admin)); Assert.True((await GroupService.CancelEventAsync(_db, _admin, _group, organizers, _now, _notify, default)).Ok); // the one who made it
        Assert.Contains("event_cancelled", await Actions());
    }

    [Fact]
    public async Task LeavingOrBeingRemovedGivesUpTheEventPlaces()
    {
        var id = Id(await MakeEvent(_admin, capacity: 5));
        foreach (var user in new[] { _member, _other, _moderator }) await GroupService.JoinEventAsync(_db, user, _group, id, _now, default);
        await GroupService.LeaveAsync(_db, _member, _group, _now, default); _now = _now.AddMinutes(1);
        await GroupService.KickAsync(_db, _admin, _group, _other, null, _now, _notify, default);
        await GroupService.BanAsync(_db, _leader, _group, _moderator, null, null, _now, _notify, default);
        Assert.Equal(0, await _db.EventAttendees.CountAsync());
    }

    [Fact]
    public async Task GroupEventsAreNotInThePublicDiscovery()
    {
        await MakeEvent(_admin);
        Assert.Empty(await _db.DiscoveryEvents.AsNoTracking().Where(e => e.IsPublic).ToListAsync());
    }

    [Fact]
    public async Task DissolvingTakesTheEventsAndAnnouncementsAlong()
    {
        await MakeEvent(_admin); await Post(_admin);
        Assert.True((await GroupService.DissolveAsync(_db, _leader, _group, "Os Exploradores", _now, _notify, default)).Ok);
        Assert.Equal(0, await _db.DiscoveryEvents.CountAsync()); Assert.All(await _db.GroupAnnouncements.AsNoTracking().ToListAsync(), a => Assert.NotNull(a.DeletedAt));
    }

    // ---- notifications ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NotificationsAreKeptUntilRead()
    {
        await Post(_admin, "Primeiro", "a"); _now = _now.AddMinutes(1); await MakeEvent(_admin);
        var list = JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _member, false, 50, _now, default)).Data, Web);
        Assert.Equal(2, list.GetProperty("unread").GetInt32()); var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["group_event", "group_announcement"], items.Select(i => i.GetProperty("type").GetString())); Assert.Equal("Os Exploradores", items[0].GetProperty("groupName").GetString());
        Assert.Equal(0, JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _admin, false, 50, _now, default)).Data, Web).GetProperty("unread").GetInt32()); // the author is not told
        Assert.Equal(0, JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _stranger, false, 50, _now, default)).Data, Web).GetProperty("items").GetArrayLength());
        var first = items[1].GetProperty("id").GetGuid();
        Assert.Equal(1, JsonSerializer.SerializeToElement((await GroupService.MarkNotificationsReadAsync(_db, _member, [first], null, _now, default)).Data, Web).GetProperty("read").GetInt32());
        Assert.Equal(1, JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _member, true, 50, _now, default)).Data, Web).GetProperty("items").GetArrayLength());
        await GroupService.MarkNotificationsReadAsync(_db, _member, null, _group, _now, default);
        Assert.Equal(0, JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _member, true, 50, _now, default)).Data, Web).GetProperty("unread").GetInt32());
        Assert.Equal(2, JsonSerializer.SerializeToElement((await GroupService.NotificationsAsync(_db, _other, false, 50, _now, default)).Data, Web).GetProperty("unread").GetInt32()); // others are untouched
        Assert.Equal(0, (await _db.GroupNotifications.CountAsync(n => n.UserId == _member && n.ReadAt == null)));
    }

    [Fact]
    public async Task OldReadNotificationsAreCleanedUp()
    {
        await Post(_admin); await GroupService.MarkNotificationsReadAsync(_db, _member, null, null, _now, default);
        _now = _now.AddDays(31); await GroupService.NotificationsAsync(_db, _member, false, 50, _now, default);
        Assert.Equal(0, await _db.GroupNotifications.CountAsync(n => n.UserId == _member)); Assert.Equal(1, await _db.GroupNotifications.CountAsync(n => n.UserId == _other)); // _other never read theirs
    }

    [Fact]
    public async Task AMemberCanSilenceTheGroupNotifications()
    {
        Assert.True((await GroupService.SetNotificationsMutedAsync(_db, _member, _group, true, _now, default)).Ok);
        Assert.Equal("forbidden", (await GroupService.SetNotificationsMutedAsync(_db, _stranger, _group, true, _now, default)).Error);
        await Post(_admin); await Say(_admin, "oi"); await MakeEvent(_admin);
        Assert.DoesNotContain(_pushed, p => p.User == _member); Assert.Equal(0, await _db.GroupNotifications.CountAsync(n => n.UserId == _member));
        Assert.Contains(_pushed, p => p.User == _other); Assert.Equal(2, await _db.GroupNotifications.CountAsync(n => n.UserId == _other));
        var view = Data<GroupService.GroupView>(await GroupService.ViewAsync(_db, _member, _group, _now, default)); Assert.True(view.NotificationsMuted);
        Assert.True((await Say(_member, "ainda posso falar")).Ok); // silencing notifications is not being muted
    }

    [Fact]
    public async Task TheNewPermissionsBelongToTheRightCargos()
    {
        Assert.Contains(GroupPermissions.Announce, GroupPermissions.Known); Assert.Contains(GroupPermissions.ManageEvents, GroupPermissions.Known);
        var roles = GroupRules.StartingRoles.ToDictionary(r => r.Key, r => r.Permissions);
        Assert.Contains("announce", roles["admin"]); Assert.Contains("manage_events", roles["admin"]); Assert.Contains("moderate_chat", roles["moderator"]); Assert.DoesNotContain("announce", roles["moderator"]); Assert.Empty(roles["member"]);
        await Task.CompletedTask;
    }
}
