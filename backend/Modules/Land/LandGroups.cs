using LifeVerse.Api.Data;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// The people a parcel has because of groups. A parcel owned by a group is run by the members whose cargo allows it (managers), built on by the
/// members who may build, and open to all of its members even when it is private. A parcel of anybody can also be opened or lent to a whole
/// group (guests). This is worked out on the spot from the current members and cargos, so nobody keeps a right they no longer hold.
/// </summary>
public sealed record ParcelPeople(IReadOnlySet<Guid> Managers, IReadOnlySet<Guid> Builders, IReadOnlySet<Guid> Members, IReadOnlySet<Guid> Guests);

public static class LandAuthority
{
    private static readonly IReadOnlySet<Guid> None = new HashSet<Guid>();
    public static readonly ParcelPeople NoOne = new(None, None, None, None);

    /// <summary>The permissions a member has in a group: the leader has all of them; legacy members without a cargo count as the cargo their old role names.</summary>
    private static HashSet<string> PermissionsOf(string roleKey, GroupRole? role)
    {
        if (role is { SystemKey: "owner" }) return [.. GroupPermissions.Known];
        if (role is not null) return [.. GroupRules.SplitPermissions(role.Permissions)];
        var key = roleKey is "owner" or "admin" or "moderator" or "member" ? roleKey : "member";
        var starting = GroupRules.StartingRoles.First(r => r.Key == key);
        return key == "owner" ? [.. GroupPermissions.Known] : [.. starting.Permissions];
    }

    /// <summary>Whether the person is a member of the group and their cargo holds the permission (the leader holds all).</summary>
    public static async Task<bool> HasGroupPermissionAsync(LifeVerseDbContext db, Guid groupId, Guid user, string permission, CancellationToken ct)
    {
        var member = await db.GroupMembers.AsNoTracking().SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == user, ct); if (member is null) return false;
        if (!await db.SocialGroups.AsNoTracking().AnyAsync(g => g.Id == groupId && g.DeletedAt == null, ct)) return false;
        var role = member.RoleId is { } id ? await db.GroupRoles.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) : await db.GroupRoles.AsNoTracking().FirstOrDefaultAsync(r => r.GroupId == groupId && r.SystemKey == member.Role, ct);
        return PermissionsOf(member.Role, role).Contains(permission);
    }

    /// <summary>May this person administer the parcel: its owner, or for a parcel of a group, the members whose cargo allows managing land.</summary>
    public static async Task<bool> CanManageAsync(LifeVerseDbContext db, IslandParcel parcel, Guid actor, CancellationToken ct) =>
        parcel.GroupId is not { } group ? parcel.OwnerUserId == actor : await HasGroupPermissionAsync(db, group, actor, GroupPermissions.ManageLand, ct);

    /// <summary>The group-made people of each parcel (parcels without any are left out).</summary>
    public static async Task<Dictionary<Guid, ParcelPeople>> PeopleAsync(LifeVerseDbContext db, IReadOnlyCollection<IslandParcel> parcels, CancellationToken ct)
    {
        var result = new Dictionary<Guid, ParcelPeople>();
        var ids = parcels.Select(p => p.Id).ToList();
        var grants = ids.Count == 0 ? [] : await db.ParcelGroupAccess.AsNoTracking().Where(a => ids.Contains(a.ParcelId) && a.RevokedAt == null).ToListAsync(ct);
        var groupIds = parcels.Where(p => p.GroupId is not null).Select(p => p.GroupId!.Value).Concat(grants.Select(g => g.GroupId)).Distinct().ToList();
        if (groupIds.Count == 0) return result;
        var live = (await db.SocialGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id) && g.DeletedAt == null).Select(g => g.Id).ToListAsync(ct)).ToHashSet();
        var members = await db.GroupMembers.AsNoTracking().Where(m => groupIds.Contains(m.GroupId)).Select(m => new { m.GroupId, m.UserId, m.RoleId, m.Role }).ToListAsync(ct);
        var roles = (await db.GroupRoles.AsNoTracking().Where(r => groupIds.Contains(r.GroupId)).ToListAsync(ct)).ToDictionary(r => r.Id);
        var byKey = roles.Values.GroupBy(r => (r.GroupId, r.SystemKey)).ToDictionary(g => g.Key, g => g.First());
        var permissions = members.Where(m => live.Contains(m.GroupId)).ToDictionary(m => (m.GroupId, m.UserId), m => PermissionsOf(m.Role, m.RoleId is { } id && roles.TryGetValue(id, out var r) ? r : byKey.GetValueOrDefault((m.GroupId, m.Role))));
        foreach (var parcel in parcels)
        {
            var managers = new HashSet<Guid>(); var builders = new HashSet<Guid>(); var inside = new HashSet<Guid>(); var guests = new HashSet<Guid>();
            if (parcel.GroupId is { } own && live.Contains(own))
                foreach (var m in members.Where(m => m.GroupId == own))
                {
                    var held = permissions[(own, m.UserId)]; inside.Add(m.UserId);
                    if (held.Contains(GroupPermissions.ManageLand)) { managers.Add(m.UserId); builders.Add(m.UserId); }
                    else if (held.Contains(GroupPermissions.BuildOnLand)) builders.Add(m.UserId);
                }
            foreach (var grant in grants.Where(g => g.ParcelId == parcel.Id && live.Contains(g.GroupId)))
                foreach (var m in members.Where(m => m.GroupId == grant.GroupId)) { guests.Add(m.UserId); if (grant.Kind == "build") builders.Add(m.UserId); }
            if (managers.Count + builders.Count + inside.Count + guests.Count > 0) result[parcel.Id] = new ParcelPeople(managers, builders, inside, guests);
        }
        return result;
    }
}
