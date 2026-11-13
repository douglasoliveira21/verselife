using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The platform plan of an island, managed from the island's window: buying, renewing, and the locks around the money.</summary>
public sealed class IslandHostingTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _admin = Guid.NewGuid(), _tenant = Guid.NewGuid(), _stranger = Guid.NewGuid();
    private readonly Island _island;
    private readonly IslandRentalPlan _basic, _gold;

    public IslandHostingTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner });
        _basic = new IslandRentalPlan { Id = Guid.NewGuid(), Name = "Básico", PriceCoins = 1000, DurationDays = 30, MaxSize = 256, MaxObjects = 5000, MaxPlayers = 30, IsActive = true };
        _gold = new IslandRentalPlan { Id = Guid.NewGuid(), Name = "Ouro", PriceCoins = 5000, DurationDays = 30, MaxSize = 512, MaxObjects = 20000, MaxPlayers = 80, IsActive = true };
        _db.IslandRentalPlans.AddRange(_basic, _gold, new IslandRentalPlan { Id = Guid.NewGuid(), Name = "Antigo", PriceCoins = 1, DurationDays = 1, MaxSize = 64, MaxObjects = 10, MaxPlayers = 2, IsActive = false });
        foreach (var user in new[] { _owner, _admin, _tenant, _stranger }) _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = 10_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private static string Key() => Guid.NewGuid().ToString();
    private Task<CommerceResult> Subscribe(IslandRentalPlan plan, Guid? by = null, string? key = null) => IslandHosting.SubscribeAsync(_db, _island.Id, by ?? _owner, plan.Id, key ?? Key(), _clock, default);
    private Task<CommerceResult> Renew(Guid? by = null, string? key = null) => IslandHosting.RenewAsync(_db, _island.Id, by ?? _owner, key ?? Key(), _clock, default);
    private IslandRental Latest() => _db.IslandRentals.AsNoTracking().OrderByDescending(r => r.ExpiresAt).First();
    private async Task<JsonElement> State(Guid who) { var result = await IslandHosting.StateAsync(_db, _island.Id, who, _clock, default); Assert.True(result.Ok, result.Error); return JsonSerializer.SerializeToElement(result.Data); }

    [Fact]
    public async Task OnlyTheOwnerBuysAPlan_AndOnlyOneRunsAtATime()
    {
        Assert.Equal("forbidden", (await Subscribe(_basic, _admin)).Error); Assert.Equal("forbidden", (await Subscribe(_basic, _tenant)).Error);
        Assert.Equal("bad_key", (await Subscribe(_basic, key: "x")).Error);
        Assert.Equal("plan_not_found", (await IslandHosting.SubscribeAsync(_db, _island.Id, _owner, _db.IslandRentalPlans.Single(p => !p.IsActive).Id, Key(), _clock, default)).Error); // a withdrawn plan cannot be bought
        Assert.Equal("not_found", (await IslandHosting.SubscribeAsync(_db, Guid.NewGuid(), _owner, _basic.Id, Key(), _clock, default)).Error);
        Assert.Equal(10_000, Coins(_owner)); Assert.Empty(_db.IslandRentals);
        Assert.True((await Subscribe(_basic)).Ok);
        Assert.Equal(9_000, Coins(_owner)); var rental = Latest(); Assert.Equal((_owner, _basic.Id, _clock.Now.AddDays(30), _clock.Now.AddDays(37)), (rental.UserId, rental.PlanId, rental.ExpiresAt, rental.RecoveryUntil));
        Assert.Equal("already_hosted", (await Subscribe(_gold)).Error); Assert.Equal(9_000, Coins(_owner)); // renew the one it has instead of paying for a second
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _owner && n.Kind == "hosting_started");
    }

    [Fact]
    public async Task AKeyIsPaidOnceAndMoneyNeverGoesBelowZero()
    {
        var key = Key(); Assert.True((await Subscribe(_basic, key: key)).Ok);
        var again = await Subscribe(_basic, key: key); Assert.True(again.Ok); Assert.Equal(9_000, Coins(_owner)); Assert.Single(_db.IslandRentals); // a double click does nothing twice
        var key2 = Key(); Assert.True((await Renew(key: key2)).Ok); Assert.True((await Renew(key: key2)).Ok); Assert.Equal(8_000, Coins(_owner)); // the same for renewing

        var poor = new Island { Id = Guid.NewGuid(), OwnerUserId = _stranger, Name = "Pobre", Slug = "pobre", IsPublic = true, Size = 256 }; _db.Islands.Add(poor);
        _db.Wallets.Single(w => w.UserId == _stranger).AvailableCoins = 100; await _db.SaveChangesAsync();
        Assert.Equal("insufficient_balance", (await IslandHosting.SubscribeAsync(_db, poor.Id, _stranger, _basic.Id, Key(), _clock, default)).Error);
        _db.Wallets.Single(w => w.UserId == _stranger).IsBlocked = true; await _db.SaveChangesAsync();
        Assert.Equal("wallet_blocked", (await IslandHosting.SubscribeAsync(_db, poor.Id, _stranger, _basic.Id, Key(), _clock, default)).Error);
        Assert.Equal(100, Coins(_stranger));
        // the money went to the platform in a balanced ledger transaction
        var ledger = _db.LedgerTransactions.AsNoTracking().Single(t => t.IdempotencyKey == key); var entries = _db.LedgerEntries.AsNoTracking().Where(e => e.TransactionId == ledger.Id).ToList();
        Assert.Equal((1000, 1000, 2), (entries.Sum(e => e.Debit), entries.Sum(e => e.Credit), entries.Count));
    }

    [Fact]
    public async Task RenewingAddsATimeAfterTheEnd_WorksInTheWeekOfRecovery_AndNotAfter()
    {
        Assert.Equal("no_hosting", (await Renew()).Error);
        Assert.True((await Subscribe(_basic)).Ok); var firstEnd = Latest().ExpiresAt;
        Assert.Equal("forbidden", (await Renew(_admin)).Error);
        Assert.True((await Renew()).Ok); Assert.Equal(firstEnd.AddDays(30), Latest().ExpiresAt); Assert.Equal(Latest().ExpiresAt.AddDays(7), Latest().RecoveryUntil); // early renewal does not lose the days left
        _clock.Now = Latest().ExpiresAt.AddDays(3); // ended three days ago
        Assert.Equal("recovery", (await State(_owner)).GetProperty("status").GetString());
        Assert.True((await Renew()).Ok); Assert.Equal(_clock.Now.AddDays(30), Latest().ExpiresAt); // counted from today, not from the old end
        _clock.Now = Latest().ExpiresAt.AddDays(8); // past the week
        Assert.Equal("ended", (await State(_owner)).GetProperty("status").GetString());
        Assert.Equal("hosting_ended", (await Renew()).Error);
        var coins = Coins(_owner); Assert.True((await Subscribe(_gold)).Ok); Assert.Equal(coins - 5000, Coins(_owner)); // after it ended a new plan can be chosen, even another one
        Assert.Equal(_gold.Id, Latest().PlanId);
    }

    [Fact]
    public async Task NoOneRenewsFurtherThanAYearAhead()
    {
        _db.Wallets.Single(w => w.UserId == _owner).AvailableCoins = 1_000_000; await _db.SaveChangesAsync(); // so that only the limit of a year can stop it
        Assert.True((await Subscribe(_basic)).Ok);
        var renewals = 0; while ((await Renew()).Ok && renewals < 30) renewals++;
        Assert.InRange(renewals, 10, 13); Assert.Equal("too_far", (await Renew()).Error);
        Assert.True(Latest().ExpiresAt - _clock.Now <= IslandHosting.MaxAhead + TimeSpan.FromDays(31));
    }

    [Fact]
    public async Task TheWindowShowsThePlanThePlansAndWhatIsPossible()
    {
        var none = await State(_owner); Assert.Equal(("none", true, false), (none.GetProperty("status").GetString(), none.GetProperty("canSubscribe").GetBoolean(), none.GetProperty("canRenew").GetBoolean()));
        Assert.Equal(new[] { "Básico", "Ouro" }, none.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray()); // cheapest first, withdrawn ones left out
        Assert.Equal(10_000, none.GetProperty("balance").GetInt64());
        Assert.True((await Subscribe(_gold)).Ok);
        var active = await State(_owner); Assert.Equal(("active", false, true, "Ouro"), (active.GetProperty("status").GetString(), active.GetProperty("canSubscribe").GetBoolean(), active.GetProperty("canRenew").GetBoolean(), active.GetProperty("current").GetProperty("plan").GetProperty("name").GetString()));
        var staff = await State(_admin); Assert.False(staff.GetProperty("canRenew").GetBoolean()); Assert.Equal(JsonValueKind.Null, staff.GetProperty("balance").ValueKind); // the administrator looks, only the owner pays
        Assert.Equal("forbidden", (await IslandHosting.StateAsync(_db, _island.Id, _stranger, _clock, default)).Error);
    }
}
