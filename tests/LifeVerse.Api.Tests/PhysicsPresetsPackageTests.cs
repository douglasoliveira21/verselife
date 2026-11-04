using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Building;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Physics settings, constructions that keep their hierarchy and pivots, and the ready-made presets.</summary>
public sealed class PhysicsAndPackageRulesTests
{
    public static IEnumerable<object?[]> PhysicsCases() =>
    [
        [null], [""], ["{}"], ["{\"collision\":true}"], ["{\"collision\":false}"], ["{\"collision\":true,\"trigger\":true}"], ["not json"], ["[]"], ["\"text\""], ["{\"collision\":\"yes\"}"],
        ["{\"mode\":\"physical\"}"], ["{\"mode\":\"physical\",\"collision\":true,\"mass\":5,\"friction\":0.4,\"bounce\":0.2}"], ["{\"mode\":\"static\"}"], ["{\"mode\":\"flying\"}"], ["{\"mode\":3}"],
        ["{\"mode\":\"physical\",\"collision\":false}"], ["{\"mode\":\"physical\",\"trigger\":true}"], ["{\"mass\":0}"], ["{\"mass\":10001}"], ["{\"mass\":\"heavy\"}"], ["{\"friction\":1.5}"], ["{\"bounce\":-0.1}"], ["{\"mass\":0.01,\"friction\":0,\"bounce\":1}"],
    ];

    [Theory, MemberData(nameof(PhysicsCases))]
    public void TheClientAndTheApiAgreeOnWhatPhysicsSettingsAreAcceptable(string? json) =>
        Assert.Equal(PhysicsRules.Validate(json) is null, ObjectPhysics.Validate(json) is null);

    [Fact]
    public void APhysicalObjectNeedsSolidCollisionAndSaneNumbers()
    {
        Assert.Null(PhysicsRules.Validate("{\"mode\":\"physical\",\"mass\":50}")); Assert.NotNull(PhysicsRules.Validate("{\"mode\":\"physical\",\"collision\":false}")); Assert.NotNull(PhysicsRules.Validate("{\"mass\":0.001}"));
        Assert.True(ObjectPhysics.IsPhysical("{\"mode\":\"physical\"}")); Assert.False(ObjectPhysics.IsPhysical("{\"mode\":\"static\"}")); Assert.False(ObjectPhysics.IsPhysical("garbage")); Assert.False(ObjectPhysics.IsPhysical(null));
    }

    [Fact]
    public void PhysicsSettingsAreWrittenAndReadBackAndAStaticObjectStaysAsItWas()
    {
        var written = PhysicsRules.Write("{\"collision\":true,\"custom\":1}", new PhysicsSettings(true, false, true, 12.5f, .3f, .6f)); Assert.Null(PhysicsRules.Validate(written)); Assert.Contains("\"custom\":1", written);
        var back = PhysicsRules.Read(written); Assert.True(back.Physical); Assert.Equal(12.5f, back.Mass); Assert.Equal(.3f, back.Friction); Assert.Equal(.6f, back.Bounce);
        var stat = PhysicsRules.Write(written, new PhysicsSettings(true)); Assert.DoesNotContain("mode", stat); Assert.DoesNotContain("mass", stat); Assert.False(PhysicsRules.Read(stat).Physical);
        var ghost = PhysicsRules.Write(null, new PhysicsSettings(false, false, true)); Assert.DoesNotContain("physical", ghost); Assert.Contains("\"collision\":false", ghost); // a ghost cannot be physical
        var wild = PhysicsRules.Write("{}", new PhysicsSettings(true, false, true, 1e9f, 9, -4)); Assert.Null(PhysicsRules.Validate(wild)); Assert.Equal(PhysicsRules.MaxMass, PhysicsRules.Read(wild).Mass);
        Assert.False(PhysicsRules.Read("garbage").Physical);
    }

    [Fact]
    public void APhysicalObjectCostsMoreThanOneThatStandsStill()
    {
        var still = BuildCost.Of("cube", "{}", "{}", "{\"collision\":true}", "[]"); var moving = BuildCost.Of("cube", "{}", "{}", "{\"collision\":true,\"mode\":\"physical\"}", "[]");
        Assert.Equal(still + 2, moving); Assert.Equal(BuildCost.Of("cube", "{}", "{}", "{\"collision\":false}", "[]"), BuildCost.Of("cube", "{}", "{}", "{\"collision\":false,\"mode\":\"physical\"}", "[]")); // a ghost costs no physics
    }

    // ---- hierarchy and pivots in saved constructions ------------------------------------------------------------------------------

    private static SourceObject Part(string name, float x, Guid? group, Guid id, Guid? parent, float pivotY = 0) =>
        new("cube", name, "", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"collision\":true}", "[]", group, id, parent, 0, pivotY, 0);

