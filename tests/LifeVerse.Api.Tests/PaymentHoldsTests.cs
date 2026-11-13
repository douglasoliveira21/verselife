using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Paying in two phases: the money is set aside first, paid out only when the title (or contract) moves, and given back when that fails or never finishes.</summary>
public sealed class PaymentHoldsTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    /// <summary>Makes the n-th save of a context fail, the way a lost race on the database would.</summary>
    private sealed class FailNthSave : SaveChangesInterceptor
    {
        public int Calls, FailOn;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Calls++; if (FailOn > 0 && Calls == FailOn) throw new DbUpdateException("someone got there first"); return base.SavingChangesAsync(eventData, result, cancellationToken); }
    }

    private readonly FailNthSave _failer = new();
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _seller = Guid.NewGuid(), _buyer = Guid.NewGuid();
    private readonly Guid _parcel;

    public PaymentHoldsTests()
    {
        _db = new LifeVerseDbContext(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(_failer).Options);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var user in new[] { _seller, _buyer }) _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = 10_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        _db.SaveChanges();
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x, z));
        var claim = LandService.ClaimAsync(_db, _island, _seller, "Casa", "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult(); Assert.Null(claim.Error); _parcel = claim.Parcel!.Id;
        _failer.Calls = 0;
    }

    public void Dispose() => _db.Dispose();

    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private static string Key() => Guid.NewGuid().ToString();
    private Task<CommerceResult> Sell(long price = 500) => LandCommerce.OfferForSaleAsync(_db, _parcel, _seller, new SaleOffer(price, null, "", true), _clock, default);
    private IslandParcel Stored() => _db.IslandParcels.AsNoTracking().Single(p => p.Id == _parcel);

    /// <summary>Money in all accounts of the ledger: debits and credits of every transaction have to match, one by one.</summary>
    private void AssertLedgerBalanced()
    {
        foreach (var group in _db.LedgerEntries.AsNoTracking().ToList().GroupBy(e => e.TransactionId)) Assert.Equal(group.Sum(e => e.Debit), group.Sum(e => e.Credit));
        var hold = _db.LedgerAccounts.AsNoTracking().SingleOrDefault(a => a.AccountType == PaymentHolds.HoldAccount);
        if (hold is not null) { var mine = _db.LedgerEntries.AsNoTracking().Where(e => e.AccountId == hold.Id).ToList(); Assert.Equal(mine.Sum(e => e.Credit), mine.Sum(e => e.Debit)); } // nothing is left sitting in the hold account
    }

    [Fact]
    public async Task APurchaseReservesThenSettles_AndLeavesTheHoldClosedAndTheLedgerBalanced()
    {
        await Sell(); var key = Key();
        var bought = await LandCommerce.BuyAsync(_db, _parcel, _buyer, key, 500, _clock, default); Assert.True(bought.Ok, bought.Error);
        Assert.Equal((9_500L, 10_500L, _buyer), (Coins(_buyer), Coins(_seller), Stored().OwnerUserId));
        var hold = Assert.Single(_db.PaymentHolds.AsNoTracking().ToList()); Assert.Equal(("settled", 500L, "land_sale", key), (hold.Status, hold.Amount, hold.Purpose, hold.Key));
        Assert.Contains(_db.LedgerTransactions.AsNoTracking(), t => t.Type == "land_sale_hold" && t.IdempotencyKey == $"hold:{hold.Id}");
        Assert.Contains(_db.LedgerTransactions.AsNoTracking(), t => t.Type == "land_sale" && t.IdempotencyKey == key);
        AssertLedgerBalanced();
        Assert.True((await LandCommerce.BuyAsync(_db, _parcel, _buyer, key, 500, _clock, default)).Ok); Assert.Equal(9_500, Coins(_buyer)); Assert.Single(_db.PaymentHolds); // a repeat of the same key does nothing twice
    }

    [Fact]
    public async Task WhenTheSecondPhaseFails_TheBuyerGetsTheMoneyBackAndNothingMoved()
    {
        await Sell(); var key = Key(); _failer.Calls = 0; _failer.FailOn = 2; // the first save is the reservation, the second one moves the title
        var result = await LandCommerce.BuyAsync(_db, _parcel, _buyer, key, 500, _clock, default);
        Assert.False(result.Ok); Assert.Equal("conflict", result.Error);
        Assert.Equal((10_000L, 10_000L, _seller, 500L), (Coins(_buyer), Coins(_seller), Stored().OwnerUserId, Stored().SalePrice)); // still for sale, nobody lost a coin
        var hold = Assert.Single(_db.PaymentHolds.AsNoTracking().ToList()); Assert.Equal(("released", "conflict"), (hold.Status, hold.Reason));
        Assert.Contains(_db.LedgerTransactions.AsNoTracking(), t => t.Type == "land_sale_release"); Assert.DoesNotContain(_db.LedgerTransactions.AsNoTracking(), t => t.IdempotencyKey == key); // the purchase itself never happened
        AssertLedgerBalanced();
        _failer.FailOn = 0; var retry = await LandCommerce.BuyAsync(_db, _parcel, _buyer, key, 500, _clock, default); Assert.True(retry.Ok, retry.Error); // the same key can be tried again
        Assert.Equal((9_500L, 10_500L), (Coins(_buyer), Coins(_seller))); AssertLedgerBalanced();
    }

    [Fact]
    public async Task ARentalThatCannotBeSavedGivesTheRentAndTheDepositBack()
    {
        Assert.True((await LandCommerce.OfferForRentAsync(_db, _parcel, _seller, new RentOffer(100, 30, 1, 6, 50, 3, false), _clock, default)).Ok);
        _failer.Calls = 0; _failer.FailOn = 2;
        var failed = await LandCommerce.RentAsync(_db, _parcel, _buyer, 2, false, Key(), 100, _clock, default); Assert.Equal("conflict", failed.Error);
        Assert.Equal((10_000L, 10_000L, 0), (Coins(_buyer), Coins(_seller), _db.ParcelRentals.Count())); AssertLedgerBalanced();
        _failer.FailOn = 0; Assert.True((await LandCommerce.RentAsync(_db, _parcel, _buyer, 2, false, Key(), 100, _clock, default)).Ok);
        Assert.Equal((9_750L, 10_200L, 1), (Coins(_buyer), Coins(_seller), _db.ParcelRentals.Count())); AssertLedgerBalanced(); // 200 rent to the landlord, 50 deposit held
    }

    [Fact]
    public async Task AnIslandSaleThatFailsGivesTheMoneyBackToo()
    {
        Assert.True((await IslandCommerce.OfferForSaleAsync(_db, _island.Id, _seller, new SaleOffer(900, null, "", true), _clock, default)).Ok);
        _failer.Calls = 0; _failer.FailOn = 2;
        var failed = await IslandCommerce.BuyAsync(_db, null, _island.Id, _buyer, Key(), 900, _clock, default); Assert.Equal("conflict", failed.Error);
        Assert.Equal((10_000L, 10_000L, _seller), (Coins(_buyer), Coins(_seller), _db.Islands.AsNoTracking().Single().OwnerUserId)); AssertLedgerBalanced();
        _failer.FailOn = 0; Assert.True((await IslandCommerce.BuyAsync(_db, null, _island.Id, _buyer, Key(), 900, _clock, default)).Ok);
        Assert.Equal((9_100L, 10_900L, _buyer), (Coins(_buyer), Coins(_seller), _db.Islands.AsNoTracking().Single().OwnerUserId)); AssertLedgerBalanced();
    }

    [Fact]
    public async Task AHoldTheServerLeftBehindIsGivenBackAfterTenMinutes_AndPickedUpIfTheBuyerTriesAgain()
    {
        await Sell(); var key = Key();
        var (hold, error) = await PaymentHolds.ReserveAsync(_db, key, "land_sale", _buyer, 500, _parcel, _clock.Now, default); Assert.Null(error); Assert.NotNull(hold);
        Assert.Equal(9_500, Coins(_buyer)); // the server dies here
        _clock.Now = _clock.Now.AddMinutes(9); Assert.Equal(0, await PaymentHolds.ReleaseStaleAsync(_db, _clock.Now, default)); Assert.Equal(9_500, Coins(_buyer)); // too soon: it may still be finishing
        var retry = await LandCommerce.BuyAsync(_db, _parcel, _buyer, key, 500, _clock, default); Assert.True(retry.Ok, retry.Error); // the retry takes the open hold instead of charging again
        Assert.Equal(9_500, Coins(_buyer)); Assert.Single(_db.PaymentHolds); Assert.Equal("settled", _db.PaymentHolds.AsNoTracking().Single().Status);

        var (lost, _) = await PaymentHolds.ReserveAsync(_db, Key(), "land_sale", _buyer, 700, Guid.NewGuid(), _clock.Now, default); Assert.NotNull(lost);
        _clock.Now = _clock.Now.AddMinutes(11); Assert.Equal(1, await PaymentHolds.ReleaseStaleAsync(_db, _clock.Now, default));
        Assert.Equal(9_500, Coins(_buyer)); Assert.Equal("released", _db.PaymentHolds.AsNoTracking().Single(h => h.Id == lost!.Id).Status); AssertLedgerBalanced();
        Assert.False(await PaymentHolds.ReleaseAsync(_db, lost!.Id, "again", _clock.Now, default)); Assert.Equal(9_500, Coins(_buyer)); // giving back twice gives back once
    }

    [Fact]
    public async Task ADifferentPurchaseCannotReuseTheKeyOfAnOpenHold_AndNoHoldIsMadeWithoutMoney()
    {
        var key = Key(); var (hold, _) = await PaymentHolds.ReserveAsync(_db, key, "land_sale", _buyer, 500, _parcel, _clock.Now, default); Assert.NotNull(hold);
        Assert.Equal("key_in_use", (await PaymentHolds.ReserveAsync(_db, key, "land_sale", _buyer, 600, _parcel, _clock.Now, default)).Error);
        Assert.Equal("key_in_use", (await PaymentHolds.ReserveAsync(_db, key, "land_rental", _buyer, 500, _parcel, _clock.Now, default)).Error);
        Assert.Equal("insufficient_balance", (await PaymentHolds.ReserveAsync(_db, Key(), "land_sale", _buyer, 50_000, _parcel, _clock.Now, default)).Error);
        _db.Wallets.Single(w => w.UserId == _seller).IsBlocked = true; await _db.SaveChangesAsync();
        Assert.Equal("wallet_blocked", (await PaymentHolds.ReserveAsync(_db, Key(), "land_sale", _seller, 10, _parcel, _clock.Now, default)).Error);
        Assert.Equal("bad_price", (await PaymentHolds.ReserveAsync(_db, Key(), "land_sale", _buyer, 0, _parcel, _clock.Now, default)).Error);
        Assert.Single(_db.PaymentHolds); Assert.Equal(9_500, Coins(_buyer));
    }
}
