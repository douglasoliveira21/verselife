using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Map;

public sealed record LandmarkRequest(string? Name, string? Description, Guid IslandId, float X, float Y, float Z, Guid? FolderId, bool IsPublic);
public sealed record LandmarkChange(string? Name, string? Description, Guid? FolderId, bool? ClearFolder, bool? IsPublic);
public sealed record FolderRequest(string? Name);
public sealed record MapOutcome(bool Ok, string? Error = null, Guid? Id = null);

/// <summary>
/// The places a person keeps and the places the world suggests, for the left column of the world map: their own landmarks (loose or in folders), the islands they favorited, the ones they visited lately, the islands of
/// their friends and the islands of their groups. A landmark is a spot inside an island with a name; it can only be made where the person may enter, and it can be shared by link only when its owner made it public.
/// </summary>
public static class MapPlaces
{
    public const int MaxLandmarks = 200, MaxFolders = 20, MaxName = 60, MaxFolderName = 40, MaxDescription = 300, MaxRecent = 15;

    private static object Item(Landmark l, string islandName) => new { id = l.Id, name = l.Name, description = l.Description, islandId = l.IslandId, islandName, x = l.PositionX, y = l.PositionY, z = l.PositionZ, isPublic = l.IsPublic, folderId = l.FolderId, createdAt = l.CreatedAt };

    /// <summary>Everything the column shows, for this viewer. Islands that are gone or that the viewer may not know exist are left out.</summary>
    public static async Task<object> PanelsAsync(LifeVerseDbContext db, Guid viewer, DateTimeOffset now, CancellationToken ct)
    {
        var visible = MapService.Visible(db, viewer, now); var visibleIds = await visible.Select(i => i.Id).ToListAsync(ct); var names = await visible.ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var folders = await db.LandmarkFolders.AsNoTracking().Where(f => f.OwnerUserId == viewer).OrderBy(f => f.Name).ToListAsync(ct);
        var marks = await db.Landmarks.AsNoTracking().Where(l => l.OwnerUserId == viewer && l.DeletedAt == null).OrderBy(l => l.Name).ToListAsync(ct);
        var mine = marks.Where(l => names.ContainsKey(l.IslandId)).Select(l => Item(l, names[l.IslandId])).ToList();

        var favorites = await db.IslandFavorites.AsNoTracking().Where(f => f.UserId == viewer && visibleIds.Contains(f.IslandId)).OrderByDescending(f => f.CreatedAt).Take(100).Select(f => f.IslandId).ToListAsync(ct);
        var recent = (await db.PlayerLocations.AsNoTracking().Where(p => p.UserId == viewer && visibleIds.Contains(p.IslandId)).OrderByDescending(p => p.UpdatedAt).Take(MaxRecent).ToListAsync(ct)).Select(p => new { islandId = p.IslandId, name = names[p.IslandId], x = p.X, z = p.Z, at = p.UpdatedAt });

        var friendIds = await FriendsOfAsync(db, viewer, ct);
        var friendsIslands = await visible.Where(i => friendIds.Contains(i.OwnerUserId) && i.IsPublic).OrderBy(i => i.Name).Take(100).Select(i => new { i.Id, i.Name, i.OwnerUserId }).ToListAsync(ct);
        var owners = await Worlds_NamesAsync(db, friendsIslands.Select(i => i.OwnerUserId), ct);

        return new
        {
            folders = folders.Select(f => new { id = f.Id, name = f.Name }), landmarks = mine,
            favorites = favorites.Select(id => new { islandId = id, name = names[id] }), recent,
            friends = friendsIslands.Select(i => new { islandId = i.Id, name = i.Name, ownerName = owners.GetValueOrDefault(i.OwnerUserId, string.Empty) }),
            groups = await GroupIslandsAsync(db, viewer, visibleIds, names, ct), limits = new { landmarks = MaxLandmarks, folders = MaxFolders }
        };
    }

