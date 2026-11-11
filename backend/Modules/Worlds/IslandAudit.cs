using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>Writes the audit entries of the management actions. An entry is added in the same save as the change it describes, so there is never a change without its entry.</summary>
public static class IslandAudit
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static string Json(object? value) => value is null ? "null" : JsonSerializer.Serialize(value, Web);

    public static void Add(LifeVerseDbContext db, Guid islandId, Guid actor, string category, string action, string summary, string resource, object? before, object? after, DateTimeOffset now, Guid? correlation = null) =>
        db.IslandAudit.Add(new IslandAuditEntry
        {
            Id = Guid.NewGuid(), IslandId = islandId, ActorUserId = actor, Category = category, Action = action, Summary = summary.Length > 400 ? summary[..400] : summary, Resource = resource.Length > 200 ? resource[..200] : resource,
            BeforeJson = Json(before), AfterJson = Json(after), CorrelationId = correlation ?? Guid.NewGuid(), CreatedAt = now,
        });

    public static async Task<string> NameAsync(LifeVerseDbContext db, Guid user, CancellationToken ct = default) =>
        await db.Avatars.AsNoTracking().Where(a => a.UserId == user).Select(a => a.DisplayName).FirstOrDefaultAsync(ct) ?? await db.Users.AsNoTracking().Where(u => u.Id == user).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "";
}

/// <summary>
/// The Histórico e Auditoria tab: everything done through the management window (settings, access, bans, permissions, returned objects), with who did it and the values before and after, together with the
/// build history the island already keeps (who created, moved, returned or deleted which object). Only the owner and administrators read it; nothing here can be changed or deleted.
/// </summary>
public static class IslandAuditManagement
{
    public const int PageSize = 10;
    private static readonly string[] Categories = ["geral", "acesso", "banimento", "permissao", "objeto", "servidor", "ambiente", "midia", "fisica", "construcao"];

