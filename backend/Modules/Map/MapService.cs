using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Map;

/// <summary>What the atlas is asked for: text, category, ratings, and the part of the grid to look at (cells).</summary>
public sealed record AtlasQuery(string? Q = null, string? Category = null, string[]? Ratings = null, int? MinCellX = null, int? MaxCellX = null, int? MinCellY = null, int? MaxCellY = null, int Limit = 300);

public sealed record TileResult(byte[] Png, string Etag, DateTimeOffset GeneratedAt);

/// <summary>
/// The cartographic base of the world map. The world is a set of independent islands, so the map is an atlas: every island has a cell of a square grid, given once by the server (the first island at the centre, the
/// next ones around it in a spiral, so cells never overlap and never move); and a picture from above, made from the saved terrain and kept until the terrain or the buildings change. Who sees an island on the
/// map follows who may know it exists; whether they can enter is worked out for each person, for the card of the island.
/// </summary>
public static class MapService
{
    public const int CellMeters = 768, TileSize = 256, MaxLimit = 500;
    public static readonly TimeSpan MinRefresh = TimeSpan.FromSeconds(60);

    /// <summary>The cell in place <paramref name="n"/> of a square spiral that starts at the centre.</summary>
    public static (int X, int Y) SpiralCell(int n)
    {
        int x = 0, y = 0, dx = 0, dy = -1;
        for (var i = 0; i < n; i++) { if (x == y || (x < 0 && x == -y) || (x > 0 && x == 1 - y)) (dx, dy) = (-dy, dx); x += dx; y += dy; }
        return (x, y);
    }

