using System.Diagnostics;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Land;
using LifeVerse.Shared.Land;
using LifeVerse.Shared.Terrain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Phase 7: what the land system must survive: changes seen by others, a restart, two administrators at once, a banned person teleporting, a big island.</summary>
public sealed class LandHardeningTests : IDisposable
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider { public override DateTimeOffset GetUtcNow() => start; }

    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly Island _island;
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly Guid _owner = Guid.NewGuid(), _visitor = Guid.NewGuid(), _admin = Guid.NewGuid(), _instance = Guid.NewGuid();

    public LandHardeningTests()
    {
        _db = new LifeVerseDbContext(_options);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = Guid.NewGuid(), Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 1024 };
        _db.Islands.Add(_island);
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _island.OwnerUserId });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static HashSet<long> Rect(int cx0, int cz0, int cx1, int cz1) { var set = new HashSet<long>(); for (var x = cx0; x <= cx1; x++) for (var z = cz0; z <= cz1; z++) set.Add(LandGrid.Key(x, z)); return set; }

    private Guid Claim(string name, HashSet<long> cells, Guid? owner = null)
    {
        var result = LandService.ClaimAsync(_db, _island, owner ?? _owner, name, "", cells, 100, null, null, null, null, null, _clock, default).GetAwaiter().GetResult();
        Assert.Null(result.Error); return result.Parcel!.Id;
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    // ---- other people see the change ----------------------------------------------------------------------------------

    [Fact]
    public async Task Version_MovesWhenTheLandChanges_AndMatchesTheView()
    {
        Assert.Equal(0, await LandService.VersionAsync(_db, _island.Id, default));
        var id = Claim("A", Rect(0, 0, 3, 3));
        var v1 = await LandService.VersionAsync(_db, _island.Id, default);
        Assert.NotEqual(0, v1);
        Assert.Equal(v1, (await LandService.ViewAsync(_db, _island, _visitor, default)).Version);
        Assert.Equal(v1, await LandService.VersionAsync(_db, _island.Id, default)); // asking does not change it

        var parcel = await _db.IslandParcels.SingleAsync(p => p.Id == id); parcel.UpdatedAt = parcel.UpdatedAt.AddSeconds(5); await _db.SaveChangesAsync();
        Assert.NotEqual(v1, await LandService.VersionAsync(_db, _island.Id, default));
        Claim("B", Rect(10, 0, 13, 3));
        Assert.Equal((await LandService.ViewAsync(_db, _island, _visitor, default)).Version, await LandService.VersionAsync(_db, _island.Id, default));
    }

    [Fact]
    public async Task Version_MovesWhenAnOwnerChangesAnOffer()
    {
        var id = Claim("A", Rect(0, 0, 3, 3));
        var before = await LandService.VersionAsync(_db, _island.Id, default);
        var later = new Clock(_clock.GetUtcNow().AddMinutes(1));
        Assert.True((await LandCommerce.OfferForSaleAsync(_db, id, _owner, new SaleOffer(500, null, null, true), later, default)).Ok);
        Assert.NotEqual(before, await LandService.VersionAsync(_db, _island.Id, default));
    }

    // ---- a banned person teleporting -------------------------------------------------------------------------------------

    [Fact]
    public async Task ABannedPersonCannotTeleportIntoTheParcel_NorToASpotInsideIt()
    {
        var id = Claim("Casa", Rect(0, 0, 3, 3));
        Assert.True((await LandAdmin.AddAccessAsync(_db, id, _owner, _visitor, "ban", null, "teste", _clock, default)).Ok);
        var answer = await LandSync.BuildAsync(_db, _instance, 0, _clock, default);
        var access = LandAccessMap.Parse(JsonSerializer.SerializeToElement(answer!.Access, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var teleport = LandTeleport.Parse(JsonSerializer.SerializeToElement(answer.Teleport, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        Assert.Equal("no_access", teleport.Resolve(id, 0, 0, (x, z) => access.Denies(_visitor, x, z, now)).Code);
        Assert.Equal("no_access", teleport.Resolve(null, 6, 6, (x, z) => access.Denies(_visitor, x, z, now)).Code);
        Assert.True(teleport.Resolve(id, 0, 0, (x, z) => access.Denies(_owner, x, z, now)).Ok); // the owner is not kept out
        Assert.True(teleport.Resolve(null, 100, 100, (x, z) => access.Denies(_visitor, x, z, now)).Ok); // open ground is fine
    }

    // ---- restart ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AfterARestartTheServerLearnsTheSameRulesFromTheDatabase()
    {
        var a = Claim("A", Rect(0, 0, 3, 3)); Claim("B", Rect(10, 0, 13, 3), _visitor);
        await LandAdmin.SetAccessModeAsync(_db, a, _owner, "list", _clock, default);
        await LandAdmin.AddAccessAsync(_db, a, _owner, _visitor, "ban", null, "x", _clock, default);
        await LandPresentation.SetArrivalAsync(_db, a, _owner, 2, 2, 90, "forced", _clock, default);
        var before = await LandSync.BuildAsync(_db, _instance, 0, _clock, default);

        using var reborn = new LifeVerseDbContext(_options); // a new process with nothing in memory
        var after = await LandSync.BuildAsync(reborn, _instance, 0, _clock, default);
        Assert.Equal(Json(before!.Access), Json(after!.Access));
        Assert.Equal(Json(before.Terrain), Json(after.Terrain));
        Assert.Equal(Json(before.Teleport), Json(after.Teleport));
        Assert.Equal(await LandService.VersionAsync(_db, _island.Id, default), await LandService.VersionAsync(reborn, _island.Id, default));
    }

    [Fact]
    public void TerrainEditedWithinRightsSurvivesSavingAndLoading()
    {
        var map = new TerrainMap();
        var rights = LandTerrainRights.FromCells([Rect(0, 0, 7, 7)], _owner);
        var mask = rights.MaskFor(_owner, map);
        Assert.True(map.Apply(TerrainTool.Raise, 16, 16, 6, 1f, 0, new BrushOptions(false, 1f), mask));
        var bytes = map.Serialize();
        Assert.True(TerrainMap.TryDeserialize(bytes, out var loaded));
        Assert.Equal(map.SampleHeight(16, 16), loaded.SampleHeight(16, 16));
        Assert.Equal(map.SampleHeight(-40, -40), loaded.SampleHeight(-40, -40));
        Assert.Equal(bytes, loaded.Serialize());
    }

    // ---- two administrators at the same moment ---------------------------------------------------------------------------

    [Fact]
    public async Task TwoAdministratorsChangingTheSameParcelAtOnce_TheSecondOneIsToldToTryAgain()
    {
        var id = Claim("A", Rect(0, 0, 3, 3));
        var start = (await _db.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == id)).Revision;
        using var one = new LifeVerseDbContext(_options); using var two = new LifeVerseDbContext(_options);
        var raced = false;
        two.SavingChanges += (_, _) =>
        {
            if (raced) return; raced = true;
            // the other administrator finishes between this one's reading of the parcel and saving
            var inner = LandAdmin.SetAccessModeAsync(one, id, _owner, "list", _clock, default).GetAwaiter().GetResult();
            Assert.True(inner.Ok, inner.Error);
        };
        var loser = await LandAdmin.SetAccessModeAsync(two, id, _owner, "private", _clock, default);
        Assert.False(loser.Ok); Assert.Equal("conflict", loser.Error);
        using var check = new LifeVerseDbContext(_options);
        var parcel = await check.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal("list", parcel.AccessMode); Assert.Equal(start + 1, parcel.Revision);
        // the loser can read the new state and try again
        var again = await LandAdmin.SetAccessModeAsync(check, id, _owner, "private", _clock, default);
        Assert.True(again.Ok, again.Error);
        Assert.Equal(start + 2, (await check.IslandParcels.AsNoTracking().SingleAsync(p => p.Id == id)).Revision);
    }

    [Fact]
    public async Task AnAdministratorSplittingWhileTheOwnerSellsLeavesOneConsistentParcel()
    {
        var id = Claim("Grande", Rect(0, 0, 7, 3));
        using var one = new LifeVerseDbContext(_options); using var two = new LifeVerseDbContext(_options);
        var split = Task.Run(() => LandAdmin.SplitAsync(one, id, _owner, Rect(4, 0, 7, 3), "Metade", _clock, default));
        var sell = Task.Run(() => LandCommerce.OfferForSaleAsync(two, id, _owner, new SaleOffer(100, null, null, true), _clock, default));
        await Task.WhenAll(split, sell);
        using var check = new LifeVerseDbContext(_options);
        var cells = await check.ParcelCells.AsNoTracking().Where(c => c.IslandId == _island.Id).ToListAsync();
        Assert.Equal(cells.Count, cells.Select(c => (c.Cx, c.Cz)).Distinct().Count()); // no cell is in two parcels, none is lost
        Assert.Equal(32, cells.Count);
        var parcels = await check.IslandParcels.AsNoTracking().Where(p => p.DeletedAt == null).ToListAsync();
        foreach (var parcel in parcels) Assert.Equal(cells.Count(c => c.ParcelId == parcel.Id) * 16.0, parcel.Area);
    }

    // ---- size ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ABigIslandIsViewedAndSyncedQuickly()
    {
        const int side = 17; var parcels = new List<IslandParcel>(); var cells = new List<ParcelCell>(); var objects = new List<BuildObject>(); var rng = new Random(7);
        for (var i = 0; i < side; i++)
            for (var j = 0; j < side; j++)
            {
                var id = Guid.NewGuid(); var cx = -120 + i * 6; var cz = -120 + j * 6;
                parcels.Add(new IslandParcel { Id = id, IslandId = _island.Id, OwnerUserId = i % 2 == 0 ? _owner : _visitor, Name = $"P{i}-{j}", BoundsJson = JsonSerializer.Serialize(new { minX = cx * 4, minZ = cz * 4, maxX = cx * 4 + 16, maxZ = cz * 4 + 16 }), Area = 256, MaxObjects = 100, CreatedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow() });
                for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) cells.Add(new ParcelCell { IslandId = _island.Id, ParcelId = id, Cx = cx + x, Cz = cz + z });
                for (var k = 0; k < 14; k++) objects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = k % 3 == 0 ? _visitor : _owner, Name = "o", PrimitiveType = "cube", PositionX = cx * 4 + 1 + (float)rng.NextDouble() * 14, PositionZ = cz * 4 + 1 + (float)rng.NextDouble() * 14, Cost = 3, CreatedAt = _clock.GetUtcNow() });
            }
        _db.IslandParcels.AddRange(parcels); _db.ParcelCells.AddRange(cells); _db.BuildObjects.AddRange(objects); await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var watch = Stopwatch.StartNew();
        var view = await LandService.ViewAsync(_db, _island, _visitor, default);
        var viewTime = watch.Elapsed; watch.Restart();
        var sync = await LandSync.BuildAsync(_db, _instance, 0, _clock, default);
        var syncTime = watch.Elapsed;
        Assert.Equal(side * side, view.Parcels.Count);
        Assert.All(view.Parcels, p => Assert.Equal(14, p.Objects)); // every object was counted in its own parcel
        Assert.NotNull(sync);
        Assert.True(viewTime < TimeSpan.FromSeconds(5), $"view took {viewTime}");
        Assert.True(syncTime < TimeSpan.FromSeconds(5), $"sync took {syncTime}");
    }
}
