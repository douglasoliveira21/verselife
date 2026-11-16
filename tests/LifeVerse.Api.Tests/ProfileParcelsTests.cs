using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>The land featured on a profile: only the owner chooses (and only land in their own name), visitors see just what was chosen, a person can hide all of it, and there is a limit.</summary>
public sealed class ProfileParcelsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _ana, _bia, _island;

    public ProfileParcelsTests()
    {
        (_ana, _bia) = (User("ana"), User("bia"));
        _island = Guid.NewGuid(); _db.Islands.Add(new Island { Id = _island, OwnerUserId = _ana, Name = "Ilha Esmeralda", Slug = "esmeralda", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now.AddDays(-30), UpdatedAt = _now.AddDays(-30) });
        _db.SaveChanges(); return id;
    }

    private Guid Parcel(Guid owner, string name, Guid? group = null, string category = "residencial")
    {
        var parcel = new IslandParcel { Id = Guid.NewGuid(), IslandId = _island, OwnerUserId = owner, GroupId = group, Name = name, Category = category, Area = 400, BoundsJson = "{}", CreatedAt = _now, UpdatedAt = _now };
        _db.IslandParcels.Add(parcel); _db.SaveChanges(); return parcel.Id;
    }

    private async Task<JsonElement> View(Guid viewer, Guid target) => JsonSerializer.SerializeToElement(await ProfileService.ViewAsync(_db, viewer, target, _now, default));

    [Fact]
    public async Task OnlyWhatTheOwnerChoseIsShownToVisitors()
    {
        var home = Parcel(_ana, "Casa da Ana"); Parcel(_ana, "Depósito");
        Assert.Empty((await View(_bia, _ana)).GetProperty("parcels").EnumerateArray());
        Assert.Equal("", await ProfileService.SetParcelShownAsync(_db, _ana, home, true, default));
        var seen = (await View(_bia, _ana)).GetProperty("parcels").EnumerateArray().ToList();
        Assert.Single(seen); Assert.Equal("Casa da Ana", seen[0].GetProperty("name").GetString()); Assert.Equal("Ilha Esmeralda", seen[0].GetProperty("islandName").GetString()); Assert.Equal("residencial", seen[0].GetProperty("category").GetString());
    }

    [Fact]
    public async Task TheOwnerSeesAllTheirLandWithWhetherEachIsShown()
    {
        var home = Parcel(_ana, "Casa"); Parcel(_ana, "Depósito"); await ProfileService.SetParcelShownAsync(_db, _ana, home, true, default);
        var mine = (await View(_ana, _ana)).GetProperty("parcels").EnumerateArray().ToList();
        Assert.Equal(2, mine.Count); Assert.True(mine[0].GetProperty("shown").GetBoolean()); Assert.False(mine[1].GetProperty("shown").GetBoolean());
    }

    [Fact]
    public async Task NobodyCanFeatureLandThatIsNotTheirOwnOrBelongsToAGroup()
    {
        var anas = Parcel(_ana, "Casa"); var shared = Parcel(_ana, "Clube", group: Guid.NewGuid());
        Assert.Null(await ProfileService.SetParcelShownAsync(_db, _bia, anas, true, default));
        Assert.Null(await ProfileService.SetParcelShownAsync(_db, _ana, shared, true, default));
        Assert.Null(await ProfileService.SetParcelShownAsync(_db, _ana, Guid.NewGuid(), true, default));
        Assert.DoesNotContain(await _db.IslandParcels.ToListAsync(), p => p.ShowInProfile);
    }

    [Fact]
    public async Task TheListHasALimit()
    {
        var ids = Enumerable.Range(0, ProfileService.MaxParcelsShown + 1).Select(i => Parcel(_ana, $"Terreno {i}")).ToList();
        foreach (var id in ids.Take(ProfileService.MaxParcelsShown)) Assert.Equal("", await ProfileService.SetParcelShownAsync(_db, _ana, id, true, default));
        Assert.Equal("too_many", await ProfileService.SetParcelShownAsync(_db, _ana, ids[^1], true, default));
        Assert.Equal("", await ProfileService.SetParcelShownAsync(_db, _ana, ids[0], false, default));
        Assert.Equal("", await ProfileService.SetParcelShownAsync(_db, _ana, ids[^1], true, default));
    }

    [Fact]
    public async Task HidingParcelsAndPrivateProfilesHideThemFromOthersButNotFromTheOwner()
    {
        var home = Parcel(_ana, "Casa"); await ProfileService.SetParcelShownAsync(_db, _ana, home, true, default);
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(HideParcels: true), _now, default));
        Assert.Empty((await View(_bia, _ana)).GetProperty("parcels").EnumerateArray());
        var mine = await View(_ana, _ana); Assert.Single(mine.GetProperty("parcels").EnumerateArray()); Assert.True(mine.GetProperty("settings").GetProperty("hideParcels").GetBoolean());
        Assert.Null(await ProfileService.UpdateAsync(_db, _ana, new ProfileUpdate(HideParcels: false, PrivateProfile: true), _now, default));
        Assert.Empty((await View(_bia, _ana)).GetProperty("parcels").EnumerateArray());
    }

    [Fact]
    public async Task DeletedLandDisappears()
    {
        var home = Parcel(_ana, "Casa"); await ProfileService.SetParcelShownAsync(_db, _ana, home, true, default);
        (await _db.IslandParcels.SingleAsync()).DeletedAt = _now; await _db.SaveChangesAsync();
        Assert.Empty((await View(_bia, _ana)).GetProperty("parcels").EnumerateArray());
    }
}
