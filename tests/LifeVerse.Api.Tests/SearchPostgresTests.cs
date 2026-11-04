using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The same search on a real PostgreSQL (full-text vector, GIN index and the raw query), which the in-memory tests cannot exercise. Runs only when
/// LIFEVERSE_TEST_PG is set (see <see cref="PostgresFactAttribute"/>); each test uses a throw-away database, migrated from scratch.
/// </summary>
public sealed class SearchPostgresTests
{
    private static async Task<(string Name, string Connection)> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!; var name = "lifeverse_s_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection); await command.ExecuteNonQueryAsync(); }
        var cs = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = true, MaxPoolSize = 10 }.ConnectionString;
        await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options); await db.Database.MigrateAsync();
        return (name, cs);
    }

    private static async Task DropAsync(string name)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!); await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task TheVectorColumnAndTheIndexExistAndFindWordBeginnings()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options); var now = DateTimeOffset.UtcNow;
            var owner = new User { Id = Guid.NewGuid(), Username = "ana", NormalizedUsername = "ANA", Email = "a@t.local", NormalizedEmail = "A@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
            db.Users.Add(owner);
            db.Islands.AddRange(
                new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = "Praia Grande", Slug = "praia-grande", IsPublic = true, Description = "Sol e mar", CreatedAt = now, UpdatedAt = now },
                new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = "Floresta Azul", Slug = "floresta-azul", IsPublic = true, Description = "Árvores altas", CreatedAt = now, UpdatedAt = now },
                new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = "Praia Secreta", Slug = "praia-secreta", IsPublic = false, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            await SearchIndexer.RunAsync(db, now, default);

            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM information_schema.columns WHERE table_schema = 'discovery' AND table_name = 'search_index' AND column_name = 'search_vector'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname = 'discovery' AND indexname = 'ix_search_index_search_vector'").SingleAsync());

            var viewer = owner.Id;
            Assert.Equal(["Praia Grande"], (await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai"), now, default)).Items.Select(i => i.Title)); // beginning of a word, and the private one is not there
            Assert.Equal(["Floresta Azul"], (await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("ARVORES"), now, default)).Items.Select(i => i.Title)); // no accents, any case
            Assert.Equal(["Praia Grande"], (await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("sol praia"), now, default)).Items.Select(i => i.Title));
            Assert.Empty((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("x:*'); DROP TABLE users;--"), now, default)).Items);
            var praia = (await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai"), now, default)).Items.Single(); // the filters are part of the SQL, check them on the real database
            Assert.Single((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai", Category: praia.Category.ToUpperInvariant(), IslandId: praia.Id), now, default)).Items);
            Assert.Empty((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai", Category: "outra-categoria"), now, default)).Items);
            Assert.Empty((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai", IslandId: Guid.NewGuid()), now, default)).Items);
            Assert.Equal("popularity", (await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("prai", Sort: "popularity"), now, default)).Sort);
            Assert.Single(await GlobalSearch.SuggestAsync(db, viewer, "flor", 8, now, default));

            // the vector follows the text when a line is rewritten
            var island = await db.Islands.SingleAsync(i => i.Slug == "praia-grande"); island.Name = "Montanha Alta"; island.UpdatedAt = now.AddMinutes(1); await db.SaveChangesAsync(); await SearchIndexer.RunAsync(db, now.AddMinutes(1), default);
            Assert.Empty((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("praia"), now, default)).Items); Assert.Single((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("montanha"), now, default)).Items);
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task FavoritesInterestsReportsAndMetricsWorkOnTheRealDatabase()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options); var now = DateTimeOffset.UtcNow;
            User Make(string u) => new() { Id = Guid.NewGuid(), Username = u, NormalizedUsername = u.ToUpperInvariant(), Email = u + "@t.local", NormalizedEmail = u.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
            var owner = Make("dono"); var fan = Make("fa"); db.Users.AddRange(owner, fan);
            var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = "Praia Musical", Slug = "praia-musical", IsPublic = true, Description = "Musica ao vivo", Category = "social", CreatedAt = now, UpdatedAt = now };
            db.Islands.Add(island); await db.SaveChangesAsync(); await SearchIndexer.RunAsync(db, now, default);

            Assert.True((await GlobalSearch.AddFavoriteAsync(db, fan.Id, "island", island.Id, now, default)).Ok); Assert.True((await GlobalSearch.AddFavoriteAsync(db, fan.Id, "island", island.Id, now, default)).Ok);
            Assert.Single(await db.SearchFavorites.ToListAsync()); Assert.True((await GlobalSearch.SearchAsync(db, fan.Id, new SearchRequest("musical"), now, default)).Items.Single().Favorite);
            await GlobalSearch.RemoveFavoriteAsync(db, fan.Id, "island", island.Id, default); Assert.Empty(await db.SearchFavorites.ToListAsync());

            await GlobalSearch.SetInterestsAsync(db, fan.Id, ["musica", "Arte"], now, default); Assert.Equal(["musica", "arte"], await GlobalSearch.InterestsAsync(db, fan.Id, default));
            Assert.Equal(["Praia Musical"], (await GlobalSearch.RecommendationsAsync(db, fan.Id, 10, now, default)).Select(i => i.Title));

            Assert.True((await SearchModeration.ReportAsync(db, fan.Id, "island", island.Id, "motivo", now, default)).Ok); Assert.True((await SearchModeration.ReportAsync(db, fan.Id, "island", island.Id, "de novo", now, default)).Ok);
            Assert.Single(await db.SearchReports.ToListAsync()); Assert.True(await SearchModeration.ResolveAsync(db, "island", island.Id, "hide", now, default));
            Assert.Empty((await GlobalSearch.SearchAsync(db, fan.Id, new SearchRequest("musical"), now, default)).Items);

            var metrics = new SearchMetrics(); metrics.Searched("praia", 2, 15, now); metrics.Used("open", "praia", now); await metrics.FlushAsync(db, default); metrics.Searched("praia", 0, 5, now); await metrics.FlushAsync(db, default);
            var report = await SearchMetrics.ReportAsync(db, 7, now, default); Assert.Equal((2, 1, 1), (report.Searches, report.ZeroResults, report.Opens));
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task AFiftyThousandLineIndexIsSearchedByTheIndexInWellUnderASecond()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            await using var db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options); var now = DateTimeOffset.UtcNow;
            string[] words = ["praia", "floresta", "montanha", "deserto", "lago", "cidade", "ilha", "vila", "porto", "campo", "rio", "vale", "ponte", "torre", "jardim"];
            var kinds = new[] { "island", "group", "product", "event", "store", "poi", "parcel", "person" };
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < 50_000; i++)
            {
                var title = $"{words[i % 15]} {words[(i / 15) % 15]} {i}";
                db.SearchEntries.Add(new SearchEntry { Id = Guid.NewGuid(), Kind = kinds[i % 8], RefId = Guid.NewGuid(), Title = title, SearchText = title + " " + words[(i / 225) % 15], Popularity = i % 100, SourceUpdatedAt = now, IndexedAt = now });
                if (i % 5_000 == 4_999) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.ChangeTracker.AutoDetectChangesEnabled = false; }
            }
            await db.SaveChangesAsync(); await db.Database.ExecuteSqlRawAsync("ANALYZE discovery.search_index");
            var query = "praia:* & floresta:*"; var all = SearchKindsAll();
            var clock = System.Diagnostics.Stopwatch.StartNew(); var best = long.MaxValue; var found = 0;
            for (var round = 0; round < 5; round++)
            {
                clock.Restart();
                found = (await db.SearchEntries.FromSqlInterpolated($"SELECT * FROM discovery.search_index WHERE NOT hidden AND kind = ANY({all}) AND search_vector @@ to_tsquery('simple', {query}) ORDER BY ts_rank(search_vector, to_tsquery('simple', {query})) DESC, popularity DESC LIMIT 400").AsNoTracking().ToListAsync()).Count;
                best = Math.Min(best, clock.ElapsedMilliseconds);
            }
            Console.WriteLine($"event=search_pg_candidates ms={best} lines=50000"); Assert.Equal(400, found); Assert.True(best < 800, $"the candidates took {best} ms");
            await db.Database.OpenConnectionAsync(); await db.Database.ExecuteSqlRawAsync("SET enable_seqscan = off"); // the planner may read a table this small whole; with that choice taken away the index must be there to answer
            var plan = await db.Database.SqlQuery<string>($"EXPLAIN SELECT id FROM discovery.search_index WHERE search_vector @@ to_tsquery('simple', 'zzzzqq:*')").ToListAsync();
            Assert.True(plan.Any(line => line.Contains("ix_search_index_search_vector")), string.Join(" / ", plan)); // the GIN index can answer a word-beginning query
        }
        finally { await DropAsync(name); }
    }

    [PostgresFact]
    public async Task EightThousandRealThingsAreSearchedByThirtyPeopleAtOnceWithTheFullPrivacyCheck()
    {
        var (name, cs) = await CreateAsync();
        try
        {
            DbContextOptions<LifeVerseDbContext> options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(cs).Options; var now = DateTimeOffset.UtcNow;
            await using var db = new LifeVerseDbContext(options); string[] words = ["praia", "floresta", "montanha", "deserto", "lago", "cidade", "vila", "porto", "campo", "rio"];
            User Make(string u) => new() { Id = Guid.NewGuid(), Username = u, NormalizedUsername = u.ToUpperInvariant(), Email = u + "@t.local", NormalizedEmail = u.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
            var owner = Make("dono"); var viewers = Enumerable.Range(0, 30).Select(i => Make("v" + i)).ToList(); db.Users.Add(owner); db.Users.AddRange(viewers); await db.SaveChangesAsync();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            for (var i = 0; i < 5000; i++) db.Islands.Add(new Island { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = $"{words[i % 10]} {words[(i / 10) % 10]} {i}", Slug = "s" + i, IsPublic = i % 7 != 0, Description = words[(i / 100) % 10], Category = i % 2 == 0 ? "social" : "negocios", CreatedAt = now, UpdatedAt = now });
            for (var i = 0; i < 3000; i++) db.SocialGroups.Add(new SocialGroup { Id = Guid.NewGuid(), OwnerUserId = owner.Id, Name = $"Grupo {words[i % 10]} {i}", NameKey = "g" + i, Privacy = i % 5 == 0 ? "private" : "public", Description = words[(i / 10) % 10], CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(); db.ChangeTracker.AutoDetectChangesEnabled = true;
            var clock = System.Diagnostics.Stopwatch.StartNew(); await SearchIndexer.RunAsync(db, now, default); var indexMs = clock.ElapsedMilliseconds;
            clock.Restart(); var idle = await SearchIndexer.RunAsync(db, now.AddSeconds(30), default); var idleMs = clock.ElapsedMilliseconds; Assert.Equal(0, idle.Written);
            await db.Database.ExecuteSqlRawAsync("ANALYZE discovery.search_index");

            var times = new System.Collections.Concurrent.ConcurrentBag<long>(); var wrong = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Parallel.ForEachAsync(viewers, new ParallelOptions { MaxDegreeOfParallelism = 30 }, async (viewer, ct) =>
            {
                await using var own = new LifeVerseDbContext(options);
                for (var round = 0; round < 5; round++)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew(); var page = await GlobalSearch.SearchAsync(own, viewer.Id, new SearchRequest("praia lago", null, 1, 20, Sort: round % 2 == 0 ? "relevance" : "popularity"), now, ct); times.Add(watch.ElapsedMilliseconds);
                    if (page.Total == 0 || page.Items.Count > 20) wrong.Add("total " + page.Total);
                    if (page.Items.Any(i => i.Kind == "group" && i.Title.Length == 0)) wrong.Add("empty group");
                    watch.Restart(); await GlobalSearch.SuggestAsync(own, viewer.Id, "pra", 8, now, ct); times.Add(watch.ElapsedMilliseconds);
                }
            });
            var sorted = times.OrderBy(t => t).ToList(); var p95 = sorted[(int)(sorted.Count * .95) - 1];
            Console.WriteLine($"event=search_pg_concurrency index_ms={indexMs} idle_ms={idleMs} calls={sorted.Count} p50_ms={sorted[sorted.Count / 2]} p95_ms={p95} max_ms={sorted[^1]}");
            Assert.Empty(wrong); Assert.True(p95 < 2000, $"p95 was {p95} ms");
            // the private things of the same index never come out, whoever asks
            var all = (await GlobalSearch.SearchAsync(db, viewers[0].Id, new SearchRequest("grupo praia", ["group"], 1, 50), now, default)).Items; Assert.NotEmpty(all);
            var hiddenGroups = await db.SocialGroups.Where(g => g.Privacy == "private").Select(g => g.Id).ToListAsync(); Assert.DoesNotContain(all, i => hiddenGroups.Contains(i.Id));
            var closedIslands = await db.Islands.Where(i => !i.IsPublic).Select(i => i.Id).ToListAsync();
            for (var page = 1; page <= 5; page++) Assert.DoesNotContain((await GlobalSearch.SearchAsync(db, viewers[1].Id, new SearchRequest("praia", ["island"], page, 50), now, default)).Items, i => closedIslands.Contains(i.Id));
        }
        finally { await DropAsync(name); }
    }

    private static string[] SearchKindsAll() => LifeVerse.Shared.Search.SearchKinds.All;
}
