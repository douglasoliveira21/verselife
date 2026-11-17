using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>A script paying out of the owner's wallet (economy.pay): only with the owner's authorization, inside its limits, from the owner's wallet only, once per request.</summary>
public sealed class ScriptPayoutTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = Key }).Build();
    private readonly DateTimeOffset _now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner, _winner, _object = Guid.NewGuid(), _script = Guid.NewGuid();
    private readonly ServerInstance _server;
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    public ScriptPayoutTests()
    {
        User Make(string name) { var user = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }; _db.Users.Add(user); return user; }
        (_owner, _winner) = (Make("dono").Id, Make("ganhador").Id);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 };
        _server = new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = _now, LastHeartbeatAt = _now };
        _db.Islands.Add(island); _db.ServerInstances.Add(_server);
        _db.BuildObjects.Add(new BuildObject { Id = _object, IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Caça-níquel", PrimitiveType = "cube" });
        _db.ObjectScripts.Add(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "premio", Source = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Wallets.AddRange(new Wallet { Id = Guid.NewGuid(), UserId = _owner, AvailableCoins = 1000, CreatedAt = _now, UpdatedAt = _now }, new Wallet { Id = Guid.NewGuid(), UserId = _winner, AvailableCoins = 0, CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext Island(string key = Key) { var context = new DefaultHttpContext(); context.Request.Headers["X-LifeVerse-Service-Key"] = key; return context; }
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    private static ScriptPaymentResult Body(IResult result) => ((result as IValueHttpResult)!.Value as ScriptPaymentResult)!;
    private Task<IResult> Payout(long amount, string request = "req-00000001", Guid? script = null, string key = Key, TimeSpan? later = null) =>
        ScriptingEndpoints.ScriptPayoutAsync(_server.Id, new ScriptPayoutCall(script ?? _script, _winner, amount, request), Island(key), _config, _db, new FixedClock(_now + (later ?? TimeSpan.Zero)), default);
    private void Authorize(long perPayment = 100, long perDay = 300, int days = 7, DateTimeOffset? revoked = null) { _db.ScriptPayoutAuthorizations.Add(new ScriptPayoutAuthorization { Id = Guid.NewGuid(), ScriptId = _script, OwnerUserId = _owner, MaxPerPayment = perPayment, MaxPerDay = perDay, ExpiresAt = _now.AddDays(days), CreatedAt = _now, RevokedAt = revoked }); _db.SaveChanges(); }
    private long Balance(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;

    [Fact]
    public async Task WithoutTheOwnersAuthorizationNothingMoves()
    {
        var result = await Payout(50); Assert.Equal(403, Status(result)); Assert.Equal("not_authorized", Body(result).Error);
        Assert.Equal((1000L, 0L), (Balance(_owner), Balance(_winner))); Assert.Empty(_db.LedgerTransactions);
    }

    [Fact]
    public async Task AnAuthorizedPayoutLeavesTheOwnersWalletAndReachesTheWinnerOnce()
    {
        Authorize();
        var paid = await Payout(50); Assert.Equal(200, Status(paid)); Assert.Equal(("completed", 50L), (Body(paid).Status, Body(paid).Amount));
        Assert.Equal((950L, 50L), (Balance(_owner), Balance(_winner)));
        var again = await Payout(50); Assert.Equal("already_processed", Body(again).Status); Assert.Equal((950L, 50L), (Balance(_owner), Balance(_winner)));
        var tx = await _db.LedgerTransactions.AsNoTracking().SingleAsync(); Assert.Equal(("script_payout", _owner, _winner), (tx.Type, tx.InitiatorUserId, tx.CounterpartyUserId));
        Assert.Single(_db.PropertyNotices.Where(n => n.UserId == _winner && n.Kind == "payment_received"));
    }

    [Fact]
    public async Task TheLimitOfEachPaymentAndOfTheDayAreKept()
    {
        Authorize(perPayment: 100, perDay: 150);
        Assert.Equal("over_payment_limit", Body(await Payout(101, "req-00000001")).Error);
        Assert.Equal(200, Status(await Payout(100, "req-00000002")));
        Assert.Equal("over_daily_limit", Body(await Payout(51, "req-00000003")).Error);
        Assert.Equal(200, Status(await Payout(50, "req-00000004")));
        Assert.Equal(200, Status(await Payout(100, "req-00000005", later: TimeSpan.FromDays(1))));                                // the next day starts again
        Assert.Equal((750L, 250L), (Balance(_owner), Balance(_winner)));
    }

    [Fact]
    public async Task ARevokedOrExpiredAuthorizationDoesNotWork()
    {
        Authorize(revoked: _now.AddHours(-1)); Assert.Equal("not_authorized", Body(await Payout(10)).Error);
        Authorize(days: 1); Assert.Equal(200, Status(await Payout(10, "req-00000002")));
        Assert.Equal("not_authorized", Body(await Payout(10, "req-00000003", later: TimeSpan.FromDays(2))).Error);
    }

    [Fact]
    public async Task OnlyTheOwnersWalletPaysAndABrokeOrBlockedOwnerPaysNothing()
    {
        Authorize(perPayment: 5000, perDay: 5000);
        Assert.Equal("insufficient_balance", Body(await Payout(2000)).Error);
        var wallet = _db.Wallets.Single(w => w.UserId == _owner); wallet.IsBlocked = true; _db.SaveChanges();
        Assert.Equal("wallet_blocked", Body(await Payout(10, "req-00000002")).Error);
        Assert.Equal((1000L, 0L), (Balance(_owner), Balance(_winner)));
    }

    [Fact]
    public async Task TheOwnerCannotBePaidAndAnUnknownScriptOrKeyIsRefused()
    {
        Authorize();
        var self = await ScriptingEndpoints.ScriptPayoutAsync(_server.Id, new ScriptPayoutCall(_script, _owner, 10, "req-00000009"), Island(), _config, _db, new FixedClock(_now), default); Assert.Equal("own_object", Body(self).Error);
        Assert.Equal(404, Status(await Payout(10, "req-00000002", script: Guid.NewGuid())));
        Assert.Equal(401, Status(await Payout(10, "req-00000003", key: "wrong")));
        Assert.Equal(400, Status(await Payout(0, "req-00000004"))); Assert.Equal(400, Status(await Payout(ScriptingEndpoints.MaxScriptPayment + 1, "req-00000005")));
        Assert.Equal((1000L, 0L), (Balance(_owner), Balance(_winner)));
    }
}
