using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The house rules of an island: who may set them, how new land starts, and which existing parcels they are pushed onto.</summary>
public sealed class IslandRulesTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _admin = Guid.NewGuid(), _stranger = Guid.NewGuid(), _buyer = Guid.NewGuid();
    private readonly Island _island;
    private int _slot;

    private static readonly IslandRuleSet Strict = new("list", "list", false, false, false, 30);

    public IslandRulesTests()
    {
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 256 };
        _db.Islands.Add(_island);
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid Claim(string name, Guid owner)
    {
        var x0 = -30 + _slot * 6; _slot++;
        var cells = new HashSet<long>(); for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(LandGrid.Key(x0 + x, z));
        var result = LandService.ClaimAsync(_db, _island, owner, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private IslandParcel Stored(Guid id) => _db.IslandParcels.AsNoTracking().Single(p => p.Id == id);

    [Fact]
    public void AnIslandWithoutRulesHasThePlainOnes()
    {
        Assert.Equal(IslandRuleSet.Plain, IslandRules.Read(_island));
        _island.RulesJson = "not json"; Assert.Equal(IslandRuleSet.Plain, IslandRules.Read(_island)); // a broken value never breaks the island
        _island.RulesJson = "{\"buildMode\":\"nonsense\",\"allowVoice\":false}"; var read = IslandRules.Read(_island);
        Assert.Equal(("owner", false), (read.BuildMode, read.AllowVoice)); // what is wrong is replaced, what is right is kept
    }

    [Fact]
    public async Task OnlyTheOwnerAndAdministratorsSetTheRules_AndTheyAreChecked()
    {
        Assert.Equal("forbidden", (await IslandRules.SetAsync(_db, _island.Id, _stranger, Strict, false, _clock, default)).Error);
        Assert.Equal("bad_mode", (await IslandRules.SetAsync(_db, _island.Id, _owner, Strict with { AccessMode = "secret" }, false, _clock, default)).Error);
        Assert.Equal("bad_mode", (await IslandRules.SetAsync(_db, _island.Id, _owner, Strict with { BuildMode = "x" }, false, _clock, default)).Error);
        Assert.Equal("bad_duration", (await IslandRules.SetAsync(_db, _island.Id, _owner, Strict with { AutoReturnMinutes = 0 }, false, _clock, default)).Error);
        Assert.Equal("not_found", (await IslandRules.SetAsync(_db, Guid.NewGuid(), _owner, Strict, false, _clock, default)).Error);
        Assert.Equal("{}", _db.Islands.AsNoTracking().Single().RulesJson); // nothing was saved by the refused ones
        Assert.True((await IslandRules.SetAsync(_db, _island.Id, _admin, Strict, false, _clock, default)).Ok);
        Assert.Equal(Strict, IslandRules.Read(_db.Islands.AsNoTracking().Single()));
    }

    [Fact]
    public async Task NewLandStartsWithTheIslandsRules_AndOldLandIsLeftAloneUnlessAsked()
    {
        var before = Claim("Antes", _owner);
        Assert.True((await IslandRules.SetAsync(_db, _island.Id, _owner, Strict, false, _clock, default)).Ok);
        Assert.Equal(("public", true), (Stored(before).AccessMode, Stored(before).AllowVoice)); // not applied to what exists
        var after = Claim("Depois", _owner); var parcel = Stored(after);
        Assert.Equal(("list", "list", false, false, 30), (parcel.AccessMode, parcel.BuildMode, parcel.AllowVoice, parcel.AllowMedia, parcel.AutoReturnMinutes));
        var permissions = JsonDocument.Parse(parcel.PermissionsJson).RootElement; Assert.False(permissions.GetProperty("run_scripts").GetBoolean()); Assert.False(permissions.GetProperty("build").GetBoolean());
    }

    [Fact]
    public async Task ApplyingToExistingLandTouchesOnlyTheIslandOwnersFreeParcels()
    {
        var free = Claim("Livre", _owner); var sold = Claim("Vendido", _buyer); var rented = Claim("Alugado", _owner);
        _db.ParcelRentals.Add(new ParcelRental { Id = Guid.NewGuid(), ParcelId = rented, LandlordUserId = _owner, TenantUserId = _buyer, PricePerPeriod = 100, PeriodDays = 30, StartsAt = _clock.Now, ExpiresAt = _clock.Now.AddDays(10), Status = "active" });
        await _db.SaveChangesAsync();
        var result = await IslandRules.SetAsync(_db, _island.Id, _owner, Strict, true, _clock, default);
        Assert.True(result.Ok, result.Error);
        Assert.Equal(("list", 30, 2), (Stored(free).AccessMode, Stored(free).AutoReturnMinutes, Stored(free).Revision));
        Assert.Equal("public", Stored(sold).AccessMode); Assert.Equal("public", Stored(rented).AccessMode); // somebody else's, and under a contract
        Assert.Contains(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == free && c.Operation == "island_rules");
        Assert.DoesNotContain(_db.ParcelChanges.AsNoTracking(), c => c.ParcelId == sold && c.Operation == "island_rules");
        Assert.True(await LandService.VersionAsync(_db, _island.Id, default) > 0);
    }

    [Fact]
    public async Task TheIslandViewCarriesTheRulesAndWhoMayEditThem()
    {
        await IslandRules.SetAsync(_db, _island.Id, _owner, Strict, false, _clock, default);
        var island = await _db.Islands.AsNoTracking().SingleAsync();
        var owner = JsonSerializer.SerializeToElement(await IslandRealEstate.ViewOfAsync(_db, island, _owner, true, _clock.Now, default));
        Assert.True(owner.GetProperty("canEditRules").GetBoolean()); Assert.Equal("list", owner.GetProperty("rules").GetProperty("buildMode").GetString()); Assert.Equal(30, owner.GetProperty("rules").GetProperty("autoReturnMinutes").GetInt32());
        var visitor = JsonSerializer.SerializeToElement(await IslandRealEstate.ViewOfAsync(_db, island, _stranger, false, _clock.Now, default));
        Assert.False(visitor.GetProperty("canEditRules").GetBoolean()); // everyone can read the rules, only staff change them
    }
}
