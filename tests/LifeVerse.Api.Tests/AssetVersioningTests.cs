using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Social;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Networking;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Versions of an asset: publishing, going back, who sees the history, and the update mark of the people who hold the item.</summary>
public sealed class AssetVersioningTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) { Files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(Files.TryGetValue(key, out var b) ? new MemoryStream(b) : null);
    }

    private sealed class Listener : WebSocket
    {
        private WebSocketState _state = WebSocketState.Open; public List<string> Sent { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null; public override string? CloseStatusDescription => null; public override WebSocketState State => _state; public override string? SubProtocol => null;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken ct) { Sent.Add(Encoding.UTF8.GetString(buffer.AsSpan())); return Task.CompletedTask; }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct) => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true));
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.Closed; return Task.CompletedTask; }
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken ct) { _state = WebSocketState.CloseSent; return Task.CompletedTask; }
        public override void Abort() => _state = WebSocketState.Aborted; public override void Dispose() { }
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryStore _store = new(); private readonly AssetStorageService _storage; private readonly Guid _maker = Guid.NewGuid(), _ana = Guid.NewGuid(), _bia = Guid.NewGuid(), _stranger = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero); private int _tick;

    public AssetVersioningTests() { _storage = new AssetStorageService(_store, NullLogger<AssetStorageService>.Instance); }
    public void Dispose() => _db.Dispose();

    private DateTimeOffset Tick() => _now.AddMinutes(++_tick);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static byte[] Png2(int n) => [.. Png, .. Enumerable.Repeat((byte)n, n + 1)]; // the same picture header with something extra after it: other bytes, still a picture
    private static byte[] Glb(float scale = 1f) => HumanoidTemplate.Build(new TemplateOptions { Scale = scale });

    private async Task<Asset> NewAsset(string category = "textures", string name = "Tinta", byte[]? first = null)
    {
        var bytes = first ?? (category == "textures" ? Png : Glb()); var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = _maker, CreatorUserId = _maker, Name = name, Category = category, CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = hash, StorageKey = "k" + asset.Id, MimeType = category == "textures" ? "image/png" : "model/gltf-binary", ByteSize = bytes.Length, CreatedAt = _now });
        await _db.SaveChangesAsync(); return asset;
    }

    private async Task<InventoryItem> Hold(Guid user, Asset asset, DateTimeOffset? since = null)
    {
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = user, AssetId = asset.Id, DisplayName = asset.Name, CreatedAt = since ?? _now, UpdatedAt = _now }; _db.InventoryItems.Add(item); await _db.SaveChangesAsync(); return item;
    }

    private Task<InventoryOutcome> Publish(Asset asset, string file, byte[] bytes, string? notes = null, Guid? by = null) => AssetVersioning.PublishAsync(_db, _storage, by ?? _maker, asset.Id, file, bytes, notes, Tick(), default);

    private async Task<List<string>> UpdatedFor(Guid user, string view = "updated")
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, user.ToString())], "test")) };
        var result = await InventoryEndpoints.ListAsync(new InventoryQuery(View: view), context, new FixedClock(_now.AddDays(5)), _db, default);
        return JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("name").GetString() + (e.GetProperty("hasUpdate").GetBoolean() ? "*" : "")).ToList();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    // ---- publishing and going back ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyTheCreatorPublishesANewVersionAndItBecomesTheCurrentOne()
    {
        var asset = await NewAsset(); var result = await Publish(asset, "tinta.png", Png2(3), "  mais\u0007 brilho  ");
        Assert.True(result.Ok, result.Error); var made = Assert.IsType<VersionResult>(result.Data); Assert.Equal((2, true), (made.Version, made.Changed));
        var after = await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id); Assert.Equal(2, after.CurrentVersion); var v2 = await _db.AssetVersions.AsNoTracking().SingleAsync(v => v.AssetId == asset.Id && v.Version == 2); Assert.Equal("mais brilho", v2.Notes); Assert.Equal("image/png", v2.MimeType); Assert.Contains(v2.StorageKey, _store.Files.Keys);
        Assert.Contains(_db.ItemAudit, a => a.Action == "version_publish" && a.AssetId == asset.Id);
        Assert.Equal("not_found", (await Publish(asset, "tinta.png", Png2(4), by: _ana)).Error); Assert.Equal(2, (await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).CurrentVersion); // a holder cannot change what everyone gets
        Assert.Equal(3, ((VersionResult)(await Publish(asset, "tinta.png", Png2(5))).Data!).Version); Assert.Equal(3, await _db.AssetVersions.CountAsync(v => v.AssetId == asset.Id));
    }

    [Fact]
    public async Task ANewVersionMustBeTheSameKindOfFileAndPassTheSameChecksAsAnUpload()
    {
        var texture = await NewAsset(); var model = await NewAsset("models", "Cadeira");
        Assert.Equal("wrong_kind", (await Publish(texture, "modelo.glb", Glb())).Error); Assert.Equal("wrong_kind", (await Publish(model, "tinta.png", Png)).Error); Assert.Equal("wrong_kind", (await Publish(texture, "tinta.exe", Png)).Error);
        Assert.Equal("invalid_file", (await Publish(texture, "falsa.png", Encoding.UTF8.GetBytes("isto nao e uma imagem"))).Error); Assert.Equal("invalid_file", (await Publish(model, "quebrado.glb", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])).Error);
        Assert.Equal("unchanged", (await Publish(texture, "tinta.png", Png)).Error); // the same bytes as the current version
        Assert.True((await Publish(model, "cadeira.glb", Glb(2f))).Ok); Assert.Equal(1, await _db.AssetVersions.CountAsync(v => v.AssetId == texture.Id)); Assert.Equal(2, await _db.AssetVersions.CountAsync(v => v.AssetId == model.Id));
        var script = await NewAsset("scripts", "Roteiro"); Assert.Equal("not_versionable", (await Publish(script, "a.png", Png)).Error); Assert.Equal("not_found", (await AssetVersioning.PublishAsync(_db, _storage, _maker, Guid.NewGuid(), "a.png", Png, null, Tick(), default)).Error);
    }

    [Fact]
    public async Task VersionsAreLimitedAndNotesAreCutToSize()
    {
        var asset = await NewAsset();
        for (var i = 2; i <= AssetVersioning.MaxVersions; i++) Assert.True((await Publish(asset, "t.png", Png2(i), "x")).Ok, $"version {i}");
        Assert.Equal("version_limit", (await Publish(asset, "t.png", Png2(99))).Error); Assert.Equal(AssetVersioning.MaxVersions, (await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).CurrentVersion);
        Assert.Equal(AssetVersioning.MaxNotes, AssetVersioning.CleanNotes(new string('a', 1000)).Length); Assert.Equal(string.Empty, AssetVersioning.CleanNotes(null)); Assert.Equal("a\nb", AssetVersioning.CleanNotes(" a\nb \u0000"));
    }

    [Fact]
    public async Task TheCreatorCanGoBackToAnEarlierVersionAndForwardAgainWithoutRewritingAnything()
    {
        var asset = await NewAsset(); await Publish(asset, "t.png", Png2(2), "dois"); await Publish(asset, "t.png", Png2(3), "três");
        var back = await AssetVersioning.ActivateAsync(_db, _maker, asset.Id, 1, Tick(), default); Assert.True(back.Ok); Assert.True(((VersionResult)back.Data!).Changed); Assert.Equal(1, (await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).CurrentVersion);
        Assert.False(((VersionResult)(await AssetVersioning.ActivateAsync(_db, _maker, asset.Id, 1, Tick(), default)).Data!).Changed); // already there
        Assert.Equal("not_found", (await AssetVersioning.ActivateAsync(_db, _maker, asset.Id, 9, Tick(), default)).Error); Assert.Equal("not_found", (await AssetVersioning.ActivateAsync(_db, _ana, asset.Id, 2, Tick(), default)).Error);
        Assert.True((await AssetVersioning.ActivateAsync(_db, _maker, asset.Id, 3, Tick(), default)).Ok); Assert.Equal(3, await _db.AssetVersions.CountAsync(v => v.AssetId == asset.Id));
        Assert.Equal(4, ((VersionResult)(await Publish(asset, "t.png", Png2(6))).Data!).Version); // numbering only goes up, whatever was activated
        Assert.Equal(2, _db.ItemAudit.Count(a => a.Action == "version_activate"));
    }

    // ---- who sees the history ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheCreatorAndTheHoldersSeeTheHistoryAndNobodyElse()
    {
        var asset = await NewAsset(); await Hold(_ana, asset); await Publish(asset, "t.png", Png2(2), "mais brilho"); await Publish(asset, "t.png", Png2(3));
        foreach (var who in new[] { _maker, _ana })
        {
            var result = await AssetVersioning.HistoryAsync(_db, who, asset.Id, default); Assert.True(result.Ok); var json = JsonSerializer.SerializeToElement(result.Data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(3, json.GetProperty("current").GetInt32()); Assert.Equal(who == _maker, json.GetProperty("isOwner").GetBoolean()); var versions = json.GetProperty("versions").EnumerateArray().ToList();
            Assert.Equal([3, 2, 1], versions.Select(v => v.GetProperty("version").GetInt32())); Assert.Equal([true, false, false], versions.Select(v => v.GetProperty("current").GetBoolean())); Assert.Equal("mais brilho", versions[1].GetProperty("notes").GetString());
        }
        Assert.Equal("not_found", (await AssetVersioning.HistoryAsync(_db, _stranger, asset.Id, default)).Error); Assert.Equal("not_found", (await AssetVersioning.HistoryAsync(_db, _maker, Guid.NewGuid(), default)).Error);
        var gone = await Hold(_bia, asset); gone.DeletedAt = _now; await _db.SaveChangesAsync(); Assert.Equal("not_found", (await AssetVersioning.HistoryAsync(_db, _bia, asset.Id, default)).Error); // an item in the bin no longer opens the history
    }

    // ---- the update mark ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AHolderSeesAnUpdateMarkUntilTheyLookAtItAndTheCreatorNeverDoes()
    {
        var asset = await NewAsset(name: "Estátua"); var mine = await Hold(_ana, asset); var makers = await Hold(_maker, asset); var late = await Hold(_bia, asset, _now.AddHours(1));
        Assert.Empty(await UpdatedFor(_ana)); // nothing was published yet
        await Publish(asset, "t.png", Png2(2), "novo brilho"); // published at _now + 1 minute: after Ana got it, before Bia did
        Assert.Equal(["Estátua*"], await UpdatedFor(_ana)); Assert.Empty(await UpdatedFor(_maker)); Assert.Empty(await UpdatedFor(_bia)); // the creator made it; Bia got it after
        Assert.Equal(["Estátua*"], await UpdatedFor(_ana, "all"));
        Assert.True((await AssetVersioning.AcknowledgeAsync(_db, _ana, mine.Id, _now.AddMinutes(30), default)).Ok); Assert.Empty(await UpdatedFor(_ana)); Assert.Equal(["Estátua"], await UpdatedFor(_ana, "all")); // looked at: the mark goes away
        _tick += 2000; await Publish(asset, "t.png", Png2(3)); // time passes: the next version is later than the moment they looked
        Assert.Equal(["Estátua*"], await UpdatedFor(_ana)); // and comes back with the next version
        Assert.Equal("not_found", (await AssetVersioning.AcknowledgeAsync(_db, _bia, mine.Id, _now, default)).Error); Assert.Equal("not_found", (await AssetVersioning.AcknowledgeAsync(_db, _ana, Guid.NewGuid(), _now, default)).Error); // only your own item
        Assert.NotNull(makers); Assert.NotNull(late);
    }

    [Fact]
    public async Task APurgedOrBinnedItemNeverShowsAnUpdate()
    {
        var asset = await NewAsset(); var item = await Hold(_ana, asset); await Publish(asset, "t.png", Png2(2)); Assert.Single(await UpdatedFor(_ana));
        item.DeletedAt = _now.AddMinutes(10); await _db.SaveChangesAsync(); Assert.Empty(await UpdatedFor(_ana)); item.Status = "purged"; await _db.SaveChangesAsync(); Assert.Empty(await UpdatedFor(_ana, "all"));
    }

    // ---- telling the holders ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheHoldersWhoAreOnlineAreToldAndTheCreatorIsNot()
    {
        var hub = new NotificationHub(TimeProvider.System, NullLogger<NotificationHub>.Instance); var maker = new Listener(); var ana = new Listener(); var bia = new Listener(); var stranger = new Listener();
        hub.TryAdd(_maker, Guid.NewGuid(), maker); hub.TryAdd(_ana, Guid.NewGuid(), ana); hub.TryAdd(_bia, Guid.NewGuid(), bia); hub.TryAdd(_stranger, Guid.NewGuid(), stranger);
        var asset = await NewAsset(name: "Estátua"); await Hold(_ana, asset); await Hold(_ana, asset); var gone = await Hold(_bia, asset); gone.DeletedAt = _now; await _db.SaveChangesAsync(); await Hold(_maker, asset);
        await Publish(asset, "t.png", Png2(2), "novo brilho"); await AssetVersioning.NotifyHoldersAsync(hub, _db, asset.Id, 2, default);
        Assert.Single(ana.Sent); Assert.Empty(maker.Sent); Assert.Empty(bia.Sent); Assert.Empty(stranger.Sent); // twice-held is still told once; the creator, the one who put it in the bin and the stranger are not
        using (var json = JsonDocument.Parse(ana.Sent[0])) { Assert.Equal("asset_updated", json.RootElement.GetProperty("type").GetString()); Assert.Equal(2, json.RootElement.GetProperty("data").GetProperty("version").GetInt32()); }
        Assert.Equal("“Estátua” ganhou a versão 2: novo brilho", NotificationFormatter.Describe(ana.Sent[0]));
        await AssetVersioning.NotifyHoldersAsync(hub, _db, Guid.NewGuid(), 1, default); // an asset that is not there: nothing, no error
    }

    [Theory]
    [InlineData("""{"type":"asset_updated","data":{"name":"Cadeira","version":3,"notes":""}}""", "“Cadeira” ganhou a versão 3.")]
    [InlineData("""{"type":"asset_updated","data":{"name":"Cadeira","version":4,"notes":"mais leve"}}""", "“Cadeira” ganhou a versão 4: mais leve")]
    [InlineData("""{"type":"asset_updated","data":{}}""", "“” ganhou a versão nova.")]
    public void TheUpdateEventBecomesShortText(string json, string expected) => Assert.Equal(expected, NotificationFormatter.Describe(json));

    // ---- products ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheProductListSaysWhichVersionOfTheAssetIsForSale()
    {
        var asset = await NewAsset(name: "Estátua"); var now = DateTimeOffset.UtcNow;
        _db.MarketplaceProducts.Add(new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = _maker, AssetId = asset.Id, Name = "Estátua", Description = "d", Category = "objetos", PriceCoins = 10, Status = "active", CreatedAt = now, UpdatedAt = now }); await _db.SaveChangesAsync();
        async Task<JsonElement> Listed() { var result = await EconomyEndpoints.ProductsAsync(null, null, 1, 10, _db, default); return JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty("items")[0]; }
        Assert.Equal(1, (await Listed()).GetProperty("assetVersion").GetInt32()); await Publish(asset, "t.png", Png2(2)); var second = await Listed(); Assert.Equal(2, second.GetProperty("assetVersion").GetInt32()); Assert.NotEqual(JsonValueKind.Null, second.GetProperty("assetUpdatedAt").ValueKind);
    }
}
