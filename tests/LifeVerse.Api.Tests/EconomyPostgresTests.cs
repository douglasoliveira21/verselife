using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Scripting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>
/// The money against a real PostgreSQL, with real concurrency (every operation has its own connection and runs at the same time). Runs only when LIFEVERSE_TEST_PG is set (see <see cref="PostgresFactAttribute"/>).
/// What is proven: a webhook delivered many times credits once; a balance cannot be spent twice; transfers in parallel keep the total and every wallet equals what the ledger says; a retry with the same key moves money once; and a failure in the middle leaves nothing behind.
/// </summary>
public sealed class EconomyPostgresTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private static readonly Clock Time = new(Now);
    private const string ServiceKey = "0123456789abcdef0123456789abcdef";

    private sealed class Scratch : IAsyncDisposable
    {
        private readonly string _admin; private readonly string _name = "lifeverse_e_" + Guid.NewGuid().ToString("N")[..12];
        public string ConnectionString { get; }
        private Scratch(string admin) { _admin = admin; ConnectionString = new NpgsqlConnectionStringBuilder(admin) { Database = _name, Pooling = true, MaxPoolSize = 40 }.ConnectionString; }
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

    private static Guid NewUser(LifeVerseDbContext db, long coins)
    {
        var id = Guid.NewGuid(); var name = "u" + id.ToString("N")[..10];
        db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.l", NormalizedEmail = $"{name}@T.L".ToUpperInvariant(), PasswordHash = "x", CreatedAt = Now, UpdatedAt = Now });
        db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = coins, CreatedAt = Now, UpdatedAt = Now });
        return id;
    }

    private static async Task<long> Balance(Scratch s, Guid user) { await using var db = s.Open(); return await db.Wallets.AsNoTracking().Where(w => w.UserId == user).Select(w => w.AvailableCoins).SingleAsync(); }
    private static HttpContext As(Guid user) { var context = new DefaultHttpContext(); context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")); return context; }
    private static int Status(IResult r) => r is IStatusCodeHttpResult { StatusCode: { } code } ? code : r is IValueHttpResult ? 200 : 0;

    [PostgresFact]
    public async Task ThePaymentWebhookDeliveredManyTimesAtOnceCreditsOnce()
    {
        await using var scratch = await Scratch.CreateAsync(); var provider = new TestPaymentProvider(); Guid user; CoinOrder order;
        await using (var db = scratch.Open()) { user = NewUser(db, 0); await db.SaveChangesAsync(); order = (await CoinPurchases.CreateAsync(db, provider, user, CoinPackages.Defaults.Single(p => p.Id == "intermediario"), "pix", "order-key-0001", Now, default)).Order; }
        var outcomes = new System.Collections.Concurrent.ConcurrentBag<PaymentOutcome>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 12), new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (_, ct) => { await using var db = scratch.Open(); outcomes.Add(await CoinPurchases.ApplyAsync(db, provider, new WebhookEvent(order.ProviderRef!, "paid", order.PriceCents), Now.AddMinutes(1), ct)); });
        Assert.Equal(1, outcomes.Count(o => o == PaymentOutcome.Credited)); Assert.All(outcomes, o => Assert.True(o is PaymentOutcome.Credited or PaymentOutcome.AlreadyApplied));
        Assert.Equal(2750, await Balance(scratch, user));
        await using var check = scratch.Open(); Assert.Equal(1, await check.LedgerTransactions.CountAsync(t => t.Type == "coin_purchase")); Assert.Equal(1, await check.PropertyNotices.CountAsync(n => n.UserId == user && n.Kind == "coins_credited"));
    }

    [PostgresFact]
    public async Task AFailureInTheMiddleOfCreditingLeavesNothingBehind()
    {
        await using var scratch = await Scratch.CreateAsync(); var provider = new TestPaymentProvider(); Guid user; CoinOrder order;
        await using (var db = scratch.Open())
        {
            user = NewUser(db, 0); await db.SaveChangesAsync(); order = (await CoinPurchases.CreateAsync(db, provider, user, CoinPackages.Defaults.Single(p => p.Id == "basico"), "pix", "order-key-0002", Now, default)).Order;
            // a previous attempt left its ledger entry behind (same key), so the credit now cannot be written: the whole step has to be undone
            db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = $"coin-order:{order.Id}", Type = "coin_purchase", Status = "completed", Amount = order.Coins, InitiatorUserId = user, CreatedAt = Now }); await db.SaveChangesAsync();
        }
        await using (var db = scratch.Open()) Assert.Equal(PaymentOutcome.AlreadyApplied, await CoinPurchases.ApplyAsync(db, provider, new WebhookEvent(order.ProviderRef!, "paid", order.PriceCents), Now, default));
        Assert.Equal(0, await Balance(scratch, user));
        await using var check = scratch.Open(); Assert.Equal("pending", (await check.CoinOrders.AsNoTracking().SingleAsync()).Status);
    }

    [PostgresFact]
    public async Task ABalanceCannotBeSpentTwiceByPayoutsAtTheSameMoment()
    {
        await using var scratch = await Scratch.CreateAsync(); Guid owner, winner, script; ServerInstance server;
        await using (var db = scratch.Open())
        {
            owner = NewUser(db, 1000); winner = NewUser(db, 0); await db.SaveChangesAsync();
            var island = new Island { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 }; db.Islands.Add(island); await db.SaveChangesAsync();
            server = new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = Now, LastHeartbeatAt = Now }; db.ServerInstances.Add(server);
            var obj = new BuildObject { Id = Guid.NewGuid(), IslandId = island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Caça-níquel", PrimitiveType = "cube" }; db.BuildObjects.Add(obj); await db.SaveChangesAsync();
            script = Guid.NewGuid(); db.ObjectScripts.Add(new ObjectScript { Id = script, ObjectId = obj.Id, OwnerUserId = owner, Name = "premio", Source = "x", CreatedAt = Now, UpdatedAt = Now });
            db.ScriptPayoutAuthorizations.Add(new ScriptPayoutAuthorization { Id = Guid.NewGuid(), ScriptId = script, OwnerUserId = owner, MaxPerPayment = 400, MaxPerDay = 100_000, ExpiresAt = Now.AddDays(1), CreatedAt = Now }); await db.SaveChangesAsync();
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = ServiceKey }).Build();
        var statuses = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 8), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (i, ct) =>
        {
            var http = new DefaultHttpContext(); http.Request.Headers["X-LifeVerse-Service-Key"] = ServiceKey; await using var db = scratch.Open();
            statuses.Add(Status(await ScriptingEndpoints.ScriptPayoutAsync(server.Id, new ScriptPayoutCall(script, winner, 400, $"request-{i:D8}"), http, config, db, Time, ct)));
        });
        var paid = await Balance(scratch, winner); var left = await Balance(scratch, owner);
        Assert.True(paid <= 1000 && left >= 0 && paid + left == 1000, $"nothing is created or lost: owner {left}, winner {paid}");
        Assert.Equal(paid / 400, statuses.Count(s => s == 200)); Assert.All(statuses, s => Assert.True(s is 200 or 409));
        await using var check = scratch.Open(); Assert.Equal(paid / 400, await check.LedgerTransactions.CountAsync(t => t.Type == "script_payout"));
    }

    [PostgresFact]
    public async Task TheSameTransferSentManyTimesAtOnceMovesTheMoneyOnce()
    {
        await using var scratch = await Scratch.CreateAsync(); Guid a, b;
        await using (var db = scratch.Open()) { a = NewUser(db, 1000); b = NewUser(db, 0); await db.SaveChangesAsync(); }
        var statuses = new System.Collections.Concurrent.ConcurrentBag<int>(); var config = new ConfigurationBuilder().Build();
        await Parallel.ForEachAsync(Enumerable.Range(0, 10), new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (_, ct) => { await using var db = scratch.Open(); statuses.Add(Status(await EconomyEndpoints.TransferAsync(new TransferRequest(b, 300, "same-key-0001"), As(a), db, config, Time, ct))); });
        Assert.All(statuses, s => Assert.True(s is 200 or 409, $"a lost race is an answer, not an error (got {s})"));
        Assert.Equal((700L, 300L), (await Balance(scratch, a), await Balance(scratch, b)));
        await using var check = scratch.Open(); Assert.Equal(1, await check.LedgerTransactions.CountAsync(t => t.IdempotencyKey == "same-key-0001"));
    }

    [PostgresFact]
    public async Task ManyTransfersAtOnceKeepTheTotalAndEveryWalletMatchesTheLedger()
    {
        await using var scratch = await Scratch.CreateAsync(); var people = new List<Guid>();
        await using (var db = scratch.Open()) { for (var i = 0; i < 6; i++) people.Add(NewUser(db, 1000)); await db.SaveChangesAsync(); }
        var config = new ConfigurationBuilder().Build(); var statuses = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Parallel.ForEachAsync(Enumerable.Range(0, 90), new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (i, ct) =>
        {
            var random = new Random(i); var from = people[random.Next(people.Count)]; var to = people[(people.IndexOf(from) + 1 + random.Next(people.Count - 1)) % people.Count];
            await using var db = scratch.Open(); statuses.Add(Status(await EconomyEndpoints.TransferAsync(new TransferRequest(to, 50 + random.Next(400), $"mass-key-{i:D6}"), As(from), db, config, Time, ct)));
        });
        Assert.All(statuses, s => Assert.True(s is 200 or 409, $"got {s}"));
        await using var check = scratch.Open(); var wallets = await check.Wallets.AsNoTracking().Where(w => people.Contains(w.UserId)).ToListAsync(); var ledger = await check.LedgerTransactions.AsNoTracking().Where(t => t.Type == "user_transfer").ToListAsync();
        Assert.Equal(6000, wallets.Sum(w => w.AvailableCoins)); Assert.All(wallets, w => Assert.True(w.AvailableCoins >= 0));
        Assert.Equal(statuses.Count(s => s == 200), ledger.Count);
        foreach (var w in wallets) Assert.Equal(1000 - ledger.Where(t => t.InitiatorUserId == w.UserId).Sum(t => t.Amount) + ledger.Where(t => t.CounterpartyUserId == w.UserId).Sum(t => t.Amount), w.AvailableCoins);
    }
}
