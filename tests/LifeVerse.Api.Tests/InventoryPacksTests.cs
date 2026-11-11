using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Avatar;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Getting objects back (returned ones and deleted ones), looking inside a saved construction, and unzipping a pack of files.</summary>
public sealed class InventoryPacksTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStore _store = new(); private readonly AssetStorageService _storage; private readonly Island _island; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _alice = Guid.NewGuid(), _bob = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public InventoryPacksTests()
    {
        _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance);
        _island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(_island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = _island.Id, Hostname = "h", Status = "healthy" });
        foreach (var user in new[] { _alice, _bob }) _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = _island.Id, UserId = user, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Box(float x, string name = "Peça", Guid? group = null, Guid? parent = null, float pivotY = 0) =>
        new("cube", x, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{\"collision\":true}", group, 0, "{}", name, null, parent, null, null, 0, pivotY, 0);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, Guid? id = null, int revision = 0, Guid[]? ids = null, Guid? package = null, BuildObjectRequest? o = null, float originX = 0)
    {
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, o, revision, ids, package, originX, 0, 0), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private async Task<Guid> Create(Guid actor, BuildObjectRequest o) { var (s, b) = await Send(actor, "create", o: o); Assert.True(s == 200, s + " " + b); return b.GetProperty("objectId").GetGuid(); }
    private async Task<int> Rev(Guid id) => (await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id)).Revision;
    private async Task Delete(Guid actor, Guid id) => Assert.Equal(200, (await Send(actor, "delete", id, await Rev(id))).Status);

    // ---- objects that were returned and objects that were deleted ---------------------------------------------------------------------

    [Fact]
    public async Task ReturnedObjectsComeBackAsAMarkedConstructionAndAreNotLostObjects()
    {
        var a = await Create(_alice, Box(0, "Sofá")); var b = await Create(_alice, Box(2, "Mesa")); var c = await Create(_alice, Box(4, "Lixo")); await Delete(_alice, c);
        Assert.Equal(200, (await Send(_alice, "return", ids: [a, b])).Status);
        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == _alice && i.DisplayName.StartsWith("Devolvido")); Assert.Equal("returned", item.Origin);
        var lost = await InventoryRecovery.ListAsync(_db, _alice, _now.AddMinutes(1).Add(DateTimeOffset.UtcNow - _now), default); Assert.Equal([c], lost.Select(l => l.Id)); // the deleted one is lost; the returned ones are safe in the inventory
        Assert.Empty(await InventoryRecovery.ListAsync(_db, _bob, DateTimeOffset.UtcNow, default));
    }

    [Fact]
    public async Task DeletedObjectsAreRecoveredIntoTheInventoryAsOneConstructionKeepingTheirTreeAndPivots()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set, pivotY: -.5f)); var child = await Create(_alice, Box(2, "Filho", set, root)); var loose = await Create(_alice, Box(9, "Solto"));
        await Delete(_alice, child); await Delete(_alice, root); await Delete(_alice, loose);
        var now = DateTimeOffset.UtcNow; var lost = await InventoryRecovery.ListAsync(_db, _alice, now, default); Assert.Equal(3, lost.Count); Assert.All(lost, l => Assert.Equal(l.DeletedAt.AddDays(30), l.ExpiresAt));
        var result = await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [root, child], now, default); Assert.True(result.Ok, result.Error);
        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == _alice && i.Origin == "recovered"); Assert.StartsWith("Recuperado", item.DisplayName);
        var (package, error) = await BuildPackageEndpoints.LoadAsync(_db, _storage, item, default); Assert.NotNull(package); Assert.Null(error); Assert.Equal(2, package!.Objects.Length);
        var rootIndex = Array.FindIndex(package.Objects, o => o.Name == "Raiz"); var childIndex = Array.FindIndex(package.Objects, o => o.Name == "Filho"); Assert.Equal(rootIndex, package.Objects[childIndex].Parent); Assert.Null(package.Objects[rootIndex].Parent); Assert.Equal(-.5f, package.Objects[rootIndex].PivotY);
        Assert.Equal([loose], (await InventoryRecovery.ListAsync(_db, _alice, now, default)).Select(l => l.Id)); // the two are no longer lost; the third still is
        Assert.Contains(_db.ItemAudit, a => a.Action == "recover" && a.ItemId == item.Id);
        var again = await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [root], now, default); Assert.Equal("not_found", again.Error); Assert.Equal(1, await _db.InventoryItems.CountAsync(i => i.Origin == "recovered")); // once only
    }

    [Fact]
    public async Task ARecoveredConstructionCanBePlacedBackAndComesBackWhole()
    {
        var set = Guid.NewGuid(); var root = await Create(_alice, Box(0, "Raiz", set)); var kid = await Create(_alice, Box(2, "Filho", set, root)); await Delete(_alice, kid); await Delete(_alice, root);
        var result = await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [root, kid], DateTimeOffset.UtcNow, default); Assert.True(result.Ok);
        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Origin == "recovered"); Assert.Equal(200, (await Send(_alice, "rez", package: item.Id, originX: 50)).Status);
        var back = await _db.BuildObjects.AsNoTracking().Where(o => o.DeletedAt == null).ToListAsync(); Assert.Equal(2, back.Count); Assert.Single(back, o => o.ParentObjectId is null); Assert.Single(back.Select(o => o.GroupId).Distinct()); Assert.NotEqual(set, back[0].GroupId);
    }

    [Fact]
    public async Task OnlyYourOwnRecentDeletedObjectsCanBeRecoveredAndAllOrNothing()
    {
        var mine = await Create(_alice, Box(0, "Meu")); var theirs = await Create(_bob, Box(2, "Dele")); var alive = await Create(_alice, Box(4, "Vivo")); var old = await Create(_alice, Box(6, "Velho")); var returned = await Create(_alice, Box(8, "Devolvido"));
        await Delete(_alice, mine); await Delete(_bob, theirs); await Delete(_alice, old); Assert.Equal(200, (await Send(_alice, "return", ids: [returned])).Status);
        var row = await _db.BuildObjects.SingleAsync(o => o.Id == old); row.DeletedAt = DateTimeOffset.UtcNow.AddDays(-31); await _db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        foreach (var bad in new[] { theirs, alive, old, returned, Guid.NewGuid() }) Assert.Equal("not_found", (await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [mine, bad], now, default)).Error);
        Assert.Equal(0, await _db.InventoryItems.CountAsync(i => i.Origin == "recovered")); Assert.Empty(_db.ItemAudit.Where(a => a.Action == "recover")); // not even "mine" was recovered
        Assert.Equal("invalid_recovery", (await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [], now, default)).Error); Assert.Equal("invalid_recovery", (await InventoryRecovery.RecoverAsync(_db, _storage, _alice, null, now, default)).Error);
        Assert.Equal("invalid_recovery", (await InventoryRecovery.RecoverAsync(_db, _storage, _alice, Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray(), now, default)).Error);
        Assert.True((await InventoryRecovery.RecoverAsync(_db, _storage, _alice, [mine], now, default)).Ok);
    }

    [Fact]
    public async Task AnObjectDeletedByAnAdminCanBeRecoveredByItsOwnerAndTheListIsCapped()
    {
        var id = await Create(_alice, Box(0, "Do Alice")); await Delete(_owner, id); // the island owner deletes it
        Assert.Single(await InventoryRecovery.ListAsync(_db, _alice, DateTimeOffset.UtcNow, default)); Assert.Empty(await InventoryRecovery.ListAsync(_db, _owner, DateTimeOffset.UtcNow, default));
        for (var i = 0; i < InventoryRecovery.MaxListed + 5; i++) _db.BuildObjects.Add(new BuildObject { Id = Guid.NewGuid(), IslandId = _island.Id, CreatorUserId = _alice, OwnerUserId = _alice, Name = $"O{i}", PrimitiveType = "cube", DeletedAt = DateTimeOffset.UtcNow.AddMinutes(-i), CreatedAt = _now, UpdatedAt = _now });
        foreach (var o in _db.BuildObjects.Local.Where(o => o.Name.StartsWith("O")).ToList()) _db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = _island.Id, ObjectId = o.Id, ActorUserId = _alice, Operation = "delete", PayloadJson = "{}", Revision = 1, CreatedAt = _now });
        await _db.SaveChangesAsync(); var listed = await InventoryRecovery.ListAsync(_db, _alice, DateTimeOffset.UtcNow, default); Assert.Equal(InventoryRecovery.MaxListed, listed.Count); Assert.Equal(listed.OrderByDescending(l => l.DeletedAt), listed);
    }

    // ---- inside a construction ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AConstructionShowsItsPartsTheThingsItIsMadeOfAndWhichOfThemYouHave()
    {
        var model = new Asset { Id = Guid.NewGuid(), OwnerUserId = _bob, CreatorUserId = _bob, Name = "Estátua", Category = "models", CreatedAt = _now, UpdatedAt = _now }; var script = new Asset { Id = Guid.NewGuid(), OwnerUserId = _bob, CreatorUserId = _bob, Name = "Porta", Category = "scripts", CreatedAt = _now, UpdatedAt = _now };
        var texture = new Asset { Id = Guid.NewGuid(), OwnerUserId = _bob, CreatorUserId = _bob, Name = "Tinta", Category = "textures", CreatedAt = _now, UpdatedAt = _now }; _db.Assets.AddRange(model, script, texture);
        _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _alice, AssetId = texture.Id, DisplayName = "Tinta", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync(); // Alice has the texture, not the model or the script
        var set = Guid.NewGuid();
        var sources = new List<PackageSourceDto>
        {
            new("mesh", "Base", "", 0, 0, 0, 0, 0, 0, 1, 1, 1, $"{{\"texture\":\"{texture.Id}\"}}".Replace("texture", "textureId"), $"{{\"assetId\":\"{model.Id}\"}}", "{}", $"[{{\"assetId\":\"{script.Id}\",\"name\":\"Porta\",\"category\":\"scripts\"}}]", set, Guid.NewGuid(), null, 0, -.5f, 0),
            new("cube", "Parte", "", 1, 0, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{}", "[]", null)
        };
        var item = await BuildPackageStore.SaveAsync(_db, _storage, _alice, PackageDocument.Create("Casa", sources), _now, default); await _db.SaveChangesAsync();
        var result = await PackContents.ReadAsync(_db, _storage, _alice, item.Id, default); Assert.True(result.Ok, result.Error);
        var json = JsonSerializer.SerializeToElement(result.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web)); Assert.Equal(2, json.GetProperty("objects").GetInt32()); Assert.Equal(1, json.GetProperty("linksets").GetInt32()); Assert.Equal(1, json.GetProperty("withScripts").GetInt32());
        var parts = json.GetProperty("parts").EnumerateArray().ToList(); Assert.Equal(["Base", "Parte"], parts.Select(p => p.GetProperty("name").GetString())); Assert.True(parts[0].GetProperty("hasPivot").GetBoolean()); Assert.Equal(1, parts[0].GetProperty("scripts").GetInt32());
        var assets = json.GetProperty("assets").EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("held").GetBoolean());
        Assert.Contains("Estátua", assets.Keys); Assert.True(assets.TryGetValue("Porta", out var hasScript) && !hasScript); Assert.False(assets["Estátua"]); Assert.Equal(2, json.GetProperty("missing").GetInt32());
        Assert.Equal("not_found", (await PackContents.ReadAsync(_db, _storage, _bob, item.Id, default)).Error); // somebody else's
        var plain = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _alice, AssetId = model.Id, DisplayName = "Não é construção", CreatedAt = _now, UpdatedAt = _now }; _db.InventoryItems.Add(plain); await _db.SaveChangesAsync();
        Assert.Equal("not_a_package", (await PackContents.ReadAsync(_db, _storage, _alice, plain.Id, default)).Error);
    }

    // ---- unzipping --------------------------------------------------------------------------------------------------------------------

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static byte[] Wav() => TestFiles.Wav();

    private static byte[] Zip(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var (name, bytes) in files) { var entry = zip.CreateEntry(name, CompressionLevel.Optimal); using var s = entry.Open(); s.Write(bytes); }
        return stream.ToArray();
    }

    private Task<(bool Ok, string? Error, object? Report)> Unpack(string name, byte[] zip) => PackUnzip.UnpackAsync(_db, _storage, _alice, name, zip, _now, default);
    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task AZipOfGoodFilesBecomesItemsInANewFolderMarkedUnpacked()
    {
        var glb = HumanoidTemplate.Build();
        var result = await Unpack("Casa de Praia.zip", Zip(("modelos/cadeira.glb", glb), ("texturas/areia.png", Png), ("sons/onda.wav", Wav()), ("__MACOSX/modelos/._cadeira.glb", [1, 2, 3]), (".DS_Store", [1]), ("leia-me/", [])));
        Assert.True(result.Ok, result.Error + " " + JsonSerializer.Serialize(result.Report));
        var report = Json(result.Report); Assert.Equal(3, report.GetProperty("count").GetInt32()); Assert.Equal("Casa de Praia", report.GetProperty("folderName").GetString());
        var folder = await _db.InventoryFolders.AsNoTracking().SingleAsync(f => f.OwnerUserId == _alice); Assert.Null(folder.ParentId);
        var items = await _db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == _alice).ToListAsync(); Assert.Equal(3, items.Count); Assert.All(items, i => { Assert.Equal(folder.Id, i.FolderId); Assert.Equal("unpacked", i.Origin); Assert.Equal(15, i.OwnerRights); });
        var assets = await _db.Assets.AsNoTracking().Where(a => items.Select(i => i.AssetId).Contains(a.Id)).ToListAsync(); Assert.Equal(["models", "sounds", "textures"], assets.Select(a => a.Category).OrderBy(c => c)); Assert.Equal(["areia", "cadeira", "onda"], items.Select(i => i.DisplayName).OrderBy(n => n));
        Assert.Equal(3, _store.Files.Count); Assert.Contains(_db.ItemAudit, a => a.Action == "unpack");
    }

    [Fact]
    public async Task UnzippingTheSameZipAgainMakesAnotherFolderAndReusesTheStoredFiles()
    {
        var zip = Zip(("a.png", Png)); Assert.True((await Unpack("Pacote.zip", zip)).Ok); var assetsBefore = await _db.Assets.CountAsync();
        Assert.True((await Unpack("Pacote.zip", zip)).Ok); Assert.Equal(["Pacote", "Pacote (2)"], (await _db.InventoryFolders.AsNoTracking().Select(f => f.Name).ToListAsync()).OrderBy(n => n)); Assert.Equal(assetsBefore, await _db.Assets.CountAsync()); Assert.Equal(2, await _db.InventoryItems.CountAsync());
        Assert.True((await Unpack("a.zip", Zip(("um.png", Png), ("dois.png", Png)))).Ok); Assert.Equal(4, await _db.InventoryItems.CountAsync()); Assert.Equal(assetsBefore, await _db.Assets.CountAsync()); // two copies of the same bytes inside one zip are two items of one asset
    }

    [Fact]
    public async Task OneBadFileStopsTheWholePackAndNothingIsCreated()
    {
        var zip = Zip(("bom.png", Png), ("texto.txt", Encoding.UTF8.GetBytes("oi")), ("falso.png", Encoding.UTF8.GetBytes("isto nao e uma imagem")), ("../fora.png", Png));
        var result = await Unpack("Mistura.zip", zip); Assert.False(result.Ok); Assert.Equal("bad_files", result.Error);
        var problems = Json(result.Report).GetProperty("problems").EnumerateArray().ToDictionary(p => p.GetProperty("file").GetString()!, p => p.GetProperty("error").GetString()!);
        Assert.Equal("unsupported_type", problems["texto.txt"]); Assert.Equal("unsafe_path", problems["../fora.png"]); Assert.Contains("image", problems["falso.png"], StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("bom.png", problems.Keys);
        Assert.Equal(0, await _db.InventoryItems.CountAsync()); Assert.Equal(0, await _db.InventoryFolders.CountAsync()); Assert.Equal(0, await _db.Assets.CountAsync()); Assert.Empty(_store.Files); // not even the good file
    }

    [Theory]
    [InlineData("C:/windows/x.png")] [InlineData("/etc/x.png")] [InlineData("a/../../x.png")] [InlineData("a\\..\\x.png")]
    public async Task PathsThatClimbOutOrNameADriveAreRefused(string name)
    {
        var result = await Unpack("x.zip", Zip((name, Png))); Assert.False(result.Ok); Assert.Equal("unsafe_path", Json(result.Report).GetProperty("problems")[0].GetProperty("error").GetString());
    }

    [Fact]
    public async Task ZipsThatAreNotZipsEmptyHugeOrBombsAreRefusedWithAReason()
    {
        Assert.Equal("invalid_zip", (await Unpack("x.zip", Encoding.UTF8.GetBytes("not a zip at all"))).Error); Assert.Equal("invalid_zip", (await Unpack("x.zip", [])).Error);
        Assert.Equal("empty_zip", (await Unpack("x.zip", Zip(("pasta/", []), (".oculto", [1])))).Error);
        var crowd = Zip(Enumerable.Range(0, PackUnzip.MaxEntries + 1).Select(i => ($"f{i}.png", Png)).ToArray()); var many = await Unpack("x.zip", crowd); Assert.Equal("too_many_files", many.Error);
        Assert.Equal("suspicious_compression", Json((await Unpack("x.zip", Zip(("bomba.wav", new byte[3 * 1024 * 1024])))).Report).GetProperty("problems")[0].GetProperty("error").GetString()); // 3 MB of nothing in a few KB
        Assert.Equal("nested_zip", Json((await Unpack("x.zip", Zip(("dentro.zip", Zip(("a.png", Png)))))).Report).GetProperty("problems")[0].GetProperty("error").GetString());
        Assert.Equal(0, await _db.InventoryItems.CountAsync());
        var damaged = Zip(("a.png", Png)); damaged[^30] ^= 0xFF; damaged[10] ^= 0xFF; var broken = await Unpack("x.zip", damaged); Assert.False(broken.Ok); // a damaged zip is a refusal, never a crash
    }

    [Fact]
    public async Task ThePackFolderNameIsMadeSafeAndAFullInventoryRefusesBeforeCreatingAnything()
    {
        Assert.True((await Unpack("..\\<estranho>:nome*.zip", Zip(("a.png", Png)))).Ok); var name = (await _db.InventoryFolders.AsNoTracking().SingleAsync()).Name; Assert.True(name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'), name); Assert.NotEmpty(name);
        for (var i = 0; i < ItemTransfers.MaxItemsPerPerson - 1; i++) _db.InventoryItems.Add(new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = _alice, AssetId = Guid.NewGuid(), DisplayName = "x", CreatedAt = _now, UpdatedAt = _now });
        await _db.SaveChangesAsync(); var before = await _db.InventoryFolders.CountAsync(); var full = await Unpack("Outro.zip", Zip(("a.png", Png), ("b.png", Png))); Assert.False(full.Ok); Assert.Equal("inventory_full", full.Error); Assert.Equal(before, await _db.InventoryFolders.CountAsync());
    }

    [Fact]
    public async Task NobodyUnpacksIntoAnotherPersonsInventory()
    {
        Assert.True((await PackUnzip.UnpackAsync(_db, _storage, _bob, "Do Bob.zip", Zip(("a.png", Png)), _now, default)).Ok);
        Assert.Equal(0, await _db.InventoryItems.CountAsync(i => i.OwnerUserId == _alice)); Assert.All(await _db.InventoryFolders.ToListAsync(), f => Assert.Equal(_bob, f.OwnerUserId));
    }
}

