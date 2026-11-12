using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The real estate market: what is listed, how the filters, sorting and pages work, and what is kept out.</summary>
public sealed class LandMarketTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _seller = Guid.NewGuid(), _buyer = Guid.NewGuid(), _tenant = Guid.NewGuid();
    private readonly Island _island, _closed;
    private int _slot;

    public LandMarketTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha Tropical", Slug = "tropical", IsPublic = true, Size = 256 };
        _closed = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha Fechada", Slug = "fechada", IsPublic = false, Size = 256 };
        _db.Islands.AddRange(_island, _closed);
        foreach (var user in new[] { _seller, _buyer, _tenant }) _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = user, AvailableCoins = 1_000_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private IslandParcel Claim(Island island, string name, int side = 4, string category = "residencial")
    {
        _clock.Now = _clock.Now.AddMinutes(1); var x0 = -30 + (_slot % 4) * 14; var z0 = -30 + (_slot / 4) * 14; _slot++;
        var cells = new HashSet<long>(); for (var x = 0; x < side; x++) for (var z = 0; z < side; z++) cells.Add(LandGrid.Key(x0 + x, z0 + z));
        var result = LandService.ClaimAsync(_db, island, _seller, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); var parcel = _db.IslandParcels.Single(p => p.Id == result.Parcel!.Id); parcel.Category = category; _db.SaveChanges(); return parcel;
    }

    private async Task Sell(IslandParcel p, long price) => Assert.True((await LandCommerce.OfferForSaleAsync(_db, p.Id, p.OwnerUserId, new SaleOffer(price, null, "", true), _clock, default)).Ok);
    private async Task Rent(IslandParcel p, long price, int days = 30) => Assert.True((await LandCommerce.OfferForRentAsync(_db, p.Id, p.OwnerUserId, new RentOffer(price, days, 1, 6, 0, 3, true), _clock, default)).Ok);
    private Task<MarketPage> Search(MarketQuery? q = null, Guid? who = null) => LandMarket.SearchAsync(_db, who ?? _buyer, q ?? Query(), default);
    private static MarketQuery Query(string? offer = null, string? category = null, string? sort = null, int page = 1, int size = 9, bool onlyAvailable = true, bool visit = false, long? min = null, long? max = null, double? minArea = null, double? maxArea = null, string? q = null) =>
        new(q, offer, category, min, max, minArea, maxArea, onlyAvailable, visit, sort, page, size);

    [Fact]
    public async Task OnlyRealOffersOnPublicIslandsAreListedAndNeverTheViewersOwn()
    {
        var a = Claim(_island, "A"); var b = Claim(_island, "B"); var hidden = Claim(_closed, "Escondida"); var plain = Claim(_island, "Sem oferta");
        await Sell(a, 5000); await Rent(b, 300); await Sell(hidden, 100); _ = plain;
        var page = await Search(); Assert.Equal(2, page.Total); Assert.Equal(new[] { "A", "B" }, page.Items.Select(i => i.Name).OrderBy(n => n));
        Assert.Equal(0, (await Search(who: _seller)).Total); // the seller does not see their own land in the market
        var sale = page.Items.Single(i => i.Name == "A"); Assert.Equal(("sale", "sale", 5000L), (sale.Offer, sale.State, sale.Price)); Assert.Equal("Ilha Tropical", sale.IslandName);
        var rent = page.Items.Single(i => i.Name == "B"); Assert.Equal(("rent", "rent", 300L, 30), (rent.Offer, rent.State, rent.Price, rent.RentPeriodDays));
    }

    [Fact]
    public async Task OfferAndCategoryFiltersNarrowTheList()
    {
        var a = Claim(_island, "Casa", category: "residencial"); var b = Claim(_island, "Loja", category: "comercial"); var c = Claim(_island, "Estúdio", category: "comercial");
        await Sell(a, 1000); await Sell(b, 2000); await Rent(c, 50);
        Assert.Equal(3, (await Search()).Total); Assert.Equal(2, (await Search(Query(offer: "sale"))).Total); Assert.Equal(1, (await Search(Query(offer: "rent"))).Total);
        Assert.Equal(2, (await Search(Query(category: "comercial"))).Total); Assert.Equal(1, (await Search(Query(category: "comercial", offer: "sale"))).Total);
        Assert.Equal(3, (await Search(Query(category: "inventada"))).Total); // an unknown category filters nothing
    }

    [Fact]
    public async Task PriceAreaAndTextFiltersWork()
    {
        var a = Claim(_island, "Pequena", side: 4); var b = Claim(_island, "Grande", side: 8); await Sell(a, 1000); await Sell(b, 9000);
        Assert.Equal(["Grande"], (await Search(Query(min: 5000))).Items.Select(i => i.Name)); Assert.Equal(["Pequena"], (await Search(Query(max: 5000))).Items.Select(i => i.Name));
        Assert.Equal(["Grande"], (await Search(Query(minArea: 500))).Items.Select(i => i.Name)); Assert.Equal(["Pequena"], (await Search(Query(maxArea: 500))).Items.Select(i => i.Name));
        Assert.Equal(["Grande"], (await Search(Query(q: "gran"))).Items.Select(i => i.Name)); Assert.Equal(2, (await Search(Query(q: "tropical"))).Total);
    }

    [Fact]
    public async Task SortingAndPagesGiveEveryListingExactlyOnce()
    {
        for (var i = 0; i < 7; i++) { var p = Claim(_island, $"T{i}", side: 3 + i % 3); await Sell(p, 1000 + i * 100); }
        var all = new List<string>();
        for (var page = 1; page <= 3; page++) { var result = await Search(Query(sort: "price", page: page, size: 3)); Assert.Equal(7, result.Total); Assert.Equal(page, result.Page); all.AddRange(result.Items.Select(x => x.Name)); }
        Assert.Equal(7, all.Distinct().Count()); Assert.Equal(["T0", "T1", "T2", "T3", "T4", "T5", "T6"], all);
        Assert.Equal("T6", (await Search(Query(sort: "price_desc", size: 3))).Items[0].Name);
        Assert.Equal(7, (await Search(Query(page: 99, size: 3))).Items.Count + 6); // a page past the end becomes the last page (one item)
        Assert.Equal(30, (await Search(Query(size: 500))).PageSize);
        var byArea = (await Search(Query(sort: "area", size: 30))).Items; Assert.True(byArea.Zip(byArea.Skip(1)).All(pair => pair.First.Area >= pair.Second.Area));
    }

    [Fact]
    public async Task RentedLandIsHiddenUnlessAllListingsAreAsked_AndSaleForSomebodyElseIsNeverShown()
    {
        var rented = Claim(_island, "Alugada"); var free = Claim(_island, "Livre"); var private_ = Claim(_island, "Para outro");
        await Rent(rented, 100); await Rent(free, 100); Assert.True((await LandCommerce.RentAsync(_db, rented.Id, _tenant, 1, false, Guid.NewGuid().ToString(), 100, _clock, default)).Ok);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, private_.Id, _seller, new SaleOffer(700, _tenant, "", true), _clock, default)).Ok);
        Assert.Equal(["Livre"], (await Search()).Items.Select(i => i.Name));
        var every = await Search(Query(onlyAvailable: false)); Assert.Equal(2, every.Total); Assert.Equal("rented", every.Items.Single(i => i.Name == "Alugada").State);
        Assert.Equal(["Para outro"], (await Search(who: _tenant, q: null)).Items.Where(i => i.Name == "Para outro").Select(i => i.Name)); // the one it was kept for sees it
    }

    [Fact]
    public async Task OnlyLandPeopleCanVisitIsKeptWhenVisitIsAsked()
    {
        var open = Claim(_island, "Aberta"); var shut = Claim(_island, "Fechada"); await Sell(open, 1); await Sell(shut, 1);
        Assert.True((await LandAdmin.SetAccessModeAsync(_db, shut.Id, _seller, "private", _clock, default)).Ok);
        Assert.Equal(2, (await Search()).Total); var visit = await Search(Query(visit: true)); Assert.Equal(["Aberta"], visit.Items.Select(i => i.Name)); Assert.All(visit.Items, i => Assert.True(i.CanVisit));
        Assert.False((await Search()).Items.Single(i => i.Name == "Fechada").CanVisit);
    }

    private Island MakeIsland(string name, int size = 256, bool isPublic = true) { var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = name, Slug = name.ToLowerInvariant().Replace(" ", "-"), IsPublic = isPublic, Size = size }; _db.Islands.Add(island); _db.SaveChanges(); return island; }

    [Fact]
    public async Task WholeIslandsForSaleOrRentAreListedNextToTheLand_WithTheirKindAndRegion()
    {
        var full = MakeIsland("Ilha Grande"); var small = MakeIsland("Ilha Pequena", 128); var hidden = MakeIsland("Ilha Secreta", isPublic: false); var parcel = Claim(_island, "Terreno"); await Sell(parcel, 900);
        Assert.True((await IslandCommerce.OfferForSaleAsync(_db, full.Id, _seller, new SaleOffer(50000, null, "", true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, small.Id, _seller, new RentOffer(2000, 30, 1, 6, 0, 3, false), _clock, default)).Ok);
        Assert.True((await IslandCommerce.OfferForSaleAsync(_db, hidden.Id, _seller, new SaleOffer(1, null, "", true), _clock, default)).Ok);
        var all = await Search(); Assert.Equal(3, all.Total); Assert.Equal(["island", "island", "parcel"], all.Items.Select(i => i.Kind).OrderBy(k => k).ToArray());
        var big = all.Items.Single(i => i.Name == "Ilha Grande"); Assert.Equal(("island", "full", "sale", 50000L, 256d * 256d), (big.Kind, big.Region, big.State, big.Price, big.Area)); Assert.Equal(Guid.Empty, big.ParcelId);
        var little = all.Items.Single(i => i.Name == "Ilha Pequena"); Assert.Equal(("homestead", "rent", 30), (little.Region, little.State, little.RentPeriodDays));
        Assert.Equal(["Terreno"], (await Search(Query() with { Type = "parcel" })).Items.Select(i => i.Name)); Assert.Equal(2, (await Search(Query() with { Type = "island" })).Total);
        Assert.Equal(["Ilha Grande"], (await Search(Query() with { Type = "island", Region = "full" })).Items.Select(i => i.Name)); Assert.Equal(["Ilha Pequena"], (await Search(Query() with { Type = "island", Region = "homestead" })).Items.Select(i => i.Name));
        Assert.Equal(["Terreno"], (await Search(Query(category: "residencial"))).Items.Select(i => i.Name)); // a category of land keeps islands out
        Assert.Equal(["Ilha Grande"], (await Search(Query(offer: "sale", min: 10000))).Items.Select(i => i.Name)); Assert.Equal(0, (await Search(who: _seller)).Total);
    }

    [Fact]
    public async Task IslandPagesAndSortingMixWithLandAndRentedIslandsNeedTheFlag()
    {
        var island = MakeIsland("Ilha Alugada"); Assert.True((await IslandCommerce.OfferForRentAsync(_db, island.Id, _seller, new RentOffer(700, 10, 1, 4, 0, 2, false), _clock, default)).Ok);
        for (var i = 0; i < 3; i++) { var p = Claim(_island, $"T{i}"); await Sell(p, 1000 + i * 100); }
        var cheap = await Search(Query(sort: "price", size: 2)); Assert.Equal(4, cheap.Total); Assert.Equal(["Ilha Alugada", "T0"], cheap.Items.Select(i => i.Name)); var second = await Search(Query(sort: "price", size: 2, page: 2)); Assert.Equal(["T1", "T2"], second.Items.Select(i => i.Name));
        Assert.True((await IslandCommerce.RentAsync(_db, island.Id, _tenant, 1, Guid.NewGuid().ToString(), 700, _clock, default)).Ok);
        Assert.Equal(3, (await Search()).Total); var every = await Search(Query(onlyAvailable: false)); Assert.Equal("rented", every.Items.Single(i => i.Name == "Ilha Alugada").State);
    }

    [Fact]
    public async Task AParcelWithAPictureCarriesItToTheMarketAndOneWithoutLeavesItEmpty()
    {
        var withPicture = Claim(_island, "Com foto"); var without = Claim(_island, "Sem foto"); await Sell(withPicture, 100); await Sell(without, 200);
        var picture = $"asset:{Guid.NewGuid():D}"; var stored = await _db.IslandParcels.SingleAsync(p => p.Id == withPicture.Id); stored.ImageUrl = picture; await _db.SaveChangesAsync();
        var items = (await Search()).Items; Assert.Equal(picture, items.Single(i => i.Name == "Com foto").Image); Assert.Equal("", items.Single(i => i.Name == "Sem foto").Image);
    }
}
