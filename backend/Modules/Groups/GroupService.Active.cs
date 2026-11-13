using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

/// <summary>What shows beside a person's name: the group they wear, its tag and the title of their cargo.</summary>
public sealed record ActiveGroupView(Guid GroupId, string GroupName, string? Tag, string Title, string Color);

/// <summary>
/// The active group of a person: the one whose tag and cargo title show above their avatar. Only a member can wear a group, only one at a time, and it stops the moment they leave, are removed or the
/// group is dissolved (the membership is the source of truth, nothing is copied).
/// </summary>
public static partial class GroupService
{
    /// <summary>What the people near an avatar should read beside it, or null when no group is worn.</summary>
    public static async Task<ActiveGroupView?> ActiveOfAsync(LifeVerseDbContext db, Guid user, CancellationToken ct)
    {
        var member = await db.GroupMembers.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == user && m.IsActive, ct); if (member is null) return null;
        var group = await db.SocialGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == member.GroupId && g.DeletedAt == null, ct); if (group is null) return null;
        var role = member.RoleId is { } id ? await db.GroupRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) : null;
        var title = role is null ? member.Role : TitleOf(role);
        return new ActiveGroupView(group.Id, group.Name, group.Tag, title, role?.Color ?? "#A9B3BD");
    }

    /// <summary>Keeps a notification for the person (so it shows in the notification center even if they were offline) and pushes it live.</summary>
    private static async Task TellKeepAsync(LifeVerseDbContext db, GroupNotifier? notify, Guid user, Guid groupId, string type, string title, string body, object data, DateTimeOffset now, CancellationToken ct)
    {
        db.GroupNotifications.Add(new GroupNotification { Id = Guid.NewGuid(), UserId = user, GroupId = groupId, Type = type, Title = title.Length > 120 ? title[..120] : title, Body = body.Length > 300 ? body[..300] : body, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; }
        await TellAsync(notify, user, type, data);
    }

    /// <summary>The line shown over the avatar: the tag (or the name) of the group worn and the title of the cargo, or empty when no group is worn.</summary>
    public static string LabelOf(ActiveGroupView? active) => active is null ? string.Empty : (string.IsNullOrEmpty(active.Tag) ? active.GroupName : $"[{active.Tag}]") + (active.Title.Length > 0 ? " " + active.Title : "");

    /// <summary>The title shown for a cargo: the one the group wrote, or the name of the cargo.</summary>
    public static string TitleOf(GroupRole role) => role.Title.Length > 0 ? role.Title : role.Name;

    /// <summary>Wears this group. The previous one stops being worn.</summary>
    public static async Task<GroupResult> ActivateAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        foreach (var other in await db.GroupMembers.Where(m => m.UserId == me && m.IsActive && m.GroupId != groupId).ToListAsync(ct)) other.IsActive = false;
        ctx!.Member!.IsActive = true;
        var role = ctx.Member.RoleId is { } id && ctx.Roles.TryGetValue(id, out var r) ? r : null;
        return await SaveAsync(db, ct, new { groupId, groupName = ctx.Group.Name, tag = ctx.Group.Tag, title = role is null ? ctx.Member.Role : TitleOf(role) });
    }

    /// <summary>Stops wearing any group.</summary>
    public static async Task<GroupResult> DeactivateAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        foreach (var member in await db.GroupMembers.Where(m => m.UserId == me && m.IsActive).ToListAsync(ct)) member.IsActive = false;
        return await SaveAsync(db, ct);
    }

    /// <summary>Gives the cargo a title (up to 24 characters) shown above the name of the members who wear the group. Same rights as editing the cargo.</summary>
    public static async Task<GroupResult> SetRoleTitleAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid roleId, string? title, DateTimeOffset now, CancellationToken ct)
    {
        var ctx = await LoadAsync(db, groupId, me, now, ct); if (ctx?.Actor is null) return GroupResult.Fail(ctx is null ? "not_found" : "forbidden");
        if (!GroupRules.Has(ctx.Actor, GroupPermissions.ManageRoles)) return GroupResult.Fail("forbidden");
        if (!ctx.Roles.TryGetValue(roleId, out var role)) return GroupResult.Fail("not_found");
        if (role.SystemKey != "owner" && role.SystemKey != "member" && !GroupRules.CanManageRank(ctx.Actor, role.Rank)) return GroupResult.Fail("outranked");
        if (role.SystemKey == "owner" && ctx.Actor.Rank < GroupRules.LeaderRank) return GroupResult.Fail("outranked"); // only the leader names the title of the leader
        var clean = CleanText(title, newLines: false); if (clean.Length > GroupRules.MaxRoleTitle) return GroupResult.Fail("bad_title");
        if (clean == role.Title) return GroupResult.Done(new { roleId, title = TitleOf(role) });
        role.Title = clean; Log(db, groupId, me, "role_title_changed", null, new { roleId, role = role.Name, title = clean }, now);
        return await SaveAsync(db, ct, new { roleId, title = TitleOf(role) });
    }
}
