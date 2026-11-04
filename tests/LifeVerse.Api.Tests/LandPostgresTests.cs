using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Runs only when LIFEVERSE_TEST_PG is set to a connection string whose user may create databases (for example
/// "Host=localhost;Username=postgres;Password=...;Database=postgres"). Each test gets a throw-away database, migrated from scratch, and drops it.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG"))) Skip = "set LIFEVERSE_TEST_PG to run the tests against a real PostgreSQL"; }
}

public sealed class LandPostgresTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static readonly Clock Time = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));

    private sealed class Scratch : IAsyncDisposable
    {
        private readonly string _admin; private readonly string _name = "lifeverse_t_" + Guid.NewGuid().ToString("N")[..12];
        public string ConnectionString { get; }
        private Scratch(string admin) { _admin = admin; ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _name, Pooling = true, MaxPoolSize = 20 }.ConnectionString; }

        public static async Task<Scratch> CreateAsync()
        {
            var scratch = new Scratch(Environment.GetEnvironmentVariable("LIFEVERSE_TEST_PG")!);
            await using (var connection = new NpgsqlConnection(scratch._admin)) { await connection.OpenAsync(); await using var command = new NpgsqlCommand($"CREATE DATABASE \"{scratch._name}\"", connection); await command.ExecuteNonQueryAsync(); }
            await using var db = scratch.Open(); await db.Database.MigrateAsync();
            return scratch;
        }

        public LifeVerseDbContext Open() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseNpgsql(ConnectionString).Options);

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(_admin); await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)", connection); await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<(Island Island, Guid Seller, Guid[] Buyers, Guid Parcel)> SeedAsync(LifeVerseDbContext db, int buyers = 2)
    {
        var seller = Guid.NewGuid(); var people = Enumerable.Range(0, buyers).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var id in people.Append(seller)) db.Users.Add(new User { Id = id, Email = $"{id:N}@test.local", Username = "u" + id.ToString("N")[..10], NormalizedUsername = "U" + id.ToString("N")[..10], NormalizedEmail = $"{id:N}@TEST.LOCAL", PasswordHash = "x", CreatedAt = Time.GetUtcNow(), UpdatedAt = Time.GetUtcNow() });
        await db.SaveChangesAsync();
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = seller, Name = "Ilha", Slug = "i" + Guid.NewGuid().ToString("N")[..8], IsPublic = true, Size = 256 };
        db.Islands.Add(island);
        foreach (var id in people.Append(seller)) db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 1000, CreatedAt = Time.GetUtcNow(), UpdatedAt = Time.GetUtcNow() });
        await db.SaveChangesAsync();
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x, z));
        var claim = await LandService.ClaimAsync(db, island, seller, "Casa", "", cells, 100, null, null, null, null, null, Time, default);
        Assert.Null(claim.Error);
        return (island, seller, people, claim.Parcel!.Id);
    }

    [PostgresFact]
    public async Task TwoPeopleBuyingTheSameParcelForReal_OnlyOneWinsAndTheMoneyMovesOnce()
    {
        await using var scratch = await Scratch.CreateAsync();
        Guid seller, parcel; Guid[] buyers;
        await using (var db = scratch.Open())
        {
            (_, seller, buyers, parcel) = await SeedAsync(db);
            Assert.True((await LandCommerce.OfferForSaleAsync(db, parcel, seller, new SaleOffer(300, null, "", true), Time, default)).Ok);
        }
        var results = await Task.WhenAll(buyers.Select(async buyer => { await using var db = scratch.Open(); return await LandCommerce.BuyAsync(db, parcel, buyer, Guid.NewGuid().ToString(), 300, Time, default); }));
        Assert.Equal(1, results.Count(r => r.Ok));
        await using var check = scratch.Open();
        var owner = (await check.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == parcel)).OwnerUserId;
        Assert.Contains(owner, buyers);
        Assert.Equal(1300, (await check.Wallets.AsNoTracking().SingleAsync(w => w.UserId == seller)).AvailableCoins);
        Assert.Equal(700, (await check.Wallets.AsNoTracking().SingleAsync(w => w.UserId == owner)).AvailableCoins);
        Assert.Equal(1000, (await check.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyers.Single(b => b != owner))).AvailableCoins); // the loser was rolled back, not charged
    }

    [PostgresFact]
    public async Task ARetryAfterALostAnswerIsNotChargedTwice()
    {
        await using var scratch = await Scratch.CreateAsync();
        await using var db = scratch.Open();
        var (_, seller, buyers, parcel) = await SeedAsync(db, 1);
        await LandCommerce.OfferForSaleAsync(db, parcel, seller, new SaleOffer(300, null, "", true), Time, default);
        var key = Guid.NewGuid().ToString();
        Assert.True((await LandCommerce.BuyAsync(db, parcel, buyers[0], key, 300, Time, default)).Ok);
        await using var retry = scratch.Open(); // the client lost the answer and asks again with the same key
        await LandCommerce.BuyAsync(retry, parcel, buyers[0], key, 300, Time, default);
        await using var check = scratch.Open();
        Assert.Equal(700, (await check.Wallets.AsNoTracking().SingleAsync(w => w.UserId == buyers[0])).AvailableCoins);
        Assert.Equal(1300, (await check.Wallets.AsNoTracking().SingleAsync(w => w.UserId == seller)).AvailableCoins);
    }

    [PostgresFact]
    public async Task TwoAdministratorsAtOnce_EveryRevisionIsUniqueAndCountsOnlySuccesses()
    {
        await using var scratch = await Scratch.CreateAsync();
        Guid seller, parcel;
        await using (var db = scratch.Open()) { (_, seller, _, parcel) = await SeedAsync(db, 0); }
        var modes = new[] { "list", "private", "list", "private", "list", "private" };
        var results = await Task.WhenAll(modes.Select(async mode => { await using var db = scratch.Open(); return await LandAdmin.SetAccessModeAsync(db, parcel, seller, mode, Time, default); }));
        Assert.Contains(results, r => r.Ok);
        await using var check = scratch.Open();
        var stored = await check.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == parcel);
        var changes = await check.ParcelChanges.AsNoTracking().Where(c => c.ParcelId == parcel && c.Operation == "access_mode").Select(c => c.Revision).ToListAsync();
        Assert.Equal(results.Count(r => r.Ok), changes.Count);
        Assert.Equal(changes.Count, changes.Distinct().Count());
        Assert.Equal(1 + changes.Count, stored.Revision);
    }

    [PostgresFact]
    public async Task TheLandSurvivesAFreshConnection_AndThePhase38ColumnsHaveTheirDefaults()
    {
        await using var scratch = await Scratch.CreateAsync();
        Guid seller, parcel;
        await using (var db = scratch.Open()) { (_, seller, _, parcel) = await SeedAsync(db, 0); await LandPresentation.SetArrivalAsync(db, parcel, seller, 2, 2, 90, "forced", Time, default); }
        NpgsqlConnection.ClearAllPools();
        await using var again = scratch.Open();
        var stored = await again.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == parcel);
        Assert.Equal("forced", stored.TeleportMode); Assert.Equal(90f, stored.ArrivalYaw); Assert.Equal("{}", stored.MediaJson);
        Assert.Equal(16, await again.ParcelCells.CountAsync(c => c.ParcelId == parcel));
    }
}
