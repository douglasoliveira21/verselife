using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>"Sobre este Terreno": what one parcel says about itself, who sees what, how the title history reads and who may set its picture.</summary>
public sealed class LandAboutTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _alice = Guid.NewGuid(), _bruno = Guid.NewGuid(), _carla = Guid.NewGuid(), _stranger = Guid.NewGuid();
    private readonly Island _island;
    private IslandParcel _parcel = null!;

    public LandAboutTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _alice, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var (id, name) in new[] { (_alice, "alice"), (_bruno, "bruno"), (_carla, "carla"), (_stranger, "estranho") })
        {
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now });
            _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 100_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        }
        _db.SaveChanges();
        var cells = new HashSet<long>(); for (var x = 0; x < 6; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(-10 + x, -10 + z));
        var result = LandService.ClaimAsync(_db, _island, _alice, "Centro Comercial", "Terreno ideal para lojas", cells, 300, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); _parcel = _db.IslandParcels.Single(p => p.Id == result.Parcel!.Id); _parcel.Code = "L-03"; _parcel.Category = "comercial"; _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private async Task<JsonElement> About(Guid who) => JsonSerializer.SerializeToElement(await LandAbout.AboutAsync(_db, _island, _parcel.Id, who, _clock.Now, default) ?? throw new InvalidOperationException("no parcel"));

    [Fact]
    public async Task TheOwnerSeesTheWholePictureAndTheFlagsToManageIt()
    {
        var data = await About(_alice); var p = data.GetProperty("parcel");
        Assert.Equal(("L-03", "Centro Comercial", "comercial", 384d, 24), (p.GetProperty("code").GetString(), p.GetProperty("name").GetString(), p.GetProperty("category").GetString(), p.GetProperty("area").GetDouble(), p.GetProperty("cells").GetInt32()));
        Assert.Equal("Terreno ideal para lojas", p.GetProperty("description").GetString()); Assert.Equal("Ilha Tropical", data.GetProperty("island").GetProperty("name").GetString());
        var owner = data.GetProperty("owner"); Assert.Equal(("person", "alice"), (owner.GetProperty("kind").GetString(), owner.GetProperty("name").GetString()));
        var me = data.GetProperty("me"); Assert.True(me.GetProperty("canManage").GetBoolean()); Assert.True(me.GetProperty("isOwner").GetBoolean()); Assert.False(me.GetProperty("isTenant").GetBoolean());
        var rights = data.GetProperty("rights"); Assert.True(rights.GetProperty("canResell").GetBoolean()); Assert.True(rights.GetProperty("canLease").GetBoolean());
        Assert.Equal(300, data.GetProperty("usage").GetProperty("maxObjects").GetInt32()); Assert.Equal("public", p.GetProperty("accessMode").GetString()); Assert.Equal("owner", p.GetProperty("buildMode").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("sale").ValueKind); Assert.Equal(JsonValueKind.Null, data.GetProperty("rental").ValueKind);
        Assert.Equal(1, data.GetProperty("history").GetArrayLength()); Assert.Equal("claim", data.GetProperty("history")[0].GetProperty("what").GetString());
    }

    [Fact]
    public async Task AVisitorSeesTheParcelButCannotManageIt_AndNotAnOfferKeptForSomebodyElse()
    {
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, _parcel.Id, _alice, new SaleOffer(9000, _bruno, "Só para você", true), _clock, default)).Ok);
        var other = await About(_stranger); var me = other.GetProperty("me"); Assert.False(me.GetProperty("canManage").GetBoolean()); Assert.False(other.GetProperty("rights").GetProperty("canResell").GetBoolean());
        Assert.Equal(JsonValueKind.Null, other.GetProperty("sale").ValueKind); // an exclusive offer is not shown to people it is not for
        var buyer = await About(_bruno); var sale = buyer.GetProperty("sale"); Assert.Equal((9000L, true), (sale.GetProperty("price").GetInt64(), sale.GetProperty("exclusive").GetBoolean()));
        Assert.Equal(9000L, (await About(_alice)).GetProperty("sale").GetProperty("price").GetInt64());
    }

    [Fact]
    public async Task TheTitleHistoryAndTheDateOfAcquisitionFollowTheSales()
    {
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, _parcel.Id, _alice, new SaleOffer(7000, null, "", true), _clock, default)).Ok);
        _clock.Now = _clock.Now.AddDays(3); Assert.True((await LandCommerce.BuyAsync(_db, _parcel.Id, _bruno, Guid.NewGuid().ToString(), 7000, _clock, default)).Ok);
        var data = await About(_bruno); Assert.Equal("bruno", data.GetProperty("owner").GetProperty("name").GetString()); Assert.Equal(_clock.Now, data.GetProperty("acquiredAt").GetDateTimeOffset());
        var rows = data.GetProperty("history").EnumerateArray().ToList(); Assert.Equal(2, rows.Count); var sale = rows[0];
        Assert.Equal(("sale", "alice", "bruno", 7000L), (sale.GetProperty("what").GetString(), sale.GetProperty("from").GetString(), sale.GetProperty("to").GetString(), sale.GetProperty("amount").GetInt64()));
        Assert.True(data.GetProperty("me").GetProperty("canManage").GetBoolean()); Assert.False((await About(_alice)).GetProperty("me").GetProperty("canManage").GetBoolean());
    }

    [Fact]
    public async Task ARentedParcelShowsItsContractToTheOwnerAndTheTenantOnly()
    {
        Assert.True((await LandCommerce.OfferForRentAsync(_db, _parcel.Id, _alice, new RentOffer(500, 30, 1, 6, 100, 3, true), _clock, default)).Ok);
        var offer = (await About(_carla)).GetProperty("rentOffer"); Assert.Equal((500L, 30, 100L), (offer.GetProperty("price").GetInt64(), offer.GetProperty("periodDays").GetInt32(), offer.GetProperty("deposit").GetInt64()));
        Assert.True((await LandCommerce.RentAsync(_db, _parcel.Id, _carla, 2, false, Guid.NewGuid().ToString(), 500, _clock, default)).Ok);
        var tenant = await About(_carla); Assert.True(tenant.GetProperty("me").GetProperty("isTenant").GetBoolean()); Assert.False(tenant.GetProperty("me").GetProperty("canManage").GetBoolean());
        var contract = tenant.GetProperty("rental"); Assert.Equal(("carla", "alice", 500L, "active"), (contract.GetProperty("tenant").GetString(), contract.GetProperty("landlord").GetString(), contract.GetProperty("price").GetInt64(), contract.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.Object, (await About(_alice)).GetProperty("rental").ValueKind); Assert.Equal(JsonValueKind.Null, (await About(_stranger)).GetProperty("rental").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await About(_alice)).GetProperty("rentOffer").ValueKind); Assert.False((await About(_alice)).GetProperty("rights").GetProperty("canResell").GetBoolean()); // not while it is rented
    }

    [Fact]
    public async Task AnUnknownParcelOrAnotherIslandsParcelHasNoAbout()
    {
        Assert.Null(await LandAbout.AboutAsync(_db, _island, Guid.NewGuid(), _alice, _clock.Now, default));
        var other = new Island { Id = Guid.NewGuid(), OwnerUserId = _bruno, Name = "Outra", Slug = "outra", IsPublic = true, Size = 256 }; _db.Islands.Add(other); await _db.SaveChangesAsync();
        Assert.Null(await LandAbout.AboutAsync(_db, other, _parcel.Id, _alice, _clock.Now, default));
    }

    private Guid GiveImage(Guid owner, string mime)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = "foto", Category = "textures", CreatedAt = _clock.Now, UpdatedAt = _clock.Now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = "h", StorageKey = "k", MimeType = mime });
        _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = "foto" }); _db.SaveChanges(); return asset.Id;
    }

    [Fact]
    public async Task OnlyTheOwnerOrStaffSetThePicture_ItMustBeAnImageTheyOwn_AndCanBeTakenOff()
    {
        var mine = GiveImage(_alice, "image/png"); var audio = GiveImage(_alice, "audio/mpeg"); var his = GiveImage(_bruno, "image/png");
        Assert.Equal("forbidden", (await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _carla, $"asset:{mine}", _clock, default)).Error);
        Assert.Equal("bad_image", (await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, "http://x/y.png", _clock, default)).Error); Assert.Equal("bad_image", (await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, $"asset:{audio}", _clock, default)).Error);
        Assert.Equal("bad_image", (await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, $"asset:{his}", _clock, default)).Error); Assert.Equal("bad_image", (await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, $"asset:{Guid.NewGuid()}", _clock, default)).Error);
        Assert.Equal("not_found", (await LandAbout.SetImageAsync(_db, Guid.NewGuid(), _parcel.Id, _alice, $"asset:{mine}", _clock, default)).Error);
        Assert.True((await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, $"asset:{mine}", _clock, default)).Ok); Assert.Equal($"asset:{mine:D}", (await About(_alice)).GetProperty("parcel").GetProperty("imageUrl").GetString());
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.Operation == "image");
        Assert.True((await LandAbout.SetImageAsync(_db, _island.Id, _parcel.Id, _alice, "", _clock, default)).Ok); Assert.Equal("", (await About(_alice)).GetProperty("parcel").GetProperty("imageUrl").GetString());
    }
}
