using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What the land management window is fed with: the totals and the state of every parcel come from the real land, only the staff read it, and the codes stay put.</summary>
public sealed class LandOverviewTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _admin = Guid.NewGuid(), _buyer = Guid.NewGuid(), _stranger = Guid.NewGuid();
    private readonly Island _island;

    public LandOverviewTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        foreach (var user in new[] { _owner, _admin, _buyer, _stranger }) { _db.Users.Add(new User { Id = user, Username = "u" + user.ToString("N")[..6], NormalizedUsername = "U" + user.ToString("N")[..6], Email = user.ToString("N")[..6] + "@t.local", NormalizedEmail = user.ToString("N")[..6].ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now }); _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = 100_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now }); }
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner, CreatedAt = _clock.Now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid Claim(string name, int cx0, int cz0 = 0, int side = 4)
    {
        _clock.Now = _clock.Now.AddMinutes(1); cx0 -= 30; var cells = new HashSet<long>(); for (var x = 0; x < side; x++) for (var z = 0; z < side; z++) cells.Add(LandGrid.Key(cx0 + x, cz0 + z));
        var result = LandService.ClaimAsync(_db, _island, _owner, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private async Task<JsonElement> Overview() { var data = await LandOverview.BuildAsync(_db, _island, _owner, _clock.Now, default); return JsonSerializer.SerializeToElement(data); }

    [Fact]
    public async Task OnlyTheOwnerAndTheAdministratorsAreStaff()
    {
        Assert.True(await LandOverview.IsStaffAsync(_db, _island, _owner, _clock.Now, default)); Assert.True(await LandOverview.IsStaffAsync(_db, _island, _admin, _clock.Now, default));
        Assert.False(await LandOverview.IsStaffAsync(_db, _island, _buyer, _clock.Now, default));
        var grant = await _db.IslandPermissions.SingleAsync(); grant.RevokedAt = _clock.Now; await _db.SaveChangesAsync();
        Assert.False(await LandOverview.IsStaffAsync(_db, _island, _admin, _clock.Now, default));
    }

    [Fact]
    public async Task CodesFollowTheOrderOfCreationAndStayWhenAParcelGoesAway()
    {
        Claim("Praia", 0); Claim("Loja", 10); Claim("Bosque", 20);
        var first = (await Overview()).GetProperty("parcels").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("code").GetString()!);
        Assert.Equal(("L-01", "L-02", "L-03"), (first["Praia"], first["Loja"], first["Bosque"]));
        var gone = await _db.IslandParcels.SingleAsync(p => p.Name == "Loja"); gone.DeletedAt = _clock.Now; await _db.SaveChangesAsync();
        Claim("Mirante", 30);
        var second = (await Overview()).GetProperty("parcels").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("code").GetString()!);
        Assert.Equal("L-01", second["Praia"]); Assert.Equal("L-03", second["Bosque"]); Assert.Equal("L-04", second["Mirante"]); // a code is not handed out again
    }

    [Fact]
    public async Task EachParcelGetsTheStateTheMapColoursItBy()
    {
        var free = Claim("Livre", 0); var forSale = Claim("A venda", 10); var forRent = Claim("Alugada", 20); var sold = Claim("Vendida", 30); var common = Claim("Praça", 40);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, forSale, _owner, new SaleOffer(500, null, "", true), _clock, default)).Ok);
        Assert.True((await LandCommerce.OfferForRentAsync(_db, forRent, _owner, new RentOffer(300, 30, 1, 6, 0, 3, true), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, forRent, _buyer, 1, false, Guid.NewGuid().ToString(), 300, _clock, default)).Ok);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, sold, _owner, new SaleOffer(700, null, "", true), _clock, default)).Ok);
        Assert.True((await LandCommerce.BuyAsync(_db, sold, _buyer, Guid.NewGuid().ToString(), 700, _clock, default)).Ok);
        Assert.True((await LandOverview.SetCategoryAsync(_db, _island.Id, common, _owner, "comum", _clock, default)).Ok);

        var overview = await Overview(); var kinds = overview.GetProperty("parcels").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("kind").GetString()!);
        Assert.Equal(("available", "sale", "rented", "sold", "common"), (kinds["Livre"], kinds["A venda"], kinds["Alugada"], kinds["Vendida"], kinds["Praça"]));
        var totals = overview.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("available").GetInt32()); Assert.Equal(1, totals.GetProperty("onSale").GetInt32()); Assert.Equal(1, totals.GetProperty("rented").GetInt32()); Assert.Equal(1, totals.GetProperty("sold").GetInt32()); Assert.Equal(1, totals.GetProperty("common").GetInt32());
        Assert.Equal(5, totals.GetProperty("parcels").GetInt32());
        Assert.Equal(300, totals.GetProperty("monthlyRevenue").GetInt64()); // a 30-day period of 300, the island owner is the landlord
        Assert.Equal(256d * 256d, totals.GetProperty("area").GetDouble()); Assert.Equal(5 * 256d, totals.GetProperty("areaParcelled").GetDouble()); Assert.Equal(256d * 256d - 5 * 256d, totals.GetProperty("areaFree").GetDouble());
        var contract = overview.GetProperty("contracts").EnumerateArray().Single(); Assert.Equal("active", contract.GetProperty("status").GetString()); Assert.Equal(300, contract.GetProperty("price").GetInt64());
        Assert.True(overview.GetProperty("history").GetArrayLength() > 0);
    }

    [Fact]
    public async Task OnlyTheParcelOwnerOrStaffChangeTheCategoryAndOnlyToAKnownOne()
    {
        var parcel = Claim("Casa", 0);
        Assert.Equal("forbidden", (await LandOverview.SetCategoryAsync(_db, _island.Id, parcel, _stranger, "comercial", _clock, default)).Error);
        Assert.Equal("bad_category", (await LandOverview.SetCategoryAsync(_db, _island.Id, parcel, _owner, "fabrica", _clock, default)).Error);
        Assert.True((await LandOverview.SetCategoryAsync(_db, _island.Id, parcel, _admin, "comercial", _clock, default)).Ok);
        Assert.Equal("comercial", (await _db.IslandParcels.AsNoTracking().SingleAsync()).Category);
        Assert.Equal("not_found", (await LandOverview.SetCategoryAsync(_db, Guid.NewGuid(), parcel, _owner, "mista", _clock, default)).Error); // a parcel is only reachable through its own island
    }

    [Fact]
    public async Task AnEmptyIslandGivesZerosAndTheWholeAreaAsFree()
    {
        var totals = (await Overview()).GetProperty("totals");
        Assert.Equal(0, totals.GetProperty("parcels").GetInt32()); Assert.Equal(0, totals.GetProperty("monthlyRevenue").GetInt64()); Assert.Equal(256d * 256d, totals.GetProperty("areaFree").GetDouble());
    }
}
