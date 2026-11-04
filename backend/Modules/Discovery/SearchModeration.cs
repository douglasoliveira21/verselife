using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public sealed record ReportResultRequest(string? Reason);
public sealed record SearchEventRequest(string? Type, string? Query);
public sealed record ResolveReportRequest(string? Action);
public sealed record ReportedResult(string Kind, Guid Id, string Title, int Reports, bool Hidden, DateTimeOffset LastReportAt, IReadOnlyList<string> Reasons);
public sealed record QueryStat(string Query, int Searches, int ZeroResults, int Opens, int Teleports);
public sealed record MetricsReport(int Days, int Searches, int ZeroResults, int Opens, int Teleports, double AverageMs, IReadOnlyList<QueryStat> Top, IReadOnlyList<QueryStat> NoResults);

/// <summary>
/// Reporting a search result and handling those reports. A person can report a result once; many different people reporting it hides it from the search
/// until somebody from the operation looks (and either keeps it hidden or puts it back). Hiding only takes the line out of the search, it never deletes anything.
/// </summary>
public static class SearchModeration
{
    public const int AutoHideAt = 5, DailyReportLimit = 20, MinReasonLength = 3, MaxReasonLength = 500;

    public static async Task<(bool Ok, string? Error)> ReportAsync(LifeVerseDbContext db, Guid reporter, string kind, Guid refId, string? reason, DateTimeOffset now, CancellationToken ct)
    {
        kind = SearchKinds.All.FirstOrDefault(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        if (kind.Length == 0) return (false, "unknown_kind");
        var text = new string([.. (reason ?? string.Empty).Where(c => !char.IsControl(c) || c == ' ')]).Trim(); if (text.Length > MaxReasonLength) text = text[..MaxReasonLength];
        if (text.Length < MinReasonLength) return (false, "reason_required");
        var entry = await db.SearchEntries.FirstOrDefaultAsync(e => e.Kind == kind && e.RefId == refId, ct);
        if (entry is null || (await GlobalSearch.AllowedAsync(db, reporter, [entry], now, ct)).Count == 0) return (false, "not_found"); // only what the person could see can be reported
        if (entry.OwnerUserId == reporter || (kind == SearchKinds.Person && refId == reporter)) return (false, "own_item");
        if (await db.SearchReports.AnyAsync(r => r.ReporterUserId == reporter && r.Kind == kind && r.RefId == refId, ct)) return (true, null); // already reported: nothing more to do
        var since = now.AddDays(-1); if (await db.SearchReports.CountAsync(r => r.ReporterUserId == reporter && r.CreatedAt >= since, ct) >= DailyReportLimit) return (false, "too_many_reports");
        db.SearchReports.Add(new SearchReport { Id = Guid.NewGuid(), ReporterUserId = reporter, Kind = kind, RefId = refId, Title = entry.Title, Reason = text, Status = "open", CreatedAt = now });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return (true, null); /* the same report arrived twice at once */ }
        if (!entry.Hidden && await db.SearchReports.CountAsync(r => r.Kind == kind && r.RefId == refId && r.Status == "open", ct) >= AutoHideAt) { entry.Hidden = true; await db.SaveChangesAsync(ct); }
        return (true, null);
    }

    /// <summary>What is waiting for a decision, the most reported first.</summary>
    public static async Task<List<ReportedResult>> OpenAsync(LifeVerseDbContext db, int take, CancellationToken ct)
    {
        var open = await db.SearchReports.AsNoTracking().Where(r => r.Status == "open").ToListAsync(ct);
        var hidden = (await db.SearchEntries.AsNoTracking().Where(e => e.Hidden).Select(e => new { e.Kind, e.RefId }).ToListAsync(ct)).Select(e => (e.Kind, e.RefId)).ToHashSet();
        return [.. open.GroupBy(r => (r.Kind, r.RefId)).Select(g => new ReportedResult(g.Key.Kind, g.Key.RefId, g.First().Title, g.Count(), hidden.Contains(g.Key), g.Max(r => r.CreatedAt), [.. g.OrderByDescending(r => r.CreatedAt).Take(5).Select(r => r.Reason)]))
            .OrderByDescending(r => r.Reports).ThenByDescending(r => r.LastReportAt).Take(Math.Clamp(take, 1, 200))];
    }

