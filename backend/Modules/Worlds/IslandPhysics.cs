using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Nodes;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record FreezeRequest(Guid[]? ObjectIds, bool All);

/// <summary>
/// The Física tab: which objects of the island are simulated, how much work the island server does for them, and freezing the ones that slow it down. Freezing makes a physical object static (it stops
/// moving and falling) and remembers what it was, so releasing puts it back exactly; the island server learns of it through the same sync that returns objects. Nothing is deleted or moved.
/// </summary>
public static class IslandPhysicsManagement
{
    public const int PageSize = 10, MaxPerCall = 500;

    public static IEndpointRouteBuilder MapIslandPhysicsManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/physics").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", GetAsync); group.MapPost("/freeze", FreezeAsync); group.MapPost("/release", ReleaseAsync);
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

    /// <summary>True when the physics settings make the object a body that moves ("mode": "physical").</summary>
    public static bool IsPhysical(string? json) { try { return JsonNode.Parse(json ?? "{}") is JsonObject o && o["mode"]?.GetValue<string>() == "physical"; } catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { return false; } }

    /// <summary>The same settings made static: the one thing that changes is "mode".</summary>
    public static string Frozen(string json) { var node = JsonNode.Parse(json) as JsonObject ?? []; node["mode"] = "static"; return node.ToJsonString(); }

    private static string Canon(string json) { try { return JsonNode.Parse(json)?.ToJsonString() ?? "{}"; } catch (System.Text.Json.JsonException) { return json; } }

    public static async Task<IResult> GetAsync(Guid islandId, string? q, string? state, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, _, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var live = db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null);
        var simulated = live.Where(o => o.FrozenPhysicsJson != null || o.PhysicsJson.Contains("\"physical\""));
        var active = await simulated.CountAsync(o => o.FrozenPhysicsJson == null, ct); var frozen = await simulated.CountAsync(o => o.FrozenPhysicsJson != null, ct);
        var instance = await ServerChannel.RunningAsync(db, islandId, now, ct); var report = instance is null ? null : ServerChannel.ReadReport(instance);
        var fresh = instance?.ReportedAt is { } reported && now - reported < ServerChannel.FreshFor && report is not null;

        var query = simulated;
        query = state switch { "active" => query.Where(o => o.FrozenPhysicsJson == null), "frozen" => query.Where(o => o.FrozenPhysicsJson != null), _ => query };
        var term = (q ?? "").Trim().ToLowerInvariant();
        if (term.Length > 0)
        {
            var owners = db.Users.AsNoTracking().Where(u => u.Username.ToLower().Contains(term) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(term))).Select(u => u.Id);
            query = query.Where(o => o.Name.ToLower().Contains(term) || owners.Contains(o.OwnerUserId));
        }
        var total = await query.CountAsync(ct); var index = Math.Max(1, page ?? 1);
        var rows = await query.OrderByDescending(o => o.Cost).ThenBy(o => o.Name).Skip((index - 1) * PageSize).Take(PageSize).Select(o => new { o.Id, o.Name, o.OwnerUserId, o.Cost, o.PositionX, o.PositionY, o.PositionZ, Frozen = o.FrozenPhysicsJson != null, o.EditLocked }).ToListAsync(ct);
        var names = new Dictionary<Guid, string>(); foreach (var id in rows.Select(r => r.OwnerUserId).Distinct()) names[id] = await IslandAudit.NameAsync(db, id, ct);
        return Results.Ok(new
        {
            fresh, server = fresh ? new { bodies = report!.PhysicsBodies, fixedBodies = report.PhysicsFixed, maxBodies = report.PhysicsMaxBodies, physicsMs = report.PhysicsMs, tickRate = report.TickRate } : null,
            summary = new { simulated = active + frozen, active, frozen }, total, page = index, pageSize = PageSize,
            items = rows.Select(r => new { id = r.Id, name = r.Name, ownerUserId = r.OwnerUserId, ownerName = names[r.OwnerUserId], cost = r.Cost, state = r.Frozen ? "frozen" : "active", x = r.PositionX, y = r.PositionY, z = r.PositionZ, locked = r.EditLocked }),
        });
    }

    private static async Task<(List<BuildObject> Items, IResult? Refusal)> Pick(Guid islandId, FreezeRequest request, bool wantFrozen, LifeVerseDbContext db, CancellationToken ct)
    {
        var ids = (request.ObjectIds ?? []).Distinct().Take(MaxPerCall).ToList();
        if (!request.All && ids.Count == 0) return ([], Results.ValidationProblem(new Dictionary<string, string[]> { ["objectIds"] = ["Escolha ao menos um objeto."] }));
        var query = db.BuildObjects.Where(o => o.IslandId == islandId && o.DeletedAt == null);
        query = wantFrozen ? query.Where(o => o.FrozenPhysicsJson != null) : query.Where(o => o.FrozenPhysicsJson == null && o.PhysicsJson.Contains("\"physical\""));
        if (!request.All) query = query.Where(o => ids.Contains(o.Id));
        return (await query.OrderBy(o => o.CreatedAt).Take(MaxPerCall).ToListAsync(ct), null);
    }

    public static async Task<IResult> FreezeAsync(Guid islandId, FreezeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (island.OwnerUserId != me) return IslandOwnerOnly.Refusal();
        var (items, bad) = await Pick(islandId, request, false, db, ct); if (bad is not null) return bad;
        var changed = new List<BuildObject>();
        foreach (var item in items.Where(i => IsPhysical(i.PhysicsJson)))
        {
            item.FrozenPhysicsJson = item.PhysicsJson; item.PhysicsJson = Frozen(item.PhysicsJson); item.Revision++; item.UpdatedAt = now; changed.Add(item);
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = islandId, ObjectId = item.Id, ActorUserId = me, Operation = "physics_admin", PayloadJson = "{\"action\":\"freeze\"}", Revision = item.Revision, CreatedAt = now });
        }
        if (changed.Count > 0) IslandAudit.Add(db, islandId, me, "fisica", "physics_frozen", changed.Count == 1 ? $"Congelou o objeto {changed[0].Name}" : $"Congelou {changed.Count} objetos", string.Join(", ", changed.Select(c => c.Name).Take(5)), null, new { count = changed.Count, names = changed.Select(c => c.Name).Take(20) }, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { changed = changed.Count });
    }

    public static async Task<IResult> ReleaseAsync(Guid islandId, FreezeRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, me, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!; if (island.OwnerUserId != me) return IslandOwnerOnly.Refusal();
        var (items, bad) = await Pick(islandId, request, true, db, ct); if (bad is not null) return bad;
        int restored = 0, kept = 0;
        foreach (var item in items)
        {
            // Put the old settings back only if nobody changed them since: if the owner edited the physics while it was frozen, their change stays.
            var untouched = Canon(item.PhysicsJson) == Canon(Frozen(item.FrozenPhysicsJson!));
            if (untouched) { item.PhysicsJson = item.FrozenPhysicsJson!; restored++; } else kept++;
            item.FrozenPhysicsJson = null; item.Revision++; item.UpdatedAt = now;
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = islandId, ObjectId = item.Id, ActorUserId = me, Operation = "physics_admin", PayloadJson = "{\"action\":\"release\"}", Revision = item.Revision, CreatedAt = now });
        }
        if (items.Count > 0) IslandAudit.Add(db, islandId, me, "fisica", "physics_released", items.Count == 1 ? $"Liberou o objeto {items[0].Name}" : $"Liberou {items.Count} objetos", string.Join(", ", items.Select(c => c.Name).Take(5)), null, new { count = items.Count, restored, kept }, now);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { changed = items.Count, restored, kept });
    }
}
