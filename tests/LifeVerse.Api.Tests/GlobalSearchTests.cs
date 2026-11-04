using System.Diagnostics;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>How text is compared, and how results are ordered.</summary>
public sealed class SearchTextTests
{
    [Theory]
    [InlineData("Ação Rápida!", "acao rapida")] [InlineData("  Praia   do  SOL ", "praia do sol")] [InlineData("café-com-leite", "cafe com leite")] [InlineData("", "")] [InlineData(null, "")] [InlineData("!!!", "")] [InlineData("Ñandú 2", "nandu 2")]
    public void TextIsLowerCaseWithoutAccentsOrPunctuation(string? input, string expected) => Assert.Equal(expected, SearchText.Normalize(input));

    [Fact]
    public void TermsAreFewShortAndDistinct()
    {
        Assert.Equal(["praia", "sol"], SearchText.Terms("Praia sol Praia"));
        Assert.Equal(SearchText.MaxTerms, SearchText.Terms("a b c d e f g h i j k l").Length);
        Assert.Equal(SearchText.MaxTermLength, SearchText.Terms(new string('x', 200))[0].Length);
        Assert.Empty(SearchText.Terms("   ")); Assert.Empty(SearchText.Terms(null));
        Assert.True(SearchText.Terms(new string('a', 5000) + " fim").Length <= SearchText.MaxTerms); // a huge query is cut before it is read
    }

    [Theory]
    [InlineData("praia do sol", "prai", true)] [InlineData("praia do sol", "sol", true)] [InlineData("praia do sol", "ol", false)] [InlineData("praia do sol", "do", true)] [InlineData("praia", "praia", true)] [InlineData("praia", "praias", false)]
    public void ATermMatchesTheBeginningOfAWordOnly(string text, string term, bool expected) => Assert.Equal(expected, SearchText.HasWordStartingWith(text, term));

    [Fact]
    public void EveryTermMustMatch() { Assert.True(SearchText.MatchesAll("ilha da praia grande", ["ilh", "gran"])); Assert.False(SearchText.MatchesAll("ilha da praia grande", ["ilh", "floresta"])); Assert.False(SearchText.MatchesAll("ilha", [])); }

    [Theory]
    [InlineData("praia", "praia:*")] [InlineData("ilha praia", "ilha:* & praia:*")]
    public void TheDatabaseQueryHasEveryTermAsAPrefix(string query, string expected) => Assert.Equal(expected, SearchText.TsQuery(SearchText.Terms(query)));

    [Theory]
    [InlineData("a'b")] [InlineData("a&b|c!d")] [InlineData("(praia) <-> sol")] [InlineData("x:*; DROP TABLE users")] [InlineData("\\")]
    public void NothingTypedCanChangeTheDatabaseQuery(string typed)
    {
        var query = SearchText.TsQuery(SearchText.Terms(typed));
        Assert.All(query.Split(" & ", StringSplitOptions.RemoveEmptyEntries), part => Assert.Matches("^[a-z0-9]+:\\*$", part));
    }

    [Fact]
    public void ARelevantTitleBeatsAMatchOnlyInTheDescription()
    {
        var now = DateTimeOffset.UtcNow; string[] terms = ["praia"];
        double Score(string title, string text) => SearchText.Relevance(title, text, "praia", terms, 0, now.AddDays(-90), now);
        var exact = Score("Praia", "praia"); var starts = Score("Praia Grande", "praia grande"); var word = Score("Ilha da Praia", "ilha da praia"); var onlyText = Score("Ilha tropical", "ilha tropical com praia");
        Assert.True(exact > starts && starts > word && word > onlyText, $"{exact} {starts} {word} {onlyText}");
    }

