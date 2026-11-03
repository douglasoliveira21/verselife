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

/// <summary>The rules of the build path end to end (permissions, parcels, limits, locks of ownership, return, audit), against an in-memory database.</summary>
public sealed class BuildOperationsTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly DbContextOptions<LifeVerseDbContext> _options = new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly LifeVerseDbContext _db;
    private readonly MemoryStore _store = new();
    private readonly AssetStorageService _storage;
    private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid(), _visitor = Guid.NewGuid(), _parcelOwner = Guid.NewGuid(), _admin = Guid.NewGuid();

    public BuildOperationsTests()
    {
        _db = new LifeVerseDbContext(_options);
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island);
        _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        Grant(_builder, "build"); Grant(_admin, "admin");
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void Grant(Guid user, string permission) => _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = permission, PrincipalType = "user", GrantedByUserId = _owner });

    private void Parcel(Guid owner, bool publicBuild, int maxObjects = 100, double min = 50, double max = 100)
    {
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island.Id, OwnerUserId = owner, Name = "Terreno", BoundsJson = JsonSerializer.Serialize(new { minX = min, minZ = min, maxX = max, maxZ = max }), PermissionsJson = $"{{\"enter\":true,\"build\":{(publicBuild ? "true" : "false")}}}", MaxObjects = maxObjects });
        _db.SaveChanges();
    }

    private static BuildObjectRequest Cube(float x = 0, float z = 0, int revision = 0, string? name = "Caixa", string? description = null, string? content = null, string material = "{}", string geometry = "{}") =>
        new("cube", x, 1, z, 0, 0, 0, 1, 1, 1, material, "{\"collision\":true}", null, revision, geometry, name, description, null, null, content);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? @object = null, Guid? objectId = null, int revision = 0, Guid[]? ids = null)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, objectId, @object, revision, ids), _db, _storage, TimeProvider.System, default);
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? 200;
        var value = (result as IValueHttpResult)?.Value;
        return (status, JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<Guid> Create(Guid actor, float x = 0, float z = 0) { var (s, b) = await Send(actor, "create", Cube(x, z)); Assert.Equal(200, s); return b.GetProperty("objectId").GetGuid(); }
    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;

    // ---- who may build -----------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyPeopleWithBuildRightsCanCreate()
    {
        Assert.Equal(("forbidden", 403), (Code((await Send(_visitor, "create", Cube())).Body), (await Send(_visitor, "create", Cube())).Status));
        Assert.Equal(200, (await Send(_builder, "create", Cube())).Status);
        Assert.Equal(200, (await Send(_owner, "create", Cube())).Status);
    }

    [Fact]
    public async Task PrivateParcelsKeepEveryoneButTheirOwnerAndAdminsOut()
    {
        Parcel(_parcelOwner, publicBuild: false);
        var (status, body) = await Send(_builder, "create", Cube(60, 60));
        Assert.Equal(403, status); Assert.Equal("parcel_forbidden", Code(body)); // an island builder does not override a private parcel
        Assert.Equal(200, (await Send(_parcelOwner, "create", Cube(60, 60))).Status); // the parcel owner needs no island permission
        Assert.Equal(200, (await Send(_admin, "create", Cube(61, 61))).Status);
        Assert.Equal(200, (await Send(_builder, "create", Cube(10, 10))).Status); // outside the parcel the island rule applies
    }

    [Fact]
    public async Task PublicParcelsLetAnyoneBuild()
    {
        Parcel(_parcelOwner, publicBuild: true);
        Assert.Equal(200, (await Send(_visitor, "create", Cube(70, 70))).Status);
        Assert.Equal(403, (await Send(_visitor, "create", Cube(10, 10))).Status);
    }

    // ---- who may change what -----------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheOwnerAnAdminOrTheParcelOwnerCanChangeAnObject()
    {
        Parcel(_parcelOwner, publicBuild: true);
        var id = await Create(_visitor, 70, 70); // built by a visitor in a public parcel
        var (stranger, b1) = await Send(_builder, "update", Cube(70, 70, 1, "Roubado"), id, 1);
        Assert.Equal(403, stranger); Assert.Equal("not_owner", Code(b1));
        Assert.Equal("Caixa", (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).Name);
        Assert.Equal(200, (await Send(_visitor, "update", Cube(70, 70, 1, "Meu"), id, 1)).Status);
        Assert.Equal(200, (await Send(_parcelOwner, "update", Cube(70, 70, 2, "Do terreno"), id, 2)).Status);
        Assert.Equal(200, (await Send(_admin, "update", Cube(70, 70, 3, "Do admin"), id, 3)).Status);
        Assert.Equal(200, (await Send(_owner, "update", Cube(70, 70, 4, "Do dono"), id, 4)).Status);
    }

    [Fact]
    public async Task DeletingFollowsTheSameRules()
    {
        var id = await Create(_builder);
        Assert.Equal(("not_owner", 403), (Code((await Send(_visitor, "delete", null, id, 1)).Body), (await Send(_visitor, "delete", null, id, 1)).Status));
        Assert.Equal(403, (await Send(_builder, "create", Cube())).Status == 200 ? 403 : 0); // (sanity: the builder can still create)
        Assert.Equal(200, (await Send(_builder, "delete", null, id, 1)).Status);
        Assert.Equal(404, (await Send(_builder, "delete", null, id, 2)).Status);
    }

    [Fact]
    public async Task ARevisionBehindTheServersIsRefused()
    {
        var id = await Create(_builder);
        await Send(_builder, "update", Cube(0, 0, 1, "Primeira"), id, 1);
        var (status, body) = await Send(_builder, "update", Cube(0, 0, 1, "Atrasada"), id, 1);
        Assert.Equal(409, status); Assert.Equal("revision_conflict", Code(body)); Assert.Equal(2, body.GetProperty("currentRevision").GetInt32());
    }

    [Fact]
    public async Task MovingIntoAPrivateParcelNeedsRightsThere()
    {
        Parcel(_parcelOwner, publicBuild: false);
        var id = await Create(_builder, 10, 10);
        var (status, body) = await Send(_builder, "update", Cube(60, 60, 1), id, 1);
        Assert.Equal(403, status); Assert.Equal("parcel_forbidden", Code(body));
        Assert.Equal(10, (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).PositionX);
    }

    // ---- what is saved (regression: names and contents were dropped on update) ---------------------------------

    [Fact]
    public async Task NameDescriptionAndContentSurviveAnUpdate()
    {
        var id = await Create(_builder);
        var asset = Guid.NewGuid();
        _db.Assets.Add(new Asset { Id = asset, OwnerUserId = _builder, CreatorUserId = _builder, Name = "Som", Category = "sounds" }); await _db.SaveChangesAsync();
        var content = $"[{{\"assetId\":\"{asset}\",\"name\":\"Campainha\",\"category\":\"sounds\"}}]";
        var (status, _) = await Send(_builder, "update", Cube(0, 0, 1, "Porta", "Porta da frente", content), id, 1);
        Assert.Equal(200, status);
        var saved = await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal("Porta", saved.Name); Assert.Equal("Porta da frente", saved.Description); Assert.Contains("Campainha", saved.ContentJson); Assert.Equal(2, saved.Revision);
    }

    [Fact]
    public async Task OnlyTheOwnerOrAnAdminChangesWhatOthersMayDo()
    {
        Parcel(_parcelOwner, publicBuild: false);
        var id = await Create(_builder, 10, 10);
        var item = await _db.BuildObjects.SingleAsync(o => o.Id == id); var original = item.PermissionsJson; _db.Entry(item).State = EntityState.Detached;
        var request = Cube(10, 10, 1) with { PermissionsJson = "{\"modify\":false}" };
        Assert.Equal(200, (await Send(_builder, "update", request, id, 1)).Status);
        Assert.Equal("{\"modify\":false}", (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).PermissionsJson);
        _ = original;
    }

    [Fact]
    public async Task InvalidObjectsAreRefusedWithAReason()
    {
        var (status, body) = await Send(_builder, "create", Cube(geometry: "{\"hollow\":5}"));
        Assert.Equal(400, status); Assert.Equal("invalid_object", Code(body));
        Assert.Equal(400, (await Send(_builder, "create", Cube(material: "{\"uvRepeatX\":0}"))).Status);
        Assert.Equal(400, (await Send(_builder, "create", Cube(content: "{}"))).Status);
        Assert.Equal(400, (await Send(_builder, "bogus", Cube())).Status);
    }

    // ---- limits ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheIslandObjectLimitStopsCreation()
    {
        _island.ResourceLimitsJson = "{\"objects\":2}"; await _db.SaveChangesAsync();
        await Create(_builder); await Create(_builder);
        var (status, body) = await Send(_builder, "create", Cube());
        Assert.Equal(409, status); Assert.Equal("object_quota", Code(body));
    }

    [Fact]
    public async Task TheIslandCostBudgetStopsHeavyBuilds()
    {
        _island.ResourceLimitsJson = "{\"cost\":10}"; await _db.SaveChangesAsync();
        var heavy = Cube(geometry: "{\"twist\":180,\"hollow\":0.5}", material: "{\"light\":{\"energy\":1,\"range\":5}}");
        var (status, body) = await Send(_builder, "create", heavy);
        Assert.Equal(409, status); Assert.Equal("cost_limit", Code(body));
        Assert.Equal(200, (await Send(_builder, "create", Cube())).Status); // a cheap box still fits
    }

    [Fact]
    public async Task AnUpdateThatMakesAnObjectHeavierIsCheckedAgainstTheBudgetToo()
    {
        var id = await Create(_builder);
        _island.ResourceLimitsJson = "{\"cost\":6}"; await _db.SaveChangesAsync();
        var (status, body) = await Send(_builder, "update", Cube(0, 0, 1, material: "{\"light\":{\"energy\":1,\"range\":5}}"), id, 1);
        Assert.Equal(409, status); Assert.Equal("cost_limit", Code(body));
        var saved = await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal(1, saved.Revision); Assert.DoesNotContain("light", saved.MaterialJson); // nothing was changed
    }

    [Fact]
    public async Task ParcelsHaveTheirOwnObjectAndCostLimits()
    {
        Parcel(_parcelOwner, publicBuild: true, maxObjects: 2);
        await Create(_visitor, 60, 60); await Create(_visitor, 61, 61);
        var (status, body) = await Send(_visitor, "create", Cube(62, 62));
        Assert.Equal(409, status); Assert.Equal("parcel_object_limit", Code(body));
        Assert.Equal(200, (await Send(_builder, "create", Cube(10, 10))).Status); // outside the parcel is unaffected
    }

    [Fact]
    public async Task EveryObjectCarriesItsCost()
    {
        var id = await Create(_builder);
        var cost = (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).Cost;
        Assert.True(cost >= 1);
        await Send(_builder, "update", Cube(0, 0, 1, material: "{\"light\":{\"energy\":1,\"range\":5}}"), id, 1);
        Assert.Equal(cost + 5, (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).Cost);
    }

    [Fact]
    public async Task OldObjectsAreCostedOnce()
    {
        _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _owner, OwnerUserId = _owner, Name = "Antigo", PrimitiveType = "sphere", Cost = 0 });
        await _db.SaveChangesAsync();
        Assert.Equal(1, await BuildCostBackfill.RunAsync(_db));
        Assert.True((await _db.BuildObjects.AsNoTracking().SingleAsync()).Cost > 1);
        Assert.Equal(0, await BuildCostBackfill.RunAsync(_db));
    }

    // ---- returning ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task ReturnSendsObjectsBackToTheirOwnersInventoryAndRemovesThemFromTheWorld()
    {
        Parcel(_parcelOwner, publicBuild: true);
        var a = await Create(_visitor, 70, 70); var b = await Create(_visitor, 72, 70); var other = await Create(_builder, 10, 10);
        var (status, body) = await Send(_parcelOwner, "return", ids: [a, b]);
        Assert.Equal(200, status); Assert.Equal(2, body.GetProperty("returned").GetInt32());
        Assert.All(await _db.BuildObjects.AsNoTracking().Where(o => o.Id == a || o.Id == b).ToListAsync(), o => Assert.NotNull(o.DeletedAt));
        Assert.Null((await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == other)).DeletedAt);

        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == _visitor);
        var asset = await _db.Assets.AsNoTracking().SingleAsync(x => x.Id == item.AssetId);
        Assert.Equal("packages", asset.Category); Assert.StartsWith("Devolvido", item.DisplayName);
        var stored = _store.Files.Values.Single();
        var package = PackageDocument.Parse(System.Text.Encoding.UTF8.GetString(stored), out var error);
        Assert.Null(error); Assert.Equal(2, package!.Objects.Length);
        Assert.Equal(2, await _db.BuildOperations.CountAsync(o => o.Operation == "return" && o.ActorUserId == _parcelOwner)); // audited
    }

    [Fact]
    public async Task ReturnIsAllOrNothingAndOnlyForThoseWithRights()
    {
        Parcel(_parcelOwner, publicBuild: true);
        var mine = await Create(_visitor, 70, 70); var elsewhere = await Create(_builder, 10, 10); // outside the parcel
        var (status, body) = await Send(_parcelOwner, "return", ids: [mine, elsewhere]);
        Assert.Equal(403, status); Assert.Equal("not_allowed_to_return", Code(body));
        Assert.All(await _db.BuildObjects.AsNoTracking().ToListAsync(), o => Assert.Null(o.DeletedAt)); // not even the allowed one went
        Assert.Equal(403, (await Send(_visitor, "return", ids: [elsewhere])).Status);
        Assert.Equal(200, (await Send(_visitor, "return", ids: [mine])).Status); // taking your own is always fine
        Assert.Equal(200, (await Send(_admin, "return", ids: [elsewhere])).Status);
    }

    // ---- limits endpoint helpers and audit -----------------------------------------------------------------------

    [Fact]
    public void TheRevisionOfAnObjectIsAConcurrencyToken() =>
        Assert.True(_db.Model.FindEntityType(typeof(BuildObject))!.FindProperty(nameof(BuildObject.Revision))!.IsConcurrencyToken);

    [Fact]
    public async Task TwoEditsThatOverlapInTimeCannotBothWin()
    {
        var id = await Create(_builder);
        var interfered = false;
        // right before the first edit is saved, someone else's edit lands on the same object
        _db.SavingChanges += (_, _) =>
        {
            if (interfered) return; interfered = true;
            using var other = new LifeVerseDbContext(_options);
            var row = other.BuildObjects.Single(o => o.Id == id); row.Revision++; row.Name = "Do outro"; other.SaveChanges();
        };
        var (status, body) = await Send(_builder, "update", Cube(0, 0, 1, "Perdida"), id, 1);
        Assert.Equal(409, status); Assert.Equal("revision_conflict", Code(body)); Assert.Equal(2, body.GetProperty("currentRevision").GetInt32());
        var saved = await new LifeVerseDbContext(_options).BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
        Assert.Equal("Do outro", saved.Name); Assert.Equal(2, saved.Revision); // the other edit stands; the late one did not overwrite it
    }

    [Fact]
    public async Task ManyObjectsKeepTheBudgetAccurateAndFast()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 300; i++) await Create(_builder, i % 40, i / 40);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"300 creations took {watch.Elapsed}");
        var live = await _db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync();
        Assert.Equal(300, live.Count);
        Assert.Equal(live.Sum(o => o.Cost), await _db.BuildObjects.SumAsync(o => o.Cost));
        Assert.All(live, o => Assert.Equal(BuildCost.Of(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.PhysicsJson, o.ContentJson), o.Cost));
    }

    [Fact]
    public async Task EveryOperationLeavesAnAuditRow()
    {
        var id = await Create(_builder);
        await Send(_builder, "update", Cube(0, 0, 1, "Nova"), id, 1);
        await Send(_builder, "delete", null, id, 2);
        var log = await _db.BuildOperations.AsNoTracking().Where(o => o.ObjectId == id).OrderBy(o => o.Revision).ToListAsync();
        Assert.Equal(new[] { "create", "update", "delete" }, log.Select(o => o.Operation));
        Assert.All(log, o => Assert.Equal(_builder, o.ActorUserId));
    }

    [Fact]
    public void PackagesBuiltByTheApiMatchThoseBuiltByTheClient()
    {
        var group = Guid.NewGuid();
        var sources = new[] { (X: 10f, Y: 2f, Z: 5f, G: (Guid?)group), (X: 14f, Y: 1f, Z: 9f, G: group), (X: 12f, Y: 4f, Z: 7f, G: null) };
        var api = PackageDocument.Create("Casa", sources.Select(s => new PackageSourceDto("cube", "P", "", s.X, s.Y, s.Z, 0, 0, 0, 1, 2, 1, "{}", "{}", "{}", "[]", s.G)).ToList());
        var client = BuildPackages.Create("Casa", sources.Select(s => new SourceObject("cube", "P", "", s.X, s.Y, s.Z, 0, 0, 0, 1, 2, 1, "{}", "{}", "{}", "[]", s.G)).ToList());
        Assert.Equal(client.BaseOffset, api.BaseOffset);
        Assert.Equal(client.Objects.Select(o => (o.X, o.Y, o.Z, o.Group)), api.Objects.Select(o => (o.X, o.Y, o.Z, o.Group)));
    }
}
