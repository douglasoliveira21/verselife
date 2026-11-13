using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Giving a parcel to another person: an offer the person has to accept, with the same locks as a sale.</summary>
public sealed class ParcelTransferTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _alice = Guid.NewGuid(), _bruno = Guid.NewGuid(), _carla = Guid.NewGuid(), _admin = Guid.NewGuid();
    private readonly Island _island;
    private readonly Guid _parcel;

    public ParcelTransferTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _admin, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var (id, name) in new[] { (_alice, "alice"), (_bruno, "bruno"), (_carla, "carla"), (_admin, "dono") })
        {
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now });
            _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 100_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        }
        _db.SaveChanges();
        _parcel = Claim("Casa", _alice, 0);
    }

    public void Dispose() => _db.Dispose();

    private Guid Claim(string name, Guid owner, int x0)
    {
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x0 + x, z));
        var result = LandService.ClaimAsync(_db, _island, owner, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private IslandParcel Stored(Guid? id = null) => _db.IslandParcels.AsNoTracking().Single(p => p.Id == (id ?? _parcel));
    private BuildObject Put(Guid owner, float x, float z) { var o = new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = owner, OwnerUserId = owner, Name = "Mesa", PrimitiveType = "cube", PositionX = x, PositionY = 1, PositionZ = z, Cost = 5, PhysicsJson = "{\"collision\":true}" }; _db.BuildObjects.Add(o); _db.SaveChanges(); return o; }
    private Task<CommerceResult> Offer(string to = "bruno", Guid? by = null, Guid? parcel = null) => LandCommerce.TransferAsync(_db, parcel ?? _parcel, by ?? _alice, to, _clock, default);
    private Task<CommerceResult> Accept(Guid who) => LandCommerce.AcceptTransferAsync(_db, _parcel, who, _clock, default);

    [Fact]
    public async Task OnlyTheOwnerOffersToAKnownPerson_AndNotWhatIsSoldRentedOrShared()
    {
        Assert.Equal("forbidden", (await Offer(by: _bruno)).Error);
        Assert.Equal("forbidden", (await Offer(by: _admin)).Error); // the island's owner does not give away other people's land
        Assert.Equal("person_not_found", (await Offer("ninguem")).Error); Assert.Equal("bad_buyer", (await Offer("alice")).Error); Assert.Equal("bad_buyer", (await Offer("  ")).Error);
        Assert.Equal("not_found", (await Offer(parcel: Guid.NewGuid())).Error);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, _parcel, _alice, new SaleOffer(500, null, "", true), _clock, default)).Ok);
        Assert.Equal("for_sale", (await Offer()).Error); await LandCommerce.CancelSaleAsync(_db, _parcel, _alice, _clock, default);
        var common = Claim("Praça", _alice, 10); _db.IslandParcels.Single(p => p.Id == common).Category = "comum"; await _db.SaveChangesAsync();
        Assert.Equal("common_area", (await Offer(parcel: common)).Error);
        var rented = Claim("Loja", _alice, 20);
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = rented, LandlordUserId = _alice, TenantUserId = _carla, PricePerPeriod = 100, PeriodDays = 30, StartsAt = _clock.Now, ExpiresAt = _clock.Now.AddDays(10), Status = "active" }); await _db.SaveChangesAsync();
        Assert.Equal("rented", (await Offer(parcel: rented)).Error);
        Assert.Null(Stored().TransferToUserId); // none of the refused ones left a trace
    }

    [Fact]
    public async Task AnOfferedParcelStaysWithItsOwnerUntilTheOtherPersonAccepts_ThenTakesItsObjectsToo()
    {
        var mine = Put(_alice, 2, 2); var someoneElses = Put(_carla, 6, 6); var outside = Put(_alice, 200, 200);
        var offer = await Offer(); Assert.True(offer.Ok, offer.Error);
        Assert.Equal((_alice, _bruno), (Stored().OwnerUserId, Stored().TransferToUserId)); Assert.Equal(_alice, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).OwnerUserId);
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _bruno && n.Kind == "parcel_transfer_offer");
        // while it waits
        Assert.Equal("transfer_pending", (await Offer("carla")).Error);
        Assert.Equal("transfer_pending", (await LandCommerce.OfferForSaleAsync(_db, _parcel, _alice, new SaleOffer(500, null, "", true), _clock, default)).Error);
        Assert.Equal("transfer_pending", (await LandCommerce.OfferForRentAsync(_db, _parcel, _alice, new RentOffer(100, 30, 1, 3, 0, 3, false), _clock, default)).Error);
        Assert.Equal("transfer_pending", (await LandAdmin.SplitAsync(_db, _parcel, _alice, new HashSet<long> { LandGrid.Key(3, 0), LandGrid.Key(3, 1), LandGrid.Key(3, 2), LandGrid.Key(3, 3) }, "Parte", _clock, default)).Error);
        Assert.Equal("transfer_pending", (await LandAdmin.ExpandAsync(_db, _parcel, _alice, new HashSet<long> { LandGrid.Key(4, 0) }, 0, _clock, default)).Error);
        Assert.Equal("transfer_pending", (await LandAdmin.ShrinkAsync(_db, _parcel, _alice, new HashSet<long> { LandGrid.Key(3, 0), LandGrid.Key(3, 1), LandGrid.Key(3, 2), LandGrid.Key(3, 3) }, _clock, default)).Error);
        Assert.Equal("no_transfer", (await Accept(_carla)).Error); Assert.Equal("no_transfer", (await Accept(_alice)).Error);
        Assert.Equal("forbidden", (await LandCommerce.CancelTransferAsync(_db, _parcel, _bruno, _clock, default)).Error);

        var accepted = await Accept(_bruno); Assert.True(accepted.Ok, accepted.Error);
        var parcel = Stored(); Assert.Equal((_bruno, (Guid?)null), (parcel.OwnerUserId, parcel.TransferToUserId));
        Assert.Equal(_bruno, _db.BuildObjects.AsNoTracking().Single(o => o.Id == mine.Id).OwnerUserId); // what the giver left there goes along
        Assert.Equal(_carla, _db.BuildObjects.AsNoTracking().Single(o => o.Id == someoneElses.Id).OwnerUserId); // what other people built stays theirs
        Assert.Equal(_alice, _db.BuildObjects.AsNoTracking().Single(o => o.Id == outside.Id).OwnerUserId); // and what is outside the parcel is untouched
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _alice && n.Kind == "parcel_given"); Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _bruno && n.Kind == "parcel_received");
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == _parcel && c.Operation == "transferred");
        Assert.Equal("no_transfer", (await Accept(_bruno)).Error); // once
    }

    [Fact]
    public async Task AnOfferCanBeTakenBackTurnedDownOrRunOut()
    {
        Assert.Equal("no_transfer", (await LandCommerce.CancelTransferAsync(_db, _parcel, _alice, _clock, default)).Error);
        Assert.True((await Offer()).Ok); Assert.True((await LandCommerce.CancelTransferAsync(_db, _parcel, _alice, _clock, default)).Ok);
        Assert.Null(Stored().TransferToUserId); Assert.Equal("no_transfer", (await Accept(_bruno)).Error);
        Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _bruno && n.Kind == "parcel_transfer_cancelled");

        Assert.True((await Offer()).Ok);
        Assert.Equal("no_transfer", (await LandCommerce.DeclineTransferAsync(_db, _parcel, _carla, _clock, default)).Error);
        Assert.True((await LandCommerce.DeclineTransferAsync(_db, _parcel, _bruno, _clock, default)).Ok);
        Assert.Equal((_alice, (Guid?)null), (Stored().OwnerUserId, Stored().TransferToUserId)); Assert.Contains(_db.PropertyNotices.AsNoTracking(), n => n.UserId == _alice && n.Kind == "parcel_transfer_declined");

        Assert.True((await Offer()).Ok); _clock.Now = _clock.Now.Add(LandCommerce.TransferWindow).AddMinutes(1);
        Assert.False(LandCommerce.HasPendingTransfer(Stored(), _clock.Now));
        Assert.Equal("transfer_expired", (await Accept(_bruno)).Error); Assert.Equal(_alice, Stored().OwnerUserId);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, _parcel, _alice, new SaleOffer(500, null, "", true), _clock, default)).Ok, "an old offer does not hold the parcel back");
    }

    [Fact]
    public async Task HandingOverTheWholeIslandDropsTheParcelOffersOfItsOwner()
    {
        var parcel = Claim("Do dono", _admin, -30);
        Assert.True((await LandCommerce.TransferAsync(_db, parcel, _admin, "carla", _clock, default)).Ok);
        Assert.True((await IslandCommerce.TransferAsync(_db, _island.Id, _admin, "bruno", _clock, default)).Ok);
        Assert.True((await IslandCommerce.AcceptTransferAsync(_db, _island.Id, _bruno, _clock, default)).Ok);
        var moved = Stored(parcel); Assert.Equal((_bruno, (Guid?)null), (moved.OwnerUserId, moved.TransferToUserId)); // the offer to carla is gone with the old title
        Assert.Equal("no_transfer", (await LandCommerce.AcceptTransferAsync(_db, parcel, _carla, _clock, default)).Error);
    }

    [Fact]
    public async Task TheViewsShowTheOfferToBothSides()
    {
        Assert.True((await Offer()).Ok);
        var bruno = System.Text.Json.JsonSerializer.SerializeToElement(await LandProperties.BuildAsync(_db, _bruno, _clock.Now, default));
        var incoming = bruno.GetProperty("incomingTransfers"); Assert.Equal(1, incoming.GetArrayLength());
        Assert.Equal(("parcel", "Casa", "alice", _parcel.ToString()), (incoming[0].GetProperty("target").GetString(), incoming[0].GetProperty("name").GetString(), incoming[0].GetProperty("from").GetString(), incoming[0].GetProperty("parcelId").GetString()));
        var alice = System.Text.Json.JsonSerializer.SerializeToElement(await LandProperties.BuildAsync(_db, _alice, _clock.Now, default));
        Assert.Equal("bruno", alice.GetProperty("parcels")[0].GetProperty("transfer").GetProperty("to").GetString());
        Assert.Equal(0, System.Text.Json.JsonSerializer.SerializeToElement(await LandProperties.BuildAsync(_db, _carla, _clock.Now, default)).GetProperty("incomingTransfers").GetArrayLength());
        _clock.Now = _clock.Now.AddDays(8);
        Assert.Equal(0, System.Text.Json.JsonSerializer.SerializeToElement(await LandProperties.BuildAsync(_db, _bruno, _clock.Now, default)).GetProperty("incomingTransfers").GetArrayLength());
    }
}
