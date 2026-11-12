using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the real estate tells people: warnings before a rental runs out (said once per date), notices of sales, rentals, grace and end, the automatic renewal of island rentals, and reading the notices.</summary>
public sealed class PropertyNoticesTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _alice = Guid.NewGuid(), _bruno = Guid.NewGuid(), _carla = Guid.NewGuid();
    private readonly Island _island;
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private int _slot;

    public PropertyNoticesTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _alice, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var (id, name) in new[] { (_alice, "alice"), (_bruno, "bruno"), (_carla, "carla") })
        {
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now });
            _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 100_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private IslandParcel Claim(string name)
    {
        _clock.Now = _clock.Now.AddMinutes(1); var x0 = -30 + (_slot % 4) * 14; var z0 = -30 + (_slot / 4) * 14; _slot++;
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x0 + x, z0 + z));
        var result = LandService.ClaimAsync(_db, _island, _alice, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); var parcel = _db.IslandParcels.Single(p => p.Id == result.Parcel!.Id); parcel.Code = $"L-{_slot:00}"; _db.SaveChanges(); return parcel;
    }

    private async Task<Guid> RentParcel(IslandParcel parcel, Guid tenant, int days = 10, int periods = 1, bool auto = false, int grace = 2)
    {
        Assert.True((await LandCommerce.OfferForRentAsync(_db, parcel.Id, _alice, new RentOffer(100, days, 1, 6, 50, grace, true), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, parcel.Id, tenant, periods, auto, Guid.NewGuid().ToString(), 100, _clock, default)).Ok);
        return (await _db.ParcelRentals.AsNoTracking().SingleAsync(r => r.ParcelId == parcel.Id && r.Status == "active")).Id;
    }

    private Task<object> NoticesOf(Guid user) => PropertyNotices.ListAsync(_db, user, 50, default);
    private async Task<JsonElement> Notices(Guid user) => JsonSerializer.SerializeToElement(await NoticesOf(user), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private async Task<int> Count(Guid user, string? kind = null) => await _db.PropertyNotices.AsNoTracking().CountAsync(n => n.UserId == user && (kind == null || n.Kind == kind));

    // ---- warnings before the end ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARentalThatEndsSoonWarnsTheRenterAndTheOwnerOnce_AndAgainWhenItIsAboutToEndInADay()
    {
        var parcel = Claim("Loja"); await RentParcel(parcel, _bruno, days: 10); // ends in 10 days
        Assert.Equal(0, await PropertyNotices.ScanAsync(_db, _clock.Now, default)); // too far away
        var threeDays = _clock.Now.AddDays(7.5); Assert.Equal(2, await PropertyNotices.ScanAsync(_db, threeDays, default));
        Assert.Equal(1, await Count(_bruno, "rent_expiring")); Assert.Equal(1, await Count(_alice, "rent_expiring_owner"));
        var tenant = (await Notices(_bruno)).GetProperty("items")[0]; Assert.Contains("Seu aluguel de L-01 — Loja vence em", tenant.GetProperty("title").GetString()); Assert.Contains("Renove", tenant.GetProperty("body").GetString());
        Assert.Contains("bruno", (await Notices(_alice)).GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(0, await PropertyNotices.ScanAsync(_db, threeDays.AddHours(3), default)); // said once
        Assert.Equal(2, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(9.2), default)); // the one-day warning is a new notice
        Assert.Equal(2, await Count(_bruno, "rent_expiring")); Assert.Equal(2, await Count(_alice, "rent_expiring_owner"));
    }

    [Fact]
    public async Task ARenewalMovesTheDateSoTheNextWarningIsSaidAgain_AndEndedOrFarContractsAreQuiet()
    {
        var parcel = Claim("Loja"); var id = await RentParcel(parcel, _bruno, days: 4);
        Assert.Equal(2, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(1.5), default)); // inside the three days
        Assert.True((await LandCommerce.RenewAsync(_db, id, _bruno, 1, Guid.NewGuid().ToString(), _clock, default)).Ok); // 8 days now
        Assert.Equal(0, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(1.5), default));
        Assert.Equal(2, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(6), default)); // the new date is close: a new notice
        var rental = await _db.ParcelRentals.SingleAsync(); rental.Status = "ended"; await _db.SaveChangesAsync();
        Assert.Equal(0, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(7.5), default)); // nothing for an ended contract
    }

    [Fact]
    public async Task IslandLeasesAreWarnedToo()
    {
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 5, 1, 4, 0, 1, true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _bruno, 1, Guid.NewGuid().ToString(), 500, _clock, default)).Ok);
        Assert.Equal(2, await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(3), default));
        Assert.Contains("a ilha Ilha Tropical", (await Notices(_bruno)).GetProperty("items")[0].GetProperty("title").GetString());
    }

    // ---- what happened ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ASaleAndARentalTellTheOwner()
    {
        var parcel = Claim("Casa"); Assert.True((await LandCommerce.OfferForSaleAsync(_db, parcel.Id, _alice, new SaleOffer(7000, null, "", true), _clock, default)).Ok);
        Assert.True((await LandCommerce.BuyAsync(_db, parcel.Id, _bruno, Guid.NewGuid().ToString(), 7000, _clock, default)).Ok);
        var sold = (await Notices(_alice)).GetProperty("items")[0]; Assert.Equal("sold", sold.GetProperty("kind").GetString()); Assert.Contains("L$ 7.000", sold.GetProperty("title").GetString()); Assert.Contains("bruno", sold.GetProperty("body").GetString());
        var other = Claim("Loja"); await RentParcel(other, _carla);
        Assert.Equal(1, await Count(_alice, "rented")); Assert.Equal(0, await Count(_carla)); // the renter is told by the screen that they just used
    }

    [Fact]
    public async Task AnIslandSaleAndAnIslandRentalTellTheOwner()
    {
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 10, 1, 4, 0, 1, false), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _bruno, 1, Guid.NewGuid().ToString(), 500, _clock, default)).Ok);
        Assert.Equal(1, await Count(_alice, "rented")); Assert.Contains("A sua ilha Ilha Tropical foi alugada", (await Notices(_alice)).GetProperty("items")[0].GetProperty("title").GetString());
        var other = new Island { Id = Guid.NewGuid(), OwnerUserId = _carla, Name = "Ilha Pequena", Slug = "pequena", IsPublic = true, Size = 128 }; _db.Islands.Add(other); await _db.SaveChangesAsync();
        Assert.True((await IslandCommerce.OfferForSaleAsync(_db, other.Id, _carla, new SaleOffer(9000, null, "", true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.BuyAsync(_db, _storage, other.Id, _bruno, Guid.NewGuid().ToString(), 9000, _clock, default)).Ok);
        Assert.Contains("foi vendida por L$ 9.000", (await Notices(_carla)).GetProperty("items")[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task GraceAndTheEndAreSaidToBothSides_ForLandAndForIslands()
    {
        var parcel = Claim("Loja"); await RentParcel(parcel, _bruno, days: 5, grace: 2);
        Assert.Equal(1, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(5).AddHours(1), default));
        Assert.Equal(1, await Count(_bruno, "rent_grace")); Assert.Equal(1, await Count(_alice, "rent_grace_owner"));
        Assert.Equal(1, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(8), default));
        Assert.Equal(1, await Count(_bruno, "rent_ended")); Assert.Equal(1, await Count(_alice, "rent_ended_owner")); Assert.Contains("terminou", (await _db.PropertyNotices.AsNoTracking().FirstAsync(n => n.Kind == "rent_ended")).Title);

        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 5, 1, 4, 0, 1, false), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _carla, 1, Guid.NewGuid().ToString(), 500, _clock, default)).Ok);
        var start = _clock.Now;
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, start.AddDays(5).AddHours(1), default)); Assert.Equal(1, await Count(_carla, "rent_grace"));
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, start.AddDays(7), default)); Assert.Equal(1, await Count(_carla, "rent_ended")); Assert.Equal(1, await Count(_alice, "rent_ended_owner") - 1);
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NoticesAreReadOnlyByTheirOwnerAndCanBeMarkedOneByOneOrAllTogether()
    {
        var parcel = Claim("Loja"); await RentParcel(parcel, _bruno); await PropertyNotices.ScanAsync(_db, _clock.Now.AddDays(8), default);
        var first = await Notices(_alice); Assert.Equal(2, first.GetProperty("unread").GetInt32()); // the "rented" one and the warning
        Assert.Equal(0, (await Notices(_carla)).GetProperty("items").GetArrayLength());
        var id = Guid.Parse(first.GetProperty("items")[0].GetProperty("id").GetString()!);
        Assert.Equal(0, await PropertyNotices.MarkReadAsync(_db, _bruno, [id], _clock.Now, default)); // not his
        Assert.Equal(1, await PropertyNotices.MarkReadAsync(_db, _alice, [id], _clock.Now, default)); Assert.Equal(1, (await Notices(_alice)).GetProperty("unread").GetInt32());
        Assert.Equal(1, await PropertyNotices.MarkReadAsync(_db, _alice, null, _clock.Now, default)); Assert.Equal(0, (await Notices(_alice)).GetProperty("unread").GetInt32());
        Assert.Equal(0, await PropertyNotices.MarkReadAsync(_db, _alice, null, _clock.Now, default)); Assert.True((await Notices(_bruno)).GetProperty("unread").GetInt32() > 0); // the renter's are untouched
    }

    // ---- island rentals that renew by themselves ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnIslandLeaseRenewsItselfOnlyWhenTheOwnerAllowedItAndTheRenterAsked()
    {
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 5, 1, 4, 0, 1, false), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _bruno, 1, Guid.NewGuid().ToString(), 500, _clock, default, autoRenew: true)).Ok);
        Assert.False((await _db.IslandLeases.AsNoTracking().SingleAsync()).AutoRenew); // not allowed by the owner: the wish is ignored
        var lease = await _db.IslandLeases.SingleAsync(); lease.Status = "ended"; await _db.SaveChangesAsync();
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 5, 1, 4, 0, 1, true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _bruno, 1, Guid.NewGuid().ToString(), 500, _clock, default, autoRenew: true)).Ok);
        var live = await _db.IslandLeases.AsNoTracking().SingleAsync(l => l.Status == "active"); Assert.True(live.AutoRenew);
        var (renter, owner) = (_db.Wallets.AsNoTracking().Single(w => w.UserId == _bruno).AvailableCoins, _db.Wallets.AsNoTracking().Single(w => w.UserId == _alice).AvailableCoins);
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, live.ExpiresAt.AddMinutes(1), default));
        var renewed = await _db.IslandLeases.AsNoTracking().SingleAsync(l => l.Id == live.Id); Assert.Equal(("active", 2, live.ExpiresAt.AddDays(5)), (renewed.Status, renewed.Periods, renewed.ExpiresAt));
        Assert.Equal((renter - 500, owner + 500), (_db.Wallets.AsNoTracking().Single(w => w.UserId == _bruno).AvailableCoins, _db.Wallets.AsNoTracking().Single(w => w.UserId == _alice).AvailableCoins));
        Assert.Equal(0, await IslandCommerce.ProcessDueAsync(_db, _storage, live.ExpiresAt.AddMinutes(2), default)); // the same date is never charged twice
        Assert.Equal(await _db.LedgerEntries.SumAsync(e => e.Debit), await _db.LedgerEntries.SumAsync(e => e.Credit));
    }

    [Fact]
    public async Task ARenewalThatCannotBeChargedFallsIntoGraceAndSaysWhy()
    {
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, _island.Id, _alice, new RentOffer(500, 5, 1, 4, 0, 2, true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, _island.Id, _bruno, 1, Guid.NewGuid().ToString(), 500, _clock, default, autoRenew: true)).Ok);
        var wallet = await _db.Wallets.SingleAsync(w => w.UserId == _bruno); wallet.AvailableCoins = 10; await _db.SaveChangesAsync();
        var live = await _db.IslandLeases.AsNoTracking().SingleAsync();
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, live.ExpiresAt.AddMinutes(1), default));
        Assert.Equal("grace", (await _db.IslandLeases.AsNoTracking().SingleAsync()).Status); Assert.Equal(1, await Count(_bruno, "renewal_failed")); Assert.Equal(1, await Count(_bruno, "rent_grace"));
        Assert.Equal(10, _db.Wallets.AsNoTracking().Single(w => w.UserId == _bruno).AvailableCoins); // nothing was charged
    }

    [Fact]
    public async Task ParcelRentalsThatRenewByThemselvesSayWhenTheyCannot()
    {
        var parcel = Claim("Loja"); await RentParcel(parcel, _bruno, days: 5, auto: true);
        var wallet = await _db.Wallets.SingleAsync(w => w.UserId == _bruno); wallet.AvailableCoins = 3; await _db.SaveChangesAsync();
        Assert.Equal(1, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(5).AddHours(1), default));
        Assert.Equal(1, await Count(_bruno, "renewal_failed")); Assert.Equal("grace", (await _db.ParcelRentals.AsNoTracking().SingleAsync()).Status);
    }
}
