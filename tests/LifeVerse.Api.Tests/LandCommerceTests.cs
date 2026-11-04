using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Selling and renting land end to end, against an in-memory database: who pays whom, what changes hands, and what can never happen twice.</summary>
public sealed class LandCommerceTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _seller = Guid.NewGuid(), _buyer = Guid.NewGuid(), _other = Guid.NewGuid();
    private readonly MemoryStore _store = new();
    private readonly AssetStorageService _storage;
    private Guid _parcel;

    public LandCommerceTests()
    {
        _db = new LifeVerseDbContext(_options);
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var user in new[] { _seller, _buyer, _other }) _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = 10_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        _db.SaveChanges();
        _parcel = Claim("Casa");
    }

    public void Dispose() => _db.Dispose();

    private Guid Claim(string name, int cx0 = 0, int cz0 = 0)
    {
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(cx0 + x, cz0 + z));
        var result = LandService.ClaimAsync(_db, _island, _seller, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private static string Key() => Guid.NewGuid().ToString();
    private Task<CommerceResult> Sell(long price = 500, Guid? exclusive = null, bool objects = true, Guid? by = null) => LandCommerce.OfferForSaleAsync(_db, _parcel, by ?? _seller, new SaleOffer(price, exclusive, "Bonito", objects), _clock, default);
    private Task<CommerceResult> Buy(Guid who, long price = 500, string? key = null, LifeVerseDbContext? db = null) => LandCommerce.BuyAsync(db ?? _db, _parcel, who, key ?? Key(), price, _clock, default);
    private Task<CommerceResult> OfferRent(long price = 100, long deposit = 50, int minPeriods = 1, int maxPeriods = 6, int grace = 3) => LandCommerce.OfferForRentAsync(_db, _parcel, _seller, new RentOffer(price, 10, minPeriods, maxPeriods, deposit, grace, true), _clock, default);
    private Task<CommerceResult> Rent(Guid who, int periods = 2, long price = 100, bool auto = false) => LandCommerce.RentAsync(_db, _parcel, who, periods, auto, Key(), price, _clock, default);
    private IslandParcel Stored() => _db.IslandParcels.AsNoTracking().Single(p => p.Id == _parcel);

    private BuildObject Put(Guid owner, float x, float z) { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Mesa", PrimitiveType = "cube", PositionX = x, PositionY = 1, PositionZ = z, Cost = 5, PhysicsJson = "{\"collision\":true}" }; _db.BuildObjects.Add(o); _db.SaveChanges(); return o; }

    // ---- selling ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheOwnerCanOfferAParcelAndThePriceHasLimits()
    {
        Assert.Equal("forbidden", (await Sell(by: _buyer)).Error);
        Assert.Equal("bad_price", (await Sell(0)).Error); Assert.Equal("bad_price", (await Sell(LandCommerce.MaxPrice + 1)).Error);
        Assert.Equal("bad_buyer", (await Sell(exclusive: _seller)).Error);
        Assert.True((await Sell()).Ok);
        Assert.Equal(500, Stored().SalePrice); Assert.Equal(2, Stored().Revision);
        Assert.Equal("forbidden", (await LandCommerce.CancelSaleAsync(_db, _parcel, _buyer, _clock, default)).Error);
        Assert.True((await LandCommerce.CancelSaleAsync(_db, _parcel, _seller, _clock, default)).Ok);
        Assert.Null(Stored().SalePrice);
        Assert.Equal("not_for_sale", (await LandCommerce.CancelSaleAsync(_db, _parcel, _seller, _clock, default)).Error);
    }

    [Fact]
    public async Task Buying_MovesTheMoneyTheOwnershipAndTheObjectsTogether()
    {
        var mine = Put(_seller, 2, 2); var visitors = Put(_other, 6, 6); var outside = Put(_seller, 100, 100);
        await Sell();
        var result = await Buy(_buyer);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(10_500, Coins(_seller)); Assert.Equal(9_500, Coins(_buyer));
        var parcel = Stored();
        Assert.Equal(_buyer, parcel.OwnerUserId); Assert.Null(parcel.SalePrice);
        Assert.Equal(_buyer, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).OwnerUserId);
        Assert.Equal(_other, _db.BuildObjects.AsNoTracking().Single(o => o.Id == visitors.Id).OwnerUserId);
        Assert.Equal(_seller, _db.BuildObjects.AsNoTracking().Single(o => o.Id == outside.Id).OwnerUserId);
        var ledger = _db.LedgerTransactions.AsNoTracking().Single(t => t.Type == "land_sale");
        Assert.Equal(500, ledger.Amount);
        var entries = _db.LedgerEntries.AsNoTracking().Where(e => e.TransactionId == ledger.Id).ToList();
        Assert.Equal(entries.Sum(e => e.Debit), entries.Sum(e => e.Credit));
        Assert.Contains(_db.ParcelTransactions.AsNoTracking(), t => t.ParcelId == _parcel && t.Type == "sale" && t.Amount == 500 && t.FromUserId == _buyer && t.ToUserId == _seller);
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == _parcel && c.Operation == "sold");
    }

    [Fact]
    public async Task Selling_CanLeaveTheObjectsWithTheSeller()
    {
        var mine = Put(_seller, 2, 2);
        await Sell(objects: false); await Buy(_buyer);
        Assert.Equal(_seller, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).OwnerUserId);
    }

    [Fact]
    public async Task Buying_IsRefusedWhenItShouldBe()
    {
        Assert.Equal("not_for_sale", (await Buy(_buyer)).Error);
        await Sell();
        Assert.Equal("own_parcel", (await Buy(_seller)).Error);
        Assert.Equal("price_changed", (await Buy(_buyer, price: 400)).Error);
        Assert.Equal("bad_key", (await Buy(_buyer, key: "curta")).Error);
        _db.Wallets.Single(w => w.UserId == _buyer).AvailableCoins = 100; _db.SaveChanges();
        Assert.Equal("insufficient_balance", (await Buy(_buyer)).Error);
        Assert.Equal(_seller, Stored().OwnerUserId); Assert.Equal(100, Coins(_buyer)); Assert.Equal(10_000, Coins(_seller));
        _db.Wallets.Single(w => w.UserId == _buyer).IsBlocked = true; _db.SaveChanges();
        Assert.Equal("wallet_blocked", (await Buy(_buyer)).Error);
    }

    [Fact]
    public async Task AnExclusiveOfferIsOnlyForThatPerson()
    {
        await Sell(exclusive: _buyer);
        Assert.Equal("not_for_you", (await Buy(_other)).Error);
        Assert.True((await Buy(_buyer)).Ok);
    }

    [Fact]
    public async Task ARepeatedRequestIsNotChargedTwice()
    {
        await Sell(); var key = Key();
        Assert.True((await Buy(_buyer, key: key)).Ok);
        await LandCommerce.OfferForSaleAsync(_db, _parcel, _buyer, new SaleOffer(300, null, "", true), _clock, default); // the new owner offers it again
        var again = await Buy(_buyer, price: 300, key: key);
        Assert.True(again.Ok); // answered as already done...
        Assert.Equal(9_500, Coins(_buyer)); Assert.Equal(_buyer, Stored().OwnerUserId); // ...and nothing else moved
        Assert.Single(_db.LedgerTransactions.AsNoTracking().Where(t => t.Type == "land_sale"));
    }

    [Fact]
    public async Task TwoPeopleBuyingTheSameParcelAtTheSameMomentOnlyOneWins()
    {
        await Sell();
        using var first = new LifeVerseDbContext(_options); using var second = new LifeVerseDbContext(_options);
        var raced = false;
        second.SavingChanges += (_, _) =>
        {
            if (raced) return; raced = true;
            // the other buyer finishes between this buyer's reading of the parcel and saving
            var inner = LandCommerce.BuyAsync(first, _parcel, _buyer, Key(), 500, _clock, default).GetAwaiter().GetResult();
            Assert.True(inner.Ok, inner.Error);
        };
        var loser = await LandCommerce.BuyAsync(second, _parcel, _other, Key(), 500, _clock, default);
        Assert.False(loser.Ok); Assert.Equal("conflict", loser.Error);
        Assert.Equal(_buyer, Stored().OwnerUserId); Assert.Null(Stored().SalePrice);
        // a relational database also rolls back the loser's wallet and ledger changes in the same save; the in-memory provider cannot show that part
    }

    // ---- renting ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARentalOfferNeedsSaneTermsAndReplacesASaleOffer()
    {
        Assert.Equal("forbidden", (await LandCommerce.OfferForRentAsync(_db, _parcel, _buyer, new RentOffer(100, 30, 1, 6, 0, 3, true), _clock, default)).Error);
        Assert.Equal("bad_price", (await OfferRent(price: 0)).Error);
        Assert.Equal("bad_terms", (await OfferRent(minPeriods: 5, maxPeriods: 2)).Error);
        Assert.Equal("bad_terms", (await LandCommerce.OfferForRentAsync(_db, _parcel, _seller, new RentOffer(100, 0, 1, 6, 0, 3, true), _clock, default)).Error);
        await Sell(); Assert.True((await OfferRent()).Ok);
        Assert.Null(Stored().SalePrice); Assert.Equal(100, Stored().RentPrice);
    }

    [Fact]
    public async Task Renting_PaysTheLandlordHoldsTheDepositAndLetsTheTenantBuild()
    {
        await OfferRent();
        var result = await Rent(_buyer, periods: 2);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(10_000 + 200, Coins(_seller)); Assert.Equal(10_000 - 200 - 50, Coins(_buyer));
        var rental = _db.ParcelRentals.AsNoTracking().Single();
        Assert.Equal("active", rental.Status); Assert.Equal(_clock.Now.AddDays(20), rental.ExpiresAt); Assert.Equal(_buyer, rental.TenantUserId); Assert.Equal(_seller, rental.LandlordUserId);
        Assert.Null(Stored().RentPrice); Assert.Equal(_seller, Stored().OwnerUserId);
        var ledger = _db.LedgerTransactions.AsNoTracking().Single(t => t.Type == "land_rental");
        var entries = _db.LedgerEntries.AsNoTracking().Where(e => e.TransactionId == ledger.Id).ToList();
        Assert.Equal(250, ledger.Amount); Assert.Equal(entries.Sum(e => e.Debit), entries.Sum(e => e.Credit));
        Assert.Contains(_db.LedgerAccounts.AsNoTracking(), a => a.AccountType == "land_escrow");

        var facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single(f => f.Id == _parcel);
        Assert.Null(BuildRules.CanBuildAt(facts, _buyer, false, false)); // the tenant may build
        Assert.Equal("parcel_forbidden", BuildRules.CanBuildAt(facts, _other, false, false));
        Assert.Equal("not_owner", BuildRules.CanEditObject(_buyer, _seller, false, facts)); // but not touch the landlord's things
        var view = await LandService.ViewAsync(_db, _island, _buyer, default);
        var shown = view.Parcels.Single(); Assert.Equal("rented_by_me", shown.State); Assert.True(shown.TenantIsMe); Assert.Equal("active", shown.RentStatus);
    }

    [Fact]
    public async Task Renting_IsRefusedWhenItShouldBe()
    {
        Assert.Equal("not_for_rent", (await Rent(_buyer)).Error);
        await OfferRent(minPeriods: 2, maxPeriods: 4);
        Assert.Equal("own_parcel", (await Rent(_seller)).Error);
        Assert.Equal("price_changed", (await Rent(_buyer, price: 90)).Error);
        Assert.Equal("bad_periods", (await Rent(_buyer, periods: 1)).Error); Assert.Equal("bad_periods", (await Rent(_buyer, periods: 5)).Error);
        _db.Wallets.Single(w => w.UserId == _buyer).AvailableCoins = 100; _db.SaveChanges();
        Assert.Equal("insufficient_balance", (await Rent(_buyer, periods: 2)).Error);
        Assert.Empty(_db.ParcelRentals);
        Assert.True((await Rent(_other, periods: 2)).Ok);
        Assert.Equal("not_for_rent", (await Rent(_buyer, periods: 2)).Error); // taken: the offer is gone
        Assert.Equal("rented", (await Sell()).Error);
    }

    [Fact]
    public async Task Renewing_ExtendsTheContractFromItsEndAndAlsoWhenOverdue()
    {
        await OfferRent(); await Rent(_buyer, periods: 1);
        var rental = _db.ParcelRentals.AsNoTracking().Single();
        Assert.Equal("not_found", (await LandCommerce.RenewAsync(_db, rental.Id, _other, 1, Key(), _clock, default)).Error);
        Assert.Equal("bad_periods", (await LandCommerce.RenewAsync(_db, rental.Id, _buyer, 0, Key(), _clock, default)).Error);
        Assert.True((await LandCommerce.RenewAsync(_db, rental.Id, _buyer, 2, Key(), _clock, default)).Ok);
        Assert.Equal(_clock.Now.AddDays(30), _db.ParcelRentals.AsNoTracking().Single().ExpiresAt);
        Assert.Equal(10_000 + 300, Coins(_seller));
        _clock.Now = _clock.Now.AddDays(31); await LandCommerce.ProcessDueAsync(_db, null, _clock.Now, default); // now overdue, in grace
        Assert.Equal("grace", _db.ParcelRentals.AsNoTracking().Single().Status);
        Assert.True((await LandCommerce.RenewAsync(_db, rental.Id, _buyer, 1, Key(), _clock, default)).Ok);
        var renewed = _db.ParcelRentals.AsNoTracking().Single(); Assert.Equal("active", renewed.Status); Assert.Equal(_clock.Now.AddDays(10), renewed.ExpiresAt); Assert.Null(renewed.GraceUntil);
    }

    [Fact]
    public async Task TheTenantCanLeaveAndGetsTheDepositBack_TheLandlordOnlyWhenItRanOut()
    {
        await OfferRent(); await Rent(_buyer, periods: 1);
        var rental = _db.ParcelRentals.AsNoTracking().Single();
        Assert.Equal("forbidden", (await LandCommerce.EndAsync(_db, null, rental.Id, _other, _clock, default)).Error);
        Assert.Equal("still_running", (await LandCommerce.EndAsync(_db, null, rental.Id, _seller, _clock, default)).Error);
        Assert.True((await LandCommerce.EndAsync(_db, null, rental.Id, _buyer, _clock, default)).Ok);
        Assert.Equal(10_000 - 100, Coins(_buyer)); // the rent is gone, the deposit came back
        var ended = _db.ParcelRentals.AsNoTracking().Single(); Assert.Equal("ended", ended.Status); Assert.Equal("tenant", ended.EndReason);
        Assert.Equal("not_found", (await LandCommerce.EndAsync(_db, null, rental.Id, _buyer, _clock, default)).Error);
        Assert.True((await OfferRent()).Ok); // free to be offered again
    }

    [Fact]
    public async Task AnExpiredContractWithoutAutoRenewGoesToGraceThenEndsAndReturnsTheTenantsObjects()
    {
        await OfferRent(grace: 3); await Rent(_buyer, periods: 1);
        var tenantObject = Put(_buyer, 4, 4); var landlordObject = Put(_seller, 8, 8); var elsewhere = Put(_buyer, 100, 100);
        _clock.Now = _clock.Now.AddDays(10).AddMinutes(1);
        Assert.Equal(1, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now, default));
        var rental = _db.ParcelRentals.AsNoTracking().Single();
        Assert.Equal("grace", rental.Status); Assert.Equal(rental.ExpiresAt.AddDays(3), rental.GraceUntil);
        var facts = (await LandService.LoadFactsAsync(_db, _island.Id, default)).Single(f => f.Id == _parcel);
        Assert.Equal("parcel_forbidden", BuildRules.CanBuildAt(facts, _buyer, false, false)); // no building while overdue
        Assert.Equal(0, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now, default));
        _clock.Now = _clock.Now.AddDays(3);
        Assert.Equal(1, await LandCommerce.ProcessDueAsync(_db, _storage, _clock.Now, default));
        Assert.Equal("ended", _db.ParcelRentals.AsNoTracking().Single().Status); Assert.Equal("expired", _db.ParcelRentals.AsNoTracking().Single().EndReason);
        Assert.Equal(10_000 - 100, Coins(_buyer));
        Assert.NotNull(_db.BuildObjects.AsNoTracking().Single(o => o.Id == tenantObject.Id).DeletedAt);
        Assert.Null(_db.BuildObjects.AsNoTracking().Single(o => o.Id == landlordObject.Id).DeletedAt);
        Assert.Null(_db.BuildObjects.AsNoTracking().Single(o => o.Id == elsewhere.Id).DeletedAt);
        Assert.Contains(_db.InventoryItems.AsNoTracking(), i => i.OwnerUserId == _buyer && i.DisplayName.StartsWith("Devolvido")); // saved, not destroyed
    }

    [Fact]
    public async Task AutoRenewPaysForAnotherPeriodOrFallsBackToGrace()
    {
        await OfferRent(); await Rent(_buyer, periods: 1, auto: true);
        _clock.Now = _clock.Now.AddDays(10).AddMinutes(1);
        await LandCommerce.ProcessDueAsync(_db, null, _clock.Now, default);
        var renewed = _db.ParcelRentals.AsNoTracking().Single();
        Assert.Equal("active", renewed.Status); Assert.Equal(2, renewed.Periods); Assert.Equal(10_000 + 200, Coins(_seller));
        await LandCommerce.ProcessDueAsync(_db, null, _clock.Now, default);
        Assert.Equal(2, _db.ParcelRentals.AsNoTracking().Single().Periods); // the same due date is never charged twice

        _db.Wallets.Single(w => w.UserId == _buyer).AvailableCoins = 10; _db.SaveChanges();
        _clock.Now = _clock.Now.AddDays(10).AddMinutes(1);
        await LandCommerce.ProcessDueAsync(_db, null, _clock.Now, default);
        Assert.Equal("grace", _db.ParcelRentals.AsNoTracking().Single().Status);
        Assert.Equal(10, Coins(_buyer));
    }

    [Fact]
    public async Task TheViewShowsOffersToOthersAndKeepsOwnParcelsOwn()
    {
        await Sell();
        Assert.Equal("sale", (await LandService.ViewAsync(_db, _island, _buyer, default)).Parcels.Single().State);
        Assert.Equal("own", (await LandService.ViewAsync(_db, _island, _seller, default)).Parcels.Single().State);
        var exclusive = await LandCommerce.OfferForSaleAsync(_db, _parcel, _seller, new SaleOffer(700, _buyer, "", true), _clock, default);
        Assert.True(exclusive.Ok);
        Assert.Equal("sale", (await LandService.ViewAsync(_db, _island, _buyer, default)).Parcels.Single().State);
        Assert.Equal("owned", (await LandService.ViewAsync(_db, _island, _other, default)).Parcels.Single().State);
        await OfferRent();
        var view = (await LandService.ViewAsync(_db, _island, _other, default)).Parcels.Single();
        Assert.Equal("rent", view.State); Assert.Equal(100, view.RentPrice); Assert.Equal(10, view.RentPeriodDays); Assert.Equal(50, view.RentDeposit);
    }
}
