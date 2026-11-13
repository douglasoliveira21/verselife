using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>"Minhas Propriedades": what a person holds, counted from the real islands, parcels, contracts and money records, and nothing of other people's.</summary>
public sealed class LandPropertiesTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _alice = Guid.NewGuid(), _bruno = Guid.NewGuid(), _carla = Guid.NewGuid();
    private readonly Island _mine, _his;
    private int _slot;

    public LandPropertiesTests()
    {
        _mine = new Island { Id = Guid.NewGuid(), OwnerUserId = _alice, Name = "Ilha da Alice", Slug = "alice", IsPublic = true, Size = 256, MaxPlayers = 60 };
        _his = new Island { Id = Guid.NewGuid(), OwnerUserId = _bruno, Name = "Ilha do Bruno", Slug = "bruno", IsPublic = true, Size = 256 };
        _db.Islands.AddRange(_mine, _his);
        foreach (var (id, name) in new[] { (_alice, "alice"), (_bruno, "bruno"), (_carla, "carla") })
        {
            _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = name + "@t.local", NormalizedEmail = name.ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _clock.Now });
            _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = id, AvailableCoins = 1_000_000, CreatedAt = _clock.Now, UpdatedAt = _clock.Now });
        }
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private IslandParcel Claim(Island island, Guid owner, string name)
    {
        _clock.Now = _clock.Now.AddMinutes(1); var x0 = -30 + (_slot % 4) * 14; var z0 = -30 + (_slot / 4) * 14; _slot++;
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x0 + x, z0 + z));
        var owned = island.OwnerUserId == owner; var result = LandService.ClaimAsync(_db, island, island.OwnerUserId, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); var parcel = _db.IslandParcels.Single(p => p.Id == result.Parcel!.Id);
        if (!owned) { parcel.OwnerUserId = owner; _db.SaveChanges(); }
        parcel.Code = $"L-{_slot:00}"; _db.SaveChanges(); return parcel;
    }

    private async Task<JsonElement> Mine(Guid who) => JsonSerializer.SerializeToElement(await LandProperties.BuildAsync(_db, who, _clock.Now, default));

    [Fact]
    public async Task EmptyAccountIsAllZeros()
    {
        var data = await Mine(_carla); var s = data.GetProperty("summary");
        Assert.Equal((0, 0, 0, 0L, 0), (s.GetProperty("total").GetInt32(), s.GetProperty("inUse").GetInt32(), s.GetProperty("rentedOut").GetInt32(), s.GetProperty("monthlyRevenue").GetInt64(), s.GetProperty("expiringSoon").GetInt32()));
        Assert.Equal(0, data.GetProperty("islands").GetArrayLength()); Assert.Equal(0, data.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task TheSummaryAndTheListsCountWhatIsReallyHeld()
    {
        var rentedOut = Claim(_mine, _alice, "Alugada"); var forSale = Claim(_mine, _alice, "Anunciada"); var free = Claim(_mine, _alice, "Livre"); var his = Claim(_his, _bruno, "Do Bruno");
        Assert.True((await LandCommerce.OfferForRentAsync(_db, rentedOut.Id, _alice, new RentOffer(3000, 30, 1, 6, 100, 3, true), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, rentedOut.Id, _bruno, 1, false, Guid.NewGuid().ToString(), 3000, _clock, default)).Ok);
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, forSale.Id, _alice, new SaleOffer(9000, null, "", true), _clock, default)).Ok);
        Assert.True((await LandCommerce.OfferForRentAsync(_db, his.Id, _bruno, new RentOffer(500, 7, 1, 4, 0, 3, true), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, his.Id, _alice, 2, false, Guid.NewGuid().ToString(), 500, _clock, default)).Ok);
        _ = free;

        var data = await Mine(_alice); var s = data.GetProperty("summary");
        Assert.Equal(1, s.GetProperty("islands").GetInt32()); Assert.Equal(3, s.GetProperty("parcels").GetInt32()); Assert.Equal(1, s.GetProperty("rentedIn").GetInt32());
        Assert.Equal(5, s.GetProperty("total").GetInt32()); // the island, three parcels, and the one she rents
        Assert.Equal(1, s.GetProperty("rentedOut").GetInt32()); Assert.Equal(3000, s.GetProperty("monthlyRevenue").GetInt64());
        Assert.Equal(3, s.GetProperty("inUse").GetInt32()); // the island is offline (not counted); two parcels she keeps for herself and the one she rents
        var kinds = data.GetProperty("parcels").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("kind").GetString()!);
        Assert.Equal(("rented", "sale", "available"), (kinds["Alugada"], kinds["Anunciada"], kinds["Livre"])); Assert.DoesNotContain("Do Bruno", kinds.Keys);
        Assert.Equal(1, data.GetProperty("listings").GetArrayLength()); var listing = data.GetProperty("listings")[0]; Assert.Equal(("sale", 9000L), (listing.GetProperty("kind").GetString(), listing.GetProperty("price").GetInt64()));
        var contracts = data.GetProperty("contracts").EnumerateArray().ToList(); Assert.Equal(2, contracts.Count);
        Assert.Equal(["landlord", "tenant"], contracts.Select(c => c.GetProperty("role").GetString()!).OrderBy(r => r).ToArray());
        Assert.Equal("Ilha do Bruno", contracts.Single(c => c.GetProperty("role").GetString() == "tenant").GetProperty("island").GetString());
    }

    [Fact]
    public async Task PaymentsAreInOrOutForThePersonAndNeverForSomebodyElse()
    {
        var parcel = Claim(_mine, _alice, "Casa");
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, parcel.Id, _alice, new SaleOffer(7000, null, "", true), _clock, default)).Ok);
        Assert.True((await LandCommerce.BuyAsync(_db, parcel.Id, _bruno, Guid.NewGuid().ToString(), 7000, _clock, default)).Ok);
        var seller = (await Mine(_alice)).GetProperty("payments").EnumerateArray().Single(); Assert.Equal(("in", 7000L, "sale", "bruno"), (seller.GetProperty("direction").GetString(), seller.GetProperty("amount").GetInt64(), seller.GetProperty("type").GetString(), seller.GetProperty("other").GetString()));
        var buyer = (await Mine(_bruno)).GetProperty("payments").EnumerateArray().Single(); Assert.Equal(("out", 7000L), (buyer.GetProperty("direction").GetString(), buyer.GetProperty("amount").GetInt64()));
        Assert.Equal(0, (await Mine(_carla)).GetProperty("payments").GetArrayLength());
        var bought = (await Mine(_bruno)).GetProperty("parcels").EnumerateArray().Single(); Assert.Equal("Casa", bought.GetProperty("name").GetString()); // the land changed hands
        Assert.Equal(0, (await Mine(_alice)).GetProperty("parcels").GetArrayLength());
    }

    [Fact]
    public async Task ContractsThatEndSoonAreCounted_AndEndedOnesStayInTheListButNotInTheSummary()
    {
        var parcel = Claim(_mine, _alice, "Loja");
        Assert.True((await LandCommerce.OfferForRentAsync(_db, parcel.Id, _alice, new RentOffer(1000, 7, 1, 4, 0, 3, true), _clock, default)).Ok);
        Assert.True((await LandCommerce.RentAsync(_db, parcel.Id, _bruno, 1, false, Guid.NewGuid().ToString(), 1000, _clock, default)).Ok);
        Assert.Equal(1, (await Mine(_alice)).GetProperty("summary").GetProperty("expiringSoon").GetInt32()); Assert.Equal(1, (await Mine(_bruno)).GetProperty("summary").GetProperty("expiringSoon").GetInt32());
        var rental = await _db.ParcelRentals.SingleAsync(); rental.Status = "ended"; rental.EndedAt = _clock.Now; await _db.SaveChangesAsync();
        var data = await Mine(_alice); Assert.Equal(0, data.GetProperty("summary").GetProperty("expiringSoon").GetInt32()); Assert.Equal(0, data.GetProperty("summary").GetProperty("monthlyRevenue").GetInt64());
        Assert.Equal(1, data.GetProperty("contracts").GetArrayLength()); Assert.Equal("ended", data.GetProperty("contracts")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnIslandShowsThePlatformPlanThatHostsIt()
    {
        Assert.Equal(JsonValueKind.Null, (await Mine(_alice)).GetProperty("islands")[0].GetProperty("hosting").ValueKind); // no plan: nothing to show
        var plan = new IslandRentalPlan { Id = Guid.NewGuid(), Name = "Plano Ouro", PriceCoins = 1000, DurationDays = 30, MaxSize = 512, MaxObjects = 20000, MaxPlayers = 80, IsActive = true };
        _db.IslandRentalPlans.Add(plan);
        _db.IslandRentals.Add(new IslandRental { Id = Guid.NewGuid(), IslandId = _mine.Id, UserId = _alice, PlanId = plan.Id, StartsAt = _clock.Now.AddDays(-20), ExpiresAt = _clock.Now.AddDays(10), RecoveryUntil = _clock.Now.AddDays(17), Status = "active" });
        await _db.SaveChangesAsync();
        var hosting = (await Mine(_alice)).GetProperty("islands")[0].GetProperty("hosting");
        Assert.Equal(("Plano Ouro", "active", 80, 20000), (hosting.GetProperty("plan").GetString(), hosting.GetProperty("status").GetString(), hosting.GetProperty("maxPlayers").GetInt32(), hosting.GetProperty("maxObjects").GetInt32()));
        _clock.Now = _clock.Now.AddDays(12); // past the end, inside the recovery window
        Assert.Equal("recovery", (await Mine(_alice)).GetProperty("islands")[0].GetProperty("hosting").GetProperty("status").GetString());
    }

    [Fact]
    public async Task AnIslandShowsItsOwnNumbers()
    {
        var parcel = Claim(_mine, _alice, "Casa"); _ = parcel;
        _db.ServerInstances.Add(new ServerInstance { Id = Guid.NewGuid(), IslandId = _mine.Id, Status = "running", PlayerCount = 7, StartedAt = _clock.Now.AddMinutes(-5), LastHeartbeatAt = _clock.Now, Hostname = "h", Port = 1 }); await _db.SaveChangesAsync();
        var island = (await Mine(_alice)).GetProperty("islands")[0];
        Assert.Equal(("Ilha da Alice", "online", 7, 60, 1), (island.GetProperty("name").GetString(), island.GetProperty("state").GetString(), island.GetProperty("visitors").GetInt32(), island.GetProperty("maxPlayers").GetInt32(), island.GetProperty("parcels").GetInt32()));
        Assert.Equal(2, (await Mine(_alice)).GetProperty("summary").GetProperty("inUse").GetInt32()); // the island is online and the parcel is hers to use
        Assert.Equal(0, (await Mine(_bruno)).GetProperty("islands").EnumerateArray().Count(i => i.GetProperty("name").GetString() == "Ilha da Alice"));
    }

    [Fact]
    public async Task IslandsOnOfferLeasesAndIslandPaymentsAreCountedToo()
    {
        var theirs = new Island { Id = Guid.NewGuid(), OwnerUserId = _bruno, Name = "Ilha do Bruno 2", Slug = "bruno2", IsPublic = true, Size = 128 }; _db.Islands.Add(theirs); _db.SaveChanges();
        Assert.True((await IslandCommerce.OfferForSaleAsync(_db, _mine.Id, _alice, new SaleOffer(40000, null, "", true), _clock, default)).Ok);
        Assert.True((await IslandCommerce.OfferForRentAsync(_db, theirs.Id, _bruno, new RentOffer(800, 30, 1, 3, 200, 2, false), _clock, default)).Ok);
        Assert.True((await IslandCommerce.RentAsync(_db, theirs.Id, _alice, 1, Guid.NewGuid().ToString(), 800, _clock, default)).Ok);
        var data = await Mine(_alice); var s = data.GetProperty("summary");
        Assert.Equal(1, s.GetProperty("rentedIn").GetInt32()); Assert.Equal(2, s.GetProperty("total").GetInt32()); // her island and the one she rents
        var island = data.GetProperty("islands")[0]; Assert.Equal((40000L, "full"), (island.GetProperty("salePrice").GetInt64(), island.GetProperty("region").GetString()));
        var listing = data.GetProperty("listings").EnumerateArray().Single(); Assert.Equal(("sale", "island", 40000L), (listing.GetProperty("kind").GetString(), listing.GetProperty("target").GetString(), listing.GetProperty("price").GetInt64()));
        var contract = data.GetProperty("contracts").EnumerateArray().Single(); Assert.Equal(("island", "tenant", "Ilha do Bruno 2"), (contract.GetProperty("target").GetString(), contract.GetProperty("role").GetString(), contract.GetProperty("island").GetString()));
        var payment = data.GetProperty("payments").EnumerateArray().Single(); Assert.Equal(("island", "out", 800L), (payment.GetProperty("target").GetString(), payment.GetProperty("direction").GetString(), payment.GetProperty("amount").GetInt64()));
        var landlord = await Mine(_bruno); Assert.Equal(1, landlord.GetProperty("summary").GetProperty("rentedOut").GetInt32()); Assert.Equal(800, landlord.GetProperty("summary").GetProperty("monthlyRevenue").GetInt64()); Assert.Equal("alice", landlord.GetProperty("islands").EnumerateArray().Single(i => i.GetProperty("name").GetString() == "Ilha do Bruno 2").GetProperty("tenant").GetString());
    }
}
