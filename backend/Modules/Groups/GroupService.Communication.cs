using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record AnnouncementRequest(string? Title, string? Body, bool? Pinned, DateTimeOffset? ExpiresAt = null, string? AttachmentUrl = null);
public sealed record GroupEventRequest(string? Name, string? Description, Guid? IslandId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, int? Capacity);
public sealed record ChatRequest(string? Content, string? ClientMessageId);
public sealed record ReadRequest(Guid[]? Ids, Guid? GroupId);
public sealed record MutedRequest(bool Muted);

/// <summary>
/// Groups 2.0, communication: the chat of the group (muted people cannot speak, leaders can clean it), announcements, events only members can see and
/// join, and the notifications that tell members about them. Only members take part in any of it, and what needs a cargo asks for a permission.
/// </summary>
public static partial class GroupService
{
    public sealed record ChatItem(Guid Id, Guid UserId, string Username, string Content, DateTimeOffset At);
    public sealed record AnnouncementItem(Guid Id, string Title, string Body, bool Pinned, Guid AuthorUserId, string Author, DateTimeOffset At, DateTimeOffset? EditedAt, DateTimeOffset? ExpiresAt = null, string AttachmentUrl = "");
    public sealed record EventItem(Guid Id, string Name, string Description, Guid IslandId, string IslandName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, int Attending, bool Joined, Guid OrganizerUserId, string Organizer);
    public sealed record NotificationItem(Guid Id, Guid GroupId, string GroupName, string Type, string Title, string Body, DateTimeOffset At, bool Read);

    /// <summary>Text from a person: no control characters (new lines only where wanted), trimmed.</summary>
    private static string CleanText(string? text, bool newLines) => new string((text ?? string.Empty).Where(c => !char.IsControl(c) || newLines && c is '\n').ToArray()).Trim();

