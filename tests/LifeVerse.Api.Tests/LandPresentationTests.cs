using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Shared.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Arrival points and teleporting, the environment of a parcel, its music, and the search for land.</summary>
public sealed class LandPresentationTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public override DateTimeOffset GetUtcNow() => start; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid(), _instance = Guid.NewGuid();

    public LandPresentationTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HashSet<long> Rect(int cx0, int cz0, int cx1, int cz1) { var set = new HashSet<long>(); for (var x = cx0; x <= cx1; x++) for (var z = cz0; z <= cz1; z++) set.Add(LandGrid.Key(x, z)); return set; }

    private Guid Claim(string name, HashSet<long> cells, Guid? owner = null)
    {
        var result = LandService.ClaimAsync(_db, _island, owner ?? _owner, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private IslandParcel Stored(Guid id) => _db.IslandParcels.AsNoTracking().Single(p => p.Id == id);

    // ---- music addresses (shared rules, and the API keeps the same ones) -------------------------------------

    [Theory]
    [InlineData("https://cdn.example.com/a.mp3", null)]
    [InlineData("https://cdn.example.com/a/b.OGG?x=1", null)]
    [InlineData("http://cdn.example.com/a.mp3", "not_https")]
    [InlineData("https://user:pw@cdn.example.com/a.mp3", "not_https")]
    [InlineData("ftp://cdn.example.com/a.mp3", "not_https")]
    [InlineData("", "not_https")]
    [InlineData("https://localhost/a.mp3", "bad_host")]
    [InlineData("https://intranet/a.mp3", "bad_host")]
    [InlineData("https://minio/a.mp3", "bad_host")]
    [InlineData("https://printer.local/a.mp3", "bad_host")]
    [InlineData("https://db.internal/a.mp3", "bad_host")]
    [InlineData("https://127.0.0.1/a.mp3", "bad_host")]
    [InlineData("https://10.1.2.3/a.mp3", "bad_host")]
    [InlineData("https://172.20.0.5/a.mp3", "bad_host")]
    [InlineData("https://192.168.0.9/a.mp3", "bad_host")]
    [InlineData("https://169.254.169.254/a.mp3", "bad_host")]
    [InlineData("https://[::1]/a.mp3", "bad_host")]
    [InlineData("https://[fd00::1]/a.mp3", "bad_host")]
    [InlineData("https://cdn.example.com/a.exe", "bad_type")]
    [InlineData("https://cdn.example.com/a.mp4", "bad_type")]
    public void MediaUrl_SharedAndApiRulesAgree(string url, string? expected)
    {
        Assert.Equal(expected, LandMediaUrl.Check(url));
        Assert.Equal(expected, LandPresentation.CheckMediaUrl(url));
    }

    [Fact]
    public void MediaUrl_TooLong()
    {
        var url = "https://cdn.example.com/" + new string('a', 600) + ".mp3";
        Assert.Equal("too_long", LandMediaUrl.Check(url)); Assert.Equal("too_long", LandPresentation.CheckMediaUrl(url));
    }

    // ---- environment ------------------------------------------------------------------------------------------

    [Fact]
    public void Environment_ParsesLeniently_AndTheApiNormalizesTheSame()
    {
        const string raw = "{\"hour\":27.5,\"weather\":\"rain\",\"fog\":3,\"light\":0.01,\"sky\":\"#aabbcc\",\"junk\":1}";
        var parsed = ParcelEnvironment.Parse(raw);
        Assert.Equal(3.5f, parsed.Hour); Assert.Equal("rain", parsed.Weather); Assert.Equal(1f, parsed.Fog); Assert.Equal(.2f, parsed.Light); Assert.Equal("#AABBCC", parsed.Sky);
        Assert.Equal(parsed.ToJson(), LandPresentation.NormalizeEnvironment(raw));
        Assert.Equal("{}", LandPresentation.NormalizeEnvironment("{\"weather\":\"lava\",\"sky\":\"red\",\"hour\":\"x\"}"));
        Assert.Null(LandPresentation.NormalizeEnvironment("[1]")); Assert.Null(LandPresentation.NormalizeEnvironment("not json"));
        Assert.True(ParcelEnvironment.Parse("garbage").IsEmpty);
    }

    [Fact]
    public void Environment_ParcelOverridesOnlyWhatItSets()
    {
        var island = new ParcelEnvironment(Hour: 12, Weather: "clear", Light: 1f);
        var seen = new ParcelEnvironment(Weather: "storm", Fog: .4f).Over(island);
        Assert.Equal(12f, seen.Hour); Assert.Equal("storm", seen.Weather); Assert.Equal(.4f, seen.Fog); Assert.Equal(1f, seen.Light); Assert.Null(seen.Sky);
    }

    [Fact]
    public async Task Environment_OnlyTheOwnerSetsIt_AndTheIslandCanForbidIt()
    {
        var id = Claim("A", Rect(0, 0, 3, 3));
        Assert.Equal("forbidden", (await LandPresentation.SetEnvironmentAsync(_db, id, _visitor, "{\"hour\":3}", _clock, default)).Error);
        Assert.Equal("bad_environment", (await LandPresentation.SetEnvironmentAsync(_db, id, _owner, "[1]", _clock, default)).Error);
        var ok = await LandPresentation.SetEnvironmentAsync(_db, id, _owner, "{\"hour\":27,\"weather\":\"fog\"}", _clock, default);
        Assert.True(ok.Ok, ok.Error); Assert.Equal("{\"hour\":3,\"weather\":\"fog\"}", Stored(id).EnvironmentJson); Assert.Equal(2, Stored(id).Revision);
        Assert.True(await _db.ParcelChanges.AnyAsync(c => c.ParcelId == id && c.Operation == "environment"));
        var island = await _db.Islands.SingleAsync(); island.EnvironmentJson = "{\"parcelEnvironment\":false}"; await _db.SaveChangesAsync();
        Assert.Equal("environment_not_allowed", (await LandPresentation.SetEnvironmentAsync(_db, id, _owner, "{\"hour\":5}", _clock, default)).Error);
    }

    // ---- music --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Media_ValidatesTheAddressAndClampsTheVolume()
    {
        var id = Claim("A", Rect(0, 0, 3, 3));
        Assert.Equal("forbidden", (await LandPresentation.SetMediaAsync(_db, id, _visitor, "https://cdn.example.com/a.mp3", .5f, true, "x", _clock, default)).Error);
        Assert.Equal("bad_host", (await LandPresentation.SetMediaAsync(_db, id, _owner, "https://192.168.1.2/a.mp3", .5f, true, "x", _clock, default)).Error);
        Assert.Equal("bad_volume", (await LandPresentation.SetMediaAsync(_db, id, _owner, "https://cdn.example.com/a.mp3", float.NaN, true, "x", _clock, default)).Error);
        Assert.Equal("{}", Stored(id).MediaJson);
        Assert.True((await LandPresentation.SetMediaAsync(_db, id, _owner, " https://cdn.example.com/a.mp3 ", 7f, false, new string('n', 200), _clock, default)).Ok);
        using var doc = JsonDocument.Parse(Stored(id).MediaJson);
        Assert.Equal("https://cdn.example.com/a.mp3", doc.RootElement.GetProperty("musicUrl").GetString()); Assert.Equal(1f, doc.RootElement.GetProperty("volume").GetSingle());
        Assert.False(doc.RootElement.GetProperty("autoplay").GetBoolean()); Assert.Equal(80, doc.RootElement.GetProperty("name").GetString()!.Length);
        Assert.True((await LandPresentation.SetMediaAsync(_db, id, _owner, "", 0, false, null, _clock, default)).Ok);
        Assert.Equal("{}", Stored(id).MediaJson);
    }

    // ---- arrival ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Arrival_MustBeOnTheParcel_AndNotInsideASolidObject()
    {
        var id = Claim("A", Rect(0, 0, 3, 3)); // 0..16 m
        Assert.Equal("forbidden", (await LandPresentation.SetArrivalAsync(_db, id, _visitor, 5, 5, 0, "free", _clock, default)).Error);
        Assert.Equal("bad_mode", (await LandPresentation.SetArrivalAsync(_db, id, _owner, 5, 5, 0, "weird", _clock, default)).Error);
        Assert.Equal("outside_parcel", (await LandPresentation.SetArrivalAsync(_db, id, _owner, 40, 5, 0, "free", _clock, default)).Error);
        Assert.Equal("outside_parcel", (await LandPresentation.SetArrivalAsync(_db, id, _owner, double.NaN, 5, 0, "free", _clock, default)).Error);
        _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Parede", PrimitiveType = "cube", PositionX = 8, PositionY = 1, PositionZ = 8, ScaleX = 4, ScaleY = 3, ScaleZ = 4, PhysicsJson = "{\"collision\":true}", CreatedAt = _clock.GetUtcNow() });
        await _db.SaveChangesAsync();
        Assert.Equal("blocked", (await LandPresentation.SetArrivalAsync(_db, id, _owner, 8.5, 8, 0, "free", _clock, default)).Error);
        var ok = await LandPresentation.SetArrivalAsync(_db, id, _owner, 2, 3, 450, "forced", _clock, default);
        Assert.True(ok.Ok, ok.Error);
        var parcel = Stored(id);
        Assert.Equal(2f, parcel.SpawnX); Assert.Equal(3f, parcel.SpawnZ); Assert.Equal(90f, parcel.ArrivalYaw); Assert.Equal("forced", parcel.TeleportMode);
    }

    [Fact]
    public async Task Arrival_WithoutAPointUsesTheMiddleOfTheLand()
    {
        var id = Claim("L", Rect(0, 0, 5, 0).Concat(Rect(0, 1, 0, 5)).ToHashSet()); // an L: the middle of its box would be outside
        Assert.True((await LandPresentation.SetArrivalAsync(_db, id, _owner, null, null, 0, "free", _clock, default)).Ok);
        var p = Stored(id);
        Assert.Contains(LandGrid.Key(LandGrid.CellOf(p.SpawnX), LandGrid.CellOf(p.SpawnZ)), _db.ParcelCells.Where(c => c.ParcelId == id).AsEnumerable().Select(c => LandGrid.Key(c.Cx, c.Cz)).ToList());
    }

    // ---- what the island server is told ---------------------------------------------------------------------------

    [Fact]
    public async Task LandSync_CarriesTheTeleportRules_AndTheServerResolvesThem()
    {
        var a = Claim("A", Rect(0, 0, 3, 3)); var b = Claim("B", Rect(10, 0, 13, 3), _visitor);
        await LandPresentation.SetArrivalAsync(_db, a, _owner, 2, 2, 90, "forced", _clock, default);
        await LandPresentation.SetArrivalAsync(_db, b, _visitor, null, null, 0, "disabled", _clock, default);
        var answer = await LandSync.BuildAsync(_db, _instance, 0, _clock, default);
        Assert.NotNull(answer?.Teleport);
        var map = LandTeleport.Parse(JsonSerializer.SerializeToElement(answer!.Teleport, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(2, map.Parcels);
        var toA = map.Resolve(a, 10, 10); Assert.True(toA.Ok); Assert.Equal(2, toA.X); Assert.Equal(2, toA.Z); Assert.Equal(90, toA.Yaw);
        Assert.Equal("teleport_disabled", map.Resolve(b, 42, 2).Code);
        Assert.Equal("teleport_disabled", map.Resolve(null, 42, 2).Code); // asking for a spot inside it is refused too
        var forced = map.Resolve(null, 12, 12); Assert.True(forced.Ok); Assert.Equal(2, forced.X); // a forced parcel redirects spots inside it
        var free = map.Resolve(null, 100, 100); Assert.True(free.Ok); Assert.Equal(100, free.X);
        Assert.Equal("unknown_parcel", map.Resolve(Guid.NewGuid(), 1, 1).Code);
        Assert.Equal("invalid", map.Resolve(null, double.NaN, 1).Code);
        Assert.Equal("no_access", map.Resolve(a, 0, 0, (x, z) => x == 2 && z == 2).Code);
    }

    // ---- property search ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_ShowsOnlyOffersThePersonCouldTake()
    {
        var cheap = Claim("Praia", Rect(0, 0, 3, 3)); var big = Claim("Colina", Rect(10, 0, 19, 9)); var reserved = Claim("Reservado", Rect(20, 0, 23, 3)); var rented = Claim("Alugado", Rect(-8, 0, -5, 3)); var quiet = Claim("Quieto", Rect(-20, 0, -17, 3));
        (await _db.IslandParcels.SingleAsync(p => p.Id == cheap)).SalePrice = 100;
        (await _db.IslandParcels.SingleAsync(p => p.Id == big)).RentPrice = 30;
        var r = await _db.IslandParcels.SingleAsync(p => p.Id == reserved); r.SalePrice = 50; r.SaleBuyerUserId = Guid.NewGuid();
        (await _db.IslandParcels.SingleAsync(p => p.Id == rented)).RentPrice = 10;
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = rented, TenantUserId = Guid.NewGuid(), Status = "active", PricePerPeriod = 10, PeriodDays = 30, LandlordUserId = _owner, StartsAt = _clock.GetUtcNow(), ExpiresAt = _clock.GetUtcNow().AddDays(30) });
        await _db.SaveChangesAsync();

        var all = await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery(null, "any", null, null, null, null, "price", 0, 20), default);
        Assert.Equal(["Colina", "Praia"], all.Select(h => h.Name).ToArray()); // sorted by price; reserved, rented and unlisted left out
        Assert.Equal("rent", all[0].Offer); Assert.Equal("sale", all[1].Offer);
        Assert.Equal(["Praia"], (await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery(null, "sale", null, null, null, null, null), default)).Select(h => h.Name).ToArray());
        Assert.Equal(["Colina"], (await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery(null, "any", null, null, 1000, null, null), default)).Select(h => h.Name).ToArray());
        Assert.Equal(["Praia"], (await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery("prai", "any", null, 500, null, null, null), default)).Select(h => h.Name).ToArray());
        Assert.Empty(await LandSearch.SearchAsync(_db, _owner, new PropertyQuery(null, "any", null, null, null, null, null), default)); // your own land is not an offer to you
        Assert.Single(await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery(null, "any", null, null, null, null, "price", 1, 5), default));
        var island = await _db.Islands.SingleAsync(); island.IsPublic = false; await _db.SaveChangesAsync();
        Assert.Empty(await LandSearch.SearchAsync(_db, _visitor, new PropertyQuery(null, "any", null, null, null, null, null), default));
    }

    [Fact]
    public async Task View_ShowsArrivalEnvironmentAndMedia()
    {
        var id = Claim("A", Rect(0, 0, 3, 3));
        await LandPresentation.SetArrivalAsync(_db, id, _owner, 6, 6, 45, "forced", _clock, default);
        await LandPresentation.SetEnvironmentAsync(_db, id, _owner, "{\"weather\":\"rain\"}", _clock, default);
        await LandPresentation.SetMediaAsync(_db, id, _owner, "https://cdn.example.com/a.mp3", .4f, true, "Radio", _clock, default);
        var view = (await LandService.ViewAsync(_db, await _db.Islands.SingleAsync(), _visitor, default)).Parcels.Single();
        Assert.Equal(6, view.ArrivalX); Assert.Equal(45f, view.ArrivalYaw); Assert.Equal("forced", view.TeleportMode); Assert.Contains("rain", view.Environment); Assert.Contains("Radio", view.Media); Assert.True(view.EnvironmentAllowed);
    }
}
