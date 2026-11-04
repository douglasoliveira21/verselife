using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Shared.Groups;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Groups;

public sealed record LandTargetRequest(Guid ParcelId);
public sealed record ReleaseLandRequest(Guid? UserId);
public sealed record GroupAccessRequest(Guid GroupId, string Kind);

/// <summary>
/// Groups 2.0, properties: land that belongs to a group, built on and run together by its members, and land opened to a whole group. The land
/// stays in the island's land system; here is only who may put land in a group, take it out, and which groups a parcel is open to. A parcel of a
/// group is recorded under the leader (so every rule that looks at an owner keeps working) but whoever holds the leader's cargo is the one who
/// counts: the rights of everybody else come from their cargo at the moment they act.
/// </summary>
public static partial class GroupService
{
    public sealed record GroupLandItem(Guid ParcelId, Guid IslandId, string IslandName, string Name, double Area, int MaxObjects, string AccessMode, string BuildMode, bool ForSale);
    public sealed record GroupAccessItem(Guid Id, Guid GroupId, string GroupName, string Kind, DateTimeOffset At);

    private static async Task<List<GroupLandItem>> LandItemsAsync(LifeVerseDbContext db, IReadOnlyList<IslandParcel> parcels, CancellationToken ct)
    {
        var islandIds = parcels.Select(p => p.IslandId).Distinct().ToList();
        var islands = await db.Islands.AsNoTracking().Where(i => islandIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        return [.. parcels.Select(p => new GroupLandItem(p.Id, p.IslandId, islands.GetValueOrDefault(p.IslandId, "?"), p.Name, p.Area, p.MaxObjects, p.AccessMode, p.BuildMode, p.SalePrice is not null || p.RentPrice is not null))];
    }

    /// <summary>The land of a group, for its members, with what each of them may do about it.</summary>
    public static async Task<GroupResult> LandsAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (ctx, error) = await MemberAsync(db, me, groupId, now, ct); if (error is not null) return error;
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.GroupId == groupId && p.DeletedAt == null).OrderBy(p => p.Name).ToListAsync(ct);
        var actor = ctx!.Actor!;
        return GroupResult.Done(new { lands = await LandItemsAsync(db, parcels, ct), canManage = GroupRules.Has(actor, GroupPermissions.ManageLand), canBuild = GroupRules.Has(actor, GroupPermissions.BuildOnLand) || GroupRules.Has(actor, GroupPermissions.ManageLand), canRelease = GroupRules.Has(actor, GroupPermissions.ReleaseLand), limit = GroupRules.MaxGroupParcels });
    }

    /// <summary>The parcels the person owns alone and could put in the group.</summary>
    public static async Task<GroupResult> OfferableLandAsync(LifeVerseDbContext db, Guid me, Guid groupId, DateTimeOffset now, CancellationToken ct)
    {
        var (_, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageLand, now, ct); if (error is not null) return error;
        var rented = db.ParcelRentals.Where(r => r.Status == "active" || r.Status == "grace").Select(r => r.ParcelId);
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.OwnerUserId == me && p.GroupId == null && p.DeletedAt == null && !rented.Contains(p.Id)).OrderBy(p => p.Name).Take(200).ToListAsync(ct);
        return GroupResult.Done(await LandItemsAsync(db, parcels, ct));
    }

    private static void ParcelLog(LifeVerseDbContext db, IslandParcel parcel, Guid actor, string operation, object data, DateTimeOffset now)
    {
        parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize(data), Revision = parcel.Revision, CreatedAt = now });
    }

    /// <summary>Puts a parcel of the person in the group. They must own it alone and hold the cargo that manages land; offers for sale or rent end.</summary>
    public static async Task<GroupResult> DonateLandAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid parcelId, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ManageLand, now, ct); if (error is not null) return error;
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct); if (parcel is null) return GroupResult.Fail("not_found");
        if (parcel.OwnerUserId != me || parcel.GroupId is not null) return GroupResult.Fail("forbidden");
        if (await db.ParcelRentals.AnyAsync(r => r.ParcelId == parcelId && (r.Status == "active" || r.Status == "grace"), ct)) return GroupResult.Fail("rented");
        if (await db.IslandParcels.CountAsync(p => p.GroupId == groupId && p.DeletedAt == null, ct) >= GroupRules.MaxGroupParcels) return GroupResult.Fail("too_many_parcels");
        parcel.GroupId = groupId; parcel.OwnerUserId = ctx!.Group.OwnerUserId;
        parcel.SalePrice = null; parcel.SaleBuyerUserId = null; parcel.SaleDescription = string.Empty; parcel.RentPrice = null;
        ParcelLog(db, parcel, me, "group_donate", new { groupId }, now);
        Log(db, groupId, me, "land_donated", null, new { parcelId, name = parcel.Name }, now);
        var users = await AddNotificationsAsync(db, groupId, null, me, "group_land", parcel.Name, "Um terreno entrou no grupo.", now, ct);
        var saved = await SaveAsync(db, ct, new { parcelId }); if (!saved.Ok) return saved;
        foreach (var user in users) await TellAsync(notify, user, "group_land", new { groupId, groupName = ctx.Group.Name, parcelId, name = parcel.Name });
        return saved;
    }

    /// <summary>Takes a parcel out of the group and hands it to one of its members. Only a cargo that releases land, a few times a day, and never while the parcel is rented.</summary>
    public static async Task<GroupResult> ReleaseLandAsync(LifeVerseDbContext db, Guid me, Guid groupId, Guid parcelId, Guid? target, DateTimeOffset now, GroupNotifier? notify, CancellationToken ct)
    {
        var (ctx, error) = await WithPermissionAsync(db, me, groupId, GroupPermissions.ReleaseLand, now, ct); if (error is not null) return error;
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.GroupId == groupId && p.DeletedAt == null, ct); if (parcel is null) return GroupResult.Fail("not_found");
        var heir = target ?? me;
        if (!await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == heir, ct)) return GroupResult.Fail("bad_target");
        if (await db.ParcelRentals.AnyAsync(r => r.ParcelId == parcelId && (r.Status == "active" || r.Status == "grace"), ct)) return GroupResult.Fail("rented");
        if (!ctx!.Actor!.IsLeader)
        {
            var since = now.AddDays(-1);
            if (await db.GroupAuditEntries.CountAsync(e => e.GroupId == groupId && e.ActorUserId == me && e.Action == "land_released" && e.CreatedAt > since, ct) >= GroupRules.MaxLandReleasesPerDay) return GroupResult.Fail("rate_limited");
        }
        parcel.GroupId = null; parcel.OwnerUserId = heir;
        foreach (var grant in await db.ParcelGroupAccess.Where(a => a.ParcelId == parcelId && a.RevokedAt == null).ToListAsync(ct)) grant.RevokedAt = now; // what the group opened to others ends with the group's ownership
        ParcelLog(db, parcel, me, "group_release", new { groupId, to = heir }, now);
        Log(db, groupId, me, "land_released", heir, new { parcelId, name = parcel.Name }, now);
        var saved = await SaveAsync(db, ct, new { parcelId }); if (!saved.Ok) return saved;
        await TellAsync(notify, heir, "group_land_released", new { groupId, groupName = ctx.Group.Name, parcelId, name = parcel.Name });
        return saved;
    }

    // ---- opening a parcel to a whole group ---------------------------------------------------------------------------------------------------------

    private static async Task<IslandParcel?> ManagedParcelAsync(LifeVerseDbContext db, Guid actor, Guid parcelId, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct);
        return parcel is not null && await LandAuthority.CanManageAsync(db, parcel, actor, ct) ? parcel : null;
    }

    public static async Task<GroupResult> GroupAccessAsync(LifeVerseDbContext db, Guid actor, Guid parcelId, CancellationToken ct)
    {
        var parcel = await ManagedParcelAsync(db, actor, parcelId, ct); if (parcel is null) return GroupResult.Fail("forbidden");
        var grants = await db.ParcelGroupAccess.AsNoTracking().Where(a => a.ParcelId == parcelId && a.RevokedAt == null).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var ids = grants.Select(g => g.GroupId).ToList(); var names = await db.SocialGroups.AsNoTracking().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        return GroupResult.Done(grants.Select(g => new GroupAccessItem(g.Id, g.GroupId, names.GetValueOrDefault(g.GroupId, "?"), g.Kind, g.CreatedAt)).ToList());
    }

    /// <summary>Opens the parcel to every member of a group: allow (may enter when the parcel lists who enters) or build (may also build). Giving the same group again changes the kind.</summary>
    public static async Task<GroupResult> GrantGroupAccessAsync(LifeVerseDbContext db, Guid actor, Guid parcelId, Guid groupId, string? kind, DateTimeOffset now, CancellationToken ct)
    {
        var parcel = await ManagedParcelAsync(db, actor, parcelId, ct); if (parcel is null) return GroupResult.Fail("forbidden");
        if (kind is not ("allow" or "build")) return GroupResult.Fail("bad_kind");
        if (parcel.GroupId == groupId) return GroupResult.Fail("bad_target"); // the owning group already has all of it
        var group = await db.SocialGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId && g.DeletedAt == null, ct);
        if (group is null || group.Privacy == "private" && !await db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == actor, ct)) return GroupResult.Fail("not_found"); // a hidden group stays hidden
        var current = await db.ParcelGroupAccess.Where(a => a.ParcelId == parcelId && a.RevokedAt == null).ToListAsync(ct);
        var same = current.FirstOrDefault(a => a.GroupId == groupId);
        if (same is not null && same.Kind == kind) return GroupResult.Done();
        if (same is null && current.Count >= GroupRules.MaxGroupAccessPerParcel) return GroupResult.Fail("too_many_grants");
        if (same is not null) same.RevokedAt = now;
        db.ParcelGroupAccess.Add(new ParcelGroupAccess { Id = Guid.NewGuid(), ParcelId = parcelId, GroupId = groupId, Kind = kind, GrantedByUserId = actor, CreatedAt = now });
        ParcelLog(db, parcel, actor, "group_access", new { groupId, kind }, now);
        if (parcel.GroupId is { } owner) Log(db, owner, actor, "land_access_granted", null, new { parcelId, name = parcel.Name, to = group.Name, kind }, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<GroupResult> RevokeGroupAccessAsync(LifeVerseDbContext db, Guid actor, Guid parcelId, Guid entryId, DateTimeOffset now, CancellationToken ct)
    {
        var parcel = await ManagedParcelAsync(db, actor, parcelId, ct); if (parcel is null) return GroupResult.Fail("forbidden");
        var entry = await db.ParcelGroupAccess.SingleOrDefaultAsync(a => a.Id == entryId && a.ParcelId == parcelId && a.RevokedAt == null, ct); if (entry is null) return GroupResult.Fail("not_found");
        entry.RevokedAt = now; ParcelLog(db, parcel, actor, "group_access_revoked", new { entry.GroupId }, now);
        if (parcel.GroupId is { } owner) Log(db, owner, actor, "land_access_revoked", null, new { parcelId, name = parcel.Name }, now);
        return await SaveAsync(db, ct);
    }

    /// <summary>The leader keeps the parcels of the group recorded under their name: when leadership changes, so do they.</summary>
    private static async Task PassStewardshipAsync(LifeVerseDbContext db, Guid groupId, Guid newLeader, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var parcel in await db.IslandParcels.Where(p => p.GroupId == groupId && p.DeletedAt == null && p.OwnerUserId != newLeader).ToListAsync(ct)) { parcel.OwnerUserId = newLeader; ParcelLog(db, parcel, actor, "group_steward", new { groupId, to = newLeader }, now); }
    }
}