    private static async Task<(Ctx? Ctx, GroupResult? Error)> MemberAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct);
        if (ctx is null) return (null, GroupResult.Fail("not_found"));
        return ctx.Member is null ? (null, GroupResult.Fail("forbidden")) : (ctx, null);
    }

    private static async Task<(Ctx? Ctx, GroupResult? Error)> WithPermissionAsync(LifeVerseDbContext db, Guid me, Guid groupId, string permission, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return (null, error);
        return GroupRules.Has(ctx!.Actor!, permission) ? (ctx, null) : (null, GroupResult.Fail("forbidden"));
    }

    private static async Task<Dictionary<Guid, string>> NamesAsync(LifeVerseDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
    }

    // ---- chat -----------------------------------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> SendChatAsync(LifeVerseDbContext db, Guid me, Guid groupId, string? content, string? clientMessageId, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        if (ctx!.Member!.MutedUntil is { } until && until > now) return GroupResult.Fail("muted", new { until });
        var text = CleanText(content, newLines: false);
        if (text.Length == 0 || text.Length > GroupRules.MaxChatLength || clientMessageId?.Length > 96) return GroupResult.Fail("bad_message");
        if (clientMessageId is not null)
        {
            var again = await db.SocialMessages.AsNoTracking().FirstOrDefaultAsync(m => m.SenderUserId == me && m.ClientMessageId == clientMessageId, ct);
            if (again is not null) return GroupResult.Done(new { id = again.Id, at = again.CreatedAt });
        }
        var since = now - GroupRules.ChatBurstWindow;
        if (await db.SocialMessages.CountAsync(m => m.Channel == "group" && m.GroupId == groupId && m.SenderUserId == me && m.CreatedAt > since, ct) >= GroupRules.ChatBurst) return GroupResult.Fail("rate_limited");
        var message = new SocialMessage { Id = Guid.NewGuid(), Channel = "group", SenderUserId = me, GroupId = groupId, Content = text, ClientMessageId = clientMessageId, CreatedAt = now };
        db.SocialMessages.Add(message);
        var saved = await SaveAsync(db, ct, new { id = message.Id, at = now }); if (!saved.Ok) return saved;
        var username = (await NamesAsync(db, [me], ct)).GetValueOrDefault(me, "?");
        var listeners = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId && m.UserId != me && !m.NotificationsMuted).Select(m => m.UserId).ToListAsync(ct);
        foreach (var user in listeners) await TellAsync(notify, user, "group_message", new { groupId, groupName = ctx.Group.Name, id = message.Id, userId = me, username, content = text, at = now });
        return saved;
    }

    public static async Task<GroupResult> ChatAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset? before, int take, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var query = db.SocialMessages.AsNoTracking().Where(m => m.Channel == "group" && m.GroupId == groupId && m.DeletedAt == null);
        if (before is { } b) query = query.Where(m => m.CreatedAt < b);
        var list = await query.OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var names = await NamesAsync(db, list.Select(m => m.SenderUserId), ct);
        return GroupResult.Done(list.OrderBy(m => m.CreatedAt).Select(m => new ChatItem(m.Id, m.SenderUserId, names.GetValueOrDefault(m.SenderUserId, "?"), m.Content, m.CreatedAt)).ToList());
    }

    /// <summary>People delete their own messages; a leader with the permission deletes those of someone below them.</summary>
    public static async Task<GroupResult> DeleteChatMessageAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid messageId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var message = await db.SocialMessages.SingleOrDefaultAsync(m => m.Id == messageId && m.Channel == "group" && m.GroupId == groupId && m.DeletedAt == null, ct);
        if (message is null) return GroupResult.Fail("not_found");
        if (message.SenderUserId != me)
        {
            if (!GroupRules.Has(ctx!.Actor!, GroupPermissions.ModerateChat)) return GroupResult.Fail("forbidden");
            var author = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == message.SenderUserId, ct);
            if (author is not null && !GroupRules.CanActOn(ctx.Actor!, RoleOf(author, ctx.Roles).Rank)) return GroupResult.Fail("outranked");
            Log(db, groupId, me, "message_deleted", message.SenderUserId, new { messageId }, now);
        }
        message.DeletedAt = now;
        return await SaveAsync(db, ct);
    }

    // ---- notifications ------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Keeps a notification for every member who has not turned them off (except the one who caused it) and returns who to push to.</summary>
    private static async Task<List<Guid>> AddNotificationsAsync(LifeVerseDbContext db, Guid groupId, IEnumerable<Guid>? only, Guid except, string type, string title, string body, DateTimeOffset now, CancellationToken ct)
    {
        var members = db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId && m.UserId != except && !m.NotificationsMuted);
        var users = only is null ? await members.Select(m => m.UserId).ToListAsync(ct) : await members.Where(m => only.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
        foreach (var user in users) db.GroupNotifications.Add(new GroupNotification { Id = Guid.NewGuid(), UserId = user, GroupId = groupId, Type = type, Title = title.Length > 120 ? title[..120] : title, Body = body.Length > 300 ? body[..300] : body, CreatedAt = now });
        return users;
    }

    public static async Task<GroupResult> NotificationsAsync(LifeVerseDbContext db, Guid me, bool unreadOnly, int take, DateTimeOffset now, CancellationToken ct)
    {
        var old = await db.GroupNotifications.Where(n => n.UserId == me && (n.ReadAt != null && n.ReadAt < now.AddDays(-30) || n.CreatedAt < now.AddDays(-90))).ToListAsync(ct);
        if (old.Count > 0) { db.GroupNotifications.RemoveRange(old); await db.SaveChangesAsync(ct); }
        var query = db.GroupNotifications.AsNoTracking().Where(n => n.UserId == me);
        var unread = await query.CountAsync(n => n.ReadAt == null, ct);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        var list = await query.OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var groupIds = list.Select(n => n.GroupId).Distinct().ToList();
        var names = await db.SocialGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        return GroupResult.Done(new { unread, items = list.Select(n => new NotificationItem(n.Id, n.GroupId, names.GetValueOrDefault(n.GroupId, "?"), n.Type, n.Title, n.Body, n.CreatedAt, n.ReadAt != null)).ToList() });
    }

    public static async Task<GroupResult> MarkNotificationsReadAsync(LifeVerseDbContext db, Guid me, Guid[]? ids, Guid? groupId, DateTimeOffset now, CancellationToken ct)
    {
        var query = db.GroupNotifications.Where(n => n.UserId == me && n.ReadAt == null);
        if (groupId is { } g) query = query.Where(n => n.GroupId == g);
        if (ids is { Length: > 0 }) { var wanted = ids.ToList(); query = query.Where(n => wanted.Contains(n.Id)); }
        var list = await query.ToListAsync(ct); foreach (var n in list) n.ReadAt = now;
        return await SaveAsync(db, ct, new { read = list.Count });
    }

    public static async Task<GroupResult> SetNotificationsMutedAsync(LifeVerseDbContext db, Guid me, Guid groupId, bool muted, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        ctx!.Member!.NotificationsMuted = muted; return await SaveAsync(db, ct);
    }

    // ---- announcements -------------------------------------------------------------------------------------------------------------------------------

    private static bool ValidAnnouncement(string title, string body) => title.Length is >= 1 and <= GroupRules.MaxAnnouncementTitle && body.Length is >= 1 and <= GroupRules.MaxAnnouncementBody;

    public static async Task<GroupResult> AnnouncementsAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error; var canManage = GroupRules.Has(ctx!.Actor!, GroupPermissions.Announce); // managers still see the expired ones
        var list = await db.GroupAnnouncements.AsNoTracking().Where(a => a.GroupId == groupId && a.DeletedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now || canManage)).OrderByDescending(a => a.Pinned).ThenByDescending(a => a.CreatedAt).Take(50).ToListAsync(ct);
        var names = await NamesAsync(db, list.Select(a => a.AuthorUserId), ct);
        return GroupResult.Done(list.Select(a => new AnnouncementItem(a.Id, a.Title, a.Body, a.Pinned, a.AuthorUserId, names.GetValueOrDefault(a.AuthorUserId, "?"), a.CreatedAt, a.EditedAt, a.ExpiresAt, a.AttachmentUrl)).ToList());
    }

    public static async Task<GroupResult> PostAnnouncementAsync(LifeVerseDbContext db, Guid me, Guid groupId, AnnouncementRequest r, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.Announce, now, ct); if (error is not null) return error;
        var title = CleanText(r.Title, false); var body = CleanText(r.Body, true);
        if (!ValidAnnouncement(title, body)) return GroupResult.Fail("bad_announcement");
        var attachment = (r.AttachmentUrl ?? string.Empty).Trim(); if (attachment.Length > 0 && (attachment.Length > 400 || !Uri.TryCreate(attachment, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps || link.UserInfo.Length > 0)) return GroupResult.Fail("bad_attachment");
        if (r.ExpiresAt is { } ends && (ends <= now.AddMinutes(1) || ends > now.AddDays(365))) return GroupResult.Fail("bad_expiry");
        var hour = now.AddHours(-1);
        if (await db.GroupAnnouncements.CountAsync(a => a.GroupId == groupId && a.CreatedAt > hour, ct) >= GroupRules.MaxAnnouncementsPerHour) return GroupResult.Fail("rate_limited");
        var pinned = r.Pinned == true;
        if (pinned && await db.GroupAnnouncements.CountAsync(a => a.GroupId == groupId && a.Pinned && a.DeletedAt == null, ct) >= GroupRules.MaxPinned) return GroupResult.Fail("too_many_pinned");
        var announcement = new GroupAnnouncement { Id = Guid.NewGuid(), GroupId = groupId, AuthorUserId = me, Title = title, Body = body, Pinned = pinned, ExpiresAt = r.ExpiresAt, AttachmentUrl = attachment, CreatedAt = now };
        db.GroupAnnouncements.Add(announcement); Log(db, groupId, me, "announcement_posted", null, new { title }, now);
        var users = await AddNotificationsAsync(db, groupId, null, me, "group_announcement", title, body, now, ct);
        var saved = await SaveAsync(db, ct, new { id = announcement.Id }); if (!saved.Ok) return saved;
        foreach (var user in users) await TellAsync(notify, user, "group_announcement", new { groupId, groupName = ctx!.Group.Name, id = announcement.Id, title, body });
        return saved;
    }

    public static async Task<GroupResult> UpdateAnnouncementAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid announcementId, AnnouncementRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.Announce, now, ct); if (error is not null) return error;
        var a = await db.GroupAnnouncements.SingleOrDefaultAsync(x => x.Id == announcementId && x.GroupId == groupId && x.DeletedAt == null, ct); if (a is null) return GroupResult.Fail("not_found");
        var title = r.Title is null ? a.Title : CleanText(r.Title, false); var body = r.Body is null ? a.Body : CleanText(r.Body, true);
        if (!ValidAnnouncement(title, body)) return GroupResult.Fail("bad_announcement");
        if (r.Pinned == true && !a.Pinned && await db.GroupAnnouncements.CountAsync(x => x.GroupId == groupId && x.Pinned && x.DeletedAt == null, ct) >= GroupRules.MaxPinned) return GroupResult.Fail("too_many_pinned");
        var textChanged = title != a.Title || body != a.Body;
        if (!textChanged && (r.Pinned is null || r.Pinned == a.Pinned)) return GroupResult.Done();
        a.Title = title; a.Body = body; if (r.Pinned is { } pin) a.Pinned = pin; if (textChanged) a.EditedAt = now;
        Log(db, groupId, me, "announcement_updated", null, new { title, pinned = a.Pinned }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> DeleteAnnouncementAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid announcementId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.Announce, now, ct); if (error is not null) return error;
        var a = await db.GroupAnnouncements.SingleOrDefaultAsync(x => x.Id == announcementId && x.GroupId == groupId && x.DeletedAt == null, ct); if (a is null) return GroupResult.Fail("not_found");
        a.DeletedAt = now; a.Pinned = false; Log(db, groupId, me, "announcement_deleted", null, new { title = a.Title }, now);
        return await SaveAsync(db, ct);
    }

    // ---- events only for members ----------------------------------------------------------------------------------------------------------------------------

    private static async Task<bool> CanUseIslandAsync(LifeVerseDbContext db, Guid islandId, Guid user, CancellationToken ct) =>
        await db.Islands.AnyAsync(i => i.Id == islandId && i.DeletedAt == null && (i.IsPublic || i.OwnerUserId == user || db.IslandPermissions.Any(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && p.Permission == "enter")), ct);

    public static async Task<GroupResult> EventsAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var events = await db.DiscoveryEvents.AsNoTracking().Where(e => e.GroupId == groupId && e.EndsAt > now).OrderBy(e => e.StartsAt).Take(50).ToListAsync(ct);
        var ids = events.Select(e => e.Id).ToList();
        var attendees = await db.EventAttendees.AsNoTracking().Where(a => ids.Contains(a.EventId)).Select(a => new { a.EventId, a.UserId }).ToListAsync(ct);
        var islandIds = events.Select(e => e.IslandId).Distinct().ToList();
        var islands = await db.Islands.AsNoTracking().Where(i => islandIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var names = await NamesAsync(db, events.Select(e => e.OrganizerUserId), ct);
        return GroupResult.Done(events.Select(e => new EventItem(e.Id, e.Name, e.Description, e.IslandId, islands.GetValueOrDefault(e.IslandId, "?"), e.StartsAt, e.EndsAt, e.Capacity,
            attendees.Count(a => a.EventId == e.Id), attendees.Any(a => a.EventId == e.Id && a.UserId == me), e.OrganizerUserId, names.GetValueOrDefault(e.OrganizerUserId, "?"))).ToList());
    }

    public static async Task<GroupResult> CreateEventAsync(LifeVerseDbContext db, Guid me, Guid groupId, GroupEventRequest r, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageEvents, now, ct); if (error is not null) return error;
        var name = CleanText(r.Name, false); var description = CleanText(r.Description, true);
        if (name.Length is < 1 or > 160) return GroupResult.Fail("bad_name");
        if (description.Length > 2000) return GroupResult.Fail("bad_description");
        if (r.StartsAt is not { } starts || r.EndsAt is not { } ends || starts >= ends || ends <= now || ends - starts > TimeSpan.FromDays(7)) return GroupResult.Fail("bad_time");
        var capacity = r.Capacity ?? 50; if (capacity is < 1 or > GroupRules.MaxEventCapacity) return GroupResult.Fail("bad_capacity");
        if (r.IslandId is not { } island || !await CanUseIslandAsync(db, island, me, ct)) return GroupResult.Fail("bad_island");
        if (await db.DiscoveryEvents.CountAsync(e => e.GroupId == groupId && e.EndsAt > now, ct) >= GroupRules.MaxUpcomingEvents) return GroupResult.Fail("too_many_events");
        var item = new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = island, OrganizerUserId = me, Name = name, Description = description, StartsAt = starts, EndsAt = ends, Capacity = capacity, IsPublic = false, GroupId = groupId, CreatedAt = now };
        db.DiscoveryEvents.Add(item); Log(db, groupId, me, "event_created", null, new { name, startsAt = starts }, now);
        var users = await AddNotificationsAsync(db, groupId, null, me, "group_event", name, $"{starts.ToLocalTime():dd/MM HH:mm}", now, ct);
        var saved = await SaveAsync(db, ct, new { id = item.Id }); if (!saved.Ok) return saved;
        foreach (var user in users) await TellAsync(notify, user, "group_event", new { groupId, groupName = ctx!.Group.Name, id = item.Id, name, startsAt = starts });
        return saved;
    }

    private static async Task<(DiscoveryEvent? Event, GroupResult? Error)> FindEventAsync(LifeVerseDbContext db, Guid groupId, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var e = await db.DiscoveryEvents.SingleOrDefaultAsync(x => x.Id == eventId && x.GroupId == groupId && x.EndsAt > now, ct);
        return e is null ? (null, GroupResult.Fail("not_found")) : (e, null);
    }

    public static async Task<GroupResult> JoinEventAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var (e, missing) = await FindEventAsync(db, groupId, eventId, now, ct); if (missing is not null) return missing;
        if (await db.EventAttendees.AnyAsync(a => a.EventId == eventId && a.UserId == me, ct)) return GroupResult.Done();
        if (await db.EventAttendees.CountAsync(a => a.EventId == eventId, ct) >= e!.Capacity) return GroupResult.Fail("event_full");
        db.EventAttendees.Add(new EventAttendee { Id = Guid.NewGuid(), EventId = eventId, UserId = me, JoinedAt = now });
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> LeaveEventAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid eventId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var (_, missing) = await FindEventAsync(db, groupId, eventId, now, ct); if (missing is not null) return missing;
        var place = await db.EventAttendees.SingleOrDefaultAsync(a => a.EventId == eventId && a.UserId == me, ct);
        if (place is not null) db.EventAttendees.Remove(place);
        return await SaveAsync(db, ct);
    }

    /// <summary>Cancels an event: whoever has the permission, or the member who made it. Those who had a place are told.</summary>
    public static async Task<GroupResult> CancelEventAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid eventId, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var (e, missing) = await FindEventAsync(db, groupId, eventId, now, ct); if (missing is not null) return missing;
        if (e!.OrganizerUserId != me && !GroupRules.Has(ctx!.Actor!, GroupPermissions.ManageEvents)) return GroupResult.Fail("forbidden");
        var places = await db.EventAttendees.Where(a => a.EventId == eventId).ToListAsync(ct);
        var attendees = places.Select(p => p.UserId).Where(u => u != me).ToList();
        db.EventAttendees.RemoveRange(places); db.DiscoveryEvents.Remove(e); Log(db, groupId, me, "event_cancelled", null, new { name = e.Name }, now);
        var users = await AddNotificationsAsync(db, groupId, attendees, me, "group_event_cancelled", e.Name, "O evento foi cancelado.", now, ct);
        var saved = await SaveAsync(db, ct); if (!saved.Ok) return saved;
        foreach (var user in users) await TellAsync(notify, user, "group_event_cancelled", new { groupId, groupName = ctx!.Group.Name, id = eventId, name = e.Name });
        return saved;
    }

    /// <summary>Somebody who is no longer a member gives up their places in the events of the group.</summary>
    private static async Task DropEventPlacesAsync(LifeVerseDbContext db, Guid groupId, Guid user, CancellationToken ct)
    {
        var ids = await db.DiscoveryEvents.Where(e => e.GroupId == groupId).Select(e => e.Id).ToListAsync(ct);
        if (ids.Count == 0) return;
        db.EventAttendees.RemoveRange(await db.EventAttendees.Where(a => a.UserId == user && ids.Contains(a.EventId)).ToListAsync(ct));
    }

    /// <summary>A group that ends takes its events with it.</summary>
    private static async Task EndGroupEventsAsync(LifeVerseDbContext db, Guid groupId, CancellationToken ct)
    {
        var events = await db.DiscoveryEvents.Where(e => e.GroupId == groupId).ToListAsync(ct); if (events.Count == 0) return;
        var ids = events.Select(e => e.Id).ToList();
        db.EventAttendees.RemoveRange(await db.EventAttendees.Where(a => ids.Contains(a.EventId)).ToListAsync(ct));
        db.DiscoveryEvents.RemoveRange(events);
    }
}