    [Fact]
    public void AConstructionRemembersWhichObjectHangsFromWhichAndTheirPivots()
    {
        var set = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var package = BuildPackages.Create("Mesa", [Part("raiz", 0, set, a, null), Part("filho", 1, set, b, a, -.5f), Part("neto", 2, set, c, b)]);
        Assert.Equal([null, 0, 1], package.Objects.Select(o => o.Parent)); Assert.Equal(-.5f, package.Objects[1].PivotY);
        var parsed = BuildPackages.Parse(BuildPackages.Serialize(package), out var error); Assert.Null(error); Assert.Equal([null, 0, 1], parsed!.Objects.Select(o => o.Parent)); Assert.Equal(-.5f, parsed.Objects[1].PivotY);
        var placed = BuildPackages.Place(parsed, System.Numerics.Vector3.Zero); Assert.Single(placed.Select(p => p.GroupId).Distinct()); Assert.Equal(1, placed[2].Source.Parent);
    }

    [Fact]
    public void AParentOutsideTheConstructionOrOutsideTheGroupIsSavedAsARoot()
    {
        var set = Guid.NewGuid(); var other = Guid.NewGuid(); var a = Guid.NewGuid(); var outsider = Guid.NewGuid(); var b = Guid.NewGuid();
        var package = BuildPackages.Create("x", [Part("a", 0, set, a, outsider), Part("b", 1, other, b, a), Part("solto", 2, null, Guid.NewGuid(), a)]);
        Assert.All(package.Objects, o => Assert.Null(o.Parent));
    }