    /// <summary>"hide" keeps the result out of the search; "dismiss" puts it back (the reports were not right). Either way the reports are closed.</summary>
    public static async Task<bool> ResolveAsync(LifeVerseDbContext db, string kind, Guid refId, string? action, DateTimeOffset now, CancellationToken ct)
    {
        var hide = string.Equals(action, "hide", StringComparison.OrdinalIgnoreCase); if (!hide && !string.Equals(action, "dismiss", StringComparison.OrdinalIgnoreCase)) return false;
        kind = kind.ToLowerInvariant(); var reports = await db.SearchReports.Where(r => r.Kind == kind && r.RefId == refId && r.Status == "open").ToListAsync(ct);
        foreach (var report in reports) { report.Status = hide ? "hidden" : "dismissed"; report.ResolvedAt = now; }
        foreach (var entry in await db.SearchEntries.Where(e => e.Kind == kind && e.RefId == refId).ToListAsync(ct)) entry.Hidden = hide;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// Counts of how the search is used, per day and per search text, with nobody's name on them. Kept in memory and written to the database in batches,
/// so searching does not write on every request. Search texts are stored normalized and only when they are real searches.
/// </summary>
public sealed class SearchMetrics
{
    private sealed class Counter { public long Searches, ZeroResults, Opens, Teleports, Milliseconds; }
    private const int MaxDistinct = 5000;
    private readonly ConcurrentDictionary<(DateOnly Day, string Query), Counter> _pending = new();

    private Counter? For(DateTimeOffset now, string? query)
    {
        var key = SearchText.Normalize(query is { Length: > SearchText.MaxQueryLength } ? query[..SearchText.MaxQueryLength] : query); if (key.Length < GlobalSearch.MinQueryLength) return null;
        var id = (DateOnly.FromDateTime(now.UtcDateTime), key);
        if (!_pending.ContainsKey(id) && _pending.Count >= MaxDistinct) return null; // a flood of different texts cannot fill the memory
        return _pending.GetOrAdd(id, _ => new Counter());
    }

    public void Searched(string? query, int results, long milliseconds, DateTimeOffset now)
    {
        if (For(now, query) is not { } c) return;
        Interlocked.Increment(ref c.Searches); Interlocked.Add(ref c.Milliseconds, Math.Max(0, milliseconds)); if (results == 0) Interlocked.Increment(ref c.ZeroResults);
    }

    /// <summary>A result was opened ("open") or somebody went there ("teleport"). False when the type is not known.</summary>
    public bool Used(string? type, string? query, DateTimeOffset now)
    {
        if (type is not ("open" or "teleport")) return false;
        if (For(now, query) is { } c) Interlocked.Increment(ref type == "open" ? ref c.Opens : ref c.Teleports);
        return true;
    }

    /// <summary>Writes what was counted so far and starts counting again; what cannot be written is kept for the next time.</summary>
    public async Task FlushAsync(LifeVerseDbContext db, CancellationToken ct)
    {
        var taken = new List<((DateOnly Day, string Query) Key, Counter Value)>();
        foreach (var key in _pending.Keys.ToList()) if (_pending.TryRemove(key, out var value)) taken.Add((key, value));
        if (taken.Count == 0) return;
        try
        {
            var days = taken.Select(t => t.Key.Day).Distinct().ToList(); var existing = await db.SearchStats.Where(s => days.Contains(s.Day)).ToListAsync(ct);
            foreach (var (key, v) in taken)
            {
                var row = existing.FirstOrDefault(s => s.Day == key.Day && s.Query == key.Query);
                if (row is null) { row = new SearchStat { Day = key.Day, Query = key.Query }; db.SearchStats.Add(row); existing.Add(row); }
                row.Searches += (int)v.Searches; row.ZeroResults += (int)v.ZeroResults; row.Opens += (int)v.Opens; row.Teleports += (int)v.Teleports; row.TotalMs += v.Milliseconds;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            foreach (var (key, v) in taken) { var c = _pending.GetOrAdd(key, _ => new Counter()); Interlocked.Add(ref c.Searches, v.Searches); Interlocked.Add(ref c.ZeroResults, v.ZeroResults); Interlocked.Add(ref c.Opens, v.Opens); Interlocked.Add(ref c.Teleports, v.Teleports); Interlocked.Add(ref c.Milliseconds, v.Milliseconds); }
        }
    }

    /// <summary>What happened in the last days: totals, the searches people make most, and the ones that found nothing.</summary>
    public static async Task<MetricsReport> ReportAsync(LifeVerseDbContext db, int days, DateTimeOffset now, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 90); var from = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-(days - 1)));
        var rows = await db.SearchStats.AsNoTracking().Where(s => s.Day >= from).ToListAsync(ct);
        var perQuery = rows.GroupBy(r => r.Query).Select(g => new QueryStat(g.Key, g.Sum(r => r.Searches), g.Sum(r => r.ZeroResults), g.Sum(r => r.Opens), g.Sum(r => r.Teleports))).ToList();
        var searches = rows.Sum(r => r.Searches);
        return new MetricsReport(days, searches, rows.Sum(r => r.ZeroResults), rows.Sum(r => r.Opens), rows.Sum(r => r.Teleports), searches == 0 ? 0 : Math.Round((double)rows.Sum(r => r.TotalMs) / searches, 1),
            [.. perQuery.OrderByDescending(q => q.Searches).ThenBy(q => q.Query, StringComparer.Ordinal).Take(20)],
            [.. perQuery.Where(q => q.ZeroResults > 0).OrderByDescending(q => q.ZeroResults).ThenBy(q => q.Query, StringComparer.Ordinal).Take(20)]);
    }
}