    public static IEndpointRouteBuilder MapIslandAuditManagement(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/worlds/islands/{islandId:guid}/management/audit").WithTags("Worlds").RequireAuthorization();
        group.MapGet("", ListAsync); group.MapGet("/{entryId:guid}", DetailAsync);
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<(Island? Island, IResult? Refusal)> AdminOf(Guid islandId, HttpContext context, LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (!Who(context, out var user)) return (null, Results.Unauthorized());
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return (null, Results.NotFound());
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        return admin ? (island, null) : (null, Results.Forbid());
    }

    private sealed record Row(Guid Id, DateTimeOffset At, Guid Actor, string Category, string Action, string Summary, string Resource);

    private static string BuildSummary(string operation, string name) => operation switch
    {
        "create" => $"Criou o objeto {name}", "delete" => $"Excluiu o objeto {name}", "return" => $"Devolveu o objeto {name}", "move" or "transform" => $"Moveu o objeto {name}", "update" or "properties" => $"Alterou o objeto {name}",
        _ => $"{operation}: {name}",
    };

    public static async Task<IResult> ListAsync(Guid islandId, int? hours, string? user, string? category, string? q, int? page, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        var since = hours is > 0 ? now.AddHours(-hours.Value) : (DateTimeOffset?)null;
        var index = Math.Max(1, page ?? 1); var take = index * PageSize;
        var term = (q ?? "").Trim().ToLowerInvariant(); var who = (user ?? "").Trim().ToLowerInvariant();
        IQueryable<Guid> actors = db.Users.AsNoTracking().Where(u => who.Length > 0 && (u.Username.ToLower().Contains(who) || db.Avatars.Any(a => a.UserId == u.Id && a.DisplayName.ToLower().Contains(who)))).Select(u => u.Id);

        var audit = db.IslandAudit.AsNoTracking().Where(e => e.IslandId == islandId);
        if (since is { } s) audit = audit.Where(e => e.CreatedAt >= s);
        if (who.Length > 0) audit = audit.Where(e => actors.Contains(e.ActorUserId));
        if (term.Length > 0) audit = audit.Where(e => e.Summary.ToLower().Contains(term) || e.Resource.ToLower().Contains(term));
        var wantsAudit = string.IsNullOrEmpty(category) || (category != "construcao" && Categories.Contains(category));
        if (wantsAudit && !string.IsNullOrEmpty(category)) audit = audit.Where(e => e.Category == category);

        var build = db.BuildOperations.AsNoTracking().Where(o => o.IslandId == islandId);
        if (since is { } s2) build = build.Where(o => o.CreatedAt >= s2);
        if (who.Length > 0) build = build.Where(o => actors.Contains(o.ActorUserId));
        if (term.Length > 0) build = build.Where(o => o.Operation.ToLower().Contains(term) || db.BuildObjects.Any(b => b.Id == o.ObjectId && b.Name.ToLower().Contains(term)));
        var wantsBuild = string.IsNullOrEmpty(category) || category == "construcao";

        var rows = new List<Row>(); var total = 0;
        if (wantsAudit) { total += await audit.CountAsync(ct); rows.AddRange((await audit.OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(ct)).Select(e => new Row(e.Id, e.CreatedAt, e.ActorUserId, e.Category, e.Action, e.Summary, e.Resource))); }
        if (wantsBuild)
        {
            total += await build.CountAsync(ct);
            var ops = await build.OrderByDescending(o => o.CreatedAt).Take(take).ToListAsync(ct);
            var objectIds = ops.Where(o => o.ObjectId != null).Select(o => o.ObjectId!.Value).Distinct().ToList();
            var names = await db.BuildObjects.AsNoTracking().Where(b => objectIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            rows.AddRange(ops.Select(o => { var name = o.ObjectId is { } id && names.TryGetValue(id, out var n) ? n : "objeto removido"; return new Row(o.Id, o.CreatedAt, o.ActorUserId, "construcao", o.Operation, BuildSummary(o.Operation, name), name); }));
        }
        var pageRows = rows.OrderByDescending(r => r.At).Skip((index - 1) * PageSize).Take(PageSize).ToList();
        var ids = pageRows.Select(r => r.Actor).Distinct().ToList();
        var actorNames = new Dictionary<Guid, string>(); foreach (var id in ids) actorNames[id] = await IslandAudit.NameAsync(db, id, ct);
        return Results.Ok(new
        {
            total, page = index, pageSize = PageSize,
            items = pageRows.Select(r => new { id = r.Id, at = r.At, actorUserId = r.Actor, actorName = actorNames[r.Actor], category = r.Category, action = r.Action, summary = r.Summary, resource = r.Resource, result = "success" }),
        });
    }

    public static async Task<IResult> DetailAsync(Guid islandId, Guid entryId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var (island, refusal) = await AdminOf(islandId, context, db, now, ct); if (island is null) return refusal!;
        object? Parse(string json) { try { var element = JsonDocument.Parse(json).RootElement.Clone(); return element.ValueKind == JsonValueKind.Null ? null : element; } catch (JsonException) { return null; } }
        var entry = await db.IslandAudit.AsNoTracking().SingleOrDefaultAsync(e => e.Id == entryId && e.IslandId == islandId, ct);
        if (entry is not null)
            return Results.Ok(new { id = entry.Id, at = entry.CreatedAt, actorName = await IslandAudit.NameAsync(db, entry.ActorUserId, ct), actorUserId = entry.ActorUserId, island = island.Name, islandId, category = entry.Category, action = entry.Action, summary = entry.Summary, resource = entry.Resource, result = "success", before = Parse(entry.BeforeJson), after = Parse(entry.AfterJson), correlationId = entry.CorrelationId });
        var op = await db.BuildOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == entryId && o.IslandId == islandId, ct); if (op is null) return Results.NotFound();
        var name = op.ObjectId is { } oid ? await db.BuildObjects.AsNoTracking().Where(b => b.Id == oid).Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "objeto removido" : "";
        return Results.Ok(new { id = op.Id, at = op.CreatedAt, actorName = await IslandAudit.NameAsync(db, op.ActorUserId, ct), actorUserId = op.ActorUserId, island = island.Name, islandId, category = "construcao", action = op.Operation, summary = BuildSummary(op.Operation, name), resource = name, result = "success", before = (object?)null, after = Parse(op.PayloadJson), correlationId = op.Id });
    }
}
