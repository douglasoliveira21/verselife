using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// The Desempenho tab: how the island server is doing now (what it reported with its last heartbeat), how it did over the last hours (one sample a minute, kept for a day), and which scripts and
/// objects weigh the most. Only what the server really measures is shown: frames per second, the time of a frame, memory, physics ticks per second and people. There is no CPU figure because the island
/// server does not report one.
/// </summary>
public static class IslandPerformanceManagement
{
    public const int MaxPoints = 120;
    public static readonly int[] Windows = [1, 6, 24];

    public static IEndpointRouteBuilder MapIslandPerformanceManagement(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/management/performance", GetAsync).WithTags("Worlds").RequireAuthorization();
        return endpoints;
    }

    private static bool Who(HttpContext c, out Guid id) => Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static object Summary(IReadOnlyList<double> values) => values.Count == 0 ? new { min = 0.0, avg = 0.0, max = 0.0 } : new { min = values.Min(), avg = values.Average(), max = values.Max() };

    public static async Task<IResult> GetAsync(Guid islandId, int? hours, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!Who(context, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct); if (island is null) return Results.NotFound();
        var admin = island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.Permission == "admin" && p.RevokedAt == null && (p.ExpiresAt == null || p.ExpiresAt > now), ct);
        if (!admin) return Results.Forbid();
        var window = Windows.Contains(hours ?? 1) ? hours ?? 1 : 1; var since = now.AddHours(-window);

        var instance = await ServerChannel.RunningAsync(db, islandId, now, ct);
        var report = instance is null ? null : ServerChannel.ReadReport(instance);
        var fresh = instance?.ReportedAt is { } reported && now - reported < ServerChannel.FreshFor && report is not null;
        var scriptRows = await db.ObjectScripts.AsNoTracking().Where(s => db.BuildObjects.Any(o => o.Id == s.ObjectId && o.IslandId == islandId && o.DeletedAt == null)).Select(s => new { s.Id, s.ObjectId, s.Name, s.Status }).Take(5000).ToListAsync(ct);
        var activeScripts = scriptRows.Count(s => s.Status == "active");

        var samples = await db.IslandMetrics.AsNoTracking().Where(m => m.IslandId == islandId && m.At >= since).OrderBy(m => m.At).ToListAsync(ct);
        var bucket = TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMinute, (now - since).Ticks / MaxPoints));
        var series = samples.GroupBy(m => (m.At - since).Ticks / bucket.Ticks).OrderBy(g => g.Key).Select(g => new { at = g.Max(m => m.At), fps = g.Average(m => m.Fps), processMs = g.Average(m => m.ProcessMs), memoryMb = g.Average(m => m.MemoryMb), tickRate = g.Average(m => m.TickRate), players = g.Max(m => m.Players) }).ToList();

        var scriptIds = scriptRows.Select(s => s.Id).ToList(); var scriptObjectIds = scriptRows.Select(s => s.ObjectId).Distinct().ToList();
        var executions = scriptRows.Count == 0 ? [] : await db.ScriptExecutions.AsNoTracking().Where(e => e.CreatedAt >= since && scriptIds.Contains(e.ScriptId)).Select(e => new { e.ScriptId, e.InstructionCount, e.Error, e.CreatedAt }).ToListAsync(ct);
        var objectNames = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null && scriptObjectIds.Contains(o.Id)).Select(o => new { o.Id, o.Name }).ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        var scripts = executions.GroupBy(e => e.ScriptId).Select(g => { var s = scriptRows.First(x => x.Id == g.Key); return new { scriptId = s.Id, objectId = s.ObjectId, name = objectNames.GetValueOrDefault(s.ObjectId, "objeto"), script = s.Name, runs = g.Count(), instructions = g.Sum(e => (long)e.InstructionCount), errors = g.Count(e => e.Error.Length > 0), lastRunAt = g.Max(e => e.CreatedAt), status = s.Status }; })
            .OrderByDescending(x => x.instructions).Take(10).ToList();
        var heavy = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null).OrderByDescending(o => o.Cost).ThenBy(o => o.Name).Take(10).Select(o => new { objectId = o.Id, name = o.Name, ownerUserId = o.OwnerUserId, cost = o.Cost, physical = o.PhysicsJson.Contains("\"physical\"") }).ToListAsync(ct);
        var owners = new Dictionary<Guid, string>(); foreach (var id in heavy.Select(h => h.ownerUserId).Distinct()) owners[id] = await IslandAudit.NameAsync(db, id, ct);

        return Results.Ok(new
        {
            window, running = instance is not null, fresh,
            current = fresh ? new { fps = report!.Fps, processMs = report.ProcessMs, memoryMb = report.MemoryMb, tickRate = report.TickRate, players = report.Players.Length, maxPlayers = island.MaxPlayers, uptimeSeconds = report.UptimeSeconds, reportedAt = instance!.ReportedAt } : null,
            scripts = new { active = activeScripts, total = scriptRows.Count },
            series, summary = new { fps = Summary([.. samples.Select(m => m.Fps)]), processMs = Summary([.. samples.Select(m => m.ProcessMs)]), memoryMb = Summary([.. samples.Select(m => m.MemoryMb)]), players = Summary([.. samples.Select(m => (double)m.Players)]) },
            diagnostics = new { scripts, objects = heavy.Select(h => new { h.objectId, h.name, ownerName = owners[h.ownerUserId], h.cost, h.physical }) },
        });
    }
}
