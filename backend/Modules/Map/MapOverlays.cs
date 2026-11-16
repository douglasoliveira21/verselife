using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Map;

/// <summary>
/// What is drawn over the islands of the world map besides the islands themselves: the parcels (their limits, with a state that means something to this viewer: yours, a group's, for sale, for rent, public, restricted),
/// and where the friends are (only those who are online, share their presence, and are on an island this viewer may know exists). Restricted land does not tell its name or its owner.
/// </summary>
public static class MapOverlays
{
    public const int MaxParcels = 400;

    private sealed record Bounds(double MinX, double MinZ, double MaxX, double MaxZ);

    private static Bounds? ReadBounds(string json)
    {
        try { using var d = JsonDocument.Parse(json); var r = d.RootElement; return new Bounds(r.GetProperty("minX").GetDouble(), r.GetProperty("minZ").GetDouble(), r.GetProperty("maxX").GetDouble(), r.GetProperty("maxZ").GetDouble()); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    /// <summary>The parcels of an island for this viewer (null: the island is not on their map).</summary>
    public static async Task<object?> ParcelsAsync(LifeVerseDbContext db, Guid viewer, Guid islandId, DateTimeOffset now, CancellationToken ct)
    {
        var island = await MapService.Visible(db, viewer, now).SingleOrDefaultAsync(i => i.Id == islandId, ct); if (island is null) return null;
        var parcels = await db.IslandParcels.AsNoTracking().Where(p => p.IslandId == islandId && p.DeletedAt == null).OrderBy(p => p.Code).ThenBy(p => p.Name).Take(MaxParcels).ToListAsync(ct); var ids = parcels.Select(p => p.Id).ToList();
        var tenants = (await db.ParcelRentals.AsNoTracking().Where(r => ids.Contains(r.ParcelId) && (r.Status == "active" || r.Status == "grace")).Select(r => new { r.ParcelId, r.TenantUserId }).ToListAsync(ct)).GroupBy(r => r.ParcelId).ToDictionary(g => g.Key, g => g.First().TenantUserId);
        var myGroups = (await db.GroupMembers.AsNoTracking().Where(m => m.UserId == viewer).Select(m => m.GroupId).ToListAsync(ct)).ToHashSet();
        var allowed = (await db.ParcelAccess.AsNoTracking().Where(a => ids.Contains(a.ParcelId) && a.UserId == viewer && a.Kind == "allow" && a.RevokedAt == null && (a.ExpiresAt == null || a.ExpiresAt > now)).Select(a => a.ParcelId).ToListAsync(ct)).ToHashSet();
        var owners = parcels.Select(p => p.OwnerUserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Avatar != null ? u.Avatar.DisplayName : u.Username, ct);
        var groupNames = await db.SocialGroups.AsNoTracking().Where(g => parcels.Select(p => p.GroupId).Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        var items = new List<object>();
        foreach (var p in parcels)
        {
            if (ReadBounds(p.BoundsJson) is not { } b) continue;
            var mine = p.OwnerUserId == viewer || tenants.GetValueOrDefault(p.Id) == viewer; var group = p.GroupId is { } g && myGroups.Contains(g); var rented = tenants.ContainsKey(p.Id);
            var restricted = p.AccessMode != "public" && !mine && !group && !allowed.Contains(p.Id) && island.OwnerUserId != viewer;
            var state = p.Category == "comum" ? "common" : mine ? "mine" : group ? "group" : restricted ? "restricted" : p.SalePrice is not null ? "sale" : p.RentPrice is not null && !rented ? "rent" : "public";
            var showOwner = mine || group || state is "sale" or "rent";
            items.Add(new
            {
                id = p.Id, code = p.Code, name = restricted ? "Terreno restrito" : p.Name, description = restricted ? string.Empty : p.Description, category = p.Category, area = p.Area, state,
                bounds = new { minX = b.MinX, minZ = b.MinZ, maxX = b.MaxX, maxZ = b.MaxZ }, spawnX = p.SpawnX, spawnZ = p.SpawnZ, teleport = p.TeleportMode != "disabled" && !restricted,
                salePrice = state == "sale" ? p.SalePrice : null, rentPrice = state == "rent" ? p.RentPrice : null, rentPeriodDays = state == "rent" ? p.RentPeriodDays : (int?)null,
                owner = showOwner ? (p.GroupId is { } gid && groupNames.TryGetValue(gid, out var gn) ? gn : names.GetValueOrDefault(p.OwnerUserId, string.Empty)) : null, isGroup = p.GroupId != null
            });
        }
        return new { islandId, size = island.Size, truncated = parcels.Count >= MaxParcels, items };
    }

    /// <summary>Where this viewer's friends are right now. Only accepted friends, online, who did not make their presence private, on an island that is on the viewer's map. Positions are as the island server reported them.</summary>
    public sealed record FriendPlace(Guid UserId, string Name, string Username, Guid IslandId, string IslandName, double X, double Z);

    public static async Task<List<FriendPlace>> FriendsAsync(LifeVerseDbContext db, Guid viewer, DateTimeOffset now, CancellationToken ct)
    {
        var friends = await MapPlaces.FriendsOfAsync(db, viewer, ct); if (friends.Count == 0) return [];
        var visible = await MapService.Visible(db, viewer, now).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var servers = await db.ServerInstances.AsNoTracking().Where(s => s.StoppedAt == null && s.Status == "healthy" && s.LastHeartbeatAt > now.AddSeconds(-45) && visible.Keys.Contains(s.IslandId)).ToListAsync(ct);
        var found = new List<(Guid Island, LifeVerse.Shared.Platform.PlayerReport Player)>();
        foreach (var server in servers) if (ServerChannel.ReadReport(server) is { } report) foreach (var p in report.Players) if (friends.Contains(p.UserId)) found.Add((server.IslandId, p));
        if (found.Count == 0) return [];
        var ids = found.Select(f => f.Player.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => ids.Contains(u.Id) && u.DeletedAt == null).ToListAsync(ct);
        var blocked = (await db.UserBlocks.AsNoTracking().Where(b => b.UserId == viewer || b.BlockedUserId == viewer).Select(b => b.UserId == viewer ? b.BlockedUserId : b.UserId).ToListAsync(ct)).ToHashSet();
        var list = new List<FriendPlace>();
        foreach (var (island, player) in found.DistinctBy(f => f.Player.UserId))
        {
            var user = users.FirstOrDefault(u => u.Id == player.UserId); if (user is null || blocked.Contains(user.Id) || ProfileService.PrivacyOf(user.PrivacyJson).PresencePrivate || user.PresenceStatus == "offline") continue;
            list.Add(new FriendPlace(user.Id, user.Avatar?.DisplayName ?? user.Username, user.Username, island, visible[island], player.X, player.Z));
        }
        return [.. list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
