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
            Assert.Single(await GlobalSearch.SuggestAsync(db, viewer, "flor", 8, now, default));

            // the vector follows the text when a line is rewritten
            var island = await db.Islands.SingleAsync(i => i.Slug == "praia-grande"); island.Name = "Montanha Alta"; island.UpdatedAt = now.AddMinutes(1); await db.SaveChangesAsync(); await SearchIndexer.RunAsync(db, now.AddMinutes(1), default);
            Assert.Empty((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("praia"), now, default)).Items); Assert.Single((await GlobalSearch.SearchAsync(db, viewer, new SearchRequest("montanha"), now, default)).Items);
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

    private static string[] SearchKindsAll() => LifeVerse.Shared.Search.SearchKinds.All;
}