    /// <summary>Gives a cell to every island that has none. The welcome island goes first (to the centre), then in order of arrival. Two requests at once cannot give the same cell: the index refuses and the loser tries again.</summary>
    public static async Task<int> EnsureAtlasAsync(LifeVerseDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var waiting = await db.Islands.Where(i => i.DeletedAt == null && i.AtlasX == null).OrderByDescending(i => i.IsWelcome).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id).ToListAsync(ct);
            if (waiting.Count == 0) return 0;
            var taken = (await db.Islands.AsNoTracking().Where(i => i.AtlasX != null).Select(i => new { i.AtlasX, i.AtlasY }).ToListAsync(ct)).Select(c => (c.AtlasX!.Value, c.AtlasY!.Value)).ToHashSet();
            var n = 0;
            foreach (var island in waiting)
            {
                (int X, int Y) cell; do cell = SpiralCell(n++); while (taken.Contains(cell));
                (island.AtlasX, island.AtlasY) = (cell.X, cell.Y); taken.Add(cell);
            }
            try { await db.SaveChangesAsync(ct); return waiting.Count; }
            catch (DbUpdateException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; }
        }
        return 0;
    }

    public static IQueryable<Island> Visible(LifeVerseDbContext db, Guid viewer, DateTimeOffset now) =>
        db.Islands.AsNoTracking().Where(i => i.DeletedAt == null
            && (i.OwnerUserId == viewer || (i.IsPublic && i.ShowOnMap) || db.IslandPermissions.Any(p => p.IslandId == i.Id && p.UserId == viewer && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now)))
            && (i.OwnerUserId == viewer || !db.UserBlocks.Any(b => b.UserId == viewer && b.BlockedUserId == i.OwnerUserId || b.UserId == i.OwnerUserId && b.BlockedUserId == viewer)));

    public static (double X, double Y) Meters(int cellX, int cellY) => ((double)cellX * CellMeters, (double)cellY * CellMeters);

    /// <summary>The islands this viewer may see on the map, with where they stand, how full they are and whether this person can enter.</summary>
    public static async Task<object> AtlasAsync(LifeVerseDbContext db, Guid viewer, AtlasQuery q, DateTimeOffset now, CancellationToken ct)
    {
        await EnsureAtlasAsync(db, ct);
        var query = Visible(db, viewer, now).Where(i => i.AtlasX != null);
        if (q.MinCellX is { } a) query = query.Where(i => i.AtlasX >= a); if (q.MaxCellX is { } b) query = query.Where(i => i.AtlasX <= b);
        if (q.MinCellY is { } c) query = query.Where(i => i.AtlasY >= c); if (q.MaxCellY is { } d) query = query.Where(i => i.AtlasY <= d);
        if (!string.IsNullOrWhiteSpace(q.Category)) { var category = q.Category.Trim().ToLower(); query = query.Where(i => i.Category.ToLower() == category); }
        if (q.Ratings is { Length: > 0 } ratings) { var wanted = ratings.Select(r => r.Trim().ToLowerInvariant()).ToList(); query = query.Where(i => wanted.Contains(i.ContentRating)); }
        if (!string.IsNullOrWhiteSpace(q.Q)) { var text = q.Q.Trim().ToLower(); query = query.Where(i => i.Name.ToLower().Contains(text) || i.Description.ToLower().Contains(text) || i.Category.ToLower().Contains(text)); }
        var total = await query.CountAsync(ct); var limit = Math.Clamp(q.Limit <= 0 ? 300 : q.Limit, 1, MaxLimit);
        var islands = await query.OrderByDescending(i => i.IsWelcome).ThenBy(i => i.Name).Take(limit).ToListAsync(ct);
        var ids = islands.Select(i => i.Id).ToList(); var owners = islands.Select(i => i.OwnerUserId).Distinct().ToList();

        var servers = await db.ServerInstances.AsNoTracking().Where(s => ids.Contains(s.IslandId) && s.StoppedAt == null && s.Status == "healthy" && s.LastHeartbeatAt > now.AddSeconds(-45)).OrderByDescending(s => s.LastHeartbeatAt).Select(s => new { s.IslandId, s.PlayerCount }).ToListAsync(ct);
        var present = servers.GroupBy(s => s.IslandId).ToDictionary(g => g.Key, g => g.First().PlayerCount);
        var reserved = (await db.TeleportTransfers.AsNoTracking().Where(t => ids.Contains(t.DestinationIslandId) && t.UserId != viewer && TeleportService.ActiveStatuses.Contains(t.Status) && t.ExpiresAt > now).GroupBy(t => t.DestinationIslandId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)).ToDictionary(g => g.Key, g => g.N);
        var permissions = (await db.IslandPermissions.AsNoTracking().Where(p => ids.Contains(p.IslandId) && p.UserId == viewer && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now)).Select(p => new { p.IslandId, p.Permission }).ToListAsync(ct)).GroupBy(p => p.IslandId).ToDictionary(g => g.Key, g => g.Select(p => p.Permission).ToHashSet());
        var banned = (await db.IslandBans.AsNoTracking().Where(x => ids.Contains(x.IslandId) && x.UserId == viewer && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now)).Select(x => x.IslandId).ToListAsync(ct)).ToHashSet();
        var favorites = (await db.IslandFavorites.AsNoTracking().Where(f => f.UserId == viewer && ids.Contains(f.IslandId)).Select(f => f.IslandId).ToListAsync(ct)).ToHashSet();
        var liked = await db.IslandFavorites.AsNoTracking().Where(f => ids.Contains(f.IslandId)).GroupBy(f => f.IslandId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var names = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Avatar != null ? u.Avatar.DisplayName : u.Username, ct);
        var tiled = (await db.MapTiles.AsNoTracking().Where(t => ids.Contains(t.IslandId)).Select(t => new { t.IslandId, t.Etag }).ToListAsync(ct)).ToDictionary(t => t.IslandId, t => t.Etag);
        var snapshots = (await db.IslandSnapshots.AsNoTracking().Where(s => ids.Contains(s.IslandId)).GroupBy(s => s.IslandId).Select(g => g.Key).ToListAsync(ct)).ToHashSet();

        string AccessOf(Island i)
        {
            var mine = permissions.GetValueOrDefault(i.Id);
            if (i.OwnerUserId == viewer || mine?.Contains("admin") == true) return "open";
            if (banned.Contains(i.Id)) return "banned";
            if (i.AccessClosed) return "closed";
            if (!(i.IsPublic || mine?.Any(p => p is "enter" or "build" or "edit" or "admin") == true)) return "private";
            return present.GetValueOrDefault(i.Id) + reserved.GetValueOrDefault(i.Id) >= i.MaxPlayers ? "full" : "open";
        }

        var items = islands.Select(i =>
        {
            var (mx, my) = Meters(i.AtlasX!.Value, i.AtlasY!.Value); var online = present.ContainsKey(i.Id);
            return new
            {
                id = i.Id, name = i.Name, description = i.Description.Length > 240 ? i.Description[..240] : i.Description, category = i.Category, contentRating = i.ContentRating, language = i.Language, isPublic = i.IsPublic, isWelcome = i.IsWelcome,
                cellX = i.AtlasX, cellY = i.AtlasY, x = mx, y = my, size = i.Size, spawnX = i.SpawnX, spawnZ = i.SpawnZ, maxPlayers = i.MaxPlayers, online, visitors = online ? (int?)present[i.Id] : null, access = AccessOf(i),
                coverImageUrl = i.CoverImageUrl, hasTile = snapshots.Contains(i.Id), tileVersion = tiled.GetValueOrDefault(i.Id), favorite = favorites.Contains(i.Id), favorites = liked.GetValueOrDefault(i.Id), createdAt = i.CreatedAt, isOwner = i.OwnerUserId == viewer,
                owner = i.IsPublic && names.TryGetValue(i.OwnerUserId, out var owner) ? new { id = i.OwnerUserId, displayName = owner } : null, updatedAt = i.UpdatedAt
            };
        }).ToList();
        return new { items, total, cellMeters = CellMeters, tileSize = TileSize, truncated = total > items.Count };
    }

    // ---- the picture from above ----------------------------------------------------------------------------------------------------

    private static string ObjectsStamp(int count, DateTimeOffset? latest) => $"{count}:{latest?.UtcTicks ?? 0}";

    private static string EtagOf(Guid island, long version, string stamp) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{island:N}|{version}|{stamp}|{TileSize}")))[..20].ToLowerInvariant();

    /// <summary>The picture of an island for this viewer, made now when it is missing or out of date (but never more than once a minute for the same island). Null when the island is not on their map or there is no saved terrain yet.</summary>
    public static async Task<TileResult?> TileAsync(LifeVerseDbContext db, Guid viewer, Guid islandId, DateTimeOffset now, CancellationToken ct)
    {
        if (!await Visible(db, viewer, now).AnyAsync(i => i.Id == islandId, ct)) return null;
        var latest = await db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == islandId).OrderByDescending(s => s.Version).Select(s => new { s.Id, s.Version }).FirstOrDefaultAsync(ct);
        var tile = await db.MapTiles.SingleOrDefaultAsync(t => t.IslandId == islandId, ct);
        if (latest is null) return tile is null ? null : new TileResult(tile.Png, tile.Etag, tile.GeneratedAt);
        var objects = db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null && o.ParentObjectId == null && o.ExpiresAt == null);
        var count = await objects.CountAsync(ct); var stamp = ObjectsStamp(count, count == 0 ? null : await objects.MaxAsync(o => (DateTimeOffset?)o.UpdatedAt, ct));
        if (tile is not null && tile.SnapshotVersion == latest.Version && tile.ObjectsStamp == stamp && tile.Size == TileSize) return new TileResult(tile.Png, tile.Etag, tile.GeneratedAt);
        if (tile is not null && now - tile.GeneratedAt < MinRefresh) return new TileResult(tile.Png, tile.Etag, tile.GeneratedAt); // changed a moment ago: the older picture does until the minute passes

        var terrainJson = await db.IslandSnapshots.AsNoTracking().Where(s => s.Id == latest.Id).Select(s => s.TerrainJson).SingleAsync(ct);
        if (IslandTerrainManagement.Read(terrainJson) is not { } map) return tile is null ? null : new TileResult(tile.Png, tile.Etag, tile.GeneratedAt);
        var footprints = (await objects.Take(MapRenderer.MaxFootprints).Select(o => new { o.PositionX, o.PositionZ, o.ScaleX, o.ScaleZ, o.RotationY }).ToListAsync(ct)).Select(o => new Footprint(o.PositionX, o.PositionZ, o.ScaleX, o.ScaleZ, o.RotationY)).ToList();
        var png = Png.Encode(TileSize, TileSize, MapRenderer.Render(map, footprints, TileSize)); var etag = EtagOf(islandId, latest.Version, stamp);
        if (tile is null) db.MapTiles.Add(tile = new MapTile { IslandId = islandId });
        (tile.SnapshotVersion, tile.ObjectsStamp, tile.Size, tile.Png, tile.Etag, tile.GeneratedAt) = (latest.Version, stamp, TileSize, png, etag, now);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* made at the same time by another request: either picture is fine */ }
        return new TileResult(png, etag, now);
    }
}

