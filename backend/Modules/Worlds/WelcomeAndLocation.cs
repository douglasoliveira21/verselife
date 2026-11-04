using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Networking;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record LocationEntry(Guid UserId, float X, float Y, float Z);
public sealed record LocationBatch(List<LocationEntry>? Players);
public sealed record StartPlace(Guid IslandId, string Name, string Reason, bool IsWelcome);

/// <summary>
/// The island every new avatar arrives on. One public island is the welcome island: an operator chooses it, and when nobody did, the oldest public island
/// that exists is used, and when there is none, a new one is made (owned by a system account). Nothing here lets a person pick the welcome island for the others.
/// </summary>
public static class WelcomeIsland
{
    public const string SystemUsername = "lifeverse";
    public const string DefaultName = "Ilha de Boas-vindas";
    public const string DefaultSlug = "boas-vindas";

    private static IQueryable<Island> Usable(LifeVerseDbContext db) => db.Islands.Where(i => i.DeletedAt == null && i.IsPublic);

    /// <summary>The welcome island, chosen or made if there was none.</summary>
    public static async Task<Island> ResolveAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var chosen = await Usable(db).Where(i => i.IsWelcome).OrderBy(i => i.CreatedAt).FirstOrDefaultAsync(ct);
        if (chosen is not null) return chosen;
        // the flag points at an island that was deleted or made private: it moves on to the oldest public island there is
        var oldest = await Usable(db).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).FirstOrDefaultAsync(ct);
        if (oldest is not null) return await MarkAsync(db, oldest, now, ct) ?? oldest;
        { var made = await CreateAsync(db, now, ct); return await MarkAsync(db, made, now, ct) ?? made; }
    }

    /// <summary>Makes an island the welcome island (the one before stops being). Only a public island that exists.</summary>
    public static async Task<Island?> MarkAsync(LifeVerseDbContext db, Island island, DateTimeOffset now, CancellationToken ct)
    {
        if (island.DeletedAt is not null || !island.IsPublic) return null;
        foreach (var other in await db.Islands.Where(i => i.IsWelcome && i.Id != island.Id).ToListAsync(ct)) other.IsWelcome = false;
        island.IsWelcome = true;
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            // somebody else chose at the same moment: theirs stands
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return await Usable(db).Where(i => i.IsWelcome).FirstOrDefaultAsync(ct) ?? island;
        }
        return island;
    }

    private static async Task<User> SystemUserAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = SystemUsername.ToUpperInvariant();
        var existing = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);
        if (existing is { PasswordHash: "!" }) return existing; // ours: it cannot sign in
        var name = existing is null ? SystemUsername : SystemUsername + "-sistema";
        var user = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@sistema.lifeverse.local", NormalizedEmail = $"{name}@SISTEMA.LIFEVERSE.LOCAL".ToUpperInvariant(), PasswordHash = "!", CreatedAt = now, UpdatedAt = now, Status = "active" };
        db.Users.Add(user); await db.SaveChangesAsync(ct); return user;
    }

    private static async Task<Island> CreateAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var owner = await SystemUserAsync(db, now, ct);
        var slug = DefaultSlug; for (var n = 2; await db.Islands.AnyAsync(i => i.Slug == slug && i.DeletedAt == null, ct); n++) slug = $"{DefaultSlug}-{n}";
        var island = new Island
        {
            Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = DefaultName, Slug = slug, IsPublic = true, Category = "social", Size = 256, MaxPlayers = 100, IsWelcome = true,
            Description = "É aqui que todo avatar novo chega. Passeie, conheça outras pessoas e use o mapa e a busca para visitar as outras ilhas.", CreatedAt = now, UpdatedAt = now
        };
        db.Islands.Add(island); await db.SaveChangesAsync(ct); return island;
    }
}

/// <summary>Where each person was last: written by the island servers, read when the person comes back (the island to enter and the spot to stand on).</summary>
public static class PlayerLocations
{
    public const int MaxPerBatch = 200;

    /// <summary>Whether a spot is a sensible place to stand on an island of this size.</summary>
    public static bool Sane(float x, float y, float z, int size) => float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) && SpawnPlanner.Inside(x, z, size) && y is > -1000f and < 5000f;

    /// <summary>Keeps where the people of one island are. Entries with an unknown person or a spot outside the island are skipped. Returns how many were kept.</summary>
    public static async Task<int> SaveAsync(LifeVerseDbContext db, Guid islandId, IReadOnlyCollection<LocationEntry> entries, DateTimeOffset now, CancellationToken ct)
    {
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return 0;
        var taken = entries.Where(e => Sane(e.X, e.Y, e.Z, island.Size)).GroupBy(e => e.UserId).Select(g => g.Last()).Take(MaxPerBatch).ToList();
        var ids = taken.Select(e => e.UserId).ToList();
        var known = (await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.DeletedAt == null).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        var rows = await db.PlayerLocations.Where(l => ids.Contains(l.UserId)).ToDictionaryAsync(l => l.UserId, ct); var kept = 0;
        foreach (var e in taken.Where(e => known.Contains(e.UserId)))
        {
            if (!rows.TryGetValue(e.UserId, out var row)) { row = new PlayerLocation { UserId = e.UserId }; db.PlayerLocations.Add(row); }
            row.IslandId = islandId; row.X = e.X; row.Y = e.Y; row.Z = e.Z; row.UpdatedAt = now; kept++;
        }
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException) { foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; return 0; }
        return kept;
    }

    /// <summary>The spot a person stood on in this island the last time, or null when they were elsewhere (or never here).</summary>
    public static async Task<LocationEntry?> ResumeAsync(LifeVerseDbContext db, Guid userId, Guid islandId, int size, CancellationToken ct)
    {
        var row = await db.PlayerLocations.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId && l.IslandId == islandId, ct);
        return row is not null && Sane(row.X, row.Y, row.Z, size) ? new LocationEntry(userId, row.X, row.Y, row.Z) : null;
    }

    /// <summary>
    /// Where a person should go when they sign in: the island they were on last, as long as it still exists and they may enter it; otherwise the welcome
    /// island (which is also where every new avatar starts).
    /// </summary>
    public static async Task<StartPlace> StartAsync(LifeVerseDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var last = await db.PlayerLocations.AsNoTracking().SingleOrDefaultAsync(l => l.UserId == userId, ct);
        if (last is not null)
        {
            var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == last.IslandId && i.DeletedAt == null, ct);
            if (island is not null && (island.IsPublic || island.OwnerUserId == userId || await db.IslandPermissions.AnyAsync(p => p.IslandId == island.Id && p.UserId == userId && p.RevokedAt == null && p.Permission == "enter", ct)))
                return new StartPlace(island.Id, island.Name, "last", island.IsWelcome);
        }
        var welcome = await WelcomeIsland.ResolveAsync(db, now, ct);
        return new StartPlace(welcome.Id, welcome.Name, "welcome", true);
    }
}

public static class WelcomeEndpoints
{
    public static IEndpointRouteBuilder MapWelcomeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds").WithTags("Worlds").RequireAuthorization();
        group.MapGet("/welcome", async (LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => { var i = await WelcomeIsland.ResolveAsync(db, t.GetUtcNow(), ct); return Results.Ok(new { islandId = i.Id, name = i.Name }); });
        group.MapGet("/me/start", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var me) ? Results.Ok(await PlayerLocations.StartAsync(db, me, t.GetUtcNow(), ct)) : Results.Unauthorized());
        return endpoints;
    }
}
