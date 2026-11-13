using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Scripting;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Paying a script: the money moves from the player to the OWNER of the object (read from the database), once, only for scripts of that island, with limits, and a repeated request changes nothing.</summary>
public sealed class ScriptPaymentTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["IslandServer:SharedKey"] = Key }).Build();
    private readonly DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _owner, _payer, _stranger, _object = Guid.NewGuid(), _script = Guid.NewGuid(), _foreignScript = Guid.NewGuid();
    private readonly ServerInstance _server;

    public ScriptPaymentTests()
    {
        User Make(string name) { var user = new User { Id = Guid.NewGuid(), Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }; _db.Users.Add(user); return user; }
        (_owner, _payer, _stranger) = (Make("dono").Id, Make("pagador").Id, Make("outro").Id);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Praça", Slug = "praca", IsPublic = true, Size = 400 }; var other = new Island { Id = Guid.NewGuid(), OwnerUserId = _stranger, Name = "Outra", Slug = "outra", IsPublic = true, Size = 400 };
        _server = new ServerInstance { Id = Guid.NewGuid(), IslandId = island.Id, Hostname = "h", Port = 1, Status = "running", StartedAt = _now, LastHeartbeatAt = _now }; var foreign = Guid.NewGuid();
        _db.Islands.AddRange(island, other); _db.ServerInstances.Add(_server);
        _db.BuildObjects.AddRange(new BuildObject { Id = _object, IslandId = island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Máquina", PrimitiveType = "cube" }, new BuildObject { Id = foreign, IslandId = other.Id, CreatorUserId = _stranger, OwnerUserId = _stranger, Name = "Alheio", PrimitiveType = "cube" });
        _db.ObjectScripts.AddRange(new ObjectScript { Id = _script, ObjectId = _object, OwnerUserId = _owner, Name = "maquina", Source = "x", CreatedAt = _now, UpdatedAt = _now }, new ObjectScript { Id = _foreignScript, ObjectId = foreign, OwnerUserId = _stranger, Name = "alheio", Source = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Wallets.AddRange(new Wallet { Id = Guid.NewGuid(), UserId = _payer, AvailableCoins = 500, CreatedAt = _now, UpdatedAt = _now }, new Wallet { Id = Guid.NewGuid(), UserId = _owner, AvailableCoins = 10, CreatedAt = _now, UpdatedAt = _now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HttpContext Island(string key = Key) { var context = new DefaultHttpContext(); context.Request.Headers["X-LifeVerse-Service-Key"] = key; return context; }
    private static int Status(IResult result) => result is IStatusCodeHttpResult { StatusCode: { } code } ? code : result is IValueHttpResult ? 200 : 0;
    private static ScriptPaymentResult Body(IResult result) => ((result as IValueHttpResult)!.Value as ScriptPaymentResult)!;
    private Task<IResult> Pay(Guid script, Guid payer, long amount, string request = "req-00000001", string key = Key) => ScriptingEndpoints.ScriptPaymentAsync(_server.Id, new ScriptPaymentCall(script, payer, amount, request), Island(key), _config, _db, new FixedClock(_now), default);
    private long Balance(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task APaymentMovesTheMoneyToTheOwnerOfTheObject_Once()
    {
        var paid = await Pay(_script, _payer, 120); Assert.Equal(200, Status(paid)); Assert.Equal(("completed", 120L), (Body(paid).Status, Body(paid).Amount));
        Assert.Equal((380L, 130L), (Balance(_payer), Balance(_owner)));
        var tx = await _db.LedgerTransactions.AsNoTracking().SingleAsync(); Assert.Equal(("script_payment", "completed", 120L, _payer), (tx.Type, tx.Status, tx.Amount, tx.InitiatorUserId)); Assert.Contains(_owner.ToString(), tx.MetadataJson); Assert.Contains(_script.ToString(), tx.MetadataJson);
        var again = await Pay(_script, _payer, 120); Assert.Equal("already_processed", Body(again).Status); Assert.Equal(tx.Id, Body(again).TransactionId);                       // a retry of the same request moves nothing
        Assert.Equal((380L, 130L), (Balance(_payer), Balance(_owner))); Assert.Single(await _db.LedgerTransactions.ToListAsync());
        var other = await Pay(_script, _payer, 30, "req-00000002"); Assert.Equal("completed", Body(other).Status); Assert.Equal((350L, 160L), (Balance(_payer), Balance(_owner)));   // a new request is a new payment
        Assert.Equal(2, await _db.ScriptLogs.CountAsync());                                                                                                                     // and the console of the script says so
    }

    [Fact]
    public async Task MoneyIsNeverMadeUpOrOverdrawn()
    {
        var poor = await Pay(_script, _payer, 501); Assert.Equal(409, Status(poor)); Assert.Equal("insufficient_balance", Body(poor).Error); Assert.Equal((500L, 10L), (Balance(_payer), Balance(_owner))); Assert.Empty(await _db.LedgerTransactions.ToListAsync());
        foreach (var amount in new long[] { 0, -5, ScriptingEndpoints.MaxScriptPayment + 1 }) Assert.Equal(400, Status(await Pay(_script, _payer, amount)));
        Assert.Equal(400, Status(await Pay(_script, _payer, 5, "short")));
        Assert.Equal(409, Status(await Pay(_script, _owner, 5)));                                                                                                            // paying yourself is not a payment
        (await _db.Wallets.SingleAsync(w => w.UserId == _owner)).IsBlocked = true; await _db.SaveChangesAsync();
        Assert.Equal("wallet_blocked", Body(await Pay(_script, _payer, 5)).Error); Assert.Equal(500L, Balance(_payer));                                                       // a blocked wallet neither receives nor is skipped
        Assert.Equal(1010L, await _db.Wallets.SumAsync(w => w.AvailableCoins) + 500L);                                                                                      // 510 in the system, none created
    }

    [Fact]
    public async Task OnlyTheIslandServerPays_ForScriptsOfItsOwnIsland_AndWhoReallyExists()
    {
        Assert.Equal(401, Status(await Pay(_script, _payer, 5, key: "x"))); Assert.Equal(404, Status(await Pay(Guid.NewGuid(), _payer, 5))); Assert.Equal(404, Status(await Pay(_foreignScript, _payer, 5)));   // another island's script
        Assert.Equal(404, Status(await Pay(_script, Guid.NewGuid(), 5)));                                                                                                                                   // a payer who does not exist
        Assert.Equal(404, Status(await ScriptingEndpoints.ScriptPaymentAsync(Guid.NewGuid(), new ScriptPaymentCall(_script, _payer, 5, "req-00000001"), Island(), _config, _db, new FixedClock(_now), default)));
        Assert.Equal((500L, 10L), (Balance(_payer), Balance(_owner)));
        // the owner is whoever owns the object now: a sale of the object changes who gets paid
        (await _db.BuildObjects.SingleAsync(o => o.Id == _object)).OwnerUserId = _stranger; await _db.SaveChangesAsync();
        await Pay(_script, _payer, 40); Assert.Equal((460L, 40L), (Balance(_payer), Balance(_stranger)));
    }
}