    [Fact]
    public void PopularityAndRecencyBreakTiesButNeverBeatAMuchBetterMatch()
    {
        var now = DateTimeOffset.UtcNow; string[] terms = ["praia"];
        double Score(string title, double pop, int ageDays) => SearchText.Relevance(title, SearchText.Normalize(title), "praia", terms, pop, now.AddDays(-ageDays), now);
        Assert.True(Score("Praia Azul", 500, 60) > Score("Praia Azul", 0, 60)); Assert.True(Score("Praia Azul", 0, 1) > Score("Praia Azul", 0, 60));
        Assert.True(Score("Praia", 0, 90) > Score("Ilha com praia ao lado", 1_000_000, 0)); // the exact name wins over any popularity
        Assert.True(SearchText.Relevance("x", "x", "x", ["x"], 1e12, now, now) < 200); // and the popularity bonus has a ceiling
    }

    [Fact]
    public void ASnippetIsCutAtAWord() { var cut = SearchText.Snippet("uma frase bem comprida que não cabe inteira no espaço", 20); Assert.True(cut.Length <= 21 && cut.EndsWith('…') && !cut.Contains("compri,")); Assert.Equal("curta", SearchText.Snippet("curta")); Assert.Equal(string.Empty, SearchText.Snippet(null)); }
}