public sealed class RecoveryRelinkTests
{
    private static BuildObject O(string name, Guid? group = null, Guid? parent = null) => new() { Id = Guid.NewGuid(), IslandId = Guid.NewGuid(), Name = name, PrimitiveType = "cube", GroupId = group, ParentObjectId = parent, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void ConnectedObjectsShareOneGroupAndHaveExactlyOneRoot()
    {
        var root = O("raiz"); var kid = O("filho", parent: root.Id); var grandkid = O("neto", parent: kid.Id); var alone = O("solto"); var stranger = O("de fora", parent: Guid.NewGuid());
        var links = InventoryRecovery.Relink([root, kid, grandkid, alone, stranger]);
        Assert.Equal((links[root.Id].Group, links[kid.Id].Group, links[grandkid.Id].Group), (links[root.Id].Group, links[root.Id].Group, links[root.Id].Group)); Assert.NotNull(links[root.Id].Group);
        Assert.Null(links[root.Id].Parent); Assert.Equal(root.Id, links[kid.Id].Parent); Assert.Equal(kid.Id, links[grandkid.Id].Parent);
        Assert.Equal((null, null), (links[alone.Id].Group, links[alone.Id].Parent)); Assert.Equal((null, null), (links[stranger.Id].Group, links[stranger.Id].Parent)); // a parent that is not being recovered does not make a linkset of one
    }

    [Fact]
    public void SeveralObjectsWithoutAParentInTheSelectionHangFromTheFirstSoThereIsOneRoot()
    {
        var group = Guid.NewGuid(); var a = O("a", group); var b = O("b", group); var c = O("c", group, a.Id); var links = InventoryRecovery.Relink([a, b, c]);
        Assert.Equal(links[a.Id].Group, links[b.Id].Group); Assert.Equal(links[a.Id].Group, links[c.Id].Group); Assert.Single(new[] { a, b, c }, o => links[o.Id].Parent is null); Assert.Equal(a.Id, links[b.Id].Parent); Assert.Equal(a.Id, links[c.Id].Parent);
        Assert.Equal(group, links[a.Id].Group); // the group they had is kept when they still share it
    }

    [Fact]
    public void ACycleInTheDataCannotBreakItAndNothingIsInventedForNothing()
    {
        var a = O("a"); var b = O("b"); a.ParentObjectId = b.Id; b.ParentObjectId = a.Id; var links = InventoryRecovery.Relink([a, b]);
        Assert.Single(new[] { a, b }, o => links[o.Id].Parent is null); Assert.Equal(links[a.Id].Group, links[b.Id].Group); Assert.Empty(InventoryRecovery.Relink([]));
    }
}