    [Fact]
    public void AConstructionWithABrokenHierarchyIsRefused()
    {
        PackageObject Make(int? group, int? parent, float pivot = 0) => new("cube", "a", "", 0, 0, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{}", "[]", group, parent, 0, pivot, 0);
        BuildPackage Pack(params PackageObject[] objects) => new(BuildPackages.FormatName, 1, "x", 0, objects);
        string? Why(BuildPackage p) { BuildPackages.Parse(BuildPackages.Serialize(p), out var e); return e; }
        Assert.Null(Why(Pack(Make(0, null), Make(0, 0), Make(0, 1))));
        Assert.NotNull(Why(Pack(Make(0, 1), Make(0, 0)))); Assert.NotNull(Why(Pack(Make(0, 0)))); Assert.NotNull(Why(Pack(Make(0, 5), Make(0, null)))); Assert.NotNull(Why(Pack(Make(0, -1), Make(0, null))));
        Assert.NotNull(Why(Pack(Make(0, null), Make(1, 0)))); Assert.NotNull(Why(Pack(Make(null, null), Make(null, 0)))); Assert.NotNull(Why(Pack(Make(0, null, 70))));
        Assert.NotNull(Why(Pack(Make(0, 2), Make(0, 0), Make(0, 1)))); // a loop of three
    }

    [Fact]
    public void AConstructionSavedBeforeHierarchiesExistedStillOpens()
    {
        const string old = "{\"format\":\"lifeverse.build\",\"version\":1,\"name\":\"Antiga\",\"baseOffset\":0.5,\"objects\":[{\"primitiveType\":\"cube\",\"name\":\"a\",\"description\":\"\",\"x\":0,\"y\":0,\"z\":0,\"rotationX\":0,\"rotationY\":0,\"rotationZ\":0,\"scaleX\":1,\"scaleY\":1,\"scaleZ\":1,\"materialJson\":\"{}\",\"geometryJson\":\"{}\",\"physicsJson\":\"{}\",\"contentJson\":\"[]\",\"group\":null}]}";
        var package = BuildPackages.Parse(old, out var error); Assert.Null(error); Assert.Null(package!.Objects[0].Parent); Assert.Equal(0f, package.Objects[0].PivotY);
        var api = PackageDocument.Parse(old, out var apiError); Assert.Null(apiError); Assert.Null(api!.Objects[0].Parent);
    }

    [Fact]
    public void TheClientAndTheApiWriteTheSameConstructionAndCheckTheSameHierarchy()
    {
        var set = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var shared = BuildPackages.Create("Mesa", [Part("raiz", 0, set, a, null), Part("filho", 1, set, b, a, .25f), Part("solto", 2, null, Guid.NewGuid(), null)]);
        var api = PackageDocument.Create("Mesa", [new("cube", "raiz", "", 0, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"collision\":true}", "[]", set, a, null, 0, 0, 0), new("cube", "filho", "", 1, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"collision\":true}", "[]", set, b, a, 0, .25f, 0), new("cube", "solto", "", 2, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"collision\":true}", "[]", null)]);
        using var left = JsonDocument.Parse(BuildPackages.Serialize(shared)); using var right = JsonDocument.Parse(PackageDocument.Serialize(api));
        Assert.Equal(left.RootElement.GetProperty("objects").EnumerateArray().Select(o => (o.GetProperty("parent").ToString(), o.GetProperty("pivotY").GetSingle())), right.RootElement.GetProperty("objects").EnumerateArray().Select(o => (o.GetProperty("parent").ToString(), o.GetProperty("pivotY").GetSingle())));
        foreach (var bad in new[] { new[] { (0, 1), (0, 0) }, new[] { (0, 0) }, new[] { (0, 9) } })
            Assert.Equal(BuildPackages.HierarchyError(bad.Select(x => ((int?)0, (int?)x.Item2)).ToArray()) is null, PackageDocument.HierarchyError(bad.Select(x => ((int?)0, (int?)x.Item2)).ToArray()) is null);
    }

    // ---- presets ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void EveryPresetIsAValidConstructionTheApiWouldAcceptAndStandsOnTheGround()
    {
        Assert.True(BuildPresets.All.Count >= 5); Assert.Equal(BuildPresets.All.Count, BuildPresets.All.Select(p => p.Id).Distinct().Count());
        foreach (var preset in BuildPresets.All)
        {
            var json = BuildPackages.Serialize(preset.Package);
            Assert.NotNull(BuildPackages.Parse(json, out var error)); Assert.Null(error);
            var api = PackageDocument.Parse(json, out var apiError); Assert.True(api is not null, $"{preset.Id}: {apiError}");
            foreach (var o in api!.Objects)
                Assert.Null(BuildEndpoints.ValidateObject(new BuildObjectRequest(o.PrimitiveType, o.X, o.Y, o.Z, o.RotationX, o.RotationY, o.RotationZ, o.ScaleX, o.ScaleY, o.ScaleZ, o.MaterialJson, o.PhysicsJson, null, 0, o.GeometryJson, o.Name, o.Description, null, null, o.ContentJson)));
            var placed = BuildPackages.Place(preset.Package, System.Numerics.Vector3.Zero);
            Assert.True(Math.Abs(placed.Min(p => p.Position.Y - p.Source.ScaleY / 2f)) < 1e-3f, $"{preset.Id} must stand on the ground"); // its lowest face is on the ground
            Assert.NotEmpty(preset.Description); Assert.True(preset.Parts is >= 1 and <= BuildPackages.MaxObjects);
        }
        Assert.Equal("Mesa", BuildPresets.Find("table")!.Name); Assert.Null(BuildPresets.Find("nothing"));
    }

    [Fact]
    public void ThePresetsThatAreLinksetsHaveOneRootAndTheirPartsHangFromIt()
    {
        foreach (var id in new[] { "stairs", "table", "room" })
        {
            var objects = BuildPresets.Find(id)!.Package.Objects; Assert.Equal(1, objects.Count(o => o.Parent is null)); Assert.Single(objects.Select(o => o.Group).Distinct()); Assert.NotNull(objects[0].Group);
        }
        Assert.Equal(5, BuildPresets.Find("table")!.Parts); Assert.Equal(5, BuildPresets.Find("room")!.Parts); Assert.Equal(6, BuildPresets.Find("stairs")!.Parts);
        foreach (var id in new[] { "wall", "floor", "pillar" }) Assert.Single(BuildPresets.Find(id)!.Package.Objects);
    }
}

/// <summary>The server side: placing a construction brings back its linksets, trees and pivots, and physics settings are checked on every object command.</summary>
public sealed class PhysicsAndHierarchyServerTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public PhysicsAndHierarchyServerTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _builder, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Box(float x, Guid? group = null, Guid? parent = null, string physics = "{\"collision\":true}", float pivotY = 0) =>
        new("cube", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", physics, group, 0, "{}", "Caixa", null, parent, null, null, 0, pivotY, 0);

    private async Task<(int Status, JsonElement Body)> Send(string operation, BuildObjectRequest? @object = null, Guid? objectId = null, Guid[]? ids = null, Guid? package = null, float ox = 0, int revision = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(_builder, operation, objectId, @object, revision, ids, package, ox, 0, 0), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<Guid> Create(BuildObjectRequest o) { var (s, b) = await Send("create", o); Assert.True(s == 200, s + " " + b); return b.GetProperty("objectId").GetGuid(); }

    private async Task<InventoryItem> SaveSet(string name, params SourceObject[] sources)
    {
        var item = await BuildPackageStore.SaveAsync(_db, _storage, _builder, PackageDocument.Create(name, sources.Select(s => new PackageSourceDto(s.PrimitiveType, s.Name, s.Description, s.X, s.Y, s.Z, s.RotationX, s.RotationY, s.RotationZ, s.ScaleX, s.ScaleY, s.ScaleZ, s.MaterialJson, s.GeometryJson, s.PhysicsJson, s.ContentJson, s.GroupId, s.Id, s.ParentId, s.PivotX, s.PivotY, s.PivotZ)).ToList()), _now, default, new LifeVerse.Shared.Items.ItemPermissions(15, 0, 15));
        await _db.SaveChangesAsync(); return item;
    }

    [Fact]
    public async Task APresetPlacedByTheServerComesBackAsALinksetWithItsTreeAndEveryPlacingMakesItsOwn()
    {
        var table = BuildPresets.Find("table")!;
        var item = await BuildPackageStore.SaveAsync(_db, _storage, _builder, PackageDocument.Parse(BuildPackages.Serialize(table.Package), out _)!, _now, default, new LifeVerse.Shared.Items.ItemPermissions(15, 0, 15)); await _db.SaveChangesAsync();
        Assert.Equal(200, (await Send("rez", package: item.Id, ox: 10)).Status); Assert.Equal(200, (await Send("rez", package: item.Id, ox: 40)).Status);
        var all = await _db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(); Assert.Equal(10, all.Count);
        var sets = all.GroupBy(o => o.GroupId).ToList(); Assert.Equal(2, sets.Count); Assert.All(sets, s => { Assert.NotNull(s.Key); Assert.Equal(5, s.Count()); Assert.Single(s, o => o.ParentObjectId is null); Assert.Equal(4, s.Count(o => o.ParentObjectId is { } p && s.Any(r => r.Id == p && r.ParentObjectId is null))); });
        Assert.Contains(all, o => o.Name == "Tampo" && o.PositionX is > 9.9f and < 10.1f);
    }

    [Fact]
    public async Task ReturningALinksetAndPlacingItAgainKeepsItsTreeAndPivots()
    {
        var set = Guid.NewGuid(); var root = await Create(Box(0, set, pivotY: -.5f)); var kid = await Create(Box(2, set, root, pivotY: .25f)); var grandkid = await Create(Box(4, set, kid));
        Assert.Equal(200, (await Send("return", ids: [root, kid, grandkid])).Status);
        var item = await _db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == _builder && i.DisplayName.StartsWith("Devolvido")).SingleAsync();
        Assert.Equal(200, (await Send("rez", package: item.Id, ox: 100)).Status);
        var back = await _db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).OrderBy(o => o.PositionX).ToListAsync(); Assert.Equal(3, back.Count);
        Assert.Null(back[0].ParentObjectId); Assert.Equal(back[0].Id, back[1].ParentObjectId); Assert.Equal(back[1].Id, back[2].ParentObjectId); Assert.Single(back.Select(o => o.GroupId).Distinct()); Assert.NotEqual(set, back[0].GroupId);
        Assert.Equal(-.5f, back[0].PivotY); Assert.Equal(.25f, back[1].PivotY);
    }