public static class MapEndpoints
{
    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/map").WithTags("Map").RequireAuthorization().RequireRateLimiting("search");
        group.MapGet("/atlas", async (string? q, string? category, string? ratings, int? minX, int? maxX, int? minY, int? maxY, int? limit, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Who(c, out var me) ? Results.Ok(await MapService.AtlasAsync(db, me, new AtlasQuery(q, category, ratings?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), minX, maxX, minY, maxY, limit ?? 300), t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/islands/{islandId:guid}/tile", async (Guid islandId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized();
            if (await MapService.TileAsync(db, me, islandId, t.GetUtcNow(), ct) is not { } tile) return Results.NotFound();
            var etag = $"\"{tile.Etag}\""; c.Response.Headers.ETag = etag; c.Response.Headers.CacheControl = "private, max-age=300"; c.Response.Headers.LastModified = tile.GeneratedAt.ToString("R");
            if (c.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.File(tile.Png, "image/png");
        });
        return endpoints;
    }

    private static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
}

public static class MapPlaceEndpoints
{
    public static IEndpointRouteBuilder MapMapPlaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/map").WithTags("Map").RequireAuthorization().RequireRateLimiting("search");
        var write = endpoints.MapGroup("/api/v1/map").WithTags("Map").RequireAuthorization().RequireRateLimiting("announce");
        group.MapGet("/landmarks", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => Who(c, out var me) ? Results.Ok(await MapPlaces.PanelsAsync(db, me, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/landmarks/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : await MapPlaces.ViewAsync(db, me, id, t.GetUtcNow(), ct) is { } view ? Results.Ok(view) : Results.NotFound());
        write.MapPost("/landmarks", async (LandmarkRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => { if (!Who(c, out var me)) return Results.Unauthorized(); var o = await MapPlaces.CreateAsync(db, me, r, t.GetUtcNow(), ct); Audit(log, "landmark_created", me, o); return Reply(o, () => Results.Created($"/api/v1/map/landmarks/{o.Id}", new { id = o.Id })); });
        write.MapPatch("/landmarks/{id:guid}", async (Guid id, LandmarkChange r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => Who(c, out var me) ? Reply(await MapPlaces.ChangeAsync(db, me, id, r, t.GetUtcNow(), ct)) : Results.Unauthorized());
        write.MapDelete("/landmarks/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => Who(c, out var me) ? Reply(await MapPlaces.DeleteAsync(db, me, id, t.GetUtcNow(), ct)) : Results.Unauthorized());
        write.MapPost("/landmark-folders", async (FolderRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => { if (!Who(c, out var me)) return Results.Unauthorized(); var o = await MapPlaces.CreateFolderAsync(db, me, r.Name, t.GetUtcNow(), ct); return Reply(o, () => Results.Created($"/api/v1/map/landmark-folders/{o.Id}", new { id = o.Id })); });
        write.MapPatch("/landmark-folders/{id:guid}", async (Guid id, FolderRequest r, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => Who(c, out var me) ? Reply(await MapPlaces.RenameFolderAsync(db, me, id, r.Name, ct)) : Results.Unauthorized());
        write.MapDelete("/landmark-folders/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => Who(c, out var me) ? Reply(await MapPlaces.DeleteFolderAsync(db, me, id, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapGet("/islands/{islandId:guid}/parcels", async (Guid islandId, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : await MapOverlays.ParcelsAsync(db, me, islandId, t.GetUtcNow(), ct) is { } view ? Results.Ok(view) : Results.NotFound());
        group.MapGet("/people", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => Who(c, out var me) ? Results.Ok(await MapOverlays.FriendsAsync(db, me, t.GetUtcNow(), ct)) : Results.Unauthorized());
        group.MapPost("/arrival", async (ArrivalRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) =>
        {
            if (!Who(c, out var me)) return Results.Unauthorized(); var answer = await MapArrival.ResolveAsync(db, me, r, t.GetUtcNow(), ct);
            log.CreateLogger("LifeVerse.Audit").LogInformation("audit action=map_arrival user={User} target={Target} result={Result}", me, r.IslandId, answer.Allowed ? "ok" : answer.Reason); return Results.Ok(answer);
        });
        return endpoints;
    }

    private static IResult Reply(MapOutcome o, Func<IResult>? ok = null) => o.Ok ? ok?.Invoke() ?? Results.NoContent() : o.Error switch
    {
        "not_found" or "island_not_found" or "folder_not_found" => Results.NotFound(new { error = o.Error }),
        "no_access" => Results.Json(new { error = o.Error }, statusCode: 403),
        "too_many_landmarks" or "too_many_folders" or "name_taken" => Results.Conflict(new { error = o.Error }),
        _ => Results.BadRequest(new { error = o.Error })
    };

    private static void Audit(ILoggerFactory log, string action, Guid user, MapOutcome o) => log.CreateLogger("LifeVerse.Audit").LogInformation("audit action={Action} user={User} target={Target} result={Result}", action, user, o.Id, o.Ok ? "ok" : o.Error);

    private static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);
}
