using System.Diagnostics;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Shared.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace LifeVerse.Api.Tests;

/// <summary>Final checks of the global search: relevance on a set of judged queries, permissions on every surface, speed and many people at once.</summary>
public sealed class SearchFinalTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _store = Guid.NewGuid().ToString();
    private readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private LifeVerseDbContext? _shared;

    private LifeVerseDbContext Open() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(_store).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
    private LifeVerseDbContext Db => _shared ??= Open();
    public void Dispose() => _shared?.Dispose();

    private User NewUser(string username, string display, string privacy = "{\"presence\":\"friends\"}")
    {
        var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now, PrivacyJson = privacy };
        Db.Users.Add(u); Db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = u.Id, DisplayName = display, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); Db.SaveChanges(); return u;
    }

    private Island NewIsland(string name, User owner, string description = "", string category = "social", int ageDays = 0, bool isPublic = true, bool deleted = false)
    {
        var at = _now.AddDays(-ageDays); var i = new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = name, Slug = Guid.NewGuid().ToString("N")[..12], IsPublic = isPublic, Description = description, Category = category, CreatedAt = at, UpdatedAt = at, DeletedAt = deleted ? _now : null };
        Db.Islands.Add(i); Db.SaveChanges(); return i;
    }

    // ---- relevance: queries with a known best answer ------------------------------------------------------------------------------

    [Fact]
    public async Task JudgedQueriesPutTheExpectedResultFirst()
    {
        var ana = NewUser("ana", "Ana");
        foreach (var (name, description, age) in new[]
        {
            ("Praia", "", 40), ("Praia Grande", "", 1), ("Grande Hotel", "Perto da praia", 1), ("Ilha da Praia Dourada", "", 5), ("Trilha da Floresta", "", 1), ("Floresta Encantada", "", 20), ("Encantada", "", 90),
            ("Casa da Música", "Shows toda noite", 10), ("Estudio Sonoro", "Gravação e música", 10), ("Surf Club", "", 30), ("Praia Calma", "Bom para surf", 1), ("Lago Azul", "", 60), ("Lago Verde", "", 2),
            ("Cidade Alta", "", 3), ("Alta Montanha", "", 3), ("Mercado Central", "Frutas e verduras", 3), ("Feira Livre", "Mercado de rua", 3),
        }) NewIsland(name, ana, description, ageDays: age);
        await SearchIndexer.RunAsync(Db, _now, default);
        foreach (var e in Db.SearchEntries.Where(e => e.Title == "Praia Grande" || e.Title == "Grande Hotel" || e.Title == "Trilha da Floresta")) e.Popularity = 800; await Db.SaveChangesAsync(); // popular decoys must not beat a better match
        var me = NewUser("eu", "Eu");
        var judged = new (string Query, string Best)[]
        {
            ("praia", "Praia"), ("PRAIA", "Praia"), ("praia grande", "Praia Grande"), ("grande", "Grande Hotel"), ("dourada", "Ilha da Praia Dourada"), ("floresta", "Floresta Encantada"), ("encantada", "Encantada"),
            ("musica", "Casa da Música"), ("Música", "Casa da Música"), ("surf", "Surf Club"), ("lago", "Lago Verde"), ("alta", "Alta Montanha"), ("mercado", "Mercado Central"), ("feira", "Feira Livre"), ("cidade alta", "Cidade Alta"),
        };
        var reciprocal = 0.0;
        foreach (var (query, best) in judged)
        {
            var titles = (await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest(query, ["island"]), _now, default)).Items.Select(i => i.Title).ToList();
            var rank = titles.IndexOf(best) + 1; Assert.True(rank > 0, $"'{query}' did not find '{best}'"); reciprocal += 1.0 / rank;
            Assert.True(rank == 1, $"'{query}' should put '{best}' first but gave: {string.Join(" | ", titles)}");
        }
        output.WriteLine($"event=search_relevance queries={judged.Length} mrr={reciprocal / judged.Length:0.000}");
        Assert.Equal(1.0, reciprocal / judged.Length, 3);
    }

    [Fact]
    public async Task EveryTermMustMatchAndTheTitleBeatsTheDescription()
    {
        var ana = NewUser("ana", "Ana"); NewIsland("Vila Pescadores", ana, "Peixe fresco"); NewIsland("Mercado do Peixe", ana, ""); NewIsland("Pescaria Azul", ana, "Barcos e peixe");
        await SearchIndexer.RunAsync(Db, _now, default); var me = NewUser("eu", "Eu");
        Assert.Equal("Mercado do Peixe", (await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("peixe", ["island"]), _now, default)).Items[0].Title);
        Assert.Equal(["Vila Pescadores"], (await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("pescadores peixe", ["island"]), _now, default)).Items.Select(i => i.Title)); // both words, in title and description
        Assert.Empty((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("peixe espada", ["island"]), _now, default)).Items); // a word that is nowhere
        Assert.Empty((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("eixe", ["island"]), _now, default)).Items); // the middle of a word is not a match
    }

    // ---- permissions: nothing forbidden leaks through any door --------------------------------------------------------------------

    [Fact]
    public async Task NothingForbiddenAppearsInSearchSuggestionsPopularRecentFavoritesRecommendationsOrReports()
    {
        var me = NewUser("eu", "Eu Viewer"); var ana = NewUser("ana", "Ana Dona"); var bia = NewUser("bia", "Bia Tabu Bloqueada"); var cris = NewUser("cris", "Cris Tabu Que Me Bloqueou");
        Db.UserBlocks.AddRange(new UserBlock { Id = Guid.NewGuid(), UserId = me.Id, BlockedUserId = bia.Id, CreatedAt = _now }, new UserBlock { Id = Guid.NewGuid(), UserId = cris.Id, BlockedUserId = me.Id, CreatedAt = _now }); await Db.SaveChangesAsync();
        NewUser("priv", "Pessoa Tabu Privada", "{\"profile\":\"private\"}"); NewUser("nobusca", "Pessoa Tabu Fora Da Busca", "{\"searchable\":\"no\"}");
        var sys = NewUser("sistema", "Pessoa Tabu Sistema"); sys.PasswordHash = "!"; var gone = NewUser("gone", "Pessoa Tabu Excluida"); gone.DeletedAt = _now; var banned = NewUser("banido", "Pessoa Tabu Suspensa"); banned.Status = "suspended"; await Db.SaveChangesAsync();

        var open = NewIsland("Ilha Tabu Aberta", ana); var closed = NewIsland("Ilha Tabu Fechada", ana, isPublic: false); NewIsland("Ilha Tabu Apagada", ana, deleted: true); NewIsland("Ilha Tabu Da Bia", bia); NewIsland("Ilha Tabu Da Cris", cris); var moderated = NewIsland("Ilha Tabu Moderada", ana);
        Db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = open.Id, OwnerUserId = ana.Id, Name = "Lote Tabu Privado", BoundsJson = "{}", AccessMode = "private", CreatedAt = _now, UpdatedAt = _now });
        Db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = closed.Id, OwnerUserId = ana.Id, Name = "Lote Tabu Em Ilha Fechada", BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now });
        Db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = ana.Id, Name = "Grupo Tabu Privado", NameKey = "gtp", Privacy = "private", CreatedAt = _now, UpdatedAt = _now });
        Db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = ana.Id, Name = "Grupo Tabu Apagado", NameKey = "gta", Privacy = "public", CreatedAt = _now, UpdatedAt = _now, DeletedAt = _now });
        Db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = bia.Id, Name = "Grupo Tabu Da Bia", NameKey = "gtb", Privacy = "public", CreatedAt = _now, UpdatedAt = _now });
        Db.MarketplaceProducts.Add(new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = ana.Id, AssetId = Guid.NewGuid(), Name = "Produto Tabu Parado", Description = "", Category = "x", Status = "inactive", CreatedAt = _now, UpdatedAt = _now });
        Db.MarketplaceProducts.Add(new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = bia.Id, AssetId = Guid.NewGuid(), Name = "Produto Tabu Da Bia", Description = "", Category = "x", Status = "active", CreatedAt = _now, UpdatedAt = _now });
        Db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = open.Id, OrganizerUserId = ana.Id, Name = "Evento Tabu Do Grupo", Description = "", StartsAt = _now, EndsAt = _now.AddHours(2), Capacity = 5, IsPublic = true, GroupId = Guid.NewGuid(), CreatedAt = _now });
        Db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = open.Id, OrganizerUserId = ana.Id, Name = "Evento Tabu Acabado", Description = "", StartsAt = _now.AddDays(-2), EndsAt = _now.AddDays(-1), Capacity = 5, IsPublic = true, CreatedAt = _now.AddDays(-3) });
        Db.DiscoveryEvents.Add(new DiscoveryEvent { Id = Guid.NewGuid(), IslandId = closed.Id, OrganizerUserId = ana.Id, Name = "Evento Tabu Em Ilha Fechada", Description = "", StartsAt = _now, EndsAt = _now.AddHours(2), Capacity = 5, IsPublic = true, CreatedAt = _now });
        Db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = ana.Id, IslandId = open.Id, Name = "Ponto Tabu Particular", IsPublic = false, CreatedAt = _now, UpdatedAt = _now });
        Db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = ana.Id, IslandId = closed.Id, Name = "Ponto Tabu Em Ilha Fechada", IsPublic = true, CreatedAt = _now, UpdatedAt = _now });
        Db.Landmarks.Add(new Landmark { Id = Guid.NewGuid(), OwnerUserId = ana.Id, IslandId = open.Id, Name = "Ponto Tabu Liberado", IsPublic = true, CreatedAt = _now, UpdatedAt = _now });
        await Db.SaveChangesAsync(); await SearchIndexer.RunAsync(Db, _now, default);
        foreach (var e in Db.SearchEntries.Where(e => e.RefId == moderated.Id)) e.Hidden = true;
        foreach (var e in Db.SearchEntries) e.Popularity += 500; // everything popular: the ranking cannot be the thing that keeps it out
        await Db.SaveChangesAsync();
        await GlobalSearch.SetInterestsAsync(Db, me.Id, ["tabu"], _now, default);

        var allowed = new HashSet<string> { "Ilha Tabu Aberta", "Ponto Tabu Liberado" }; string[] Bad(IEnumerable<string> titles) => [.. titles.Where(t => !allowed.Contains(t))];
        var found = new List<string>();
        for (var page = 1; page <= 3; page++) found.AddRange((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("tabu", null, page, 50), _now, default)).Items.Select(i => i.Title));
        Assert.Empty(Bad(found.Where(t => !t.StartsWith("Pessoa") && !t.StartsWith("Eu "))));
        Assert.Contains("Ilha Tabu Aberta", found); Assert.Contains("Ponto Tabu Liberado", found); // the search itself works
        Assert.DoesNotContain(found, t => t.Contains("Pessoa Tabu") || t.Contains("Bia Tabu") || t.Contains("Cris Tabu")); // no people who are private, out of search, system, deleted, suspended, blocked either way
        foreach (var sort in new[] { "relevance", "popularity", "updated" }) Assert.Empty(Bad((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("tabu", null, 1, 50, Sort: sort), _now, default)).Items.Select(i => i.Title).Where(t => !t.StartsWith("Pessoa")))); // every order
        Assert.Empty(Bad((await GlobalSearch.SuggestAsync(Db, me.Id, "tabu", 20, _now, default)).Select(s => s.Text)));
        Assert.All((await GlobalSearch.PopularPlacesAsync(Db, me.Id, 50, _now, default)).Select(i => i.Title), t => Assert.True(!t.Contains("Tabu") || allowed.Contains(t), "popular leaked " + t));
        Assert.All((await GlobalSearch.RecentPlacesAsync(Db, me.Id, 50, _now, default)).Select(i => i.Title), t => Assert.True(!t.Contains("Tabu") || allowed.Contains(t), "recent leaked " + t));
        Assert.Empty(Bad((await GlobalSearch.RecommendationsAsync(Db, me.Id, 50, _now, default)).Select(i => i.Title)));

        // cannot favorite or report what cannot be seen, and a favorite made before it was closed is not listed
        foreach (var entry in await Db.SearchEntries.AsNoTracking().Where(e => e.Title.Contains("Tabu") && !allowed.Contains(e.Title)).ToListAsync())
        {
            if (entry.Kind == "person") continue; // people have their own check below
            Assert.False((await GlobalSearch.AddFavoriteAsync(Db, me.Id, entry.Kind, entry.RefId, _now, default)).Ok, $"favorited {entry.Title}");
            Assert.False((await SearchModeration.ReportAsync(Db, me.Id, entry.Kind, entry.RefId, "motivo", _now, default)).Ok, $"reported {entry.Title}");
            Db.SearchFavorites.Add(new SearchFavorite { Id = Guid.NewGuid(), UserId = me.Id, Kind = entry.Kind, RefId = entry.RefId, CreatedAt = _now });
        }
        await Db.SaveChangesAsync();
        Assert.Empty(Bad((await GlobalSearch.FavoritesAsync(Db, me.Id, null, 1, 50, _now, default)).Items.Select(i => i.Title)));
        foreach (var person in await Db.SearchEntries.AsNoTracking().Where(e => e.Kind == "person" && e.Title.Contains("Tabu")).ToListAsync()) Assert.False((await GlobalSearch.AddFavoriteAsync(Db, me.Id, "person", person.RefId, _now, default)).Ok, $"favorited {person.Title}");
        Assert.Empty(await Db.SearchReports.ToListAsync());
    }

    [Fact]
    public async Task APersonNeverFindsThemselvesAndAnotherViewerSeesTheirOwnPicture()
    {
        var me = NewUser("eu", "Marina Tabu"); var other = NewUser("outro", "Outro Visitante"); NewUser("terceira", "Marina Terceira");
        await SearchIndexer.RunAsync(Db, _now, default);
        Assert.DoesNotContain("Marina Tabu", (await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("marina", ["person"]), _now, default)).Items.Select(i => i.Title));
        Assert.Contains("Marina Tabu", (await GlobalSearch.SearchAsync(Db, other.Id, new SearchRequest("marina", ["person"]), _now, default)).Items.Select(i => i.Title));
        Db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = other.Id, BlockedUserId = me.Id, CreatedAt = _now }); await Db.SaveChangesAsync();
        Assert.DoesNotContain("Marina Tabu", (await GlobalSearch.SearchAsync(Db, other.Id, new SearchRequest("marina", ["person"]), _now, default)).Items.Select(i => i.Title)); // blocked: gone for the one who blocked
        Assert.DoesNotContain(await GlobalSearch.SuggestAsync(Db, me.Id, "outro", 10, _now, default), s => s.Text == "Outro Visitante"); // and for the one who was blocked
    }

    [Fact]
    public async Task TheStrangestTextsNeverBreakTheSearch()
    {
        var ana = NewUser("ana", "Ana"); NewIsland("Ilha Normal", ana); await SearchIndexer.RunAsync(Db, _now, default); var me = NewUser("eu", "Eu");
        foreach (var text in new[] { "", " ", "a", "'; DROP TABLE users;--", "%", "_", "\\", "ilha & normal", "ilha | !normal", "(ilha)", "ilha:*", new string('x', 5000), "ilha\0normal", "😀 ilha", "ILHA\r\nNORMAL", string.Join(" ", Enumerable.Range(0, 200).Select(i => "palavra" + i)) })
        {
            var page = await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest(text, null, -5, 9999), _now, default); Assert.InRange(page.PageSize, 1, GlobalSearch.MaxPageSize); Assert.True(page.Page >= 1);
            await GlobalSearch.SuggestAsync(Db, me.Id, text, 5000, _now, default);
        }
        Assert.NotEmpty((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest("😀 ilha"), _now, default)).Items); // the emoji is only noise
    }

    // ---- speed and many people at once --------------------------------------------------------------------------------------------

    [Fact]
    public async Task ThreeThousandIslandsAreIndexedAndSearchedQuickly()
    {
        var ana = NewUser("ana", "Ana"); string[] words = ["praia", "floresta", "montanha", "deserto", "lago", "cidade", "vila", "porto", "campo", "rio"];
        Db.ChangeTracker.AutoDetectChangesEnabled = false;
        for (var i = 0; i < 3000; i++) Db.Islands.Add(new Island { Id = Guid.NewGuid(), OwnerUserId = ana.Id, Name = $"{words[i % 10]} {words[(i / 10) % 10]} {i}", Slug = "s" + i, IsPublic = true, Description = words[(i / 100) % 10], Category = i % 2 == 0 ? "social" : "negocios", CreatedAt = _now.AddMinutes(-i), UpdatedAt = _now.AddMinutes(-i) });
        await Db.SaveChangesAsync(); Db.ChangeTracker.AutoDetectChangesEnabled = true;
        var clock = Stopwatch.StartNew(); var report = await SearchIndexer.RunAsync(Db, _now, default); var indexed = clock.ElapsedMilliseconds;
        Assert.True(report.Written >= 3000); clock.Restart(); var again = await SearchIndexer.RunAsync(Db, _now.AddSeconds(30), default); var idle = clock.ElapsedMilliseconds;
        Assert.Equal(0, again.Written); // an idle pass writes nothing
        var me = NewUser("eu", "Eu"); clock.Restart();
        for (var i = 0; i < 20; i++) Assert.NotEmpty((await GlobalSearch.SearchAsync(Db, me.Id, new SearchRequest(words[i % 10] + " " + words[(i + 3) % 10], ["island"], 1, 20, Sort: i % 2 == 0 ? "popularity" : "relevance"), _now, default)).Items);
        var searches = clock.ElapsedMilliseconds;
        output.WriteLine($"event=search_memory_speed index_ms={indexed} idle_ms={idle} twenty_searches_ms={searches}");
        Assert.True(indexed < 30_000, $"indexing took {indexed} ms"); Assert.True(idle < 3_000, $"an idle pass took {idle} ms"); Assert.True(searches < 15_000, $"20 searches took {searches} ms");
    }

    [Fact]
    public async Task ManyPeopleSearchingAtOnceAllGetTheRightAnswer()
    {
        var ana = NewUser("ana", "Ana"); for (var i = 0; i < 300; i++) NewIsland($"Laguna {i} Azul", ana);
        await SearchIndexer.RunAsync(Db, _now, default); var viewers = Enumerable.Range(0, 40).Select(i => NewUser("v" + i, "Visitante " + i).Id).ToList();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>(); var clock = Stopwatch.StartNew();
        await Parallel.ForEachAsync(viewers, new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (viewer, ct) =>
        {
            await using var db = Open(); // each request has its own context, as in the real server
            for (var round = 0; round < 5; round++)
            {
                var page = await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("laguna azul", ["island"], 1, 20), _now, ct);
                if (page.Total != 300 || page.Items.Count != 20) failures.Add($"total {page.Total}, items {page.Items.Count}");
                var suggestions = await GlobalSearch.SuggestAsync(db, viewer, "lagu", 8, _now, ct); if (suggestions.Count == 0) failures.Add("no suggestions");
                await GlobalSearch.AddFavoriteAsync(db, viewer, "island", page.Items[round].Id, _now, ct);
            }
        });
        output.WriteLine($"event=search_concurrency people=40 rounds=5 ms={clock.ElapsedMilliseconds}");
        Assert.Empty(failures); Assert.Equal(200, await Db.SearchFavorites.CountAsync()); Assert.True(clock.ElapsedMilliseconds < 60_000);
    }
}
