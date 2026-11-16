using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

/// <summary>
/// What the search window shows about a place (an island) beyond the search line: the owner, category, rating, how many are there now, the arrival point, and, for this viewer, whether they can enter and why not
/// (offline, full, closed, private, banned). Only islands the viewer may know exist; anything else is not found.
/// </summary>
public static class PlaceDirectory
{
    public static async Task<object?> InfoAsync(LifeVerseDbContext db, Guid viewer, Guid islandId, DateTimeOffset now, CancellationToken ct)
    {
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return null;
        var mine = island.OwnerUserId == viewer;
        if (!mine && !(island.IsPublic && island.ShowInSearch) && !await IslandAccess.AllowsAsync(db, island, viewer, now, ct)) return null;
        if (!mine && await db.UserBlocks.AsNoTracking().AnyAsync(b => b.UserId == viewer && b.BlockedUserId == island.OwnerUserId || b.UserId == island.OwnerUserId && b.BlockedUserId == viewer, ct)) return null;

        var owner = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => u.Id == island.OwnerUserId).Select(u => new { u.Id, u.Username, Display = u.Avatar != null ? u.Avatar.DisplayName : u.Username }).SingleOrDefaultAsync(ct);
        var server = await db.ServerInstances.AsNoTracking().Where(s => s.IslandId == islandId && s.StoppedAt == null && s.Status == "healthy" && s.LastHeartbeatAt > now.AddSeconds(-45)).OrderByDescending(s => s.LastHeartbeatAt).Select(s => new { s.PlayerCount }).FirstOrDefaultAsync(ct);
        var allowed = await IslandAccess.AllowsAsync(db, island, viewer, now, ct);
        string access;
        if (allowed) access = (await TeleportService.TakenPlacesAsync(db, islandId, viewer, now, ct)) >= island.MaxPlayers ? "full" : "open";
        else if (await db.IslandBans.AsNoTracking().AnyAsync(b => b.IslandId == islandId && b.UserId == viewer && b.RevokedAt == null && (b.ExpiresAt == null || b.ExpiresAt > now), ct)) access = "banned";
        else access = island.AccessClosed ? "closed" : "private";
        var favorite = await db.IslandFavorites.AsNoTracking().AnyAsync(f => f.UserId == viewer && f.IslandId == islandId, ct);
        return new
        {
            id = island.Id, name = island.Name, description = island.Description, category = island.Category, contentRating = island.ContentRating, language = island.Language, coverImageUrl = island.CoverImageUrl,
            owner = owner is null ? null : new { id = owner.Id, username = owner.Username, displayName = owner.Display },
            online = server is not null, visitors = server?.PlayerCount, maxPlayers = island.MaxPlayers, size = island.Size, spawnX = island.SpawnX, spawnZ = island.SpawnZ,
            access, canEnter = access is "open", favorite, allowsGuestEvents = island.AllowGuestEvents, isOwner = mine, visibility = IslandAccess.ModeOf(island)
        };
    }
}
