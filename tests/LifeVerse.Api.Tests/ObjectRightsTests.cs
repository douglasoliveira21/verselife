using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Scripting;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Rights of objects in the world: what they inherit from their parts, who may modify them, things that cannot be copied, and placing a saved construction.</summary>
public sealed class ObjectRightsTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly MemoryStore _store = new();
    private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _builder = Guid.NewGuid(), _admin = Guid.NewGuid(), _maker = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public ObjectRightsTests()
    {
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _builder, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = _admin, Permission = "admin", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    // ---- helpers ------------------------------------------------------------------------------------------------------------------

    /// <summary>A 3D model made by somebody else, held by the builder with the given rights.</summary>
    private (Asset Asset, InventoryItem Item) Model(int rights, int next, Guid? holder = null)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = _maker, CreatorUserId = _maker, Name = "Modelo", Category = "mesh", CurrentVersion = 1, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k" + asset.Id, MimeType = "model/gltf-binary", ByteSize = 10 });
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = holder ?? _builder, AssetId = asset.Id, DisplayName = "Modelo", OwnerRights = rights, NextOwnerRights = next, CreatedAt = _now, UpdatedAt = _now }; _db.InventoryItems.Add(item); _db.SaveChanges(); return (asset, item);
    }

    private (Asset Asset, InventoryItem Item) Script(int rights, int next)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = _maker, CreatorUserId = _maker, Name = "Roteiro", Category = "scripts", CurrentVersion = 1, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _builder, AssetId = asset.Id, DisplayName = "Roteiro", OwnerRights = rights, NextOwnerRights = next, CreatedAt = _now, UpdatedAt = _now }; _db.InventoryItems.Add(item); _db.SaveChanges(); return (asset, item);
    }

    private static BuildObjectRequest Cube(float x = 0, float z = 0, int revision = 0, string? name = "Caixa", string? content = null, string material = "{}", float scale = 1, Guid? parent = null) =>
        new("cube", x, 1, z, 0, 0, 0, scale, scale, scale, material, "{\"collision\":true}", null, revision, "{}", name, null, parent, null, content);

    private static BuildObjectRequest MeshOf(Asset asset, float x = 0, float z = 0) => new("mesh", x, 1, z, 0, 0, 0, 1, 1, 1, "{}", "{}", null, 0, $"{{\"assetId\":\"{asset.Id}\"}}", "Peça", null, null, null, null);
    private static string Contents(Asset asset, string category = "scripts") => $"[{{\"assetId\":\"{asset.Id}\",\"name\":\"{asset.Name}\",\"category\":\"{category}\"}}]";

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? @object = null, Guid? objectId = null, int revision = 0, Guid[]? ids = null, Guid? package = null, float ox = 0, float oz = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, objectId, @object, revision, ids, package, ox, 0, oz), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string Code(JsonElement body) => body.GetProperty("code").GetString()!;
    private async Task<Guid> Create(Guid actor, BuildObjectRequest o) { var (s, b) = await Send(actor, "create", o); Assert.True(s == 200, s + " " + b); return b.GetProperty("objectId").GetGuid(); }
    private async Task<int> Rev(Guid id) => (await Obj(id)).Revision;
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);

    // ---- what an object inherits --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnObjectMadeOfPlainShapesOrOfThingsTheMakerMadeHasEveryRight()
    {
        var id = await Create(_builder, Cube()); var o = await Obj(id); Assert.Equal((ItemRights.All, ItemRights.All), (o.OwnerRights, o.NextOwnerRights));
        var own = new Asset { Id = Guid.NewGuid(), OwnerUserId = _builder, CreatorUserId = _builder, Name = "Meu", Category = "mesh", CurrentVersion = 1, CreatedAt = _now, UpdatedAt = _now }; _db.Assets.Add(own);
        _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = own.Id, Version = 1, ContentHash = "h", StorageKey = "k", MimeType = "model/gltf-binary", ByteSize = 1 }); await _db.SaveChangesAsync();
        Assert.Equal(ItemRights.All, (await Obj(await Create(_builder, MeshOf(own)))).OwnerRights);
    }

    [Fact]
    public async Task AnObjectCanDoNoMoreThanTheThingsItIsMadeOfAllow()
    {
        var (asset, _) = Model(rights: ItemRights.Modify | ItemRights.Transfer, next: ItemRights.Transfer);
        var o = await Obj(await Create(_builder, MeshOf(asset))); Assert.Equal((6, 4), (o.OwnerRights, o.NextOwnerRights)); Assert.False(ItemRights.IsCopyable(o.OwnerRights)); // made of something that cannot be copied
        var (asset2, _) = Model(ItemRights.Standard, ItemRights.Copy); var both = await Obj(await Create(_builder, MeshOf(asset2, 5, 5))); Assert.Equal((7, 1), (both.OwnerRights, both.NextOwnerRights));
    }

    [Fact]
    public async Task ContentsAddedLaterOnlyTakeRightsAway()
    {
        var id = await Create(_builder, Cube()); var (script, _) = Script(ItemRights.Copy | ItemRights.Modify, ItemRights.Copy);
        var (status, body) = await Send(_builder, "update", Cube(content: Contents(script)), id, await Rev(id)); Assert.True(status == 200, body.ToString());
        var o = await Obj(id); Assert.Equal((3, 1), (o.OwnerRights, o.NextOwnerRights)); Assert.False(ItemRights.Has(o.OwnerRights, Right.Transfer)); // a script that cannot be passed on makes the object impossible to pass on
    }

    [Fact]
    public async Task ChildrenAreLimitedByTheirParents()
    {
        var (asset, _) = Model(ItemRights.Copy | ItemRights.Transfer, ItemRights.All); var group = Guid.NewGuid(); var parent = await Create(_builder, MeshOf(asset) with { GroupId = group }); var child = await Create(_builder, Cube(2, 2, parent: parent) with { GroupId = group });
        var effective = await ObjectRights.EffectiveAsync(_db, await Obj(child), default); Assert.Equal((5, 5), (effective.Owner, effective.NextOwner)); Assert.False(ItemRights.Has(effective.Owner, Right.Modify));
        var (status, body) = await Send(_builder, "update", Cube(2, 2, 0, "Outro nome", parent: parent), child, await Rev(child)); Assert.Equal((403, "no_modify"), (status, Code(body))); // its own rights allow it; the parent's do not
    }

    // ---- modifying ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MovingNeverNeedsModifyButChangingWhatTheObjectIsDoes()
    {
        var (asset, _) = Model(ItemRights.Copy | ItemRights.Transfer, ItemRights.All); var id = await Create(_builder, MeshOf(asset)); var rev = (await Obj(id)).Revision;
        var move = MeshOf(asset, 12, 7) with { RotationY = 1.5f }; var (s1, _) = await Send(_builder, "update", move, id, rev); Assert.Equal(200, s1); var moved = await Obj(id); Assert.Equal((12f, 7f), (moved.PositionX, moved.PositionZ));
        var resized = MeshOf(asset, 12, 7) with { ScaleX = 3 }; var (s2, b2) = await Send(_builder, "update", resized, id, moved.Revision); Assert.Equal((403, "no_modify"), (s2, Code(b2)));
        var renamed = MeshOf(asset, 12, 7) with { Name = "Novo" }; Assert.Equal("no_modify", Code((await Send(_builder, "update", renamed, id, moved.Revision)).Body));
        var painted = MeshOf(asset, 12, 7) with { MaterialJson = "{\"color\":\"#ff0000\"}" }; Assert.Equal("no_modify", Code((await Send(_builder, "update", painted, id, moved.Revision)).Body));
        Assert.Equal("no_modify", Code((await Send(_admin, "update", resized, id, moved.Revision)).Body)); // an island administrator may move and return it, but cannot rebuild it either
        Assert.Equal(1f, (await Obj(id)).ScaleX);
    }

    [Fact]
    public async Task SendingTheSameSettingsWrittenDifferentlyIsNotAChange()
    {
        var (asset, _) = Model(ItemRights.Copy | ItemRights.Transfer, ItemRights.All); var id = await Create(_builder, MeshOf(asset)); var rev = (await Obj(id)).Revision;
        var same = MeshOf(asset, 4, 4) with { GeometryJson = $"{{ \"assetId\" : \"{asset.Id}\" }}", PhysicsJson = "{ }", MaterialJson = "{ }", Name = "Peça" };
        Assert.Equal(200, (await Send(_builder, "update", same, id, rev)).Status);
    }

    [Fact]
    public async Task AnObjectThatMayBeModifiedStillMayByAnyoneWhoCouldBefore()
    {
        var id = await Create(_builder, Cube()); var rev = (await Obj(id)).Revision;
        Assert.Equal(200, (await Send(_builder, "update", Cube(name: "Renomeada", scale: 2), id, rev)).Status); Assert.Equal(200, (await Send(_admin, "update", Cube(name: "Pelo admin", scale: 2), id, rev + 1)).Status);
    }

    // ---- things that cannot be copied are in one object only ----------------------------------------------------------------------

    [Fact]
    public async Task AModelThatCannotBeCopiedIsInOneObjectAtATime()
    {
        var (asset, _) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); var first = await Create(_builder, MeshOf(asset));
        var (status, body) = await Send(_builder, "create", MeshOf(asset, 9, 9)); Assert.Equal((409, "no_copy_in_use"), (status, Code(body)));
        Assert.Equal(200, (await Send(_builder, "return", objectId: first, revision: 0)).Status); Assert.Equal(200, (await Send(_builder, "create", MeshOf(asset, 9, 9))).Status); // returned: free again
    }

    [Fact]
    public async Task AModelThatCanBeCopiedCanBeInManyObjects()
    {
        var (asset, _) = Model(ItemRights.Standard, ItemRights.Standard); for (var i = 0; i < 3; i++) Assert.Equal(200, (await Send(_builder, "create", MeshOf(asset, i * 3, 0))).Status);
    }

    [Fact]
    public async Task APartThatCannotBeCopiedCannotBeAddedToASecondObjectEither()
    {
        var (script, _) = Script(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); var a = await Create(_builder, Cube(0, 0)); var b = await Create(_builder, Cube(5, 5));
        Assert.Equal(200, (await Send(_builder, "update", Cube(0, 0, content: Contents(script)), a, await Rev(a))).Status);
        var (status, body) = await Send(_builder, "update", Cube(5, 5, content: Contents(script)), b, await Rev(b)); Assert.Equal((409, "no_copy_in_use"), (status, Code(body)));
        Assert.Equal(200, (await Send(_builder, "update", Cube(0, 0, 1, content: Contents(script), scale: 1), a, await Rev(a))).Status); // the object that already holds it can keep it
    }

    [Fact]
    public async Task AThingInUseCannotBeGivenAwayOrSoldUntilTheObjectIsReturned()
    {
        var (asset, item) = Model(ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, ItemRights.All); var other = Guid.NewGuid(); _db.Users.Add(new User { Id = other, Username = "outro", NormalizedUsername = "OUTRO", Email = "o@t.local", NormalizedEmail = "O@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();
        var id = await Create(_builder, MeshOf(asset));
        Assert.Equal("item_in_use", (await ItemTransfers.OfferAsync(_db, _builder, item.Id, other, "gift", null, false, "chave-12345678", _now, default)).Error);
        var listing = await Modules_Publish(item); Assert.Equal(403, listing);
        Assert.Equal(200, (await Send(_builder, "return", objectId: id)).Status); Assert.True((await ItemTransfers.OfferAsync(_db, _builder, item.Id, other, "gift", null, false, "chave-87654321", _now, default)).Ok);
    }

    private async Task<int> Modules_Publish(InventoryItem item)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", _builder.ToString())], "test")) };
        var result = await LifeVerse.Api.Modules.Economy.EconomyEndpoints.PublishAsync(new LifeVerse.Api.Modules.Economy.PublishProductRequest(item.AssetId, "Revenda", "", "x", 100, null, null, null, null, item.Id, null), context, _db, TimeProvider.System, default);
        return result is IStatusCodeHttpResult { StatusCode: { } code } ? code : 0;
    }

    // ---- return and constructions -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ReturnedObjectsComeBackAsAnItemWithTheStrictestRightsOfThem()
    {
        var (asset, _) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.Transfer); var a = await Create(_builder, MeshOf(asset)); var b = await Create(_builder, Cube(8, 8));
        Assert.Equal(200, (await Send(_builder, "return", ids: [a, b])).Status);
        var package = await _db.InventoryItems.AsNoTracking().Join(_db.Assets, i => i.AssetId, x => x.Id, (i, x) => new { i, x }).Where(p => p.x.Category == "packages" && p.i.OwnerUserId == _builder).Select(p => p.i).SingleAsync();
        Assert.Equal((6, 4), (package.OwnerRights, package.NextOwnerRights)); Assert.False(ItemRights.IsCopyable(package.OwnerRights));
    }

    private async Task<InventoryItem> SavePackage(Guid owner, int rights, int next, params BuildObjectRequest[] objects)
    {
        var sources = objects.Select(o => new PackageSourceDto(o.PrimitiveType, o.Name ?? "x", "", o.PositionX, o.PositionY, o.PositionZ, 0, 0, 0, o.ScaleX, o.ScaleY, o.ScaleZ, o.MaterialJson ?? "{}", o.GeometryJson ?? "{}", o.PhysicsJson ?? "{}", o.ContentJson ?? "[]", null)).ToList();
        var item = await BuildPackageStore.SaveAsync(_db, _storage, owner, PackageDocument.Create("Casa", sources), _now, default, new ItemPermissions(rights, 0, next)); await _db.SaveChangesAsync(); return item;
    }

    [Fact]
    public async Task AConstructionYouCanCopyIsPlacedByTheServerAndStaysInTheInventory()
    {
        var package = await SavePackage(_builder, ItemRights.Standard, ItemRights.Standard, Cube(0, 0), Cube(2, 0)); var (s1, b1) = await Send(_builder, "rez", package: package.Id, ox: 10, oz: 10); Assert.Equal(200, s1);
        Assert.False(b1.GetProperty("consumed").GetBoolean()); Assert.Equal(2, b1.GetProperty("objects").GetArrayLength()); var placed = await _db.BuildObjects.AsNoTracking().Where(o => o.IslandId == _island.Id).OrderBy(o => o.PositionX).ToListAsync();
        Assert.Equal(new[] { 9f, 11f }, placed.Select(o => o.PositionX)); Assert.Equal(new[] { 10f, 10f }, placed.Select(o => o.PositionZ)); Assert.Equal(new[] { 7, 7 }, placed.Select(o => o.OwnerRights)); Assert.Equal(new[] { 7, 7 }, placed.Select(o => o.NextOwnerRights));
        Assert.Equal("active", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == package.Id)).Status); Assert.Equal(200, (await Send(_builder, "rez", package: package.Id, ox: 50, oz: 50)).Status);
        Assert.Contains(_db.ItemAudit, a => a.Action == "rez" && a.ItemId == package.Id);
    }

    [Fact]
    public async Task AConstructionYouCannotCopyIsUsedUpByPlacingItAndIsNotBroughtBackFromTheBin()
    {
        var package = await SavePackage(_builder, ItemRights.Modify | ItemRights.Transfer, ItemRights.Transfer, Cube(0, 0)); var (status, body) = await Send(_builder, "rez", package: package.Id); Assert.Equal(200, status); Assert.True(body.GetProperty("consumed").GetBoolean());
        var used = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == package.Id); Assert.Equal(("consumed", true), (used.Status, used.DeletedAt is not null));
        Assert.Equal(404, (await Send(_builder, "rez", package: package.Id, ox: 5)).Status); Assert.Equal(1, await _db.BuildObjects.CountAsync(o => o.DeletedAt == null)); // not twice
        var placed = await _db.BuildObjects.AsNoTracking().SingleAsync(); Assert.Equal((6, 4), (placed.OwnerRights, placed.NextOwnerRights));
        Assert.Equal(200, (await Send(_builder, "return", objectId: placed.Id)).Status); var again = await _db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == _builder && i.DeletedAt == null && i.Status == "active").SingleAsync(); Assert.Equal((6, 4), (again.OwnerRights, again.NextOwnerRights));
        Assert.Equal(200, (await Send(_builder, "rez", package: again.Id, ox: 20)).Status); Assert.Equal(1, await _db.BuildObjects.CountAsync(o => o.DeletedAt == null)); // used up, returned and placed again: always one
    }

    [Fact]
    public async Task APlacementIsAllOrNothingAndFollowsTheBuildRules()
    {
        var package = await SavePackage(_builder, ItemRights.Modify | ItemRights.Transfer, ItemRights.Transfer, Cube(0, 0), Cube(1, 0));
        _db.IslandParcels.Add(new IslandParcel { Id = Guid.NewGuid(), IslandId = _island.Id, OwnerUserId = _owner, Name = "Do dono", BoundsJson = "{\"minX\":100,\"minZ\":100,\"maxX\":150,\"maxZ\":150}", PermissionsJson = "{\"enter\":true,\"build\":false}", MaxObjects = 100 }); await _db.SaveChangesAsync();
        var (status, body) = await Send(_builder, "rez", package: package.Id, ox: 120, oz: 120); Assert.Equal((403, "parcel_forbidden"), (status, Code(body)));
        Assert.Equal(0, await _db.BuildObjects.CountAsync()); Assert.Equal("active", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == package.Id)).Status); // nothing placed and the item is still there
        var stranger = Guid.NewGuid(); Assert.Equal(404, (await Send(_builder, "rez", package: Guid.NewGuid())).Status); Assert.Equal(404, (await Send(stranger, "rez", package: package.Id)).Status);
        Assert.Equal(400, (await Send(_builder, "rez")).Status); Assert.Equal(400, (await Send(_builder, "rez", package: package.Id, ox: float.NaN)).Status);
    }

    [Fact]
    public async Task APlacementOfSomeoneElsesItemOrAnItemOnOfferIsRefused()
    {
        var package = await SavePackage(_builder, ItemRights.Standard, ItemRights.Standard, Cube());
        Assert.Equal(404, (await Send(_admin, "rez", package: package.Id)).Status); // not theirs
        var tracked = await _db.InventoryItems.SingleAsync(i => i.Id == package.Id); tracked.Status = "offered"; await _db.SaveChangesAsync();
        var (status, body) = await Send(_builder, "rez", package: package.Id); Assert.Equal((409, "item_busy"), (status, Code(body))); Assert.Equal(0, await _db.BuildObjects.CountAsync());
    }

    [Fact]
    public async Task APlacementWithAModelThatCannotBeCopiedTwiceInOnePackageIsRefused()
    {
        var (asset, _) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); var package = await SavePackage(_builder, ItemRights.Standard, ItemRights.Standard, MeshOf(asset, 0, 0), MeshOf(asset, 4, 0));
        var (status, body) = await Send(_builder, "rez", package: package.Id); Assert.Equal((409, "no_copy_in_use"), (status, Code(body))); Assert.Equal(0, await _db.BuildObjects.CountAsync());
    }

    // ---- scripts and the rights of one object ----------------------------------------------------------------------------------------

    private DefaultHttpContext As(Guid user) => new() { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test")) };

    [Fact]
    public async Task AScriptCanOnlyBeAddedToOrChangedInAnObjectThatMayBeModified()
    {
        var (asset, _) = Model(ItemRights.Copy | ItemRights.Transfer, ItemRights.All); var closed = await Create(_builder, MeshOf(asset)); var open = await Create(_builder, Cube(7, 7));
        var options = new LuaRuntimeOptions(); var request = new ObjectScriptRequest("Roteiro", "function on_start(event) end");
        var denied = await ScriptingEndpoints.CreateAsync(_island.Id, closed, request, As(_owner), _db, options, TimeProvider.System, default); Assert.Equal(403, (denied as IStatusCodeHttpResult)?.StatusCode);
        var allowed = await ScriptingEndpoints.CreateAsync(_island.Id, open, request, As(_owner), _db, options, TimeProvider.System, default); Assert.Equal(201, (allowed as IStatusCodeHttpResult)?.StatusCode);
        var script = await _db.ObjectScripts.AsNoTracking().SingleAsync(); var obj = await _db.BuildObjects.SingleAsync(o => o.Id == open); obj.OwnerRights = ItemRights.Copy | ItemRights.Transfer; await _db.SaveChangesAsync(); // the object lost Modify afterwards
        var update = await ScriptingEndpoints.UpdateAsync(_island.Id, open, script.Id, request with { Source = "function on_start(event) object.set_position({x=1,y=0,z=0}) end" }, As(_owner), _db, options, TimeProvider.System, default); Assert.Equal(403, (update as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, (await _db.ObjectScripts.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task TheOwnerCanOnlyLowerTheRightsOfAnObject()
    {
        var id = await Create(_builder, Cube()); var o = await Obj(id); var ok = await ObjectRights.SetAsync(_island.Id, id, new SetObjectRightsRequest(ItemRights.Copy | ItemRights.Modify, ItemRights.Copy, o.Revision), As(_builder), _db, TimeProvider.System, default);
        Assert.Equal(200, (ok as IStatusCodeHttpResult)?.StatusCode); var lowered = await Obj(id); Assert.Equal((3, 1, o.Revision + 1), (lowered.OwnerRights, lowered.NextOwnerRights, lowered.Revision));
        var raise = await ObjectRights.SetAsync(_island.Id, id, new SetObjectRightsRequest(ItemRights.All, ItemRights.All, lowered.Revision), As(_builder), _db, TimeProvider.System, default); Assert.Equal(400, (raise as IStatusCodeHttpResult)?.StatusCode); Assert.Equal(3, (await Obj(id)).OwnerRights);
        var stale = await ObjectRights.SetAsync(_island.Id, id, new SetObjectRightsRequest(1, 1, 0), As(_builder), _db, TimeProvider.System, default); Assert.Equal(409, (stale as IStatusCodeHttpResult)?.StatusCode);
        var notOwner = await ObjectRights.SetAsync(_island.Id, id, new SetObjectRightsRequest(1, 1, lowered.Revision), As(_admin), _db, TimeProvider.System, default); Assert.Equal(404, (notOwner as IStatusCodeHttpResult)?.StatusCode);
        Assert.Contains(_db.ItemAudit, a => a.Action == "object_rights" && a.Outcome == "ok"); Assert.Contains(_db.ItemAudit, a => a.Action == "denied" && a.Detail.Contains("rights: asked"));
    }

    [Fact]
    public async Task ThePermissionsTextOfAnObjectIsLeftAsItWas()
    {
        var (asset, _) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); var id = await Create(_builder, MeshOf(asset)); Assert.Contains("\"copy\":true", (await Obj(id)).PermissionsJson); // that text is about what others may do; the rights live in their own columns
    }

    // ---- exploitation attempts ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnItemOnOfferCannotBeBuiltIntoAnObjectBeforeTheAnswer()
    {
        var (asset, item) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); var other = Person();
        Assert.True((await ItemTransfers.OfferAsync(_db, _builder, item.Id, other, "gift", null, false, "chave-exploit-1", _now, default)).Ok);
        var (status, body) = await Send(_builder, "create", MeshOf(asset)); Assert.Equal((400, "invalid_object"), (status, Code(body))); // it would stay behind when the item moves
        Assert.Equal(0, await _db.BuildObjects.CountAsync());
        var (script, scriptItem) = Script(ItemRights.Modify | ItemRights.Transfer, ItemRights.All); await ItemTransfers.OfferAsync(_db, _builder, scriptItem.Id, other, "gift", null, false, "chave-exploit-2", _now, default);
        var id = await Create(_builder, Cube()); var (s2, b2) = await Send(_builder, "update", Cube(content: Contents(script)), id, await Rev(id)); Assert.Equal((400, "invalid_object"), (s2, Code(b2)));
    }

    [Fact]
    public async Task AnItemForSaleCannotBeBuiltIntoAnObjectEither()
    {
        var (asset, item) = Model(ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, ItemRights.All); Assert.Equal(201, await PublishAs(item, 100));
        var (status, body) = await Send(_builder, "create", MeshOf(asset)); Assert.Equal((400, "invalid_object"), (status, Code(body)));
    }

    private Guid Person() { var id = Guid.NewGuid(); _db.Users.Add(new User { Id = id, Username = "p" + id.ToString("N")[..8], NormalizedUsername = "P" + id.ToString("N")[..8].ToUpperInvariant(), Email = id.ToString("N")[..8] + "@t.local", NormalizedEmail = id.ToString("N")[..8].ToUpperInvariant() + "@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id; }

    private async Task<int> PublishAs(InventoryItem item, long price)
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", _builder.ToString())], "test")) };
        var result = await LifeVerse.Api.Modules.Economy.EconomyEndpoints.PublishAsync(new LifeVerse.Api.Modules.Economy.PublishProductRequest(item.AssetId, "Revenda", "", "x", price, null, null, null, null, item.Id, null), context, _db, TimeProvider.System, default);
        return result is IStatusCodeHttpResult { StatusCode: { } code } ? code : 0;
    }

    [Fact]
    public async Task APriceThatCouldOverflowTheFeeIsRefused()
    {
        var item = Model(ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, ItemRights.All).Item;
        Assert.Equal(400, await PublishAs(item, long.MaxValue)); Assert.Equal(400, await PublishAs(item, LifeVerse.Api.Modules.Economy.EconomyEndpoints.MaxPriceCoins + 1)); Assert.Equal(400, await PublishAs(item, -1));
        Assert.Equal("active", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).Status); Assert.Equal(201, await PublishAs(item, LifeVerse.Api.Modules.Economy.EconomyEndpoints.MaxPriceCoins));
    }

    [Fact]
    public async Task AnItemUsedUpByPlacingItCannotBeBroughtBackFromTheBinAndLockedItemsRefuseTheOldEndpoints()
    {
        var package = await SavePackage(_builder, ItemRights.Modify | ItemRights.Transfer, ItemRights.Transfer, Cube(0, 0)); Assert.Equal(200, (await Send(_builder, "rez", package: package.Id)).Status);
        var restore = await AssetEndpoints.RestoreItemAsync(package.Id, As(_builder), _db, default); Assert.Equal(404, (restore as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(1, await _db.BuildObjects.CountAsync(o => o.DeletedAt == null)); Assert.True((await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == package.Id)).DeletedAt is not null);
        // an item waiting for an answer does not answer to the old endpoints either: rename, move, delete, wear, place
        var (_, item) = Model(ItemRights.All, ItemRights.All); await ItemTransfers.OfferAsync(_db, _builder, item.Id, Person(), "gift", null, false, "chave-exploit-3", _now, default);
        Assert.Equal(404, (await AssetEndpoints.UpdateItemAsync(item.Id, new InventoryItemUpdateRequest("Outro", null), As(_builder), _db, TimeProvider.System, default) as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(404, (await AssetEndpoints.DeleteItemAsync(item.Id, As(_builder), _db, TimeProvider.System, default) as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(404, (await AssetEndpoints.EquipAsync(item.Id, new EquipAssetRequest("hat"), As(_builder), _db, TimeProvider.System, default) as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(404, (await AssetEndpoints.PlaceAsync(item.Id, new PlaceAssetRequest(_island.Id, 0, 0, 0, 0, 0, 0), As(_builder), _db, TimeProvider.System, default) as IStatusCodeHttpResult)?.StatusCode);
        var stored = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id); Assert.Equal(("offered", true), (stored.Status, stored.DeletedAt is null));
    }

    [Fact]
    public async Task AnItemThatCannotBeCopiedCannotBePlacedByTheOldEndpointToGetAFreeCopy()
    {
        var (_, item) = Model(ItemRights.Modify | ItemRights.Transfer, ItemRights.All);
        var result = await AssetEndpoints.PlaceAsync(item.Id, new PlaceAssetRequest(_island.Id, 0, 0, 0, 0, 0, 0), As(_builder), _db, TimeProvider.System, default); Assert.Equal(409, (result as IStatusCodeHttpResult)?.StatusCode);
        Assert.Equal(0, await _db.WorldAssetInstances.CountAsync());
    }

    [Fact]
    public async Task AnAssetFileIsServedOnlyToThoseWhoCanEnterAndOnlyForWhatTheIslandUses()
    {
        var (asset, _) = Model(ItemRights.Standard, ItemRights.Standard); _store.Files["k" + asset.Id] = [1, 2, 3, 4]; _db.AssetVersions.Local.First(v => v.AssetId == asset.Id).StorageKey = "k" + asset.Id; await _db.SaveChangesAsync();
        Assert.Equal(404, (await MeshAssets.IslandAssetAsync(_island.Id, asset.Id, As(_builder), _db, _storage, default) as IStatusCodeHttpResult)?.StatusCode); // not used by the island: not served
        await Create(_builder, MeshOf(asset)); var served = await MeshAssets.IslandAssetAsync(_island.Id, asset.Id, As(_builder), _db, _storage, default); Assert.True(served is not IStatusCodeHttpResult { StatusCode: 404 } and not ForbidHttpResult, served.GetType().Name);
        var closed = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Fechada", Slug = "fechada", IsPublic = false, Size = 100 }; _db.Islands.Add(closed); await _db.SaveChangesAsync();
        Assert.IsType<ForbidHttpResult>(await MeshAssets.IslandAssetAsync(closed.Id, asset.Id, As(Guid.NewGuid()), _db, _storage, default));
    }

    [Fact]
    public async Task ASoundAnObjectCarriesIsServedToThoseOnTheIslandAndNothingElseIs()
    {
        var (sound, _) = Model(ItemRights.Standard, ItemRights.Standard); _store.Files["s" + sound.Id] = [9, 9, 9]; var version = _db.AssetVersions.Local.First(v => v.AssetId == sound.Id); version.StorageKey = "s" + sound.Id; version.MimeType = "audio/wav"; await _db.SaveChangesAsync();
        Assert.Equal(404, (await MeshAssets.IslandAssetAsync(_island.Id, sound.Id, As(_builder), _db, _storage, default) as IStatusCodeHttpResult)?.StatusCode); // in nobody's content: not served
        var id = await Create(_builder, Cube(0, 0)); var carrier = await _db.BuildObjects.SingleAsync(o => o.Id == id);
        carrier.ContentJson = $"[{{\"assetId\":\"{sound.Id}\",\"name\":\"Campainha\",\"category\":\"sounds\"}}]"; await _db.SaveChangesAsync();
        var served = await MeshAssets.IslandAssetAsync(_island.Id, sound.Id, As(_builder), _db, _storage, default);
        Assert.True(served is not IStatusCodeHttpResult { StatusCode: 404 } and not ForbidHttpResult, served.GetType().Name);
        var (model, _) = Model(ItemRights.Standard, ItemRights.Standard); _store.Files["m" + model.Id] = [1]; _db.AssetVersions.Local.First(v => v.AssetId == model.Id).StorageKey = "m" + model.Id; await _db.SaveChangesAsync();
        carrier.ContentJson = $"[{{\"assetId\":\"{model.Id}\",\"name\":\"Modelo\",\"category\":\"sounds\"}}]"; await _db.SaveChangesAsync();
        Assert.Equal(404, (await MeshAssets.IslandAssetAsync(_island.Id, model.Id, As(_builder), _db, _storage, default) as IStatusCodeHttpResult)?.StatusCode); // only audio is served this way
    }
}