/// <summary>The index of what can be found, kept from the real data, and the search over it.</summary>
public sealed class GlobalSearchTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly User _me, _ana, _bia;

    public GlobalSearchTests() { _me = NewUser("eu", "Eu Mesmo"); _ana = NewUser("ana", "Ana Praia"); _bia = NewUser("bia", "Bia Costa"); }
    public void Dispose() => _db.Dispose();

    private User NewUser(string username, string display, string privacy = "{\"presence\":\"friends\"}", string bio = "")
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PrivacyJson = privacy, Bio = bio };
        _db.Users.Add(u); _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = u.Id, DisplayName = display, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return u;
    }

    private Island NewIsland(string name, User? owner = null, bool isPublic = true, string description = "", string category = "social", TimeSpan? age = null)
    {
        var at = _now - (age ?? TimeSpan.Zero);
        var i = new Island { Id = Guid.NewGuid(), OwnerUserId = (owner ?? _ana).Id, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, Description = description, Category = category, CreatedAt = at, UpdatedAt = at };
        _db.Islands.Add(i); _db.SaveChanges(); return i;
    }

    private Task<IndexReport> Index() => SearchIndexer.RunAsync(_db, _now, default);
    private Task<SearchPage> Find(string q, string[]? types = null, int page = 1, int size = 20, User? who = null) => GlobalSearch.SearchAsync(_db, (who ?? _me).Id, new SearchRequest(q, types, page, size), _now, default);
    private async Task<List<string>> Titles(string q, string[]? types = null, User? who = null) => (await Find(q, types, who: who)).Items.Select(i => i.Title).ToList();
    private async Task<int> Lines(string? kind = null) => await _db.SearchEntries.CountAsync(e => kind == null || e.Kind == kind);
    private async Task<int> Things() => await _db.SearchEntries.CountAsync(e => e.Kind != "person"); // everything that is not a person (the people of the test are always there)

    // ---- what is indexed ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryKindOfThingIsIndexedWithItsPlace()
    {
        var island = NewIsland("Ilha Tropical", description: "Praia, coqueiros e festa");
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = _ana.Id, Name = "Lote da Orla", BoundsJson = "{}", SpawnX = 12, SpawnZ = -7, SalePrice = 900, CreatedAt = _now, UpdatedAt = _now });
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Surfistas da Orla", NameKey = "surfistas", Tag = "SRF", Privacy = "public", Description = "Ondas todo dia", CreatedAt = _now, UpdatedAt = _now });
        var product = new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = _ana.Id, AssetId = Guid.NewGuid(), Name = "Prancha de Surf", Description = "Para ondas grandes", Category = "esporte", PriceCoins = 250, Status = "active", CreatedAt = _now, UpdatedAt = _now };
        _db.MarketplaceProducts.Add(product);
        _db.WorldStores.Add(new WorldStore { Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = _ana.Id, ProductId = product.Id, DisplayName = "Loja do Surfista", CreatedAt = _now, UpdatedAt = _now });
        _db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = island.Id, OrganizerUserId = _ana.Id, Name = "Festival de Surf", Description = "Campeonato aberto", StartsAt = _now.AddDays(1), EndsAt = _now.AddDays(1).AddHours(4), Capacity = 50, IsPublic = true, CreatedAt = _now });
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = island.Id, Name = "Farol Velho", PositionX = 40, PositionZ = 25, IsPublic = true, CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync();
        var report = await Index();
        Assert.True(report.Written >= 8);
        foreach (var kind in SearchKinds.All) Assert.True(await Lines(kind) >= 1, $"nothing indexed for {kind}");
        var surf = await Find("surf"); Assert.Equal(4, surf.Total); // group, product, store and event
        Assert.Equal(new[] { "event", "group", "product", "store" }, surf.Counts.Keys.Order());
        var lote = (await Find("lote orla")).Items.Single(); Assert.Equal("parcel", lote.Kind); Assert.Equal(island.Id, lote.IslandId); Assert.Equal((12f, -7f), (lote.X, lote.Z)); Assert.Equal("venda", lote.Category); Assert.Contains("Ilha Tropical", lote.Subtitle);
        var farol = (await Find("farol")).Items.Single(); Assert.Equal((40f, 25f), (farol.X, farol.Z)); Assert.Equal("Ponto de interesse", farol.KindLabel);
        Assert.Equal("Ana Praia", (await Find("ana praia", ["person"])).Items.Single().Title);
        Assert.Equal("@ana", (await Find("ana", ["person"])).Items.First().Subtitle);
    }

    [Fact]
    public async Task NothingPrivateOrGoneIsIndexed()
    {
        var closed = NewIsland("Segredo Privado", isPublic: false); var deleted = NewIsland("Segredo Apagado"); deleted.DeletedAt = _now; await _db.SaveChangesAsync();
        var open = NewIsland("Segredo Aberto");
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = open.Id, OwnerUserId = _ana.Id, Name = "Segredo Lote Fechado", BoundsJson = "{}", AccessMode = "private", CreatedAt = _now, UpdatedAt = _now });
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = closed.Id, OwnerUserId = _ana.Id, Name = "Segredo Lote Da Ilha Privada", BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now });
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Segredo Grupo Privado", NameKey = "sgp", Privacy = "private", CreatedAt = _now, UpdatedAt = _now });
        _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Segredo Grupo Apagado", NameKey = "sga", Privacy = "public", CreatedAt = _now, UpdatedAt = _now, DeletedAt = _now });
        _db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = open.Id, OrganizerUserId = _ana.Id, Name = "Segredo Evento Do Grupo", Description = "", StartsAt = _now, EndsAt = _now.AddHours(2), Capacity = 5, IsPublic = true, GroupId = Guid.NewGuid(), CreatedAt = _now });
        _db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = open.Id, OrganizerUserId = _ana.Id, Name = "Segredo Evento Acabado", Description = "", StartsAt = _now.AddDays(-2), EndsAt = _now.AddDays(-1), Capacity = 5, IsPublic = true, CreatedAt = _now.AddDays(-3) });
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = open.Id, Name = "Segredo Ponto Particular", IsPublic = false, CreatedAt = _now, UpdatedAt = _now });
        _db.MarketplaceProducts.Add(new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = _ana.Id, AssetId = Guid.NewGuid(), Name = "Segredo Produto Parado", Description = "", Category = "x", Status = "inactive", CreatedAt = _now, UpdatedAt = _now });
        NewUser("segredo1", "Segredo Perfil Privado", "{\"profile\":\"private\"}"); NewUser("segredo2", "Segredo Fora Da Busca", "{\"searchable\":\"no\"}");
        var system = NewUser("segredo3", "Segredo Sistema"); system.PasswordHash = "!"; var gone = NewUser("segredo4", "Segredo Excluido"); gone.DeletedAt = _now; var banned = NewUser("segredo5", "Segredo Suspenso"); banned.Status = "suspended";
        await _db.SaveChangesAsync(); await Index();
        Assert.Equal(["Segredo Aberto"], await Titles("segredo"));
        Assert.Equal(0, await _db.SearchEntries.CountAsync(e => e.Title.Contains("Privad") || e.Title.Contains("Apagad") || e.Title.Contains("Particular")));
    }

    [Fact]
    public async Task APrivateChangeHidesThingsAtOnceEvenBeforeTheIndexCatchesUp()
    {
        var island = NewIsland("Ilha Mutante"); var group = new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Grupo Mutante", NameKey = "gm", Privacy = "public", CreatedAt = _now, UpdatedAt = _now };
        _db.SocialGroups.Add(group); await _db.SaveChangesAsync(); await Index();
        Assert.Equal(2, (await Find("mutante")).Total);
        island.IsPublic = false; group.Privacy = "private"; await _db.SaveChangesAsync(); // the index still has both lines
        Assert.Equal(2, await Things()); Assert.Equal(0, (await Find("mutante")).Total); // but nobody is shown them
        Assert.Empty(await GlobalSearch.SuggestAsync(_db, _me.Id, "mutan", 8, _now, default));
        _now = _now.AddMinutes(1); var report = await Index(); Assert.True(report.Removed + report.Written >= 1); Assert.Equal(0, await Lines("island")); Assert.Equal(0, await Lines("group")); // and the next pass takes them out
    }

    [Fact]
    public async Task APersonWhoBlockedYouOrWhomYouBlockedDoesNotShowUp()
    {
        NewIsland("Ilha da Bia", _bia); NewIsland("Ilha da Ana", _ana); await Index();
        Assert.Equal(2, (await Titles("ilha")).Count);
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _me.Id, BlockedUserId = _bia.Id, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Equal(["Ilha da Ana"], await Titles("ilha")); Assert.DoesNotContain("Bia Costa", await Titles("bia", ["person"]));
        Assert.Contains("Ilha da Bia", await Titles("ilha", who: _ana)); // for the others nothing changed
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _ana.Id, BlockedUserId = _me.Id, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Empty(await Titles("ilha")); // she blocked me: her things are hidden from me as well
        Assert.DoesNotContain("Eu Mesmo", await Titles("eu", ["person"])); // nobody finds themselves
    }

    [Fact]
    public async Task ModerationCanHideAThingAndARewriteDoesNotBringItBack()
    {
        var island = NewIsland("Ilha Denunciada"); await Index(); Assert.Equal(1, (await Find("denunciada")).Total);
        var line = await _db.SearchEntries.SingleAsync(e => e.RefId == island.Id); line.Hidden = true; await _db.SaveChangesAsync();
        Assert.Equal(0, (await Find("denunciada")).Total);
        island.Description = "mudou"; island.UpdatedAt = _now.AddMinutes(1); _now = _now.AddMinutes(2); await _db.SaveChangesAsync(); await Index();
        Assert.True((await _db.SearchEntries.SingleAsync(e => e.RefId == island.Id)).Hidden); Assert.Equal(0, (await Find("denunciada")).Total);
    }

    // ---- keeping up --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyWhatChangedIsWrittenAndRunningAgainChangesNothing()
    {
        var a = NewIsland("Praia Azul"); NewIsland("Praia Verde"); await Index();
        var again = await Index(); Assert.Equal(0, again.Written); Assert.Equal(0, again.Removed);
        _now = _now.AddMinutes(1); a.Name = "Praia Dourada"; a.UpdatedAt = _now; var fresh = NewIsland("Praia Nova"); fresh.UpdatedAt = _now; await _db.SaveChangesAsync();
        var report = await Index(); Assert.Equal(2, report.Written); // the renamed one and the new one, not the one that stayed
        Assert.Contains("Praia Dourada", await Titles("dourada")); Assert.Empty(await Titles("azul")); Assert.Equal(3, await Lines("island"));
        Assert.Equal(0, (await Index()).Written);
    }

    [Fact]
    public async Task TheWholeIndexCanBeRebuiltAndComesBackTheSame()
    {
        NewIsland("Ilha Um"); NewIsland("Ilha Dois"); _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Grupo Um", NameKey = "g1", Privacy = "public", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); await Index();
        var before = (await _db.SearchEntries.AsNoTracking().ToListAsync()).Select(e => (e.Kind, e.RefId, e.Title, e.SearchText)).Order().ToList();
        _db.SearchEntries.RemoveRange(_db.SearchEntries); await _db.SaveChangesAsync();
        var rebuilt = await SearchIndexer.RebuildAsync(_db, _now, default); Assert.True(rebuilt.Written > 0);
        Assert.Equal(before, (await _db.SearchEntries.AsNoTracking().ToListAsync()).Select(e => (e.Kind, e.RefId, e.Title, e.SearchText)).Order().ToList());
    }

    [Fact]
    public async Task ThingsThatEndedOrWereDeletedLeaveTheIndex()
    {
        var island = NewIsland("Ilha Viva");
        var ev = new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = island.Id, OrganizerUserId = _ana.Id, Name = "Show Passageiro", Description = "", StartsAt = _now.AddHours(1), EndsAt = _now.AddHours(3), Capacity = 5, IsPublic = true, CreatedAt = _now };
        var landmark = new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = island.Id, Name = "Marco Passageiro", IsPublic = true, CreatedAt = _now, UpdatedAt = _now };
        _db.DiscoveryEvents.Add(ev); _db.Landmarks.Add(landmark); await _db.SaveChangesAsync(); await Index();
        Assert.Equal(2, (await Find("passageiro")).Total);
        _now = _now.AddHours(4); landmark.DeletedAt = _now; landmark.UpdatedAt = _now; await _db.SaveChangesAsync(); await Index();
        Assert.Equal(0, await Lines("event")); Assert.Equal(0, await Lines("poi")); Assert.Equal(0, (await Find("passageiro")).Total); // one ended, one was deleted
        _db.Islands.Single().DeletedAt = _now; await _db.SaveChangesAsync(); _now = _now.AddMinutes(1); await Index(); Assert.Equal(0, await Lines("island"));
    }

    [Fact]
    public async Task ParcelsAndPlacesFollowTheirIslandWhenItOpensOrCloses()
    {
        var island = NewIsland("Ilha Fechada Por Enquanto", isPublic: false);
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = _ana.Id, Name = "Lote Esperando", BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now });
        _db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, IslandId = island.Id, Name = "Marco Esperando", IsPublic = true, CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync(); await Index(); Assert.Equal(0, await Things());
        _now = _now.AddMinutes(1); island.IsPublic = true; island.UpdatedAt = _now; await _db.SaveChangesAsync(); await Index(); // only the island changed
        Assert.Equal(3, await Things()); Assert.Equal(2, (await Find("esperando")).Total);
        _now = _now.AddMinutes(1); island.IsPublic = false; island.UpdatedAt = _now; await _db.SaveChangesAsync(); await Index();
        Assert.Equal(0, await Things());
    }

    [Fact]
    public async Task APersonIsFoundByTheNewNameAfterChangingIt()
    {
        await Index(); Assert.Contains("Ana Praia", await Titles("praia", ["person"]));
        _now = _now.AddMinutes(1); var avatar = await _db.Avatars.SingleAsync(a => a.UserId == _ana.Id); avatar.DisplayName = "Ana Serra"; avatar.UpdatedAt = _now; await _db.SaveChangesAsync(); await Index();
        Assert.Contains("Ana Serra", await Titles("serra", ["person"])); Assert.Empty(await Titles("praia", ["person"]));
        _now = _now.AddMinutes(1); _ana.PrivacyJson = "{\"profile\":\"private\"}"; _ana.UpdatedAt = _now; await _db.SaveChangesAsync(); await Index();
        Assert.Empty(await Titles("serra", ["person"])); // making the profile private takes the person out
    }

    // ---- the search ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ResultsAreOrderedByRelevance()
    {
        NewIsland("Ilha tropical", description: "com uma praia linda"); NewIsland("Ilha da Praia"); NewIsland("Praia"); NewIsland("Praia Grande");
        await Index();
        Assert.Equal(["Praia", "Praia Grande", "Ilha da Praia", "Ilha tropical"], await Titles("praia", ["island"]));
    }

    [Fact]
    public async Task PopularityDecidesBetweenEquallyRelevantResults()
    {
        var quiet = NewIsland("Ilha Azul A"); var loved = NewIsland("Ilha Azul B"); await Index();
        foreach (var n in Enumerable.Range(0, 5)) _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IslandId = loved.Id, CreatedAt = _now });
        await _db.SaveChangesAsync(); await Index();
        var found = await Find("ilha azul"); Assert.Equal(["Ilha Azul B", "Ilha Azul A"], found.Items.Select(i => i.Title)); Assert.True(found.Items[0].Popularity > found.Items[1].Popularity);
        _db.PlayerLocations.AddRange(Enumerable.Range(0, 8).Select(_ => new PlayerLocation { UserId = Guid.NewGuid(), IslandId = quiet.Id, UpdatedAt = _now })); await _db.SaveChangesAsync(); await Index(); // eight people there today
        Assert.Equal("Ilha Azul A", (await Find("ilha azul")).Items[0].Title);
    }

    [Fact]
    public async Task AccentsCaseAndWordBeginningsDoNotMatter()
    {
        NewIsland("Ação e Aventura"); NewIsland("São João da Praia"); await Index();
        Assert.Single(await Titles("acao")); Assert.Single(await Titles("AVENTURA")); Assert.Single(await Titles("sao joao")); Assert.Single(await Titles("São JOÃO praia")); Assert.Single(await Titles("aven")); Assert.Empty(await Titles("ventura")); // the beginning of a word, not the middle
    }

    [Fact]
    public async Task EveryWordMustBeThere()
    {
        NewIsland("Ilha da Floresta Azul"); NewIsland("Ilha da Praia Azul"); await Index();
        Assert.Equal(2, (await Find("ilha azul")).Total); Assert.Equal(["Ilha da Praia Azul"], await Titles("praia azul")); Assert.Empty(await Titles("praia floresta"));
    }

    [Fact]
    public async Task ShortOrOddQueriesGiveNothingAndNeverBreak()
    {
        NewIsland("Ilha A B"); await Index();
        foreach (var q in new[] { "", " ", "a", "!!", "'", "&|!", "x:*", "\\", "'; DROP TABLE users; --", new string('z', 5000)}) Assert.Equal(0, (await Find(q)).Total);
        Assert.Equal(1, (await Find("ilha a", ["island"])).Total);
    }

    [Fact]
    public async Task TheKindsCanBeChosen()
    {
        NewIsland("Surf Island"); _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Surf Club", NameKey = "sc", Privacy = "public", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); await Index();
        Assert.Equal(2, (await Find("surf")).Total); Assert.Equal(["Surf Club"], await Titles("surf", ["group"])); Assert.Equal(2, (await Find("surf", ["group", "island", "nonsense"])).Total); Assert.Equal(2, (await Find("surf", ["nonsense"])).Total); // names that mean nothing are ignored
        var counts = (await Find("surf")).Counts; Assert.Equal(1, counts["island"]); Assert.Equal(1, counts["group"]);
    }

    [Fact]
    public async Task PagesAreStableDisjointAndComplete()
    {
        foreach (var n in Enumerable.Range(1, 45)) NewIsland($"Ilha Numero {n:00}"); await Index();
        var first = await Find("ilha numero", page: 1, size: 20); var second = await Find("ilha numero", page: 2, size: 20); var third = await Find("ilha numero", page: 3, size: 20); var beyond = await Find("ilha numero", page: 4, size: 20);
        Assert.Equal((45, 20, 20, 5, 0), (first.Total, first.Items.Count, second.Items.Count, third.Items.Count, beyond.Items.Count));
        var all = first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Id).ToList(); Assert.Equal(45, all.Distinct().Count());
        Assert.Equal(all, (await Find("ilha numero", page: 1, size: 20)).Items.Concat((await Find("ilha numero", page: 2, size: 20)).Items).Concat((await Find("ilha numero", page: 3, size: 20)).Items).Select(i => i.Id)); // asking again gives the same order
        Assert.Equal(GlobalSearch.MaxPageSize, (await Find("ilha numero", size: 5000)).PageSize); Assert.Equal(20, (await Find("ilha numero", size: 0)).PageSize); Assert.Equal(1, (await Find("ilha numero", page: -3)).Page);
    }

    // ---- suggestions, popular and recent ---------------------------------------------------------------------------------------

    [Fact]
    public async Task SuggestionsCompleteTheWordBeingTypedWithoutRepeating()
    {
        NewIsland("Praia Grande"); NewIsland("Praia Pequena"); NewIsland("Floresta"); _db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = _ana.Id, Name = "Praia Grande", NameKey = "pg", Privacy = "public", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); await Index();
        var typed = await GlobalSearch.SuggestAsync(_db, _me.Id, "pra", 8, _now, default); Assert.Equal(["Ana Praia", "Praia Grande", "Praia Pequena"], typed.Select(s => s.Text).Order()); // a person whose name has the word counts too
        Assert.Single(await GlobalSearch.SuggestAsync(_db, _me.Id, "praia gra", 8, _now, default)); Assert.Empty(await GlobalSearch.SuggestAsync(_db, _me.Id, "p", 8, _now, default)); Assert.Empty(await GlobalSearch.SuggestAsync(_db, _me.Id, "zzz", 8, _now, default));
        Assert.Single(await GlobalSearch.SuggestAsync(_db, _me.Id, "pra", 1, _now, default));
        foreach (var n in Enumerable.Range(0, 30)) NewIsland($"Praia {n:00} Distante"); await Index();
        Assert.Equal(8, (await GlobalSearch.SuggestAsync(_db, _me.Id, "pra", 8, _now, default)).Count);
    }

    [Fact]
    public async Task PopularAndRecentPlacesAreOrderedAndPublicOnly()
    {
        var old = NewIsland("Ilha Antiga", age: TimeSpan.FromDays(60)); var popular = NewIsland("Ilha Famosa", age: TimeSpan.FromDays(30)); var newest = NewIsland("Ilha Recem Chegada", age: TimeSpan.FromHours(2)); NewIsland("Ilha Escondida", isPublic: false);
        _db.IslandFavorites.AddRange(Enumerable.Range(0, 6).Select(_ => new IslandFavorite { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IslandId = popular.Id, CreatedAt = _now })); _db.IslandFavorites.Add(new IslandFavorite { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IslandId = old.Id, CreatedAt = _now });
        await _db.SaveChangesAsync(); await Index();
        Assert.Equal(["Ilha Famosa", "Ilha Antiga"], (await GlobalSearch.PopularPlacesAsync(_db, _me.Id, 2, _now, default)).Select(p => p.Title));
        var recent = (await GlobalSearch.RecentPlacesAsync(_db, _me.Id, 10, _now, default)).Select(p => p.Title).ToList(); Assert.Equal(["Ilha Recem Chegada", "Ilha Famosa", "Ilha Antiga"], recent); Assert.DoesNotContain("Ilha Escondida", recent); _ = newest;
    }

    // ---- speed -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AThousandsStrongIndexIsBuiltAndSearchedQuickly()
    {
        var owner = _ana.Id; var words = new[] { "praia", "floresta", "montanha", "deserto", "lago", "cidade", "ilha", "vila", "porto", "campo" };
        for (var i = 0; i < 1_500; i++) _db.Islands.Add(new Island { Id = Guid.NewGuid(), OwnerUserId = owner, Name = $"{words[i % 10]} {words[(i / 10) % 10]} {i}", Slug = $"s{i}", IsPublic = true, Description = $"lugar {words[(i / 100) % 10]}", CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync();
        var clock = Stopwatch.StartNew(); var report = await Index(); var built = clock.ElapsedMilliseconds;
        Assert.Equal(1_500, await Lines("island")); Assert.True(built < 15_000, $"building took {built} ms");
        clock.Restart(); var found = await Find("praia floresta"); var searched = clock.ElapsedMilliseconds;
        Assert.True(found.Total >= 10 && found.Total <= GlobalSearch.CandidateLimit && searched < 3_000, $"{found.Total} results in {searched} ms");
        clock.Restart(); var again = await Index(); Assert.Equal(0, again.Written); Assert.True(clock.ElapsedMilliseconds < 3_000, $"an idle pass took {clock.ElapsedMilliseconds} ms"); _ = report;
    }
}
