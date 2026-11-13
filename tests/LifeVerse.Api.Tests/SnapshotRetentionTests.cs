using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>How long the automatic saves of an island are kept: the policy itself, and the pass that deletes.</summary>
public sealed class SnapshotRetentionTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly SnapshotRetentionOptions _options = new();
    private long _version;

    public void Dispose() => _db.Dispose();

    private SnapshotStub Stub(int daysAgo, bool named = false, int hours = 0) => new(Guid.NewGuid(), ++_version, Now.AddDays(-daysAgo).AddHours(-hours), named);

    [Fact]
    public void EverythingRecentIsKept()
    {
        var stubs = Enumerable.Range(0, 40).Select(i => Stub(i % 30)).ToList(); // 40 saves in 30 days
        Assert.Empty(SnapshotRetention.ToDelete(stubs, Now, _options));
    }

    [Fact]
    public void OlderThanThirtyDaysOnlyTheNewestOfEachWeekStays()
    {
        // 28 saves, two per day, over two weeks older than 30 days
        var week = Enumerable.Range(40, 14).SelectMany(d => new[] { Stub(d), Stub(d, hours: 3) }).ToList();
        var doomed = SnapshotRetention.ToDelete([.. week, .. Enumerable.Range(0, 6).Select(i => Stub(i))], Now, _options);
        var kept = week.Where(s => !doomed.Contains(s.Id)).ToList();
        Assert.InRange(kept.Count, 2, 3); // one per calendar week the window touches
        foreach (var group in kept.GroupBy(s => System.Globalization.ISOWeek.GetWeekOfYear(s.CreatedAt.UtcDateTime))) Assert.Single(group);
        Assert.True(kept.All(k => week.Where(w => System.Globalization.ISOWeek.GetWeekOfYear(w.CreatedAt.UtcDateTime) == System.Globalization.ISOWeek.GetWeekOfYear(k.CreatedAt.UtcDateTime)).Max(w => w.CreatedAt) == k.CreatedAt), "and it is the newest of its week");
    }

    [Fact]
    public void WhatIsOlderThanTwelveWeeksGoes_ExceptNamedBackupsAndTheNewestFive()
    {
        var named = Stub(200, named: true); var ancient = Enumerable.Range(0, 10).Select(i => Stub(100 + i)).ToList();
        var doomed = SnapshotRetention.ToDelete([named, .. ancient], Now, _options);
        Assert.DoesNotContain(named.Id, doomed); // the owner's own backups are theirs to delete
        Assert.Equal(5, doomed.Count); // the newest five of all (versions 6..11) stay even when they are old
        var newestFive = new[] { named }.Concat(ancient).OrderByDescending(s => s.Version).Take(5).Select(s => s.Id);
        Assert.Empty(doomed.Intersect(newestFive));
    }

    [Fact]
    public void AnIslandWithFewSavesKeepsThemAll_AndTheLatestIsNeverLost()
    {
        var few = Enumerable.Range(0, 4).Select(i => Stub(300 + i)).ToList();
        Assert.Empty(SnapshotRetention.ToDelete(few, Now, _options));
        var older = Enumerable.Range(0, 20).Select(i => Stub(400 + i)).ToList(); var latest = Stub(500); var doomed = SnapshotRetention.ToDelete([.. older, latest], Now, _options); // the newest by version, even if it is the oldest by date
        Assert.DoesNotContain(latest.Id, doomed); Assert.Empty(SnapshotRetention.ToDelete([], Now, _options));
        Assert.Equal(16, doomed.Count); // 21 saves, all ancient: the newest five stay
    }

    private Island Island(string slug) { var island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = slug, Slug = slug, IsPublic = true }; _db.Islands.Add(island); return island; }
    private void Save(Island island, int daysAgo, string? backup = null) => _db.IslandSnapshots.Add(new IslandSnapshot { Id = Guid.NewGuid(), IslandId = island.Id, Version = ++_version, TerrainJson = "{\"big\":true}", EnvironmentJson = "{}", IntegrityHash = "h", CreatedAt = Now.AddDays(-daysAgo), BackupName = backup, BackupAt = backup is null ? null : Now });

    [Fact]
    public async Task ThePassDeletesOnlyWhatThePolicySaysAndWritesAnAuditLine()
    {
        var crowded = Island("cheia"); var quiet = Island("calma");
        for (var i = 0; i < 12; i++) Save(crowded, 120 + i); // all very old
        Save(crowded, 150, backup: "Antes da reforma"); Save(crowded, 2); Save(crowded, 1);
        for (var i = 0; i < 3; i++) Save(quiet, 200 + i); // few saves, ancient: kept (only three)
        await _db.SaveChangesAsync();
        var deleted = await SnapshotRetention.RunAsync(_db, _options, Now, default);
        Assert.Equal(10, deleted); // 15 on the crowded island: the 10 oldest go; the newest 5 (two old ones, the named one and the two recent) stay
        var left = _db.IslandSnapshots.AsNoTracking().Where(s => s.IslandId == crowded.Id).ToList();
        Assert.Equal(5, left.Count); Assert.Contains(left, s => s.BackupName == "Antes da reforma");
        Assert.Equal(3, _db.IslandSnapshots.AsNoTracking().Count(s => s.IslandId == quiet.Id));
        var audit = Assert.Single(_db.IslandAudit.AsNoTracking().Where(a => a.Action == "snapshots_pruned").ToList()); Assert.Equal(crowded.Id, audit.IslandId);
        Assert.Equal(0, await SnapshotRetention.RunAsync(_db, _options, Now, default)); // nothing more to let go
    }

    [Fact]
    public async Task DryRunAndDisabledDeleteNothing()
    {
        var island = Island("velha"); for (var i = 0; i < 12; i++) Save(island, 200 + i); await _db.SaveChangesAsync();
        Assert.Equal(7, await SnapshotRetention.RunAsync(_db, new SnapshotRetentionOptions { DryRun = true }, Now, default)); // says what it would do
        Assert.Equal(12, _db.IslandSnapshots.AsNoTracking().Count()); Assert.Empty(_db.IslandAudit);
        Assert.Equal(0, await SnapshotRetention.RunAsync(_db, new SnapshotRetentionOptions { Enabled = false }, Now, default));
        Assert.Equal(12, _db.IslandSnapshots.AsNoTracking().Count());
    }
}
