using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record GroupResult(bool Ok, string? Error = null, object? Data = null)
{
    public static GroupResult Done(object? data = null) => new(true, null, data);
    public static GroupResult Fail(string error, object? data = null) => new(false, error, data);
}

/// <summary>Something to tell a person right away (a push to the clients that are connected); the service does not care how.</summary>
public delegate Task GroupNotifier(Guid userId, string type, object data);

public sealed record GroupProfileRequest(string? Name, string? Description, string? ImageUrl, string? Privacy, string? JoinPolicy, string? AccentColor, string? Emblem, string? Tag, int? MemberLimit);
public sealed record RoleRequest(string? Name, int? Rank, string[]? Permissions, string? Color);

/// <summary>
/// Groups 2.0: the rules of who may do what in a group. Every operation checks the group, the person's cargo, the hierarchy (nobody acts on or
/// promotes above themselves, nobody hands out a permission they lack), the limits, and then writes what happened to the audit history.
/// Nothing here trusts the client; the client only asks.
/// </summary>
public static partial class GroupService
{
    private static readonly string[] ModerationActions = ["member_kicked", "member_banned", "member_muted", "role_assigned"];
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // ---- loading ------------------------------------------------------------------------------------------------------------------------

    private sealed record Ctx(SocialGroup Group, GroupMember? Member, Dictionary<Guid, GroupRole> Roles, RoleView? Actor);

    public static RoleView ToView(GroupRole role) => new(role.Id, role.Name, role.Rank, GroupRules.SplitPermissions(role.Permissions), role.SystemKey == "owner", role.SystemKey == "member");

    /// <summary>The four starting roles, for a group that has none yet (a group made before cargos, or one made by older code).</summary>
    private static async Task<Dictionary<Guid, GroupRole>> RolesOfAsync(LifeVerseDbContext db, SocialGroup group, DateTimeOffset now, CancellationToken ct)
    {
        var roles = await db.GroupRoles.Where(r => r.GroupId == group.Id).ToListAsync(ct);
        foreach (var (key, name, rank, permissions) in GroupRules.StartingRoles)
            if (roles.All(r => r.SystemKey != key))
            {
                var role = new GroupRole { Id = Guid.NewGuid(), GroupId = group.Id, Name = name, SystemKey = key, Rank = rank, Permissions = GroupRules.JoinPermissions(permissions), CreatedAt = now };
                db.GroupRoles.Add(role); roles.Add(role);
            }
        return roles.ToDictionary(r => r.Id);
    }

    private static GroupRole RoleOf(GroupMember member, Dictionary<Guid, GroupRole> roles)
    {
        if (member.RoleId is { } id && roles.TryGetValue(id, out var role)) return role;
        var key = member.Role is "owner" or "admin" or "moderator" or "member" ? member.Role : "member";
        return roles.Values.First(r => r.SystemKey == key);
    }

