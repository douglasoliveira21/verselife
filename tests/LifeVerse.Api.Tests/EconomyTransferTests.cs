using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Transfers between players as the API runs them: the money moves once, nothing moves when it must not, a retry is harmless, and losing a race on a wallet is "try again", never a server error.</summary>
public sealed class EconomyTransferTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private readonly string _store = Guid.NewGuid().ToString();
    private readonly IConfiguration _config = new ConfigurationBuilder().Build();
    private readonly Guid _a = Guid.NewGuid(), _b = Guid.NewGuid();

    private LifeVerseDbContext Open() => new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(_store).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);

    private LifeVerseDbContext Seed(long a = 1000, long b = 0)
    {
        var db = Open();
        foreach (var (id, name) in new[] { (_a, "ana"), (_b, "bia") }) db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.l", NormalizedEmail = $"{name}@T.L", PasswordHash = "x", CreatedAt = Now, UpdatedAt = Now });
        db.Wallets.AddRange(new Wallet { Id = Guid.NewGuid(), UserId = _a, AvailableCoins = a, CreatedAt = Now, UpdatedAt = Now }, new Wallet { Id = Guid.NewGuid(), UserId = _b, AvailableCoins = b, CreatedAt = Now, UpdatedAt = Now });
        db.SaveChanges(); return db;
    }

    private static HttpContext As(Guid user) { var context = new DefaultHttpContext(); context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")); return context; }
    private static int Status(IResult r) => r is IStatusCodeHttpResult { StatusCode: { } code } ? code : r is IValueHttpResult ? 200 : 0;
    private Task<IResult> Send(LifeVerseDbContext db, Guid from, Guid to, long amount, string key = "key-00000001", string? message = null) => EconomyEndpoints.TransferAsync(new TransferRequest(to, amount, key, message), As(from), db, _config, new FixedClock(Now), default);
    private static long Balance(LifeVerseDbContext db, Guid user) => db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;

    [Fact]
    public async Task ATransferMovesTheMoneyOnceEvenIfSentAgain()
    {
        using var db = Seed();
        Assert.Equal(200, Status(await Send(db, _a, _b, 300))); Assert.Equal((700L, 300L), (Balance(db, _a), Balance(db, _b)));
        for (var i = 0; i < 3; i++) Assert.Equal(200, Status(await Send(db, _a, _b, 300))); Assert.Equal((700L, 300L), (Balance(db, _a), Balance(db, _b)));
        Assert.Equal(1, await db.LedgerTransactions.CountAsync(x => x.Type == "user_transfer"));
    }

    [Fact]
    public async Task NothingMovesWhenItMustNot()
    {
        using var db = Seed(100, 0);
        Assert.Equal(409, Status(await Send(db, _a, _b, 101, "key-00000001")));                     // not enough
        Assert.Equal(400, Status(await Send(db, _a, _a, 10, "key-00000002")));                      // to oneself
        Assert.Equal(400, Status(await Send(db, _a, _b, 0, "key-00000003"))); Assert.Equal(400, Status(await Send(db, _a, _b, -5, "key-00000004")));
        Assert.Equal(404, Status(await Send(db, _a, Guid.NewGuid(), 10, "key-00000005")));          // nobody there
        Assert.Equal(409, Status(await Send(db, _a, _b, EconomyLimits.DefaultMaxTransfer + 1, "key-00000006")));
        db.Wallets.Single(w => w.UserId == _b).IsBlocked = true; db.SaveChanges();
        Assert.Equal(409, Status(await Send(db, _a, _b, 10, "key-00000007")));                     // a blocked wallet neither pays nor receives
        Assert.Equal((100L, 0L), (Balance(db, _a), Balance(db, _b))); Assert.Empty(db.LedgerTransactions);
    }

    [Fact]
    public async Task TheMessageAndTheNoticeAreKeptAndTheMessageIsCut()
    {
        using var db = Seed();
        await Send(db, _a, _b, 50, message: new string('x', 500));
        var tx = await db.LedgerTransactions.AsNoTracking().SingleAsync(); Assert.Contains(new string('x', 200), tx.MetadataJson); Assert.DoesNotContain(new string('x', 201), tx.MetadataJson);
        var notice = await db.PropertyNotices.AsNoTracking().SingleAsync(n => n.UserId == _b); Assert.Equal("payment_received", notice.Kind); Assert.Contains("ana", notice.Body);
    }

    [Fact]
    public async Task TheDayLimitStopsAThiefWhoEmptiesAWalletInPieces()
    {
        using var db = Seed(10_000_000);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Economy:Limits:DailyTransfer"] = "1000" }).Build();
        Task<IResult> Pay(long n, string key) => EconomyEndpoints.TransferAsync(new TransferRequest(_b, n, key), As(_a), db, config, new FixedClock(Now), default);
        Assert.Equal(200, Status(await Pay(600, "key-00000001"))); Assert.Equal(200, Status(await Pay(400, "key-00000002"))); Assert.Equal(409, Status(await Pay(1, "key-00000003")));
        Assert.Equal(1000, Balance(db, _b));
    }

    [Fact]
    public async Task LosingTheRaceOnAWalletIsTryAgainAndKeepsNothing()
    {
        using var first = Seed(1000, 0); using var second = Open();
        // the second operation reads the wallet, then another one changes it before the second can save
        var wallet = await second.Wallets.SingleAsync(w => w.UserId == _a); var sneaky = await first.Wallets.SingleAsync(w => w.UserId == _a); sneaky.AvailableCoins -= 900; await first.SaveChangesAsync();
        await using var tx = await second.Database.BeginTransactionAsync(); wallet.AvailableCoins -= 800; second.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = "lost", Type = "user_transfer", Status = "completed", Amount = 800, CreatedAt = Now });
        Assert.False(await EconomyConcurrency.CommitAsync(second, tx, default));
        using var check = Open(); Assert.Equal(100, Balance(check, _a));       // the other operation's balance stands; the in-memory store has no transactions, so that the ledger entry is rolled back too is proven in the PostgreSQL tests
    }
}

public sealed class EconomyRaceClassificationTests
{
    [Fact]
    public void OnlyRealRacesAreTreatedAsTryAgain()
    {
        Assert.True(EconomyConcurrency.IsRace(new DbUpdateConcurrencyException()));
        foreach (var state in new[] { "23505", "40001", "40P01" }) Assert.True(EconomyConcurrency.IsRace(new DbUpdateException("x", new Npgsql.PostgresException("m", "ERROR", "ERROR", state))), state);
        Assert.False(EconomyConcurrency.IsRace(new DbUpdateException("x", new Npgsql.PostgresException("m", "ERROR", "ERROR", "23503"))));      // a missing foreign key is a bug, not a race
        Assert.False(EconomyConcurrency.IsRace(new InvalidOperationException()));
    }
}

public sealed class EconomyClientCannotDecideTests
{
    [Fact]
    public void NoRequestOfTheEconomyLetsTheClientNameWhoPaysOrWhatTheBalanceIs()
    {
        var requests = new[] { typeof(TransferRequest), typeof(PurchaseRequest), typeof(CoinOrderRequest), typeof(LifeVerse.Api.Modules.Scripting.PayoutAuthorizationRequest), typeof(WalletAdjustRequest), typeof(WalletBlockRequest) };
        foreach (var type in requests)
            foreach (var property in type.GetProperties())
                Assert.DoesNotMatch("(?i)^(payer|sender|from|owner|balance|available|wallet|user)", property.Name);
    }
}
