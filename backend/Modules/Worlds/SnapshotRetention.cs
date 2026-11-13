using System.Globalization;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// How long the automatic saves of an island are kept. The defaults: everything from the last 30 days, then one per week up to 12 weeks, and never fewer than the 5 newest.
/// Saves the owner named (backups) are never touched. Set <c>Enabled</c> to false to keep everything, or <c>DryRun</c> to only log what would be deleted.
/// </summary>
public sealed class SnapshotRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; }
    /// <summary>Every save younger than this is kept.</summary>
    public int KeepAllDays { get; set; } = 30;
    /// <summary>After that, the newest save of each week is kept until this age; older saves are deleted.</summary>
    public int KeepWeeklyUntilDays { get; set; } = 84;
    /// <summary>The newest saves of an island are always kept, whatever their age.</summary>
    public int KeepNewest { get; set; } = 5;
    /// <summary>How many islands one pass looks at (the rest wait for the next pass).</summary>
    public int IslandsPerPass { get; set; } = 50;
}

/// <summary>What the policy needs to know about a save (not the save itself, which is large).</summary>
public sealed record SnapshotStub(Guid Id, long Version, DateTimeOffset CreatedAt, bool Named);

public static class SnapshotRetention
{
    /// <summary>The saves of one island that the policy lets go. Pure: nothing is read or deleted here.</summary>
    public static IReadOnlyList<Guid> ToDelete(IReadOnlyCollection<SnapshotStub> snapshots, DateTimeOffset now, SnapshotRetentionOptions options)
    {
        var keep = new HashSet<Guid>();
        foreach (var named in snapshots.Where(s => s.Named)) keep.Add(named.Id);
        foreach (var newest in snapshots.OrderByDescending(s => s.Version).Take(Math.Max(1, options.KeepNewest))) keep.Add(newest.Id);
        var recentEdge = now.AddDays(-options.KeepAllDays); var weeklyEdge = now.AddDays(-options.KeepWeeklyUntilDays);
        foreach (var recent in snapshots.Where(s => s.CreatedAt >= recentEdge)) keep.Add(recent.Id);
        // the weekly ones: the newest of each calendar week between the two edges
        foreach (var week in snapshots.Where(s => s.CreatedAt < recentEdge && s.CreatedAt >= weeklyEdge).GroupBy(s => (ISOWeek.GetYear(s.CreatedAt.UtcDateTime), ISOWeek.GetWeekOfYear(s.CreatedAt.UtcDateTime))))
            keep.Add(week.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Version).First().Id);
        return [.. snapshots.Where(s => !keep.Contains(s.Id)).Select(s => s.Id)];
    }

    /// <summary>One pass: deletes the saves the policy lets go of the islands that have more than the newest few. Returns how many were (or, in a dry run, would be) deleted.</summary>
    public static async Task<int> RunAsync(LifeVerseDbContext db, SnapshotRetentionOptions options, DateTimeOffset now, CancellationToken ct)
    {
        if (!options.Enabled) return 0;
        var crowded = await db.IslandSnapshots.AsNoTracking().GroupBy(s => s.IslandId).Where(g => g.Count() > options.KeepNewest).Select(g => g.Key).Take(Math.Max(1, options.IslandsPerPass)).ToListAsync(ct);
        var total = 0;
        foreach (var islandId in crowded)
        {
            var stubs = await db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == islandId).Select(s => new SnapshotStub(s.Id, s.Version, s.CreatedAt, s.BackupName != null)).ToListAsync(ct);
            var doomed = ToDelete(stubs, now, options); if (doomed.Count == 0) continue;
            total += doomed.Count; if (options.DryRun) continue;
            var tracked = db.ChangeTracker.Entries<IslandSnapshot>().ToDictionary(e => e.Entity.Id, e => e.Entity);
            foreach (var id in doomed) db.IslandSnapshots.Remove(tracked.TryGetValue(id, out var loaded) ? loaded : new IslandSnapshot { Id = id, IslandId = islandId, TerrainJson = string.Empty, EnvironmentJson = string.Empty, IntegrityHash = string.Empty }); // by key: the large text is not read just to be deleted
            IslandAudit.Add(db, islandId, Guid.Empty, "sistema", "snapshots_pruned", $"{doomed.Count} salvamento(s) automático(s) antigo(s) foram apagados", "Backups", new { before = stubs.Count }, new { deleted = doomed.Count, kept = stubs.Count - doomed.Count }, now);
            await db.SaveChangesAsync(ct);
        }
        return total;
    }
}

/// <summary>Runs <see cref="SnapshotRetention.RunAsync"/> a few minutes after start and then every six hours.</summary>
public sealed class SnapshotRetentionWorker(IServiceScopeFactory scopes, SnapshotRetentionOptions options, ILogger<SnapshotRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) { logger.LogInformation("event=snapshot_retention_off"); return; }
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var count = await SnapshotRetention.RunAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), options, DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0) logger.LogInformation("event=snapshot_retention count={Count} dry_run={DryRun}", count, options.DryRun);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=snapshot_retention_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
