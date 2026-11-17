using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The limits on transfers, the direction of each entry in a history, and the numbers of the administration.</summary>
public sealed class EconomyLimitsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
    private readonly Guid _me = Guid.NewGuid(), _you = Guid.NewGuid();
    public void Dispose() => _db.Dispose();
    private static IConfiguration Config(params (string, string)[] values) => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Item1, x => (string?)x.Item2)).Build();
    private void Sent(long amount, DateTimeOffset at, Guid? by = null) { _db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("N"), Type = "user_transfer", Status = "completed", Amount = amount, InitiatorUserId = by ?? _me, CounterpartyUserId = _you, CreatedAt = at, CompletedAt = at }); _db.SaveChanges(); }

    [Fact]
    public async Task OneTransferAboveTheLimitIsRefused()
    {
        Assert.False(await EconomyLimits.ExceededAsync(_db, Config(), _me, EconomyLimits.DefaultMaxTransfer, _now, default));
        Assert.True(await EconomyLimits.ExceededAsync(_db, Config(), _me, EconomyLimits.DefaultMaxTransfer + 1, _now, default));
        Assert.True(await EconomyLimits.ExceededAsync(_db, Config(("Economy:Limits:MaxTransfer", "500")), _me, 501, _now, default));
    }

    [Fact]
    public async Task TheDayAddsUpAndStartsAgainAtMidnightUtc()
    {
        var config = Config(("Economy:Limits:DailyTransfer", "1000"));
        Sent(900, _now.AddHours(-2));
        Assert.False(await EconomyLimits.ExceededAsync(_db, config, _me, 100, _now, default)); Assert.True(await EconomyLimits.ExceededAsync(_db, config, _me, 101, _now, default));
        Sent(5000, _now.AddDays(-1)); Sent(5000, _now.AddHours(-1), _you);                                   // yesterday, and somebody else's
        Assert.False(await EconomyLimits.ExceededAsync(_db, config, _me, 100, _now, default));
    }

    [Fact]
    public async Task AWalletCanHaveItsOwnDailyLimit()
    {
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _me, AvailableCoins = 1, DailyTransferLimit = 200, CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        Assert.False(await EconomyLimits.ExceededAsync(_db, Config(), _me, 200, _now, default)); Assert.True(await EconomyLimits.ExceededAsync(_db, Config(), _me, 201, _now, default));
        Sent(150, _now.AddHours(-1)); Assert.True(await EconomyLimits.ExceededAsync(_db, Config(), _me, 51, _now, default));
    }

    [Fact]
    public async Task WalletsMovingMuchMoneyOrTalkingToManyAreFlagged()
    {
        var busy = Guid.NewGuid(); var popular = Guid.NewGuid(); var quiet = Guid.NewGuid();
        _db.Users.Add(new User { Id = busy, Username = "movimentado", NormalizedUsername = "MOVIMENTADO", Email = "m@t.l", NormalizedEmail = "M@T.L", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        for (var i = 0; i < EconomyAdmin.SuspiciousTransferCount; i++) Sent(10, _now.AddMinutes(-i - 1), busy);
        for (var i = 0; i < EconomyAdmin.SuspiciousSenders; i++) _db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("N"), Type = "user_transfer", Status = "completed", Amount = 1, InitiatorUserId = Guid.NewGuid(), CounterpartyUserId = popular, CreatedAt = _now.AddMinutes(-5), CompletedAt = _now });
        Sent(10, _now.AddMinutes(-3), quiet); await _db.SaveChangesAsync();
        var flagged = await EconomyAdmin.SuspiciousDataAsync(_db, Config(), _now, default);
        Assert.Contains(flagged, f => f.UserId == busy && f.Reasons.Contains("many_transfers_sent")); Assert.Contains(flagged, f => f.UserId == popular && f.Reasons.Contains("received_from_many_people")); Assert.DoesNotContain(flagged, f => f.UserId == quiet);
    }

    [Fact]
    public async Task InvalidConfigurationFallsBackToTheDefaults() => Assert.Equal((EconomyLimits.DefaultMaxTransfer, EconomyLimits.DefaultDailyTransfer), EconomyLimits.For(Config(("Economy:Limits:MaxTransfer", "abc"), ("Economy:Limits:DailyTransfer", "-5"))));

    [Fact]
    public void TheDirectionTellsWhereTheCoinsWent()
    {
        LedgerTransaction T(string type, long amount = 10) => new() { Id = Guid.NewGuid(), IdempotencyKey = "k", Type = type, Status = "completed", Amount = amount, InitiatorUserId = _me, CounterpartyUserId = _you, CreatedAt = _now };
        Assert.Equal("out", EconomyEndpoints.Direction(T("user_transfer"), _me)); Assert.Equal("in", EconomyEndpoints.Direction(T("user_transfer"), _you));
        Assert.Equal("in", EconomyEndpoints.Direction(T("coin_purchase"), _me)); Assert.Equal("out", EconomyEndpoints.Direction(T("coin_refund"), _me));
        Assert.Equal("in", EconomyEndpoints.Direction(T("admin_adjustment", 50), _me)); Assert.Equal("out", EconomyEndpoints.Direction(T("admin_adjustment", -50), _me));
    }

    [Fact]
    public async Task TheSummaryCountsCirculationSalesAndOrdersInReview()
    {
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _me, AvailableCoins = 700, CreatedAt = _now, UpdatedAt = _now }); _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _you, AvailableCoins = 300, IsBlocked = true, CreatedAt = _now, UpdatedAt = _now });
        _db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = "a", Type = "coin_purchase", Status = "completed", Amount = 2750, CreatedAt = _now, CompletedAt = _now });
        _db.CoinOrders.Add(new CoinOrder { Id = Guid.NewGuid(), UserId = _me, PackageId = "x", Coins = 2750, PriceCents = 2500, Method = "pix", Provider = "test", Status = "paid", IdempotencyKey = "o1", CreatedAt = _now, ExpiresAt = _now });
        _db.CoinOrders.Add(new CoinOrder { Id = Guid.NewGuid(), UserId = _me, PackageId = "x", Coins = 10, PriceCents = 10, Method = "pix", Provider = "test", Status = "chargeback_review", IdempotencyKey = "o2", CreatedAt = _now, ExpiresAt = _now });
        await _db.SaveChangesAsync();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(await EconomyAdmin.SummaryDataAsync(_db, _now, default));
        Assert.Equal((1000L, 2, 1, 2750L, 2500L, 1), (json.GetProperty("coinsInWallets").GetInt64(), json.GetProperty("wallets").GetInt32(), json.GetProperty("blockedWallets").GetInt32(), json.GetProperty("coinsSoldTotal").GetInt64(), json.GetProperty("revenueCentsTotal").GetInt64(), json.GetProperty("ordersInReview").GetInt32()));
    }
}