public static class SearchModerationEndpoints
{
    public static IEndpointRouteBuilder MapSearchModerationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/search").WithTags("Search").RequireAuthorization();
        group.MapPost("/report/{kind}/{id:guid}", async (string kind, Guid id, ReportResultRequest body, HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
        {
            if (!Guid.TryParse(c.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var me)) return Results.Unauthorized();
            var (ok, error) = await SearchModeration.ReportAsync(db, me, kind, id, body.Reason, t.GetUtcNow(), ct);
            return ok ? Results.Accepted() : error switch { "not_found" => Results.NotFound(new { error }), "too_many_reports" => Results.StatusCode(429), _ => Results.BadRequest(new { error }) };
        });
        group.MapPost("/events", (SearchEventRequest body, SearchMetrics metrics, TimeProvider t) => metrics.Used(body.Type, body.Query, t.GetUtcNow()) ? Results.NoContent() : Results.BadRequest(new { error = "unknown_type" }));
        var platform = endpoints.MapGroup("/api/v1/platform/search").WithTags("Platform");
        platform.MapGet("/reports", async (int? take, HttpContext c, IConfiguration config, LifeVerseDbContext db, CancellationToken ct) =>
            !Platform.PlatformEndpoints.HasValidServiceKey(c, config) ? Results.Unauthorized() : Results.Ok(await SearchModeration.OpenAsync(db, take ?? 50, ct)));
        platform.MapPost("/reports/{kind}/{id:guid}/resolve", async (string kind, Guid id, ResolveReportRequest body, HttpContext c, IConfiguration config, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) =>
            !Platform.PlatformEndpoints.HasValidServiceKey(c, config) ? Results.Unauthorized() : await SearchModeration.ResolveAsync(db, kind, id, body.Action, t.GetUtcNow(), ct) ? Results.NoContent() : Results.BadRequest(new { error = "unknown_action" }));
        platform.MapGet("/metrics", async (int? days, HttpContext c, IConfiguration config, LifeVerseDbContext db, SearchMetrics metrics, TimeProvider t, CancellationToken ct) =>
        {
            if (!Platform.PlatformEndpoints.HasValidServiceKey(c, config)) return Results.Unauthorized();
            await metrics.FlushAsync(db, ct); return Results.Ok(await SearchMetrics.ReportAsync(db, days ?? 7, t.GetUtcNow(), ct));
        });
        return endpoints;
    }
}