    private static async Task<Dictionary<Guid, string>> Worlds_NamesAsync(LifeVerseDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Avatar != null ? u.Avatar.DisplayName : u.Username, ct);
    }

    public static async Task<HashSet<Guid>> FriendsOfAsync(LifeVerseDbContext db, Guid me, CancellationToken ct)
    {
        var links = await db.Friendships.AsNoTracking().Where(f => f.Status == "accepted" && (f.UserId == me || f.FriendUserId == me)).Select(f => new { f.UserId, f.FriendUserId }).ToListAsync(ct);
        return [.. links.Select(l => l.UserId == me ? l.FriendUserId : l.UserId)];
    }

    /// <summary>The islands of the groups the viewer belongs to: where the group has its headquarters and where it owns land.</summary>
    public static async Task<List<object>> GroupIslandsAsync(LifeVerseDbContext db, Guid viewer, IReadOnlyCollection<Guid> visibleIds, IReadOnlyDictionary<Guid, string> names, CancellationToken ct)
    {
        var groups = await db.GroupMembers.AsNoTracking().Where(m => m.UserId == viewer).Join(db.SocialGroups.AsNoTracking().Where(g => g.DeletedAt == null), m => m.GroupId, g => g.Id, (m, g) => new { g.Id, g.Name, g.Tag, g.AccentColor, g.HeadquartersIslandId }).OrderBy(g => g.Name).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var lands = (await db.IslandParcels.AsNoTracking().Where(p => p.GroupId != null && ids.Contains(p.GroupId!.Value) && p.DeletedAt == null).GroupBy(p => new { Group = p.GroupId!.Value, p.IslandId }).Select(g => new { g.Key.Group, g.Key.IslandId, N = g.Count() }).ToListAsync(ct)).Where(l => visibleIds.Contains(l.IslandId)).ToList();
        var result = new List<object>();
        foreach (var g in groups)
        {
            var places = new List<object>();
            if (g.HeadquartersIslandId is { } hq && names.TryGetValue(hq, out var hqName)) places.Add(new { islandId = hq, name = hqName, kind = "headquarters", parcels = 0 });
            foreach (var l in lands.Where(l => l.Group == g.Id && l.IslandId != g.HeadquartersIslandId)) places.Add(new { islandId = l.IslandId, name = names[l.IslandId], kind = "land", parcels = l.N });
            if (places.Count > 0) result.Add(new { groupId = g.Id, name = g.Name, tag = g.Tag, color = g.AccentColor, places });
        }
        return result;
    }

    // ---- landmarks --------------------------------------------------------------------------------------------------------------------------

    public static async Task<MapOutcome> CreateAsync(LifeVerseDbContext db, Guid me, LandmarkRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var name = (r.Name ?? string.Empty).Trim(); if (name.Length < 1 || name.Length > MaxName) return new(false, "bad_name");
        var description = (r.Description ?? string.Empty).Trim(); if (description.Length > MaxDescription) return new(false, "bad_description");
        var island = await MapService.Visible(db, me, now).SingleOrDefaultAsync(i => i.Id == r.IslandId, ct); if (island is null) return new(false, "island_not_found");
        if (!await IslandAccess.AllowsAsync(db, island, me, now, ct)) return new(false, "no_access");
        var half = island.Size / 2f; if (!float.IsFinite(r.X) || !float.IsFinite(r.Y) || !float.IsFinite(r.Z) || Math.Abs(r.X) > half || Math.Abs(r.Z) > half || r.Y < -50 || r.Y > 2000) return new(false, "bad_position");
        if (r.FolderId is { } folder && !await db.LandmarkFolders.AnyAsync(f => f.Id == folder && f.OwnerUserId == me, ct)) return new(false, "folder_not_found");
        if (await db.Landmarks.CountAsync(l => l.OwnerUserId == me && l.DeletedAt == null, ct) >= MaxLandmarks) return new(false, "too_many_landmarks");
        var landmark = new Landmark { Id = Guid.NewGuid(), OwnerUserId = me, IslandId = island.Id, Name = name, Description = description, PositionX = r.X, PositionY = r.Y, PositionZ = r.Z, IsPublic = r.IsPublic, FolderId = r.FolderId, CreatedAt = now, UpdatedAt = now };
        db.Landmarks.Add(landmark); await db.SaveChangesAsync(ct); return new(true, null, landmark.Id);
    }

    public static async Task<MapOutcome> ChangeAsync(LifeVerseDbContext db, Guid me, Guid id, LandmarkChange c, DateTimeOffset now, CancellationToken ct)
    {
        var l = await db.Landmarks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == me && x.DeletedAt == null, ct); if (l is null) return new(false, "not_found");
        if (c.Name is not null) { var name = c.Name.Trim(); if (name.Length < 1 || name.Length > MaxName) return new(false, "bad_name"); l.Name = name; }
        if (c.Description is not null) { var d = c.Description.Trim(); if (d.Length > MaxDescription) return new(false, "bad_description"); l.Description = d; }
        if (c.ClearFolder == true) l.FolderId = null;
        else if (c.FolderId is { } folder) { if (!await db.LandmarkFolders.AnyAsync(f => f.Id == folder && f.OwnerUserId == me, ct)) return new(false, "folder_not_found"); l.FolderId = folder; }
        if (c.IsPublic is { } open) l.IsPublic = open;
        l.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, id);
    }

    public static async Task<MapOutcome> DeleteAsync(LifeVerseDbContext db, Guid me, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var l = await db.Landmarks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == me && x.DeletedAt == null, ct); if (l is null) return new(false, "not_found");
        l.DeletedAt = now; l.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, id);
    }

    /// <summary>One landmark for a link: the owner always, anybody else only when it is public, on an island they may know, and not of somebody they are blocked with.</summary>
    public static async Task<object?> ViewAsync(LifeVerseDbContext db, Guid viewer, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var l = await db.Landmarks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, ct); if (l is null) return null;
        if (l.OwnerUserId != viewer && (!l.IsPublic || await db.UserBlocks.AsNoTracking().AnyAsync(b => b.UserId == viewer && b.BlockedUserId == l.OwnerUserId || b.UserId == l.OwnerUserId && b.BlockedUserId == viewer, ct))) return null;
        var island = await MapService.Visible(db, viewer, now).SingleOrDefaultAsync(i => i.Id == l.IslandId, ct); if (island is null) return null;
        var owner = (await Worlds_NamesAsync(db, [l.OwnerUserId], ct)).GetValueOrDefault(l.OwnerUserId, string.Empty);
        return new { id = l.Id, name = l.Name, description = l.Description, islandId = island.Id, islandName = island.Name, x = l.PositionX, y = l.PositionY, z = l.PositionZ, isPublic = l.IsPublic, isMine = l.OwnerUserId == viewer, ownerName = l.IsPublic || l.OwnerUserId == viewer ? owner : string.Empty };
    }

    // ---- folders ---------------------------------------------------------------------------------------------------------------------------

    public static async Task<MapOutcome> CreateFolderAsync(LifeVerseDbContext db, Guid me, string? name, DateTimeOffset now, CancellationToken ct)
    {
        var clean = (name ?? string.Empty).Trim(); if (clean.Length < 1 || clean.Length > MaxFolderName) return new(false, "bad_name");
        if (await db.LandmarkFolders.CountAsync(f => f.OwnerUserId == me, ct) >= MaxFolders) return new(false, "too_many_folders");
        if (await db.LandmarkFolders.AnyAsync(f => f.OwnerUserId == me && f.Name.ToLower() == clean.ToLower(), ct)) return new(false, "name_taken");
        var folder = new LandmarkFolder { Id = Guid.NewGuid(), OwnerUserId = me, Name = clean, CreatedAt = now }; db.LandmarkFolders.Add(folder); await db.SaveChangesAsync(ct); return new(true, null, folder.Id);
    }

    public static async Task<MapOutcome> RenameFolderAsync(LifeVerseDbContext db, Guid me, Guid id, string? name, CancellationToken ct)
    {
        var folder = await db.LandmarkFolders.SingleOrDefaultAsync(f => f.Id == id && f.OwnerUserId == me, ct); if (folder is null) return new(false, "not_found");
        var clean = (name ?? string.Empty).Trim(); if (clean.Length < 1 || clean.Length > MaxFolderName) return new(false, "bad_name");
        if (await db.LandmarkFolders.AnyAsync(f => f.Id != id && f.OwnerUserId == me && f.Name.ToLower() == clean.ToLower(), ct)) return new(false, "name_taken");
        folder.Name = clean; await db.SaveChangesAsync(ct); return new(true, null, id);
    }

    /// <summary>Takes the folder away; what was inside stays, loose.</summary>
    public static async Task<MapOutcome> DeleteFolderAsync(LifeVerseDbContext db, Guid me, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var folder = await db.LandmarkFolders.SingleOrDefaultAsync(f => f.Id == id && f.OwnerUserId == me, ct); if (folder is null) return new(false, "not_found");
        foreach (var l in await db.Landmarks.Where(l => l.FolderId == id && l.OwnerUserId == me).ToListAsync(ct)) { l.FolderId = null; l.UpdatedAt = now; }
        db.LandmarkFolders.Remove(folder); await db.SaveChangesAsync(ct); return new(true, null, id);
    }
}