    private static async Task<Ctx?> LoadAsync(LifeVerseDbContext db, Guid groupId, Guid? user, DateTimeOffset now, CancellationToken ct)
    {
        var group = await db.SocialGroups.SingleOrDefaultAsync(g => g.Id == groupId && g.DeletedAt == null, ct); if (group is null) return null;
        var roles = await RolesOfAsync(db, group, now, ct);
        var member = user is { } u ? await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == u, ct) : null;
        if (member is not null && member.RoleId is null) member.RoleId = RoleOf(member, roles).Id;
        return new Ctx(group, member, roles, member is null ? null : ToView(RoleOf(member, roles)));
    }

    private static void Log(LifeVerseDbContext db, Guid groupId, Guid actor, string action, Guid? target, object? detail, DateTimeOffset now) =>
        db.GroupAuditEntries.Add(new GroupAuditEntry { Id = Guid.NewGuid(), GroupId = groupId, ActorUserId = actor, Action = action, TargetUserId = target, DetailJson = JsonSerializer.Serialize(detail ?? new { }, Web), CreatedAt = now });

    private static async Task<GroupResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return GroupResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return GroupResult.Fail("conflict"); }
    }

    private static async Task TellAsync(GroupNotifier? notify, Guid user, string type, object data) { if (notify is null) return; try { await notify(user, type, data); } catch (Exception) { /* a push that cannot be delivered is not worth failing the action */ } }

    private static async Task<bool> BannedAsync(LifeVerseDbContext db, Guid groupId, Guid user, DateTimeOffset now, CancellationToken ct) =>
        await db.GroupBans.AnyAsync(b => b.GroupId == groupId && b.UserId == user && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct);

    /// <summary>Whoever takes many moderation actions in a short time is stopped (the leader is not): a stolen or abusive account cannot empty a group in a minute.</summary>
    private static async Task<bool> BurstAsync(LifeVerseDbContext db, Guid groupId, RoleView actor, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        if (actor.IsLeader) return false;
        var since = now - GroupRules.ModerationBurstWindow;
        return await db.GroupAuditEntries.CountAsync(e => e.GroupId == groupId && e.ActorUserId == user && e.CreatedAt > since && ModerationActions.Contains(e.Action), ct) >= GroupRules.ModerationBurstLimit;
    }

    // ---- creating and the profile -----------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> CreateAsync(LifeVerseDbContext db, Guid me, GroupProfileRequest request, DateTimeOffset now, CancellationToken ct)
    {
        if (!GroupRules.ValidName(request.Name, out var name)) return GroupResult.Fail("bad_name");
        var description = (request.Description ?? string.Empty).Trim(); if (description.Length > 600) return GroupResult.Fail("bad_description");
        var privacy = request.Privacy ?? "public"; var policy = request.JoinPolicy ?? (privacy == "private" ? GroupRules.InviteOnly : GroupRules.Request);
        if (!GroupRules.Privacies.Contains(privacy) || !GroupRules.JoinPolicies.Contains(policy) || privacy == "private" && policy != GroupRules.InviteOnly) return GroupResult.Fail("bad_policy");
        var accent = request.AccentColor ?? "#12F2E0"; var emblem = request.Emblem ?? "people";
        if (!GroupRules.ValidColour(accent) || !GroupRules.Emblems.Contains(emblem) || !GroupRules.ValidImageUrl(request.ImageUrl)) return GroupResult.Fail("bad_identity");
        if (!GroupRules.ValidTag(request.Tag)) return GroupResult.Fail("bad_tag");
        var limit = Math.Clamp(request.MemberLimit ?? GroupRules.DefaultMemberLimit, 2, GroupRules.MaxMemberLimit);
        if (await db.SocialGroups.CountAsync(g => g.OwnerUserId == me && g.DeletedAt == null, ct) >= GroupRules.MaxGroupsCreated) return GroupResult.Fail("too_many_groups_created");
        if (await db.GroupMembers.CountAsync(m => m.UserId == me, ct) >= GroupRules.MaxMemberships) return GroupResult.Fail("too_many_memberships");
        var key = name.ToLowerInvariant();
        if (await db.SocialGroups.AnyAsync(g => g.NameKey == key && g.DeletedAt == null, ct)) return GroupResult.Fail("name_taken");
        var tag = string.IsNullOrEmpty(request.Tag) ? null : request.Tag.ToUpperInvariant();
        if (tag is not null && await db.SocialGroups.AnyAsync(g => g.Tag == tag && g.DeletedAt == null, ct)) return GroupResult.Fail("tag_taken");
        var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = me, Name = name, NameKey = key, Description = description, ImageUrl = string.IsNullOrEmpty(request.ImageUrl) ? null : request.ImageUrl, Tag = tag, Privacy = privacy, JoinPolicy = policy, AccentColor = accent.ToUpperInvariant(), Emblem = emblem, MemberLimit = limit, CreatedAt = now, UpdatedAt = now };
        db.SocialGroups.Add(group);
        var roles = await RolesOfAsync(db, group, now, ct);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = me, Role = "owner", RoleId = roles.Values.First(r => r.SystemKey == "owner").Id, JoinedAt = now });
        Log(db, group.Id, me, "group_created", null, new { name }, now);
        var saved = await SaveAsync(db, ct, new { id = group.Id }); return saved;
    }

    public static async Task<GroupResult> UpdateProfileAsync(LifeVerseDbContext db, Guid me, Guid groupId, GroupProfileRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.EditProfile)) return GroupResult.Fail("forbidden");
        var g = ctx.Group; var changed = new List<string>();
        if (r.Name is not null && r.Name != g.Name)
        {
            if (!GroupRules.ValidName(r.Name, out var name)) return GroupResult.Fail("bad_name");
            var key = name.ToLowerInvariant(); if (key != g.NameKey && await db.SocialGroups.AnyAsync(x => x.NameKey == key && x.DeletedAt == null && x.Id != g.Id, ct)) return GroupResult.Fail("name_taken");
            g.Name = name; g.NameKey = key; changed.Add("name");
        }
        if (r.Description is not null) { var d = r.Description.Trim(); if (d.Length > 600) return GroupResult.Fail("bad_description"); if (d != g.Description) { g.Description = d; changed.Add("description"); } }
        if (r.ImageUrl is not null) { if (!GroupRules.ValidImageUrl(r.ImageUrl)) return GroupResult.Fail("bad_identity"); var url = r.ImageUrl.Length == 0 ? null : r.ImageUrl; if (url != g.ImageUrl) { g.ImageUrl = url; changed.Add("image"); } }
        if (r.AccentColor is not null) { if (!GroupRules.ValidColour(r.AccentColor) || r.AccentColor.Length == 0) return GroupResult.Fail("bad_identity"); if (!string.Equals(r.AccentColor, g.AccentColor, StringComparison.OrdinalIgnoreCase)) { g.AccentColor = r.AccentColor.ToUpperInvariant(); changed.Add("color"); } }
        if (r.Emblem is not null) { if (!GroupRules.Emblems.Contains(r.Emblem)) return GroupResult.Fail("bad_identity"); if (r.Emblem != g.Emblem) { g.Emblem = r.Emblem; changed.Add("emblem"); } }
        if (r.Tag is not null)
        {
            if (!GroupRules.ValidTag(r.Tag)) return GroupResult.Fail("bad_tag"); var tag = r.Tag.Length == 0 ? null : r.Tag.ToUpperInvariant();
            if (tag != g.Tag) { if (tag is not null && await db.SocialGroups.AnyAsync(x => x.Tag == tag && x.DeletedAt == null && x.Id != g.Id, ct)) return GroupResult.Fail("tag_taken"); g.Tag = tag; changed.Add("tag"); }
        }
        var privacy = r.Privacy ?? g.Privacy; var policy = r.JoinPolicy ?? g.JoinPolicy;
        if (!GroupRules.Privacies.Contains(privacy) || !GroupRules.JoinPolicies.Contains(policy)) return GroupResult.Fail("bad_policy");
        if (privacy == "private" && policy != GroupRules.InviteOnly) { if (r.JoinPolicy is not null) return GroupResult.Fail("bad_policy"); policy = GroupRules.InviteOnly; } // a hidden group is entered by invitation only
        if (privacy != g.Privacy) { g.Privacy = privacy; changed.Add("privacy"); }
        if (policy != g.JoinPolicy) { g.JoinPolicy = policy; changed.Add("join_policy"); }
        if (r.MemberLimit is { } limit)
        {
            var members = await db.GroupMembers.CountAsync(m => m.GroupId == g.Id, ct);
            if (limit < Math.Max(2, members) || limit > GroupRules.MaxMemberLimit) return GroupResult.Fail("bad_limit");
            if (limit != g.MemberLimit) { g.MemberLimit = limit; changed.Add("member_limit"); }
        }
        if (changed.Count == 0) return GroupResult.Done();
        g.UpdatedAt = now; Log(db, g.Id, me, "profile_updated", null, new { fields = changed }, now);
        return await SaveAsync(db, ct);
    }

    // ---- looking ----------------------------------------------------------------------------------------------------------------------------

    public sealed record GroupCard(Guid Id, string Name, string? Tag, string Description, string? ImageUrl, string AccentColor, string Emblem, string Privacy, string JoinPolicy, int Members, int MemberLimit, bool IsMember, bool RequestPending, bool Invited, bool Banned);

    private static async Task<List<GroupCard>> CardsAsync(LifeVerseDbContext db, IReadOnlyList<SocialGroup> groups, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var ids = groups.Select(g => g.Id).ToList();
        var counts = await db.GroupMembers.AsNoTracking().Where(m => ids.Contains(m.GroupId)).GroupBy(m => m.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var mine = (await db.GroupMembers.AsNoTracking().Where(m => m.UserId == me && ids.Contains(m.GroupId)).Select(m => m.GroupId).ToListAsync(ct)).ToHashSet();
        var requests = (await db.GroupJoinRequests.AsNoTracking().Where(r => r.UserId == me && r.Status == "pending" && ids.Contains(r.GroupId)).Select(r => r.GroupId).ToListAsync(ct)).ToHashSet();
        var invites = (await db.GroupInvites.AsNoTracking().Where(i => i.InvitedUserId == me && i.Status == "pending" && ids.Contains(i.GroupId)).Select(i => i.GroupId).ToListAsync(ct)).ToHashSet();
        var bans = (await db.GroupBans.AsNoTracking().Where(b => b.UserId == me && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now) && ids.Contains(b.GroupId)).Select(b => b.GroupId).ToListAsync(ct)).ToHashSet();
        return [.. groups.Select(g => new GroupCard(g.Id, g.Name, g.Tag, g.Description, g.ImageUrl, g.AccentColor, g.Emblem, g.Privacy, g.JoinPolicy, counts.GetValueOrDefault(g.Id), g.MemberLimit, mine.Contains(g.Id), requests.Contains(g.Id), invites.Contains(g.Id), bans.Contains(g.Id)))];
    }

    /// <summary>The groups anybody can find: public ones, by name, tag or description. Private groups never show up.</summary>
    public static async Task<IReadOnlyList<GroupCard>> SearchAsync(LifeVerseDbContext db, Guid me, string? query, int page, DateTimeOffset now, CancellationToken ct)
    {
        var q = (query ?? string.Empty).Trim().ToLowerInvariant(); page = Math.Max(0, page);
        var groups = db.SocialGroups.AsNoTracking().Where(g => g.DeletedAt == null && g.Privacy == "public");
        if (q.Length > 0) groups = groups.Where(g => g.NameKey.Contains(q) || (g.Tag != null && g.Tag.ToLower() == q) || g.Description.ToLower().Contains(q));
        var list = await groups.OrderBy(g => g.NameKey).Skip(page * 20).Take(20).ToListAsync(ct);
        return await CardsAsync(db, list, me, now, ct);
    }

    public static async Task<IReadOnlyList<GroupCard>> MineAsync(LifeVerseDbContext db, Guid me, DateTimeOffset now, CancellationToken ct)
    {
        var ids = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == me).Select(m => m.GroupId).ToListAsync(ct);
        var list = await db.SocialGroups.AsNoTracking().Where(g => ids.Contains(g.Id) && g.DeletedAt == null).OrderBy(g => g.NameKey).ToListAsync(ct);
        return await CardsAsync(db, list, me, now, ct);
    }

    public sealed record GroupView(GroupCard Card, string? MyRole, int MyRank, string[] MyPermissions, bool IsLeader, DateTimeOffset CreatedAt, string LeaderName, DateTimeOffset? MutedUntil, bool NotificationsMuted = false);

    /// <summary>A group as one person sees it. Members see everything about themselves; others see the public profile of a public group, and of a private group only when invited.</summary>
    public static async Task<GroupResult> ViewAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx is null) return GroupResult.Fail("not_found");
        var invited = ctx.Member is null && await db.GroupInvites.AnyAsync(i => i.GroupId == groupId && i.InvitedUserId == me && i.Status == "pending", ct);
        if (ctx.Member is null && ctx.Group.Privacy == "private" && !invited) return GroupResult.Fail("not_found");
        var card = (await CardsAsync(db, [ctx.Group], me, now, ct))[0];
        var leader = await db.Users.AsNoTracking().Where(u => u.Id == ctx.Group.OwnerUserId).Select(u => u.Username).SingleOrDefaultAsync(ct) ?? "?";
        var role = ctx.Member is null ? null : RoleOf(ctx.Member, ctx.Roles);
        return GroupResult.Done(new GroupView(card, role?.Name, role?.Rank ?? -1, ctx.Actor is null ? [] : ctx.Actor.IsLeader ? [.. GroupPermissions.Known] : [.. ctx.Actor.Permissions], ctx.Actor?.IsLeader == true, ctx.Group.CreatedAt, leader, ctx.Member?.MutedUntil, ctx.Member?.NotificationsMuted == true));
    }

    // ---- coming in ------------------------------------------------------------------------------------------------------------------------------

    private static async Task<string?> CannotJoinAsync(LifeVerseDbContext db, Ctx ctx, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        if (ctx.Member is not null) return "already_member";
        if (await BannedAsync(db, ctx.Group.Id, user, now, ct)) return "banned";
        if (await db.GroupMembers.CountAsync(m => m.GroupId == ctx.Group.Id, ct) >= ctx.Group.MemberLimit) return "group_full";
        if (await db.GroupMembers.CountAsync(m => m.UserId == user, ct) >= GroupRules.MaxMemberships) return "too_many_memberships";
        return null;
    }

    private static void AddMember(LifeVerseDbContext db, Ctx ctx, Guid user, DateTimeOffset now)
    {
        var member = ctx.Roles.Values.First(r => r.SystemKey == "member");
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = ctx.Group.Id, UserId = user, Role = "member", RoleId = member.Id, JoinedAt = now });
    }

    private static async Task NotifyApproversAsync(LifeVerseDbContext db, Ctx ctx, string permission, string type, object data, GroupNotifier? notify, CancellationToken ct)
    {
        var members = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == ctx.Group.Id).ToListAsync(ct);
        foreach (var member in members.Where(m => GroupRules.Has(ToView(RoleOf(m, ctx.Roles)), permission)).Take(20)) await TellAsync(notify, member.UserId, type, data);
    }

    /// <summary>Joins a group (open), or asks to (request). Private groups and invitation-only ones are entered by invitation.</summary>
    public static async Task<GroupResult> JoinAsync(LifeVerseDbContext db, Guid me, Guid groupId, string? message, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx is null) return GroupResult.Fail("not_found");
        if (ctx.Member is null && ctx.Group.Privacy == "private") return GroupResult.Fail("not_found"); // it cannot be found, so it cannot be joined
        if (await CannotJoinAsync(db, ctx, me, now, ct) is { } why) return GroupResult.Fail(why);
        if (ctx.Group.JoinPolicy == GroupRules.InviteOnly) return GroupResult.Fail("invite_only");
        if (ctx.Group.JoinPolicy == GroupRules.Open)
        {
            AddMember(db, ctx, me, now); Log(db, groupId, me, "member_joined", me, new { how = "open" }, now);
            return await SaveAsync(db, ct, new { joined = true });
        }
        var text = (message ?? string.Empty).Trim(); if (text.Length > 300) return GroupResult.Fail("bad_message");
        if (await db.GroupJoinRequests.AnyAsync(r => r.GroupId == groupId && r.UserId == me && r.Status == "pending", ct)) return GroupResult.Done(new { requested = true });
        if (await db.GroupJoinRequests.CountAsync(r => r.UserId == me && r.Status == "pending", ct) >= GroupRules.MaxPendingRequestsPerUser) return GroupResult.Fail("too_many_requests");
        var request = new GroupJoinRequest { Id = Guid.NewGuid(), GroupId = groupId, UserId = me, Message = text, Status = "pending", CreatedAt = now };
        db.GroupJoinRequests.Add(request);
        var saved = await SaveAsync(db, ct, new { requested = true }); if (!saved.Ok) return saved;
        var name = await db.Users.AsNoTracking().Where(u => u.Id == me).Select(u => u.Username).SingleOrDefaultAsync(ct) ?? "?";
        await NotifyApproversAsync(db, ctx, GroupPermissions.ApproveRequests, "group_join_request", new { groupId, groupName = ctx.Group.Name, userId = me, username = name }, notify, ct);
        return saved;
    }

    public static async Task<GroupResult> CancelRequestAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var request = await db.GroupJoinRequests.SingleOrDefaultAsync(r => r.GroupId == groupId && r.UserId == me && r.Status == "pending", ct); if (request is null) return GroupResult.Fail("not_found");
        request.Status = "cancelled"; request.DecidedAt = now; return await SaveAsync(db, ct);
    }

    public sealed record RequestItem(Guid Id, Guid UserId, string Username, string Message, DateTimeOffset At);

    public static async Task<GroupResult> RequestsAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ApproveRequests)) return GroupResult.Fail("forbidden");
        var list = await db.GroupJoinRequests.AsNoTracking().Where(r => r.GroupId == groupId && r.Status == "pending").OrderBy(r => r.CreatedAt).Take(100).ToListAsync(ct);
        var ids = list.Select(r => r.UserId).ToList(); var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return GroupResult.Done(list.Select(r => new RequestItem(r.Id, r.UserId, names.GetValueOrDefault(r.UserId, "?"), r.Message, r.CreatedAt)).ToList());
    }

    public static async Task<GroupResult> DecideRequestAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid requestId, bool approve, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ApproveRequests)) return GroupResult.Fail("forbidden");
        var request = await db.GroupJoinRequests.SingleOrDefaultAsync(r => r.Id == requestId && r.GroupId == groupId && r.Status == "pending", ct); if (request is null) return GroupResult.Fail("not_found");
        if (approve)
        {
            var target = await LoadAsync(db, groupId, request.UserId, now, ct); if (target is null) return GroupResult.Fail("not_found");
            var why = await CannotJoinAsync(db, target, request.UserId, now, ct);
            if (why is not null && why != "already_member") return GroupResult.Fail(why); // banned since asking, group full, too many groups
            if (why is null) AddMember(db, ctx, request.UserId, now);
        }
        request.Status = approve ? "approved" : "denied"; request.DecidedAt = now; request.DecidedByUserId = me;
        Log(db, groupId, me, approve ? "request_approved" : "request_denied", request.UserId, new { }, now);
        var saved = await SaveAsync(db, ct); if (!saved.Ok) return saved;
        await TellAsync(notify, request.UserId, "group_request_decided", new { groupId, groupName = ctx.Group.Name, approved = approve });
        return saved;
    }

    public static async Task<GroupResult> InviteAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid userId, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.Invite)) return GroupResult.Fail("forbidden");
        if (userId == me) return GroupResult.Fail("bad_target");
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null, ct)) return GroupResult.Fail("not_found");
        if (await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct)) return GroupResult.Fail("already_member");
        if (await BannedAsync(db, groupId, userId, now, ct)) return GroupResult.Fail("banned");
        if (await db.GroupInvites.AnyAsync(i => i.GroupId == groupId && i.InvitedUserId == userId && i.Status == "pending", ct)) return GroupResult.Done(new { invited = true });
        if (await db.GroupInvites.CountAsync(i => i.GroupId == groupId && i.Status == "pending", ct) >= GroupRules.MaxPendingInvites) return GroupResult.Fail("too_many_invites");
        db.GroupInvites.Add(new GroupInvite { Id = Guid.NewGuid(), GroupId = groupId, InvitedUserId = userId, InvitedByUserId = me, Status = "pending", CreatedAt = now });
        Log(db, groupId, me, "invited", userId, new { }, now);
        var saved = await SaveAsync(db, ct, new { invited = true }); if (!saved.Ok) return saved;
        await TellAsync(notify, userId, "group_invite", new { groupId, groupName = ctx.Group.Name, from = me });
        return saved;
    }

    /// <summary>Answers an invitation. Accepting goes through the same checks as any other way in (banned, full group, limits).</summary>
    public static async Task<GroupResult> RespondToInviteAsync(LifeVerseDbContext db, Guid me, Guid inviteId, bool accept, DateTimeOffset now, CancellationToken ct)
    {
        var invite = await db.GroupInvites.SingleOrDefaultAsync(i => i.Id == inviteId && i.InvitedUserId == me && i.Status == "pending", ct); if (invite is null) return GroupResult.Fail("not_found");
        invite.Status = accept ? "accepted" : "declined";
        if (accept)
        {
            var ctx = await LoadAsync(db, invite.GroupId, me, now, ct);
            if (ctx is null) { await db.SaveChangesAsync(ct); return GroupResult.Fail("not_found"); }
            if (await CannotJoinAsync(db, ctx, me, now, ct) is { } why) { if (why != "already_member") { invite.Status = "pending"; return GroupResult.Fail(why); } await db.SaveChangesAsync(ct); return GroupResult.Done(); }
            AddMember(db, ctx, me, now); Log(db, invite.GroupId, me, "member_joined", me, new { how = "invite", invitedBy = invite.InvitedByUserId }, now);
            foreach (var pending in await db.GroupJoinRequests.Where(r => r.GroupId == invite.GroupId && r.UserId == me && r.Status == "pending").ToListAsync(ct)) { pending.Status = "approved"; pending.DecidedAt = now; }
        }
        return await SaveAsync(db, ct, new { groupId = invite.GroupId });
    }

    // ---- members and moderation --------------------------------------------------------------------------------------------------------------------

    public sealed record MemberItem(Guid UserId, string Username, Guid RoleId, string RoleName, int Rank, DateTimeOffset JoinedAt, DateTimeOffset? MutedUntil);

    public static async Task<GroupResult> MembersAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Member is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        var members = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).ToListAsync(ct);
        var ids = members.Select(m => m.UserId).ToList(); var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return GroupResult.Done(members.Select(m => { var role = RoleOf(m, ctx.Roles); return new MemberItem(m.UserId, names.GetValueOrDefault(m.UserId, "?"), role.Id, role.Name, role.Rank, m.JoinedAt, m.MutedUntil > now ? m.MutedUntil : null); }).OrderByDescending(m => m.Rank).ThenBy(m => m.Username).ToList());
    }

    /// <summary>Loads the actor and the target and checks the hierarchy: the target must be a member who ranks strictly below the actor.</summary>
    private static async Task<(Ctx? Ctx, GroupMember? Target, GroupRole? TargetRole, GroupResult? Error)> ForModerationAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, string permission, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx is null) return (null, null, null, GroupResult.Fail("not_found"));
        if (ctx.Actor is null || !GroupRules.Has(ctx.Actor, permission)) return (ctx, null, null, GroupResult.Fail("forbidden"));
        if (targetUser == me) return (ctx, null, null, GroupResult.Fail("bad_target"));
        var target = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == targetUser, ct); if (target is null) return (ctx, null, null, GroupResult.Fail("not_found"));
        var targetRole = RoleOf(target, ctx.Roles);
        if (targetRole.SystemKey == "owner" || !GroupRules.CanActOn(ctx.Actor, targetRole.Rank)) return (ctx, target, targetRole, GroupResult.Fail("outranked"));
        if (await BurstAsync(db, groupId, ctx.Actor, me, now, ct)) return (ctx, target, targetRole, GroupResult.Fail("rate_limited"));
        return (ctx, target, targetRole, null);
    }

    public static async Task<GroupResult> KickAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, string? reason, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, target, _, error) = await ForModerationAsync(db, me, groupId, targetUser, GroupPermissions.Kick, now, ct); if (error is not null) return error;
        db.GroupMembers.Remove(target!); await DropEventPlacesAsync(db, groupId, targetUser, ct); Log(db, groupId, me, "member_kicked", targetUser, new { reason = Clean(reason) }, now);
        var saved = await SaveAsync(db, ct); if (saved.Ok) await TellAsync(notify, targetUser, "group_kicked", new { groupId, groupName = ctx!.Group.Name }); return saved;
    }

    public static async Task<GroupResult> BanAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, string? reason, int? days, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        if (days is < 1 or > 3650) return GroupResult.Fail("bad_duration");
        var (ctx, target, _, error) = await ForModerationAsync(db, me, groupId, targetUser, GroupPermissions.Ban, now, ct); if (error is not null) return error;
        db.GroupMembers.Remove(target!); await DropEventPlacesAsync(db, groupId, targetUser, ct);
        db.GroupBans.Add(new GroupBan { Id = Guid.NewGuid(), GroupId = groupId, UserId = targetUser, BannedByUserId = me, Reason = Clean(reason), CreatedAt = now, ExpiresAt = days is { } d ? now.AddDays(d) : null });
        Log(db, groupId, me, "member_banned", targetUser, new { reason = Clean(reason), days }, now);
        var saved = await SaveAsync(db, ct); if (saved.Ok) await TellAsync(notify, targetUser, "group_banned", new { groupId, groupName = ctx!.Group.Name, days }); return saved;
    }

    public static async Task<GroupResult> UnbanAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.Ban)) return GroupResult.Fail("forbidden");
        var bans = await db.GroupBans.Where(b => b.GroupId == groupId && b.UserId == targetUser && b.RevokedAt == null).ToListAsync(ct); if (bans.Count == 0) return GroupResult.Fail("not_found");
        foreach (var ban in bans) ban.RevokedAt = now;
        Log(db, groupId, me, "member_unbanned", targetUser, new { }, now); return await SaveAsync(db, ct);
    }

    public sealed record BanItem(Guid UserId, string Username, string Reason, DateTimeOffset At, DateTimeOffset? ExpiresAt);

    public static async Task<GroupResult> BansAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.Ban)) return GroupResult.Fail("forbidden");
        var list = await db.GroupBans.AsNoTracking().Where(b => b.GroupId == groupId && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now)).OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct);
        var ids = list.Select(b => b.UserId).ToList(); var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return GroupResult.Done(list.Select(b => new BanItem(b.UserId, names.GetValueOrDefault(b.UserId, "?"), b.Reason, b.CreatedAt, b.ExpiresAt)).ToList());
    }

    /// <summary>Silences a member for some minutes (up to a week); 0 minutes lifts it.</summary>
    public static async Task<GroupResult> MuteAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, int minutes, string? reason, DateTimeOffset now, CancellationToken ct)
    {
        if (minutes is < 0 or > 7 * 24 * 60) return GroupResult.Fail("bad_duration");
        var (_, target, _, error) = await ForModerationAsync(db, me, groupId, targetUser, GroupPermissions.Mute, now, ct); if (error is not null) return error;
        target!.MutedUntil = minutes == 0 ? null : now.AddMinutes(minutes);
        Log(db, groupId, me, "member_muted", targetUser, new { minutes, reason = Clean(reason) }, now); return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> SetRoleAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid targetUser, Guid roleId, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct, string? roleKey = null)
    {
        var (ctx, target, targetRole, error) = await ForModerationAsync(db, me, groupId, targetUser, GroupPermissions.AssignRoles, now, ct); if (error is not null) return error;
        var role = roleKey is not null ? ctx!.Roles.Values.FirstOrDefault(r => r.SystemKey == roleKey) : ctx!.Roles.GetValueOrDefault(roleId);
        if (role is null) return GroupResult.Fail("not_found");
        if (role.SystemKey == "owner" || !GroupRules.CanManageRank(ctx.Actor!, role.Rank) && !(role.Rank == GroupRules.MemberRank)) return GroupResult.Fail("outranked"); // only below one's own cargo (the plain member cargo is always allowed)
        if (role.Id == targetRole!.Id) return GroupResult.Done();
        target!.RoleId = role.Id; target.Role = role.SystemKey.Length > 0 ? role.SystemKey : role.Name;
        Log(db, groupId, me, "role_assigned", targetUser, new { from = targetRole.Name, to = role.Name }, now);
        var saved = await SaveAsync(db, ct); if (saved.Ok) await TellAsync(notify, targetUser, "group_role_changed", new { groupId, groupName = ctx.Group.Name, role = role.Name }); return saved;
    }

    private static string Clean(string? text) { var t = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim(); return t.Length > 300 ? t[..300] : t; }

    // ---- cargos -------------------------------------------------------------------------------------------------------------------------------------

    public sealed record RoleItem(Guid Id, string Name, string SystemKey, int Rank, string[] Permissions, string Color, int Members, bool CanEdit);

    public static async Task<GroupResult> RolesAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Member is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        var counts = await db.GroupMembers.AsNoTracking().Where(m => m.GroupId == groupId).GroupBy(m => m.RoleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key ?? Guid.Empty, g => g.Count, ct);
        var canManage = GroupRules.Has(ctx.Actor!, GroupPermissions.ManageRoles);
        return GroupResult.Done(ctx.Roles.Values.OrderByDescending(r => r.Rank).Select(r => new RoleItem(r.Id, r.Name, r.SystemKey, r.Rank, [.. GroupRules.SplitPermissions(r.Permissions).Order()], r.Color, counts.GetValueOrDefault(r.Id), canManage && r.SystemKey != "owner" && GroupRules.CanManageRank(ctx.Actor!, r.Rank) || canManage && r.SystemKey == "member")).ToList());
    }

    public static async Task<GroupResult> CreateRoleAsync(LifeVerseDbContext db, Guid me, Guid groupId, RoleRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ManageRoles)) return GroupResult.Fail("forbidden");
        if (!GroupRules.ValidRoleName(r.Name, out var name)) return GroupResult.Fail("bad_name");
        var rank = r.Rank ?? 10; if (rank is < 1 or > GroupRules.MaxCustomRank) return GroupResult.Fail("bad_rank");
        if (!GroupRules.CanManageRank(ctx.Actor, rank)) return GroupResult.Fail("outranked");
        if (GroupRules.CleanPermissions(r.Permissions) is not { } permissions) return GroupResult.Fail("bad_permissions");
        if (!GroupRules.CanGrant(ctx.Actor, permissions)) return GroupResult.Fail("cannot_grant");
        if (!GroupRules.ValidColour(r.Color)) return GroupResult.Fail("bad_identity");
        if (ctx.Roles.Count >= GroupRules.MaxRoles) return GroupResult.Fail("too_many_roles");
        if (ctx.Roles.Values.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return GroupResult.Fail("name_taken");
        var role = new GroupRole { Id = Guid.NewGuid(), GroupId = groupId, Name = name, Rank = rank, Permissions = GroupRules.JoinPermissions(permissions), Color = string.IsNullOrEmpty(r.Color) ? "#A9B3BD" : r.Color.ToUpperInvariant(), CreatedAt = now };
        db.GroupRoles.Add(role); Log(db, groupId, me, "role_created", null, new { name, rank, permissions }, now);
        return await SaveAsync(db, ct, new { id = role.Id });
    }

    public static async Task<GroupResult> UpdateRoleAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid roleId, RoleRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ManageRoles)) return GroupResult.Fail("forbidden");
        if (!ctx.Roles.TryGetValue(roleId, out var role)) return GroupResult.Fail("not_found");
        if (role.SystemKey == "owner") return GroupResult.Fail("outranked"); // the leader cargo is never edited, only handed over
        var isMember = role.SystemKey == "member";
        if (!isMember && !GroupRules.CanManageRank(ctx.Actor, role.Rank)) return GroupResult.Fail("outranked");
        var changes = new List<string>();
        if (r.Name is not null && r.Name != role.Name)
        {
            if (role.SystemKey.Length > 0) return GroupResult.Fail("system_role"); // the starting cargos keep their names
            if (!GroupRules.ValidRoleName(r.Name, out var name)) return GroupResult.Fail("bad_name");
            if (ctx.Roles.Values.Any(x => x.Id != role.Id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) return GroupResult.Fail("name_taken");
            role.Name = name; changes.Add("name");
            foreach (var m in await db.GroupMembers.Where(x => x.GroupId == groupId && x.RoleId == role.Id).ToListAsync(ct)) m.Role = name;
        }
        if (r.Rank is { } rank && rank != role.Rank)
        {
            if (role.SystemKey.Length > 0) return GroupResult.Fail("system_role");
            if (rank is < 1 or > GroupRules.MaxCustomRank) return GroupResult.Fail("bad_rank");
            if (!GroupRules.CanManageRank(ctx.Actor, rank)) return GroupResult.Fail("outranked");
            role.Rank = rank; changes.Add("rank");
        }
        if (r.Permissions is not null)
        {
            if (GroupRules.CleanPermissions(r.Permissions) is not { } permissions) return GroupResult.Fail("bad_permissions");
            var old = GroupRules.SplitPermissions(role.Permissions);
            if (!GroupRules.CanGrant(ctx.Actor, permissions.Where(p => !old.Contains(p)))) return GroupResult.Fail("cannot_grant"); // adding needs holding; taking away does not
            var text = GroupRules.JoinPermissions(permissions); if (text != role.Permissions) { role.Permissions = text; changes.Add("permissions"); }
        }
        if (r.Color is not null) { if (!GroupRules.ValidColour(r.Color) || r.Color.Length == 0) return GroupResult.Fail("bad_identity"); if (!string.Equals(r.Color, role.Color, StringComparison.OrdinalIgnoreCase)) { role.Color = r.Color.ToUpperInvariant(); changes.Add("color"); } }
        if (changes.Count == 0) return GroupResult.Done();
        Log(db, groupId, me, "role_updated", null, new { role = role.Name, fields = changes }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> DeleteRoleAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid roleId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ManageRoles)) return GroupResult.Fail("forbidden");
        if (!ctx.Roles.TryGetValue(roleId, out var role)) return GroupResult.Fail("not_found");
        if (role.SystemKey.Length > 0) return GroupResult.Fail("system_role");
        if (!GroupRules.CanManageRank(ctx.Actor, role.Rank)) return GroupResult.Fail("outranked");
        var fallback = ctx.Roles.Values.First(x => x.SystemKey == "member");
        var moved = 0; foreach (var m in await db.GroupMembers.Where(x => x.GroupId == groupId && x.RoleId == role.Id).ToListAsync(ct)) { m.RoleId = fallback.Id; m.Role = "member"; moved++; }
        db.GroupRoles.Remove(role); Log(db, groupId, me, "role_deleted", null, new { name = role.Name, members = moved }, now);
        return await SaveAsync(db, ct);
    }

    // ---- history --------------------------------------------------------------------------------------------------------------------------------------

    public sealed record AuditItem(Guid Id, DateTimeOffset At, string Action, Guid ActorUserId, string Actor, Guid? TargetUserId, string? Target, JsonElement Detail);

    public static async Task<GroupResult> AuditAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset? before, int take, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ViewAudit)) return GroupResult.Fail("forbidden");
        var query = db.GroupAuditEntries.AsNoTracking().Where(e => e.GroupId == groupId); if (before is { } b) query = query.Where(e => e.CreatedAt < b);
        var list = await query.OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(take <= 0 ? 50 : take, 1, 200)).ToListAsync(ct);
        var ids = list.Select(e => e.ActorUserId).Concat(list.Where(e => e.TargetUserId is not null).Select(e => e.TargetUserId!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return GroupResult.Done(list.Select(e => new AuditItem(e.Id, e.CreatedAt, e.Action, e.ActorUserId, names.GetValueOrDefault(e.ActorUserId, "?"), e.TargetUserId, e.TargetUserId is { } t ? names.GetValueOrDefault(t, "?") : null, JsonSerializer.Deserialize<JsonElement>(e.DetailJson))).ToList());
    }

    // ---- leaving, leadership and the end ---------------------------------------------------------------------------------------------------------------

    public static async Task<GroupResult> LeaveAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Member is null) return GroupResult.Fail("not_found");
        if (ctx.Actor!.IsLeader) return GroupResult.Fail("leader_cannot_leave");
        db.GroupMembers.Remove(ctx.Member); await DropEventPlacesAsync(db, groupId, me, ct); Log(db, groupId, me, "member_left", me, new { }, now); return await SaveAsync(db, ct);
    }

    /// <summary>Hands the group to another member. Only the leader, only to a member in good standing, and only after writing the name of the group to be sure. The old leader becomes an administrator.</summary>
    public static async Task<GroupResult> TransferLeadershipAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid newLeader, string? confirm, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!ctx.Actor.IsLeader) return GroupResult.Fail("forbidden");
        if (!string.Equals((confirm ?? string.Empty).Trim(), ctx.Group.Name, StringComparison.Ordinal)) return GroupResult.Fail("confirmation_needed");
        if (newLeader == me) return GroupResult.Fail("bad_target");
        var target = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == newLeader, ct); if (target is null) return GroupResult.Fail("not_found");
        var owner = ctx.Roles.Values.First(r => r.SystemKey == "owner"); var admin = ctx.Roles.Values.First(r => r.SystemKey == "admin");
        target.RoleId = owner.Id; target.Role = "owner"; ctx.Member!.RoleId = admin.Id; ctx.Member.Role = "admin";
        ctx.Group.OwnerUserId = newLeader; ctx.Group.UpdatedAt = now;
        Log(db, groupId, me, "leadership_transferred", newLeader, new { }, now);
        var saved = await SaveAsync(db, ct); if (!saved.Ok) return saved;
        await TellAsync(notify, newLeader, "group_leader_changed", new { groupId, groupName = ctx.Group.Name, leader = true });
        return saved;
    }

    /// <summary>What stops a group from being dissolved: things the group owns that would be left without an owner. Later parts add their own.</summary>
    public static async Task<List<string>> DissolutionBlockersAsync(LifeVerseDbContext db, Guid groupId, CancellationToken ct)
    {
        var blockers = new List<string>();
        if (await db.IslandParcels.AnyAsync(p => p.GroupId == groupId && p.DeletedAt == null, ct)) blockers.Add("owns_land");
        return blockers;
    }

    /// <summary>Dissolves a group: only the leader, only after writing its name, and never while the group still owns land. Members are told; the group stops existing (the audit history stays).</summary>
    public static async Task<GroupResult> DissolveAsync(LifeVerseDbContext db, Guid me, Guid groupId, string? confirm, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!ctx.Actor.IsLeader) return GroupResult.Fail("forbidden");
        if (!string.Equals((confirm ?? string.Empty).Trim(), ctx.Group.Name, StringComparison.Ordinal)) return GroupResult.Fail("confirmation_needed");
        if (await DissolutionBlockersAsync(db, groupId, ct) is { Count: > 0 } blockers) return GroupResult.Fail("blocked", blockers);
        var members = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
        db.GroupMembers.RemoveRange(members);
        foreach (var i in await db.GroupInvites.Where(i => i.GroupId == groupId && i.Status == "pending").ToListAsync(ct)) i.Status = "declined";
        foreach (var r in await db.GroupJoinRequests.Where(r => r.GroupId == groupId && r.Status == "pending").ToListAsync(ct)) { r.Status = "cancelled"; r.DecidedAt = now; }
        foreach (var p in await db.IslandPermissions.Where(p => p.GroupId == groupId && p.RevokedAt == null).ToListAsync(ct)) p.RevokedAt = now; // what the group was allowed on islands ends with it
        await EndGroupEventsAsync(db, groupId, ct);
        foreach (var a in await db.GroupAnnouncements.Where(a => a.GroupId == groupId && a.DeletedAt == null).ToListAsync(ct)) a.DeletedAt = now;
        ctx.Group.DeletedAt = now; ctx.Group.UpdatedAt = now;
        Log(db, groupId, me, "group_dissolved", null, new { members = members.Count }, now);
        var saved = await SaveAsync(db, ct); if (!saved.Ok) return saved;
        foreach (var m in members.Where(m => m.UserId != me)) await TellAsync(notify, m.UserId, "group_dissolved", new { groupId, groupName = ctx.Group.Name });
        return saved;
    }
}
