using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record ReturnObjectsRequest(Guid[] ObjectIds);
public sealed record ReturnOwnerRequest(Guid OwnerUserId);

/// <summary>
/// The Objetos e Prims tab: how much of the island's budget is used, the objects with who owns them and what each costs, the consumption by owner, the details of one object and returning objects.
/// Returning never deletes for good: it uses the same operation as the land system, which saves each owner's objects as a package in their inventory and only then takes them off the island.
/// Only the owner and administrators see and do this.
/// </summary>
public static class IslandObjectManagement
{
    public const int PageSize = 10;

    public static IEndpointRouteBuilder MapIslandObjectManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/objects").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", ListAsync); group.MapGet("/owners", OwnersAsync); group.MapGet("/{objectId:guid}", DetailAsync); group.MapPost("/return", ReturnAsync); group.MapPost("/return-owner", ReturnOwnerAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, Guid User, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, default, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, user, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, user, null) : (null, user, Results.Forbid());
    }

    private static async Task<Dictionary<Guid, string>> Names(LifeVerseDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var avatars = await db.Avatars.AsNoTracking().Where(a => list.Contains(a.UserId)).Select(a => new { a.UserId, a.DisplayName }).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToListAsync(ct);
        return list.ToDictionary(id => id, id => avatars.FirstOrDefault(a => a.UserId == id)?.DisplayName ?? users.FirstOrDefault(u => u.Id == id)?.Username ?? "");
    }

    private static string PhysicsOf(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("mode", out var m) && m.GetString() == "physical" ? "physical" : doc.RootElement.TryGetProperty("collision", out var c) && c.ValueKind == JsonValueKind.False ? "none" : "static"; }
        catch (JsonException) { return "static"; }
    }

    private static object Usage(Island island, int objects, long cost) => new { objects, maxObjects = BuildCost.Limit(island.ResourceLimitsJson, "objects", 5000), cost, maxCost = BuildCost.Limit(island.ResourceLimitsJson, "cost", BuildCost.DefaultIslandBudget) };

    public static async Task<IResult> ListAsync(Guid islandId, string? q, Guid? owner, string? type, string? scripts, string? sort, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var live = db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null);
        var count = await live.CountAsync(ct); var cost = await live.SumAsync(o => (long?)o.Cost, ct) ?? 0;
        var query = live;
        if (owner is { } ownerId) query = query.Where(o => o.OwnerUserId == ownerId);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(o => o.PrimitiveType == type);
        if (scripts == "with") query = query.Where(o => db.ObjectScripts.Any(s => s.ObjectId == o.Id && s.Status == "active"));
        else if (scripts == "without") query = query.Where(o => !db.ObjectScripts.Any(s => s.ObjectId == o.Id && s.Status == "active"));
        var term = (q ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            var owners = db.Users.AsNoTracking().Where(u => u.Username.ToLower().Contains(term) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(term))).Select(u => u.Id);
            query = query.Where(o => o.Name.ToLower().Contains(term) || owners.Contains(o.OwnerUserId));
        }
        query = sort switch { "recent" => query.OrderByDescending(o => o.CreatedAt), "name" => query.OrderBy(o => o.Name), _ => query.OrderByDescending(o => o.Cost).ThenBy(o => o.Name) };
        var total = await query.CountAsync(ct); var index = Math.Max(1, page ?? 1);
        var rows = await query.Skip((index - 1) * PageSize).Take(PageSize).Select(o => new { o.Id, o.Name, o.OwnerUserId, o.PrimitiveType, o.Cost, o.PhysicsJson, o.PositionX, o.PositionY, o.PositionZ, o.EditLocked, Scripts = db.ObjectScripts.Count(s => s.ObjectId == o.Id && s.Status == "active") }).ToListAsync(ct);
        var names = await Names(db, rows.Select(r => r.OwnerUserId), ct);
        var types = await live.Select(o => o.PrimitiveType).Distinct().OrderBy(t => t).ToListAsync(ct);
        var items = rows.Select(r => new { id = r.Id, name = r.Name, ownerUserId = r.OwnerUserId, ownerName = names[r.OwnerUserId], type = r.PrimitiveType, cost = r.Cost, scripts = r.Scripts, physics = PhysicsOf(r.PhysicsJson), x = r.PositionX, y = r.PositionY, z = r.PositionZ, locked = r.EditLocked });
        return Results.Ok(new { usage = Usage(island, count, cost), types, total, page = index, pageSize = PageSize, items });
    }

    public static async Task<IResult> OwnersAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var grouped = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null).GroupBy(o => o.OwnerUserId).Select(g => new { Owner = g.Key, Objects = g.Count(), Cost = g.Sum(o => (long)o.Cost) }).OrderByDescending(g => g.Cost).Take(100).ToListAsync(ct);
        var names = await Names(db, grouped.Select(g => g.Owner), ct);
        var scriptRows = await db.ObjectScripts.AsNoTracking().Where(s => s.Status == "active" && db.BuildObjects.Any(o => o.Id == s.ObjectId && o.IslandId == islandId && o.DeletedAt == null)).Select(s => new { s.ObjectId, Owner = db.BuildObjects.Where(o => o.Id == s.ObjectId).Select(o => o.OwnerUserId).FirstOrDefault() }).ToListAsync(ct);
        var scriptsByOwner = scriptRows.GroupBy(s => s.Owner).ToDictionary(g => g.Key, g => g.Count());
        return Results.Ok(new { items = grouped.Select(g => new { userId = g.Owner, name = names[g.Owner], objects = g.Objects, cost = g.Cost, scripts = scriptsByOwner.GetValueOrDefault(g.Owner) }) });
    }

    public static async Task<IResult> DetailAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var o = await db.BuildObjects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == objectId && x.IslandId == islandId && x.DeletedAt == null, ct); if (o is null) return Results.NotFound();
        var names = await Names(db, new[] { o.OwnerUserId, o.CreatorUserId, o.LastOwnerUserId ?? o.OwnerUserId }, ct);
        var scripts = await db.ObjectScripts.AsNoTracking().CountAsync(s => s.ObjectId == objectId && s.Status == "active", ct);
        JsonElement permissions = default; try { permissions = JsonDocument.Parse(o.PermissionsJson).RootElement.Clone(); } catch (JsonException) { }
        return Results.Ok(new
        {
            id = o.Id, name = o.Name, description = o.Description, type = o.PrimitiveType, ownerUserId = o.OwnerUserId, ownerName = names[o.OwnerUserId], creatorName = names[o.CreatorUserId], lastOwnerName = o.LastOwnerUserId is null ? null : names[o.LastOwnerUserId.Value],
            cost = o.Cost, scripts, physics = PhysicsOf(o.PhysicsJson), position = new { x = o.PositionX, y = o.PositionY, z = o.PositionZ }, rotation = new { x = o.RotationX, y = o.RotationY, z = o.RotationZ }, scale = new { x = o.ScaleX, y = o.ScaleY, z = o.ScaleZ },
            locked = o.EditLocked, temporaryUntil = o.ExpiresAt, createdAt = o.CreatedAt, updatedAt = o.UpdatedAt, permissions,
        });
    }

    public static async Task<IResult> ReturnAsync(Guid islandId, ReturnObjectsRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var ids = (request.ObjectIds ?? []).Distinct().Take(BuildPackageLimits.MaxReturnAtOnce).ToArray();
        if (ids.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["objectIds"] = ["Escolha ao menos um objeto."] });
        var tracked = await db.Islands.SingleAsync(i => i.Id == islandId, ct);
        var named = await db.BuildObjects.AsNoTracking().Where(o => ids.Contains(o.Id) && o.IslandId == islandId && o.DeletedAt == null).Select(o => new { o.Name, o.OwnerUserId }).ToListAsync(ct);
        var outcome = await BuildOperations.ReturnForLandAsync(tracked, me, ids, db, storage, now, ct);
        if (((outcome as IStatusCodeHttpResult)?.StatusCode ?? 200) < 400 && named.Count > 0)
        {
            IslandAudit.Add(db, islandId, me, "objeto", "objects_returned", named.Count == 1 ? $"Devolveu o objeto {named[0].Name}" : $"Devolveu {named.Count} objetos", string.Join(", ", named.Select(n => n.Name).Take(5)), null, new { count = named.Count, names = named.Select(n => n.Name).Take(20) }, now); await db.SaveChangesAsync(ct);
        }
        return outcome;
    }

    /// <summary>Returns everything one person has on the island, in batches, and says how many were returned and how many are left.</summary>
    public static async Task<IResult> ReturnOwnerAsync(Guid islandId, ReturnOwnerRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        if (request.OwnerUserId == island.OwnerUserId && me != island.OwnerUserId) return Results.Conflict(new { error = "owner", message = "Só o proprietário devolve os objetos do próprio proprietário." });
        var tracked = await db.Islands.SingleAsync(i => i.Id == islandId, ct);
        var before = await db.BuildObjects.CountAsync(o => o.IslandId == islandId && o.OwnerUserId == request.OwnerUserId && o.DeletedAt == null, ct);
        var failure = (IResult?)null;
        for (var round = 0; round < 25; round++)
        {
            var ids = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.OwnerUserId == request.OwnerUserId && o.DeletedAt == null).OrderBy(o => o.CreatedAt).Select(o => o.Id).Take(BuildPackageLimits.MaxReturnAtOnce).ToArrayAsync(ct);
            if (ids.Length == 0) break;
            var result = await BuildOperations.ReturnForLandAsync(tracked, me, ids, db, storage, now, ct);
            if ((result as IStatusCodeHttpResult)?.StatusCode is { } code and >= 400) { failure = result; break; }
        }
        var left = await db.BuildObjects.CountAsync(o => o.IslandId == islandId && o.OwnerUserId == request.OwnerUserId && o.DeletedAt == null, ct);
        if (before - left > 0) { var person = await IslandAudit.NameAsync(db, request.OwnerUserId, ct); IslandAudit.Add(db, islandId, me, "objeto", "owner_objects_returned", $"Devolveu {before - left} objeto(s) de {person}", person, null, new { returned = before - left, remaining = left }, now); await db.SaveChangesAsync(ct); }
        if (failure is not null && left == before) return failure;
        return Results.Ok(new { returned = before - left, remaining = left });
    }
}
