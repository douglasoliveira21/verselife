using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Social;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>How much storage a person uses, the limit on storing more, and keeping several sessions of the same person in step.</summary>
public sealed class InventoryStorageTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
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
    private readonly AssetStorageService _storage = new(new MemoryStore(), NullLogger<AssetStorageService>.Instance); private readonly Guid _ana = Guid.NewGuid(), _bia = Guid.NewGuid();
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _db.Dispose();

    private async Task<Asset> NewAsset(Guid owner, string category, long size, string name = "Peça", int versions = 1, long olderSize = 0)
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = name, Category = category, CurrentVersion = versions, CreatedAt = _now, UpdatedAt = _now }; _db.Assets.Add(asset);
        for (var v = 1; v <= versions; v++) _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = v, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k", MimeType = "x/y", ByteSize = v == versions ? size : olderSize, CreatedAt = _now });
        await _db.SaveChangesAsync(); return asset;
    }

    private async Task<InventoryItem> Hold(Guid user, Asset asset, string status = "active", bool binned = false)
    {
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = user, AssetId = asset.Id, DisplayName = asset.Name, Status = status, DeletedAt = binned ? _now : null, CreatedAt = _now, UpdatedAt = _now }; _db.InventoryItems.Add(item); await _db.SaveChangesAsync(); return item;
    }

    // ---- how much is used ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheReportCountsEachAssetOnceBySizeAndKind()
    {
        var model = await NewAsset(_bia, "models", 5000); var texture = await NewAsset(_bia, "textures", 300); var sound = await NewAsset(_bia, "sounds", 1000);
        await Hold(_ana, model); await Hold(_ana, model); await Hold(_ana, texture); await Hold(_ana, sound); await Hold(_bia, model); // Ana holds the model twice
        var report = await InventoryStorage.ReportAsync(_db, _ana, 100_000, default);
        Assert.Equal((6300L, 100_000L, 0L, 0L, 4, 3), (report.UsedBytes, report.QuotaBytes, report.InBinBytes, report.OlderVersionsBytes, report.Items, report.Assets));
        Assert.Equal([("models", 5000L, 1), ("sounds", 1000L, 1), ("textures", 300L, 1)], report.ByCategory.Select(c => (c.Category, c.Bytes, c.Assets))); Assert.Equal(.063, report.Fraction, 3);
        Assert.Equal(5000, (await InventoryStorage.ReportAsync(_db, _bia, 100_000, default)).UsedBytes); Assert.Equal(0, (await InventoryStorage.ReportAsync(_db, Guid.NewGuid(), 100_000, default)).UsedBytes);
    }

    [Fact]
    public async Task TheBinStillCountsButGoneThingsDoNot()
    {
        var a = await NewAsset(_bia, "models", 1000); var b = await NewAsset(_bia, "models", 2000); var c = await NewAsset(_bia, "models", 4000); var d = await NewAsset(_bia, "models", 8000); var e = await NewAsset(_bia, "models", 16000);
        await Hold(_ana, a); await Hold(_ana, b, binned: true); await Hold(_ana, c, status: "purged", binned: true); await Hold(_ana, d, status: "consumed", binned: true); await Hold(_ana, e, status: "offered"); await Hold(_ana, e, binned: true);
        var report = await InventoryStorage.ReportAsync(_db, _ana, 1 << 20, default);
        Assert.Equal(1000 + 2000 + 16000, report.UsedBytes); Assert.Equal(2000, report.InBinBytes); // purged and used-up items are gone; "e" is held alive once and binned once, so it is not in the bin
    }

    [Fact]
    public async Task OnlyTheSizeInUseCountsForWhatYouHoldButOlderVersionsCountForWhatYouMade()
    {
        var bought = await NewAsset(_bia, "models", 3000, versions: 3, olderSize: 900); var made = await NewAsset(_ana, "models", 4000, versions: 3, olderSize: 500);
        await Hold(_ana, bought); await Hold(_ana, made);
        var report = await InventoryStorage.ReportAsync(_db, _ana, 1 << 20, default); Assert.Equal(3000 + 4000 + 2 * 500, report.UsedBytes); Assert.Equal(1000, report.OlderVersionsBytes); // the seller's older versions are the seller's
        Assert.Equal(2 * 900, (await InventoryStorage.ReportAsync(_db, _bia, 1 << 20, default)).UsedBytes); // the seller holds none of it any more, but still stores the old versions of what they made
    }

    [Fact]
    public async Task StoringMoreIsRefusedOnlyWhenItWouldGoOverTheLimit()
    {
        var asset = await NewAsset(_ana, "models", 600); await Hold(_ana, asset);
        Assert.True(await InventoryStorage.FitsAsync(_db, _ana, 400, 1000, default)); Assert.False(await InventoryStorage.FitsAsync(_db, _ana, 401, 1000, default)); Assert.True(await InventoryStorage.FitsAsync(_db, _ana, 0, 600, default)); Assert.True(await InventoryStorage.FitsAsync(_db, _ana, -5, 600, default));
        Assert.True(await InventoryStorage.FitsAsync(_db, _bia, 1000, 1000, default)); Assert.False(await InventoryStorage.FitsAsync(_db, _bia, 1001, 1000, default)); // each person has their own
        Assert.Equal(InventoryStorage.DefaultQuotaBytes, InventoryStorage.QuotaOf(null)); var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Inventory:QuotaBytes"] = "5000" }).Build(); Assert.Equal(5000, InventoryStorage.QuotaOf(configured));
        Assert.Equal(InventoryStorage.DefaultQuotaBytes, InventoryStorage.QuotaOf(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Inventory:QuotaBytes"] = "lots" }).Build())); Assert.Equal(InventoryStorage.DefaultQuotaBytes, InventoryStorage.QuotaOf(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Inventory:QuotaBytes"] = "-1" }).Build()));
    }

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static byte[] Zip(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream(); using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) foreach (var (name, bytes) in files) { var entry = zip.CreateEntry(name); using var s = entry.Open(); s.Write(bytes); }
        return stream.ToArray();
    }

    [Fact]
    public async Task AnUnzipThatWouldGoOverTheLimitCreatesNothing()
    {
        var zip = Zip(("a.png", Png), ("b.png", [.. Png, 1, 2, 3]));
        var refused = await PackUnzip.UnpackAsync(_db, _storage, _ana, "x.zip", zip, _now, default, quota: Png.Length * 2 - 1); Assert.False(refused.Ok); Assert.Equal("storage_full", refused.Error);
        Assert.Equal(0, await _db.InventoryItems.CountAsync()); Assert.Equal(0, await _db.InventoryFolders.CountAsync()); Assert.Equal(0, await _db.Assets.CountAsync());
        Assert.True((await PackUnzip.UnpackAsync(_db, _storage, _ana, "x.zip", zip, _now, default, quota: 1000)).Ok);
        Assert.Equal("storage_full", (await PackUnzip.UnpackAsync(_db, _storage, _ana, "y.zip", Zip(("c.png", [.. Png, 9, 9, 9, 9])), _now, default, quota: (await InventoryStorage.ReportAsync(_db, _ana, 1000, default)).UsedBytes + 3)).Error); // what is already stored counts
    }

    [Fact]
    public async Task APublishedVersionThatWouldGoOverTheLimitIsRefusedAndKeepsTheCurrentOne()
    {
        var asset = await NewAsset(_ana, "textures", Png.Length); await Hold(_ana, asset); var bigger = new byte[Png.Length + 50]; Png.CopyTo(bigger, 0);
        var refused = await AssetVersioning.PublishAsync(_db, _storage, _ana, asset.Id, "t.png", bigger, null, _now, default, quota: Png.Length + 49); Assert.Equal("storage_full", refused.Error);
        Assert.Equal(1, (await _db.Assets.AsNoTracking().SingleAsync(a => a.Id == asset.Id)).CurrentVersion); Assert.Equal(1, await _db.AssetVersions.CountAsync(v => v.AssetId == asset.Id));
        Assert.True((await AssetVersioning.PublishAsync(_db, _storage, _ana, asset.Id, "t.png", bigger, null, _now, default, quota: Png.Length * 3)).Ok); // the old version stays and counts too
        var report = await InventoryStorage.ReportAsync(_db, _ana, 1 << 20, default); Assert.Equal(bigger.Length + Png.Length, report.UsedBytes); Assert.Equal(Png.Length, report.OlderVersionsBytes);
    }

    // ---- sessions in step ----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("POST", "/api/v1/inventory/move", true)] [InlineData("DELETE", "/api/v1/inventory/items/123", true)] [InlineData("PATCH", "/api/v1/inventory/items/123", true)] [InlineData("POST", "/api/v1/inventory/folders", true)]
    [InlineData("POST", "/api/v1/assets/upload", true)] [InlineData("POST", "/api/v1/assets/abc/versions", true)] [InlineData("POST", "/api/v1/economy/products/abc/purchase", true)] [InlineData("PUT", "/API/V1/INVENTORY/items/1/rights", true)]
    [InlineData("GET", "/api/v1/inventory/items", false)] [InlineData("HEAD", "/api/v1/inventory/items", false)] [InlineData("OPTIONS", "/api/v1/inventory/items", false)] [InlineData("POST", "/api/v1/economy/transfer", false)]
    [InlineData("POST", "/api/v1/social/messages", false)] [InlineData("POST", "/api/v1/economy/products", false)] [InlineData("POST", "/api/v1/inventoryx", false)]
    public void OnlyChangesToTheInventoryAreAnnounced(string method, string path, bool expected) => Assert.Equal(expected, InventorySync.Changes(method, path));

    private async Task<(Listener Mine, Listener Other, Listener Stranger)> Run(string method, string path, int status, string? source, bool signedIn = true)
    {
        var hub = new NotificationHub(TimeProvider.System, NullLogger<NotificationHub>.Instance); var mine = new Listener(); var other = new Listener(); var stranger = new Listener(); hub.TryAdd(_ana, Guid.NewGuid(), mine); hub.TryAdd(_ana, Guid.NewGuid(), other); hub.TryAdd(_bia, Guid.NewGuid(), stranger);
        var services = new ServiceCollection().AddSingleton(hub).BuildServiceProvider(); var app = new ApplicationBuilder(services); app.UseInventorySync(); app.Run(context => { context.Response.StatusCode = status; return Task.CompletedTask; });
        var context = new DefaultHttpContext { RequestServices = services }; context.Request.Method = method; context.Request.Path = path; if (source is not null) context.Request.Headers[InventorySync.Header] = source;
        if (signedIn) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, _ana.ToString())], "test"));
        await app.Build()(context); return (mine, other, stranger);
    }

    [Fact]
    public async Task EveryOpenSessionOfThePersonIsToldWhichSessionMadeTheChangeAndNobodyElseIs()
    {
        var (mine, other, stranger) = await Run("POST", "/api/v1/inventory/move", 200, "sessao-a");
        Assert.Single(mine.Sent); Assert.Single(other.Sent); Assert.Empty(stranger.Sent);
        using var json = JsonDocument.Parse(mine.Sent[0]); Assert.Equal("inventory_changed", json.RootElement.GetProperty("type").GetString()); Assert.Equal("sessao-a", json.RootElement.GetProperty("data").GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("POST", "/api/v1/inventory/move", 400)] [InlineData("POST", "/api/v1/inventory/move", 404)] [InlineData("POST", "/api/v1/inventory/move", 409)] [InlineData("POST", "/api/v1/inventory/move", 500)]
    [InlineData("GET", "/api/v1/inventory/items", 200)] [InlineData("POST", "/api/v1/social/messages", 200)]
    public async Task NothingIsAnnouncedWhenNothingChanged(string method, string path, int status)
    {
        var (mine, other, stranger) = await Run(method, path, status, "x"); Assert.Empty(mine.Sent); Assert.Empty(other.Sent); Assert.Empty(stranger.Sent);
    }

    [Fact]
    public async Task AnUnsignedRequestAndAnOverlongSessionNameAreHarmless()
    {
        var (mine, _, _) = await Run("POST", "/api/v1/inventory/move", 200, "x", signedIn: false); Assert.Empty(mine.Sent);
        var (loud, _, _) = await Run("POST", "/api/v1/inventory/move", 200, new string('s', 500)); using var json = JsonDocument.Parse(loud.Sent[0]); Assert.Equal(64, json.RootElement.GetProperty("data").GetProperty("source").GetString()!.Length);
        var (quiet, _, _) = await Run("POST", "/api/v1/inventory/move", 204, null); Assert.Single(quiet.Sent); // a change with no session name is still announced to everyone
    }
}