    [Fact]
    public async Task PhysicsSettingsAreCheckedOnEveryObjectCommand()
    {
        var good = await Create(Box(0, physics: "{\"collision\":true,\"mode\":\"physical\",\"mass\":20,\"friction\":0.8}"));
        Assert.Equal("physical", JsonDocument.Parse((await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == good)).PhysicsJson).RootElement.GetProperty("mode").GetString());
        foreach (var bad in new[] { "{\"mode\":\"physical\",\"collision\":false}", "{\"mode\":\"flying\"}", "{\"mass\":0}", "{\"mass\":99999}", "{\"bounce\":2}", "[1]" })
        {
            var (status, body) = await Send("create", Box(3, physics: bad)); Assert.Equal(400, status); Assert.Equal("invalid_object", body.GetProperty("code").GetString());
            var revision = (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == good)).Revision; var (updateStatus, _) = await Send("update", Box(0, physics: bad) with { ExpectedRevision = revision }, good, revision: revision); Assert.Equal(400, updateStatus);
        }
        Assert.Equal(1, await _db.BuildObjects.CountAsync(o => o.DeletedAt == null));
        var packageObject = new PackageObjectDto("cube", "x", "", 0, 0, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"mode\":\"physical\",\"collision\":false}", "[]", null);
        Assert.NotNull(await BuildPackageEndpoints.ValidateAsync(_db, _builder, new PackageDto(PackageDocument.FormatName, 1, "x", 0, [packageObject]), default)); // a saved construction cannot smuggle it in either
    }

    [Fact]
    public async Task ThePhysicalCostIsChargedToTheIslandBudget()
    {
        var still = await Create(Box(0)); var moving = await Create(Box(2, physics: "{\"collision\":true,\"mode\":\"physical\"}"));
        var costs = await _db.BuildObjects.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Cost); Assert.Equal(costs[still] + 2, costs[moving]);
    }
}
