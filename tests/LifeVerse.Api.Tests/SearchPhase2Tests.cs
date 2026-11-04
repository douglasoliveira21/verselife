using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Search filters, ordering, favorites, interests and recommendations.</summary>
public sealed class SearchPhase2Tests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly User _me, _ana;

    public SearchPhase2Tests() { _me = NewUser("eu"); _ana = NewUser("ana"); }
    public void Dispose() => _db.Dispose();

    private User NewUser(string username)
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PrivacyJson = "{\"presence\":\"friends\"}" };
        _db.Users.Add(u); _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = u.Id, DisplayName = username, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return u;
    }

    private Island NewIsland(string name, string category, TimeSpan age, bool isPublic = true)
    {
        var at = _now - age; var i = new Island { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, Description = "", Category = category, CreatedAt = at, UpdatedAt = at };
        _db.Islands.Add(i); _db.SaveChanges(); return i;
    }

    private async Task<SearchPage> Find(SearchRequest request, User? who = null) { await SearchIndexer.RunAsync(_db, _now, default); return await GlobalSearch.SearchAsync(_db, (who ?? _me).Id, request, _now, default); }

    // ---- filters and ordering -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheCategoryFilterKeepsOnlyThatCategoryAndTheCountsListTheCategoriesFound()
    {
        NewIsland("Mar Azul", "social", TimeSpan.Zero); NewIsland("Mar Verde", "negocios", TimeSpan.Zero); NewIsland("Mar Roxo", "social", TimeSpan.Zero);
        var all = await Find(new SearchRequest("mar", ["island"]));
        Assert.Equal(3, all.Total); Assert.Equal(2, all.Categories!["social"]); Assert.Equal(1, all.Categories["negocios"]);
        var only = await Find(new SearchRequest("mar", ["island"], Category: "SOCIAL")); // not case sensitive
        Assert.Equal(2, only.Total); Assert.All(only.Items, i => Assert.Equal("social", i.Category));
        Assert.Equal(0, (await Find(new SearchRequest("mar", ["island"], Category: "nada"))).Total);
    }

    [Fact]
    public async Task TheIslandFilterKeepsOnlyWhatIsInThatIsland()
    {
        var a = NewIsland("Porto Norte", "social", TimeSpan.Zero); var b = NewIsland("Porto Sul", "social", TimeSpan.Zero);
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = a.Id, Name = "Porto Farol", IsPublic = true, CreatedAt = _now, UpdatedAt = _now });
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = b.Id, Name = "Porto Mercado", IsPublic = true, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var here = await Find(new SearchRequest("porto", ["poi"], IslandId: a.Id));
        Assert.Equal(["Porto Farol"], here.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task TheOrderCanBeRelevancePopularityOrRecentChange()
    {
        var exact = NewIsland("Lago", "social", TimeSpan.FromDays(100)); var popular = NewIsland("Lago Grande Famoso", "social", TimeSpan.FromDays(60)); var fresh = NewIsland("Lago Novo Pequeno", "social", TimeSpan.FromDays(1));
        for (var i = 0; i < 4; i++) _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = NewUser("fa" + i).Id, IslandId = popular.Id, CreatedAt = _now }); await _db.SaveChangesAsync(); // what makes an island popular
        Assert.Equal("Lago", (await Find(new SearchRequest("lago", ["island"]))).Items[0].Title); // the whole title is the query
        var byPopularity = await Find(new SearchRequest("lago", ["island"], Sort: "popularity")); Assert.Equal("Lago Grande Famoso", byPopularity.Items[0].Title); Assert.Equal("popularity", byPopularity.Sort);
        var byUpdate = await Find(new SearchRequest("lago", ["island"], Sort: "updated")); Assert.Equal(["Lago Novo Pequeno", "Lago Grande Famoso", "Lago"], byUpdate.Items.Select(i => i.Title));
        Assert.Equal("relevance", (await Find(new SearchRequest("lago", ["island"], Sort: "qualquer coisa"))).Sort); // unknown means by relevance
        Assert.Equal(popular.Id, (await Find(new SearchRequest("lago", ["island"], Sort: "popular"))).Items[0].Id); // short names are accepted too
        _ = exact; _ = fresh;
    }

    [Fact]
    public async Task PagingKeepsTheChosenOrder()
    {
        for (var i = 0; i < 5; i++) NewIsland($"Serra {i}", "social", TimeSpan.FromDays(i));
        var first = await Find(new SearchRequest("serra", ["island"], 1, 2, Sort: "updated")); var second = await Find(new SearchRequest("serra", ["island"], 2, 2, Sort: "updated"));
        Assert.Equal(["Serra 0", "Serra 1"], first.Items.Select(i => i.Title)); Assert.Equal(["Serra 2", "Serra 3"], second.Items.Select(i => i.Title)); Assert.Equal(5, second.Total);
    }

    // ---- favorites ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFavoriteIsMarkedInTheResultsAndListedLastFirst()
    {
        var a = NewIsland("Ponte Alta", "social", TimeSpan.Zero); var b = NewIsland("Ponte Baixa", "social", TimeSpan.Zero); await SearchIndexer.RunAsync(_db, _now, default);
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", a.Id, _now, default)).Ok);
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "ISLAND", b.Id, _now.AddMinutes(1), default)).Ok);
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", b.Id, _now.AddMinutes(2), default)).Ok); // again: nothing changes
        Assert.Equal(2, await _db.SearchFavorites.CountAsync());
        var results = await GlobalSearch.SearchAsync(_db, _me.Id, new SearchRequest("ponte", ["island"]), _now, default); Assert.All(results.Items, i => Assert.True(i.Favorite));
        Assert.Equal(["Ponte Baixa", "Ponte Alta"], (await GlobalSearch.FavoritesAsync(_db, _me.Id, null, 1, 20, _now, default)).Items.Select(i => i.Title));
        Assert.DoesNotContain(await GlobalSearch.SearchAsync(_db, _ana.Id, new SearchRequest("ponte", ["island"]), _now, default) is { } p ? p.Items : [], i => i.Favorite); // favorites belong to the person
        await GlobalSearch.RemoveFavoriteAsync(_db, _me.Id, "island", a.Id, default);
        Assert.Equal(["Ponte Baixa"], (await GlobalSearch.FavoritesAsync(_db, _me.Id, null, 1, 20, _now, default)).Items.Select(i => i.Title));
    }

    [Fact]
    public async Task OnlyWhatCanBeSeenCanBeMadeFavoriteAndWhatStopsBeingSeenLeavesTheList()
    {
        var closed = NewIsland("Cais Fechado", "social", TimeSpan.Zero, isPublic: false); var open = NewIsland("Cais Aberto", "social", TimeSpan.Zero); await SearchIndexer.RunAsync(_db, _now, default);
        Assert.Equal((false, "not_found"), await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", closed.Id, _now, default));
        Assert.Equal((false, "not_found"), await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", Guid.NewGuid(), _now, default));
        Assert.Equal((false, "unknown_kind"), await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "banana", open.Id, _now, default));
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", open.Id, _now, default)).Ok);
        open.IsPublic = false; await _db.SaveChangesAsync(); // the owner closed it: gone from the list at once, even before the index catches up
        Assert.Empty((await GlobalSearch.FavoritesAsync(_db, _me.Id, null, 1, 20, _now, default)).Items);
        open.IsPublic = true; await _db.SaveChangesAsync(); Assert.Single((await GlobalSearch.FavoritesAsync(_db, _me.Id, null, 1, 20, _now, default)).Items); // and back when it is open again
    }

    [Fact]
    public async Task ModeratedOrBlockedThingsCannotBeFavoritedOrListed()
    {
        var island = NewIsland("Doca Escondida", "social", TimeSpan.Zero); await SearchIndexer.RunAsync(_db, _now, default);
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", island.Id, _now, default)).Ok);
        foreach (var e in _db.SearchEntries.Where(e => e.RefId == island.Id)) e.Hidden = true; await _db.SaveChangesAsync();
        Assert.Empty((await GlobalSearch.FavoritesAsync(_db, _me.Id, null, 1, 20, _now, default)).Items); // moderation hides it from the list as well
        Assert.Equal((false, "not_found"), await GlobalSearch.AddFavoriteAsync(_db, _ana.Id, "island", island.Id, _now, default));
    }

    [Fact]
    public async Task TheFavoritesListCanBeFilteredByKindAndPaged()
    {
        for (var i = 0; i < 3; i++) NewIsland($"Baia {i}", "social", TimeSpan.Zero);
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = _db.Islands.First().Id, Name = "Baia Mirante", IsPublic = true, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); await SearchIndexer.RunAsync(_db, _now, default);
        var n = 0; foreach (var e in await _db.SearchEntries.Where(e => e.Title.StartsWith("Baia")).ToListAsync()) Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, e.Kind, e.RefId, _now.AddSeconds(n++), default)).Ok);
        var islands = await GlobalSearch.FavoritesAsync(_db, _me.Id, ["island"], 1, 2, _now, default); Assert.Equal(3, islands.Total); Assert.Equal(2, islands.Items.Count);
        Assert.Single((await GlobalSearch.FavoritesAsync(_db, _me.Id, ["island"], 2, 2, _now, default)).Items);
        Assert.Equal(["poi"], (await GlobalSearch.FavoritesAsync(_db, _me.Id, ["poi"], 1, 20, _now, default)).Items.Select(i => i.Kind));
    }

    // ---- interests and recommendations --------------------------------------------------------------------------------------------

    [Fact]
    public async Task InterestsAreCleanedDeduplicatedAndLimited()
    {
        var saved = await GlobalSearch.SetInterestsAsync(_db, _me.Id, ["  Música!! ", "musica", "x", "", null, "pesca esportiva em alto mar agora", "Arte"], _now, default);
        Assert.Equal(["musica", "pesca esportiva em", "arte"], saved);
        Assert.Equal(saved, await GlobalSearch.InterestsAsync(_db, _me.Id, default));
        Assert.Empty(await GlobalSearch.InterestsAsync(_db, _ana.Id, default)); // each person has their own
        Assert.Equal(["jogos"], await GlobalSearch.SetInterestsAsync(_db, _me.Id, ["jogos"], _now, default)); // saving replaces
        Assert.Equal(GlobalSearch.MaxInterests, (await GlobalSearch.SetInterestsAsync(_db, _me.Id, Enumerable.Range(0, 50).Select(i => "tema " + i), _now, default)).Count);
        Assert.Empty(await GlobalSearch.SetInterestsAsync(_db, _me.Id, [], _now, default));
    }

    [Fact]
    public async Task RecommendationsFollowTheInterestsAndPutWhatMatchesMoreFirst()
    {
        NewIsland("Festival de Musica", "social", TimeSpan.Zero); NewIsland("Estudio de Musica e Arte", "social", TimeSpan.Zero); NewIsland("Oficina Mecanica", "negocios", TimeSpan.Zero);
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Amantes de Arte", NameKey = "arte", Privacy = "public", Description = "", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); await SearchIndexer.RunAsync(_db, _now, default);
        Assert.Empty(await GlobalSearch.RecommendationsAsync(_db, _me.Id, 10, _now, default)); // no interests, nothing to recommend
        await GlobalSearch.SetInterestsAsync(_db, _me.Id, ["musica", "arte"], _now, default);
        var items = await GlobalSearch.RecommendationsAsync(_db, _me.Id, 10, _now, default);
        Assert.Equal("Estudio de Musica e Arte", items[0].Title); // matches both interests
        Assert.Equal(new[] { "Amantes de Arte", "Estudio de Musica e Arte", "Festival de Musica" }, items.Select(i => i.Title).Order());
        Assert.DoesNotContain(items, i => i.Title == "Oficina Mecanica" || i.Kind == "person");
    }

    [Fact]
    public async Task RecommendationsSkipFavoritesAndPrivateThingsAndBlockedOwners()
    {
        var fav = NewIsland("Jardim Zen", "social", TimeSpan.Zero); NewIsland("Jardim Secreto", "social", TimeSpan.Zero, isPublic: false); NewIsland("Jardim Publico", "social", TimeSpan.Zero);
        var bia = NewUser("bia"); var blocked = new Island { Id = Guid.NewGuid(), OwnerUserId = bia.Id, Name = "Jardim Do Bloqueado", Slug = "jb", IsPublic = true, Description = "", Category = "social", CreatedAt = _now, UpdatedAt = _now };
        _db.Islands.Add(blocked); _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _me.Id, BlockedUserId = bia.Id, CreatedAt = _now }); await _db.SaveChangesAsync(); await SearchIndexer.RunAsync(_db, _now, default);
        await GlobalSearch.SetInterestsAsync(_db, _me.Id, ["jardim"], _now, default);
        Assert.True((await GlobalSearch.AddFavoriteAsync(_db, _me.Id, "island", fav.Id, _now, default)).Ok);
        Assert.Equal(["Jardim Publico"], (await GlobalSearch.RecommendationsAsync(_db, _me.Id, 10, _now, default)).Select(i => i.Title));
    }

    // ---- the local history --------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheHistoryKeepsTheNewestFirstWithoutRepeatsAndWithinTheLimit()
    {
        var h = new SearchHistory();
        Assert.False(h.Add("a")); Assert.False(h.Add("  ")); Assert.False(h.Add(null));
        h.Add("Praia Grande"); h.Add("floresta"); h.Add("praia  GRANDE!");
        Assert.Equal(["praia  GRANDE!", "floresta"], h.Items);
        for (var i = 0; i < 40; i++) h.Add("busca " + i);
        Assert.Equal(SearchHistory.Capacity, h.Items.Count); Assert.Equal("busca 39", h.Items[0]);
        Assert.True(h.Remove("BUSCA 39")); Assert.False(h.Remove("nao existe")); Assert.DoesNotContain("busca 39", h.Items);
        h.Clear(); Assert.Empty(h.Items);
    }

    [Fact]
    public void TheHistoryOffersPastSearchesWhileTyping()
    {
        var h = new SearchHistory(); h.Add("Praia do Sol"); h.Add("Festa na praia"); h.Add("floresta");
        Assert.Equal(["Festa na praia", "Praia do Sol"], h.Matching("pra")); Assert.Equal(["floresta", "Festa na praia", "Praia do Sol"], h.Matching("")); Assert.Equal(["Praia do Sol"], h.Matching("praia sol"));
        Assert.Equal(["floresta"], h.Matching("", 1));
    }

    [Fact]
    public void TheHistorySurvivesSavingAndADamagedFileGivesAnEmptyOne()
    {
        var h = new SearchHistory(); h.Add("um"); h.Add("dois"); h.Add("três");
        var back = SearchHistory.FromJson(h.ToJson()); Assert.Equal(h.Items, back.Items);
        Assert.Empty(SearchHistory.FromJson("{ not json").Items); Assert.Empty(SearchHistory.FromJson(null).Items); Assert.Empty(SearchHistory.FromJson("{\"a\":1}").Items);
        var path = Path.Combine(Path.GetTempPath(), "lv_hist_" + Guid.NewGuid().ToString("N"), "history.json");
        try { h.Save(path); Assert.Equal(h.Items, SearchHistory.Load(path).Items); File.WriteAllText(path, "lixo"); Assert.Empty(SearchHistory.Load(path).Items); Assert.Empty(SearchHistory.Load(path + ".nao").Items); }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { } }
    }
}
