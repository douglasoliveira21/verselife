using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Reporting search results and the usage numbers of the search.</summary>
public sealed class SearchPhase3Tests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly User _owner;

    public SearchPhase3Tests() { _owner = NewUser("dono"); }
    public void Dispose() => _db.Dispose();

    private User NewUser(string username)
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PrivacyJson = "{\"presence\":\"friends\"}" };
        _db.Users.Add(u); _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = u.Id, DisplayName = username, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return u;
    }

    private async Task<Island> IndexedIsland(string name)
    {
        var i = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner.Id, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = true, Description = "", Category = "social", CreatedAt = _now, UpdatedAt = _now };
        _db.Islands.Add(i); await _db.SaveChangesAsync(); await SearchIndexer.RunAsync(_db, _now, default); return i;
    }

    private Task<(bool Ok, string? Error)> Report(User who, Island island, string? reason = "conteudo improprio") => SearchModeration.ReportAsync(_db, who.Id, "island", island.Id, reason, _now, default);
    private async Task<bool> IsHidden(Island island) => (await _db.SearchEntries.SingleAsync(e => e.RefId == island.Id)).Hidden;

    // ---- reports ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AReportIsKeptOnceAndAskedForAReason()
    {
        var island = await IndexedIsland("Ilha Duvidosa"); var me = NewUser("eu");
        Assert.Equal((false, "reason_required"), await Report(me, island, " a ")); Assert.Equal((false, "reason_required"), await Report(me, island, null));
        Assert.Equal((false, "unknown_kind"), await SearchModeration.ReportAsync(_db, me.Id, "banana", island.Id, "motivo", _now, default));
        Assert.True((await Report(me, island)).Ok); Assert.True((await Report(me, island, "outra vez")).Ok); // the second one changes nothing
        var saved = await _db.SearchReports.SingleAsync(); Assert.Equal("open", saved.Status); Assert.Equal("Ilha Duvidosa", saved.Title); Assert.Equal("conteudo improprio", saved.Reason);
        Assert.False(await IsHidden(island)); // one report does not hide anything
    }

    [Fact]
    public async Task OnlyWhatCanBeSeenCanBeReportedAndNotYourOwn()
    {
        var island = await IndexedIsland("Ilha Fechada Depois"); var me = NewUser("eu");
        Assert.Equal((false, "not_found"), await SearchModeration.ReportAsync(_db, me.Id, "island", Guid.NewGuid(), "motivo", _now, default));
        Assert.Equal((false, "own_item"), await Report(_owner, island));
        (await _db.Islands.SingleAsync(i => i.Id == island.Id)).IsPublic = false; await _db.SaveChangesAsync();
        Assert.Equal((false, "not_found"), await Report(me, island)); // already private: cannot be seen, so cannot be reported
        Assert.Empty(_db.SearchReports);
    }

    [Fact]
    public async Task ManyDifferentPeopleHideTheResultUntilSomebodyDecides()
    {
        var island = await IndexedIsland("Ilha Denunciada");
        for (var i = 0; i < SearchModeration.AutoHideAt - 1; i++) Assert.True((await Report(NewUser("r" + i), island)).Ok);
        Assert.False(await IsHidden(island));
        Assert.True((await Report(NewUser("ultimo"), island)).Ok); Assert.True(await IsHidden(island));
        var viewer = NewUser("eu"); Assert.Empty((await GlobalSearch.SearchAsync(_db, viewer.Id, new SearchRequest("denunciada"), _now, default)).Items);
        await SearchIndexer.RunAsync(_db, _now.AddMinutes(5), default); Assert.True(await IsHidden(island)); // the index does not bring it back
        var waiting = await SearchModeration.OpenAsync(_db, 10, default); var line = Assert.Single(waiting); Assert.Equal((5, true, "Ilha Denunciada"), (line.Reports, line.Hidden, line.Title));
        Assert.True(await SearchModeration.ResolveAsync(_db, "island", island.Id, "dismiss", _now, default));
        Assert.False(await IsHidden(island)); Assert.Empty(await SearchModeration.OpenAsync(_db, 10, default)); Assert.All(_db.SearchReports, r => Assert.Equal("dismissed", r.Status));
        Assert.Single((await GlobalSearch.SearchAsync(_db, viewer.Id, new SearchRequest("denunciada"), _now, default)).Items); // put back
    }

    [Fact]
    public async Task TheOperatorCanKeepAResultHiddenAndUnknownActionsDoNothing()
    {
        var island = await IndexedIsland("Ilha Para Esconder"); await Report(NewUser("a"), island);
        Assert.False(await SearchModeration.ResolveAsync(_db, "island", island.Id, "apagar tudo", _now, default)); Assert.False(await IsHidden(island));
        Assert.True(await SearchModeration.ResolveAsync(_db, "ISLAND", island.Id, "hide", _now, default)); Assert.True(await IsHidden(island));
        Assert.Equal("hidden", (await _db.SearchReports.SingleAsync()).Status); Assert.NotNull((await _db.SearchReports.SingleAsync()).ResolvedAt);
    }

    [Fact]
    public async Task APersonCannotFloodTheQueueWithReports()
    {
        var spammer = NewUser("spammer");
        for (var i = 0; i < SearchModeration.DailyReportLimit; i++) Assert.True((await Report(spammer, await IndexedIsland("Ilha " + i + " Alvo"))).Ok);
        Assert.Equal((false, "too_many_reports"), await Report(spammer, await IndexedIsland("Ilha Extra Alvo")));
        Assert.True((await SearchModeration.ReportAsync(_db, spammer.Id, "island", (await _db.Islands.FirstAsync()).Id, "motivo", _now.AddDays(2), default)).Ok); // already reported: still fine
    }

    // ---- metrics ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UsageIsCountedPerDayAndSearchTextWithoutNamesAndWrittenInBatches()
    {
        var metrics = new SearchMetrics();
        metrics.Searched("Praia", 5, 20, _now); metrics.Searched("praia!", 3, 40, _now); metrics.Searched("nada aqui", 0, 10, _now); metrics.Searched("x", 4, 5, _now); metrics.Searched(null, 4, 5, _now); // too short: not counted
        Assert.True(metrics.Used("open", "praia", _now)); Assert.True(metrics.Used("teleport", "Praia", _now)); Assert.False(metrics.Used("hack", "praia", _now));
        await metrics.FlushAsync(_db, default);
        var praia = await _db.SearchStats.SingleAsync(s => s.Query == "praia"); Assert.Equal((2, 0, 1, 1, 60), (praia.Searches, praia.ZeroResults, praia.Opens, praia.Teleports, (int)praia.TotalMs));
        Assert.Equal(1, (await _db.SearchStats.SingleAsync(s => s.Query == "nada aqui")).ZeroResults); Assert.Equal(2, await _db.SearchStats.CountAsync());
        metrics.Searched("praia", 1, 10, _now); await metrics.FlushAsync(_db, default); // a second batch adds to the same line
        Assert.Equal(3, (await _db.SearchStats.SingleAsync(s => s.Query == "praia")).Searches); Assert.Equal(2, await _db.SearchStats.CountAsync());
    }

    [Fact]
    public async Task TheReportShowsTotalsTheTopSearchesAndTheOnesThatFoundNothing()
    {
        var metrics = new SearchMetrics();
        for (var i = 0; i < 3; i++) metrics.Searched("praia", 4, 30, _now);
        metrics.Searched("unicornio", 0, 10, _now); metrics.Searched("unicornio", 0, 10, _now); metrics.Searched("festa", 1, 20, _now.AddDays(-2)); metrics.Searched("antiga", 1, 20, _now.AddDays(-30));
        metrics.Used("teleport", "praia", _now); await metrics.FlushAsync(_db, default);
        var week = await SearchMetrics.ReportAsync(_db, 7, _now, default);
        Assert.Equal((6, 2, 0, 1), (week.Searches, week.ZeroResults, week.Opens, week.Teleports)); Assert.Equal(["praia", "unicornio", "festa"], week.Top.Select(q => q.Query));
        Assert.Equal(["unicornio"], week.NoResults.Select(q => q.Query)); Assert.Equal(21.7, week.AverageMs); // (3 x 30 + 2 x 10 + 20) ms over 6 searches
        Assert.Equal(7, (await SearchMetrics.ReportAsync(_db, 90, _now, default)).Searches); // the old one counts in a longer window
        Assert.Equal(0, (await SearchMetrics.ReportAsync(new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options), 7, _now, default)).Searches);
    }

    [Fact]
    public async Task AFloodOfDifferentSearchTextsCannotFillTheMemory()
    {
        var metrics = new SearchMetrics();
        for (var i = 0; i < 6000; i++) metrics.Searched("busca " + i, 1, 1, _now);
        await metrics.FlushAsync(_db, default);
        Assert.InRange(await _db.SearchStats.CountAsync(), 1, 5000);
    }
}
