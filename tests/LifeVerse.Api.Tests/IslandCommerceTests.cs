using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Selling, renting and handing over whole islands: who pays whom, what changes hands, what stays, and what can never happen twice.</summary>
public sealed class IslandCommerceTests : IDisposable
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
    private readonly Guid _seller = Guid.NewGuid(), _buyer = Guid.NewGuid(), _other = Guid.NewGuid(), _parcelOwner = Guid.NewGuid();
    private readonly Island _island;
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
    private int _slot;

    public IslandCommerceTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var (id, name) in new[] { (_seller, "alice"), (_buyer, "bruno"), (_other, "carla"), (_parcelOwner, "duda") })
        {
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now });
            _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 100_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private IslandParcel Claim(string name, Guid? owner = null)
    {
        _clock.Now = _clock.Now.AddMinutes(1); var x0 = -30 + (_slot % 4) * 14; var z0 = -30 + (_slot / 4) * 14; _slot++;
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x0 + x, z0 + z));
        var result = LandService.ClaimAsync(_db, _island, _island.OwnerUserId, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); var parcel = _db.IslandParcels.Single(p => p.Id == result.Parcel!.Id); if (owner is { } o) { parcel.OwnerUserId = o; _db.SaveChanges(); } return parcel;
    }

    private BuildObject Put(Guid owner, float x = 0, float z = 0) { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Mesa", PrimitiveType = "cube", PositionX = x, PositionY = 1, PositionZ = z, Cost = 5, PhysicsJson = "{\"collision\":true}" }; _db.BuildObjects.Add(o); _db.SaveChanges(); return o; }
    private long Coins(Guid user) => _db.Wallets.AsNoTracking().Single(w => w.UserId == user).AvailableCoins;
    private static string Key() => Guid.NewGuid().ToString();
    private Island Stored() => _db.Islands.AsNoTracking().Single(i => i.Id == _island.Id);
    private Task<CommerceResult> Sell(long price = 5000, Guid? exclusive = null, bool objects = true, Guid? by = null) => IslandCommerce.OfferForSaleAsync(_db, _island.Id, by ?? _seller, new SaleOffer(price, exclusive, "Linda", objects), _clock, default);
    private Task<CommerceResult> Buy(Guid who, long price = 5000, string? key = null) => IslandCommerce.BuyAsync(_db, _storage, _island.Id, who, key ?? Key(), price, _clock, default);
    private Task<CommerceResult> OfferRent(long price = 300, long deposit = 100, int days = 10, int grace = 2) => IslandCommerce.OfferForRentAsync(_db, _island.Id, _seller, new RentOffer(price, days, 1, 6, deposit, grace, false), _clock, default);
    private Task<CommerceResult> Rent(Guid who, int periods = 2, long price = 300) => IslandCommerce.RentAsync(_db, _island.Id, who, periods, Key(), price, _clock, default);

    // ---- selling -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheOwnerOffersTheIsland_AndThePriceAndBuyerHaveLimits()
    {
        Assert.Equal("forbidden", (await Sell(by: _buyer)).Error); Assert.Equal("bad_price", (await Sell(0)).Error); Assert.Equal("bad_price", (await Sell(IslandCommerce.MaxPrice + 1)).Error); Assert.Equal("bad_buyer", (await Sell(exclusive: _seller)).Error);
        Assert.True((await Sell()).Ok); Assert.Equal(5000, Stored().SalePrice);
        Assert.Equal("forbidden", (await IslandCommerce.CancelSaleAsync(_db, _island.Id, _buyer, _clock, default)).Error);
        Assert.True((await IslandCommerce.CancelSaleAsync(_db, _island.Id, _seller, _clock, default)).Ok); Assert.Null(Stored().SalePrice);
        Assert.Equal("not_for_sale", (await IslandCommerce.CancelSaleAsync(_db, _island.Id, _seller, _clock, default)).Error);
        Assert.Contains(_db.IslandTransactions.AsNoTracking(), t => t.Type == "sale_listed");
    }

    [Fact]
    public async Task TheWelcomeIslandIsNeverOnTheMarket()
    {
        _island.IsWelcome = true; _db.SaveChanges();
        Assert.Equal("welcome_island", (await Sell()).Error); Assert.Equal("welcome_island", (await OfferRent()).Error);
        Assert.Equal("welcome_island", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Error);
    }

    [Fact]
    public async Task ABuyerPaysTheSellerAndGetsTheIslandItsLandAndItsObjects_WhileOthersKeepTheirs()
    {
        var sellers = Claim("Da Alice"); var duda = Claim("Da Duda", _parcelOwner);
        var mineObject = Put(_seller, 0, 0); var dudaObject = Put(_parcelOwner, 5, 5);
        Assert.True((await Sell(5000)).Ok);
        Assert.Equal("price_changed", (await Buy(_buyer, 4000)).Error); Assert.Equal("own_island", (await Buy(_seller)).Error);
        var before = (Coins(_buyer), Coins(_seller));
        var result = await Buy(_buyer); Assert.True(result.Ok, result.Error);
        Assert.Equal((before.Item1 - 5000, before.Item2 + 5000), (Coins(_buyer), Coins(_seller)));
        Assert.Equal(_buyer, Stored().OwnerUserId); Assert.Null(Stored().SalePrice);
        Assert.Equal(_buyer, _db.IslandParcels.AsNoTracking().Single(p => p.Id == sellers.Id).OwnerUserId); Assert.Equal(_parcelOwner, _db.IslandParcels.AsNoTracking().Single(p => p.Id == duda.Id).OwnerUserId);
        Assert.Equal(_buyer, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mineObject.Id).OwnerUserId); Assert.Equal(_parcelOwner, _db.BuildObjects.AsNoTracking().Single(o => o.Id == dudaObject.Id).OwnerUserId);
        var transaction = await _db.IslandTransactions.AsNoTracking().SingleAsync(t => t.Type == "sale"); Assert.Equal((_buyer, _seller, 5000L), (transaction.FromUserId, transaction.ToUserId, transaction.Amount));
        Assert.Equal(await _db.LedgerEntries.SumAsync(e => e.Debit), await _db.LedgerEntries.SumAsync(e => e.Credit)); // the books balance
        Assert.Contains(_db.IslandAudit.AsNoTracking(), a => a.Action == "island_sold");
    }

    [Fact]
    public async Task ObjectsNotIncludedInTheSaleGoBackToTheSeller_NotDestroyed()
    {
        var mine = Put(_seller, 1, 1); Assert.True((await Sell(objects: false)).Ok); Assert.True((await Buy(_buyer)).Ok);
        Assert.Equal(_buyer, Stored().OwnerUserId); Assert.NotNull(_db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).DeletedAt);
        Assert.Contains(_db.InventoryItems.AsNoTracking(), i => i.OwnerUserId == _seller && i.DisplayName.StartsWith("Devolvido"));
    }

    [Fact]
    public async Task RunningRentalsOnTheLandStayAndTheirRentNowGoesToTheNewOwner()
    {
        var parcel = Claim("Alugada"); Assert.True((await LandCommerce.OfferForRentAsync(_db, parcel.Id, _seller, new RentOffer(100, 10, 1, 4, 0, 2, false), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, parcel.Id, _other, 1, false, Key(), 100, _clock, default)).Ok);
        Assert.True((await Sell()).Ok); Assert.True((await Buy(_buyer)).Ok);
        var rental = await _db.ParcelRentals.AsNoTracking().SingleAsync(); Assert.Equal((_buyer, "active"), (rental.LandlordUserId, rental.Status)); Assert.Equal(_buyer, (await _db.IslandParcels.AsNoTracking().SingleAsync()).OwnerUserId);
    }

    [Fact]
    public async Task ExclusiveSalesAreForOnePerson_AndTwoBuyersCannotBothWin()
    {
        Assert.True((await Sell(exclusive: _buyer)).Ok); Assert.Equal("not_for_you", (await Buy(_other)).Error); Assert.True((await Buy(_buyer)).Ok);
        Assert.Equal("not_for_sale", (await Buy(_other)).Error); Assert.Equal(_buyer, Stored().OwnerUserId); Assert.Equal(100_000 - 5000, Coins(_buyer)); Assert.Equal(100_000, Coins(_other));
    }

    [Fact]
    public async Task ABuyWithoutMoneyChangesNothing_AndARepeatedKeyChargesOnce()
    {
        Assert.True((await Sell(500_000)).Ok); Assert.Equal("insufficient_balance", (await Buy(_buyer, 500_000)).Error); Assert.Equal(_seller, Stored().OwnerUserId); Assert.Equal(100_000, Coins(_buyer));
        Assert.True((await Sell(2000)).Ok); var key = Key(); Assert.True((await Buy(_buyer, 2000, key)).Ok); var again = await Buy(_buyer, 2000, key); Assert.True(again.Ok); Assert.Equal(98_000, Coins(_buyer));
    }

    // ---- gifts -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOwnerCanGiveTheIslandAwayToAKnownPerson_ButNotWhileItIsForSaleOrRented()
    {
        var parcel = Claim("Minha");
        Assert.Equal("forbidden", (await IslandCommerce.TransferAsync(_db, _island.Id, _buyer, "carla", _clock, default)).Error);
        Assert.Equal("person_not_found", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "ninguem", _clock, default)).Error); Assert.Equal("bad_buyer", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "alice", _clock, default)).Error);
        Assert.True((await Sell()).Ok); Assert.Equal("for_sale", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Error); await IslandCommerce.CancelSaleAsync(_db, _island.Id, _seller, _clock, default);
        var coins = Coins(_seller); Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Ok);
        Assert.Equal(_seller, Stored().OwnerUserId); Assert.True((await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default)).Ok); // nothing moves until the other person says yes
        Assert.Equal(_buyer, Stored().OwnerUserId); Assert.Equal(_buyer, _db.IslandParcels.AsNoTracking().Single(p => p.Id == parcel.Id).OwnerUserId); Assert.Equal(coins, Coins(_seller)); // no money moves in a gift
    }

    [Fact]
    public async Task AGiftTellsBothPeople_AndTheHostingPlanGoesWithTheIsland()
    {
        var plan = new IslandRentalPlan { Id = Guid.NewGuid(), Name = "Plano Ouro", PriceCoins = 1000, DurationDays = 30, MaxSize = 512, MaxObjects = 20000, MaxPlayers = 80, IsActive = true };
        _db.IslandRentalPlans.Add(plan);
        var hosting = new IslandRental { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _seller, PlanId = plan.Id, StartsAt = _clock.Now, ExpiresAt = _clock.Now.AddDays(20), RecoveryUntil = _clock.Now.AddDays(27), Status = "active" };
        _db.IslandRentals.Add(hosting); await _db.SaveChangesAsync();
        Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Ok); Assert.True((await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default)).Ok);
        Assert.Equal(_buyer, _db.IslandRentals.AsNoTracking().Single(r => r.Id == hosting.Id).UserId); // the new owner pays and renews it from now on
        var notices = _db.PropertyNotices.AsNoTracking().ToList();
        Assert.Contains(notices, n => n.UserId == _buyer && n.Kind == "island_received" && n.Title.Contains("Ilha Tropical"));
        Assert.Contains(notices, n => n.UserId == _seller && n.Kind == "island_given" && n.Title.Contains("Ilha Tropical"));
    }

    [Fact]
    public async Task AnOfferedIslandStaysWithItsOwnerUntilTheOtherPersonAccepts()
    {
        var parcel = Claim("Minha");
        var offer = await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default); Assert.True(offer.Ok, offer.Error);
        Assert.Equal((_seller, _buyer), (Stored().OwnerUserId, Stored().TransferToUserId)); Assert.Equal(_seller, _db.IslandParcels.AsNoTracking().Single(p => p.Id == parcel.Id).OwnerUserId);
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _buyer && n.Kind == "island_transfer_offer");
        // while it waits: no second offer, no market, nobody else answers
        Assert.Equal("transfer_pending", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "carla", _clock, default)).Error);
        Assert.Equal("transfer_pending", (await Sell()).Error);
        Assert.Equal("transfer_pending", (await IslandCommerce.OfferForRentAsync(_db, _island.Id, _seller, new RentOffer(100, 30, 1, 3, 0, 3, false), _clock, default)).Error);
        Assert.Equal("no_transfer", (await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _other, _clock, default)).Error);
        Assert.Equal("no_transfer", (await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _seller, _clock, default)).Error); // the giver cannot accept their own gift
        Assert.Equal("forbidden", (await IslandCommerce.CancelTransferAsync(_db, _island.Id, _buyer, _clock, default)).Error);
        var accepted = await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default); Assert.True(accepted.Ok, accepted.Error);
        Assert.Equal((_buyer, (Guid?)null), (Stored().OwnerUserId, Stored().TransferToUserId)); Assert.Equal(_buyer, _db.IslandParcels.AsNoTracking().Single(p => p.Id == parcel.Id).OwnerUserId);
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _seller && n.Kind == "island_given");
        Assert.Equal("no_transfer", (await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default)).Error); // once
    }

    [Fact]
    public async Task AnOfferCanBeTakenBackTurnedDownOrRunOut()
    {
        Assert.Equal("no_transfer", (await IslandCommerce.CancelTransferAsync(_db, _island.Id, _seller, _clock, default)).Error);
        Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Ok);
        Assert.True((await IslandCommerce.CancelTransferAsync(_db, _island.Id, _seller, _clock, default)).Ok);
        Assert.Null(Stored().TransferToUserId); Assert.Equal("no_transfer", (await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default)).Error);
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _buyer && n.Kind == "island_transfer_cancelled");

        Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Ok);
        Assert.Equal("no_transfer", (await IslandCommerce.DeclineTransferAsync(_db, _island.Id, _other, _clock, default)).Error);
        Assert.True((await IslandCommerce.DeclineTransferAsync(_db, _island.Id, _buyer, _clock, default)).Ok);
        Assert.Equal((_seller, (Guid?)null), (Stored().OwnerUserId, Stored().TransferToUserId)); Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _seller && n.Kind == "island_transfer_declined");

        Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "bruno", _clock, default)).Ok);
        _clock.Now = _clock.Now.Add(IslandCommerce.TransferWindow).AddMinutes(1); // nobody answered
        Assert.False(IslandCommerce.HasPendingTransfer(Stored(), _clock.Now));
        Assert.Equal("transfer_expired", (await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _buyer, _clock, default)).Error); Assert.Equal(_seller, Stored().OwnerUserId);
        Assert.True((await Sell()).Ok, "an old offer does not hold the island back"); // the owner is free again to sell
    }

    // ---- renting ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RentingChargesRentAndDeposit_GivesAdministratorPowersButNotTheTitle()
    {
        Assert.Equal("not_for_rent", (await Rent(_buyer)).Error); Assert.Equal("bad_terms", (await IslandCommerce.OfferForRentAsync(_db, _island.Id, _seller, new RentOffer(100, 0, 1, 2, 0, 1, false), _clock, default)).Error);
        Assert.True((await OfferRent()).Ok); Assert.Equal("price_changed", (await Rent(_buyer, 2, 999)).Error); Assert.Equal("bad_periods", (await Rent(_buyer, 9)).Error); Assert.Equal("own_island", (await Rent(_seller)).Error);
        var (b0, s0) = (Coins(_buyer), Coins(_seller)); Assert.True((await Rent(_buyer, 2)).Ok);
        Assert.Equal((b0 - 600 - 100, s0 + 600), (Coins(_buyer), Coins(_seller))); Assert.Equal(_seller, Stored().OwnerUserId); Assert.Null(Stored().RentPrice);
        var lease = await _db.IslandLeases.AsNoTracking().SingleAsync(); Assert.Equal(("active", _clock.Now.AddDays(20)), (lease.Status, lease.ExpiresAt));
        var permission = await _db.IslandPermissions.AsNoTracking().SingleAsync(p => p.UserId == _buyer); Assert.Equal(("admin", lease.ExpiresAt), (permission.Permission, permission.ExpiresAt)); Assert.Null(permission.RevokedAt);
        Assert.True(await IslandOwnerOnly.AllowsAsync(_db, Stored(), _buyer, _clock.Now)); Assert.False(await IslandOwnerOnly.AllowsAsync(_db, Stored(), _other, _clock.Now));
        Assert.False(await IslandOwnerOnly.AllowsAsync(_db, Stored(), _buyer, _clock.Now.AddDays(21))); // when the contract has run out the powers are gone
        Assert.Equal("rented", (await Sell()).Error); Assert.Equal("rented", (await OfferRent()).Error); Assert.Equal("not_for_rent", (await Rent(_other)).Error);
    }

    [Fact]
    public async Task RenewingExtendsTheContractAndThePermission_AndOnlyTheTenantRenews()
    {
        Assert.True((await OfferRent()).Ok); Assert.True((await Rent(_buyer, 1)).Ok); var lease = await _db.IslandLeases.AsNoTracking().SingleAsync();
        Assert.Equal("not_found", (await IslandCommerce.RenewAsync(_db, lease.Id, _other, 1, Key(), _clock, default)).Error);
        Assert.True((await IslandCommerce.RenewAsync(_db, lease.Id, _buyer, 2, Key(), _clock, default)).Ok);
        var renewed = await _db.IslandLeases.AsNoTracking().SingleAsync(); Assert.Equal(lease.ExpiresAt.AddDays(20), renewed.ExpiresAt); Assert.Equal(renewed.ExpiresAt, (await _db.IslandPermissions.AsNoTracking().SingleAsync(p => p.UserId == _buyer)).ExpiresAt);
        Assert.Equal(1, await _db.IslandTransactions.CountAsync(t => t.Type == "rent_payment"));
    }

    [Fact]
    public async Task TheTenantLeavesAnytimeTheOwnerOnlyAfterTheEnd_TheDepositGoesBackAndThePowersAreTaken()
    {
        Assert.True((await OfferRent()).Ok); Assert.True((await Rent(_buyer, 1)).Ok); var lease = await _db.IslandLeases.AsNoTracking().SingleAsync(); var tenantObject = Put(_buyer, 3, 3); var ownerObject = Put(_seller, 8, 8);
        Assert.Equal("still_running", (await IslandCommerce.EndAsync(_db, _storage, lease.Id, _seller, _clock, default)).Error); Assert.Equal("forbidden", (await IslandCommerce.EndAsync(_db, _storage, lease.Id, _other, _clock, default)).Error);
        var coins = Coins(_buyer); Assert.True((await IslandCommerce.EndAsync(_db, _storage, lease.Id, _buyer, _clock, default)).Ok);
        Assert.Equal(coins + 100, Coins(_buyer)); Assert.Equal("ended", (await _db.IslandLeases.AsNoTracking().SingleAsync()).Status); Assert.NotNull((await _db.IslandPermissions.AsNoTracking().SingleAsync(p => p.UserId == _buyer)).RevokedAt);
        Assert.NotNull(_db.BuildObjects.AsNoTracking().Single(o => o.Id == tenantObject.Id).DeletedAt); Assert.Null(_db.BuildObjects.AsNoTracking().Single(o => o.Id == ownerObject.Id).DeletedAt);
        Assert.Contains(_db.InventoryItems.AsNoTracking(), i => i.OwnerUserId == _buyer && i.DisplayName.StartsWith("Devolvido"));
        Assert.Equal(await _db.LedgerEntries.SumAsync(e => e.Debit), await _db.LedgerEntries.SumAsync(e => e.Credit));
        Assert.True((await OfferRent()).Ok); Assert.True((await Rent(_other, 1)).Ok); var second = await _db.IslandLeases.AsNoTracking().SingleAsync(l => l.TenantUserId == _other);
        _clock.Now = _clock.Now.AddDays(11); Assert.True((await IslandCommerce.EndAsync(_db, _storage, second.Id, _seller, _clock, default)).Ok); // after the end the owner may close it
    }

    [Fact]
    public async Task TheClockMovesALeaseToGraceThenEndsIt_AndNothingHappensTwice()
    {
        Assert.True((await OfferRent(grace: 2)).Ok); Assert.True((await Rent(_buyer, 1)).Ok);
        Assert.Equal(0, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(5), default));
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(10).AddHours(1), default)); Assert.Equal("grace", (await _db.IslandLeases.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(0, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(11), default));
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(12).AddHours(1), default)); var ended = await _db.IslandLeases.AsNoTracking().SingleAsync(); Assert.Equal(("ended", "expired"), (ended.Status, ended.EndReason));
        Assert.Equal(0, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(30), default)); Assert.Equal(1, await _db.IslandTransactions.CountAsync(t => t.Type == "rent_end"));
    }

    [Fact]
    public async Task WithNoGraceALeaseEndsAtOnce_AndRegionClassFollowsTheSize()
    {
        Assert.True((await OfferRent(grace: 0)).Ok); Assert.True((await Rent(_buyer, 1)).Ok);
        Assert.Equal(1, await IslandCommerce.ProcessDueAsync(_db, _storage, _clock.Now.AddDays(10).AddHours(1), default)); Assert.Equal("ended", (await _db.IslandLeases.AsNoTracking().SingleAsync()).Status);
        Assert.Equal("full", IslandCommerce.RegionOf(new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "x", Slug = "x", Size = 256 })); Assert.Equal("homestead", IslandCommerce.RegionOf(new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "y", Slug = "y", Size = 128 }));
    }

    [Fact]
    public async Task ARentedIslandCannotBeGivenAway()
    {
        Assert.True((await OfferRent()).Ok); Assert.True((await Rent(_buyer, 1)).Ok);
        Assert.Equal("rented", (await IslandCommerce.TransferAsync(_db, _island.Id, _seller, "carla", _clock, default)).Error);
    }
}
