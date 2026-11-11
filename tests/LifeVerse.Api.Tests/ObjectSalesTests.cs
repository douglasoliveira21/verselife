using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Platform;
using LifeVerse.Api.Modules.Worlds;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>What happens when you click an object (the click action) and buying an object that is for sale: the original, a copy or the contents, with real coins on the ledger.</summary>
public sealed class ObjectSalesTests : IDisposable
{
    private sealed class MemoryStore : IObjectStore
    {
        public Task PutAsync(string key, byte[] bytes, CancellationToken ct) => Task.CompletedTask;
        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult<Stream?>(null);
    }

    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly AssetStorageService _storage; private readonly Guid _instance = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid(), _seller = Guid.NewGuid(), _buyer = Guid.NewGuid();

    public ObjectSalesTests()
    {
        _storage = new AssetStorageService(new MemoryStore(), NullLogger<AssetStorageService>.Instance);
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _owner, Name = "Ilha", Slug = "ilha", IsPublic = true, Size = 400 };
        _db.Islands.Add(island); _db.ServerInstances.Add(new ServerInstance { Id = _instance, IslandId = island.Id, Hostname = "h", Status = "healthy" });
        _db.IslandPermissions.Add(new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = _seller, Permission = "build", PrincipalType = "user", GrantedByUserId = _owner });
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _buyer, AvailableCoins = 150, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = _seller, AvailableCoins = 10, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();

    private static BuildObjectRequest Cube(Guid? group = null, string content = "[]") => new("cube", 0, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{\"collision\":true}", group, 0, "{}", "Caixa", null, null, null, content);

    private async Task<(int Status, JsonElement Body)> Send(Guid actor, string operation, BuildObjectRequest? o = null, Guid? id = null, Guid[]? ids = null, string? properties = null)
    {
        JsonElement? props = properties is null ? null : JsonDocument.Parse(properties).RootElement.Clone();
        var result = await BuildOperations.ApplyAsync(_instance, new ServerBuildOperationRequest(actor, operation, id, o, 0, ids, null, 0, 0, 0, null, null, props), _db, _storage, TimeProvider.System, default);
        return ((result as IStatusCodeHttpResult)?.StatusCode ?? 200, JsonSerializer.SerializeToElement((result as IValueHttpResult)?.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string Code(JsonElement b) => b.GetProperty("code").GetString()!;
    private async Task<Guid> Make(Guid actor, BuildObjectRequest? o = null) { var (s, b) = await Send(actor, "create", o ?? Cube()); Assert.Equal(200, s); return b.GetProperty("objectId").GetGuid(); }
    private async Task<BuildObject> Obj(Guid id) => await _db.BuildObjects.AsNoTracking().SingleAsync(o => o.Id == id);
    private async Task<long> Coins(Guid user) => (await _db.Wallets.AsNoTracking().SingleAsync(w => w.UserId == user)).AvailableCoins;
    private Task<(int Status, JsonElement Body)> Sell(Guid actor, Guid id, string type, int price) => Send(actor, "properties", ids: [id], properties: $"{{\"saleType\":\"{type}\",\"salePrice\":{price}}}");
    private Task<(int Status, JsonElement Body)> Buy(Guid actor, Guid id, long price, string key) => Send(actor, "buy", id: id, properties: $"{{\"idempotencyKey\":\"{key}\",\"expectedPrice\":{price}}}");

    // ---- the click action ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task EveryClickActionIsKnownAndAnObjectStartsWithTouch()
    {
        var id = await Make(_seller); Assert.Equal("touch", (await Obj(id)).ClickAction);
        foreach (var action in new[] { "sit", "pay", "open", "zoom", "ignore", "none", "touch" })
        {
            var (status, body) = await Send(_seller, "properties", ids: [id], properties: $"{{\"clickAction\":\"{action}\"}}");
            Assert.Equal(200, status); Assert.Equal(action, (await Obj(id)).ClickAction);
            if (action != "touch") Assert.Equal(action, body.GetProperty("objects")[0].GetProperty("clickAction").GetString());
        }
        var bad = await Send(_seller, "properties", ids: [id], properties: "{\"clickAction\":\"dance\"}");
        Assert.Equal((400, "invalid_click_action"), (bad.Status, Code(bad.Body)));
        var other = await Send(_buyer, "properties", ids: [id], properties: "{\"clickAction\":\"sit\"}");
        Assert.Equal((403, "not_owner"), (other.Status, Code(other.Body)));
    }

    [Fact]
    public async Task BuyAsAClickNeedsASaleAndTakingTheObjectOffSaleTakesTheClickWithIt()
    {
        var id = await Make(_seller);
        var refused = await Send(_seller, "properties", ids: [id], properties: "{\"clickAction\":\"buy\"}");
        Assert.Equal((409, "buy_needs_sale"), (refused.Status, Code(refused.Body)));
        Assert.Equal(200, (await Sell(_seller, id, "original", 50)).Status);
        Assert.Equal(200, (await Send(_seller, "properties", ids: [id], properties: "{\"clickAction\":\"buy\"}")).Status); Assert.Equal("buy", (await Obj(id)).ClickAction);
        Assert.Equal(200, (await Send(_seller, "properties", ids: [id], properties: "{\"saleType\":\"none\"}")).Status);
        Assert.Equal("touch", (await Obj(id)).ClickAction);
    }

    // ---- buying the original -----------------------------------------------------------------------------------------

    [Fact]
    public async Task BuyingTheOriginalMovesTheCoinsAndTheOwnershipAndEndsTheSale()
    {
        var id = await Make(_seller); await Sell(_seller, id, "original", 100);
        await Send(_seller, "properties", ids: [id], properties: "{\"clickAction\":\"buy\",\"anyoneMove\":true}");
        var (status, body) = await Buy(_buyer, id, 100, "compra-0001");
        Assert.Equal(200, status); Assert.Equal("bought", body.GetProperty("operation").GetString());
        var after = await Obj(id);
        Assert.Equal((_buyer, _seller), (after.OwnerUserId, after.LastOwnerUserId)); Assert.Equal(_seller, after.CreatorUserId);
        Assert.Equal("none", BuildSharing.Sale(after.PermissionsJson).Type); Assert.False(BuildSharing.AnyoneMove(after.PermissionsJson));
        Assert.Equal("touch", after.ClickAction); Assert.Equal(4, after.Revision); // created, put on sale, shared, sold
        Assert.Equal(50, await Coins(_buyer)); Assert.Equal(10 + 95, await Coins(_seller)); // a 5% fee goes to the platform
        var entries = await _db.LedgerEntries.AsNoTracking().ToListAsync();
        Assert.Equal(entries.Sum(e => e.Debit), entries.Sum(e => e.Credit)); // the ledger balances
        Assert.Equal(1, await _db.BuildOperations.CountAsync(o => o.Operation == "transfer" && o.ObjectId == id));
    }

    [Fact]
    public async Task TheSameKeyIsTheSamePurchaseAndIsNeverChargedTwice()
    {
        var id = await Make(_seller); await Sell(_seller, id, "original", 100);
        Assert.Equal(200, (await Buy(_buyer, id, 100, "compra-0002")).Status);
        var again = await Buy(_buyer, id, 100, "compra-0002");
        Assert.Equal(200, again.Status); Assert.True(again.Body.GetProperty("replay").GetBoolean());
        Assert.Equal(50, await Coins(_buyer)); Assert.Equal(1, await _db.LedgerTransactions.CountAsync());
        var stolen = await Buy(_seller, id, 100, "compra-0002");
        Assert.Equal((409, "key_reused"), (stolen.Status, Code(stolen.Body)));
    }

    [Fact]
    public async Task APurchaseIsRefusedWhenThePriceChangedTheBalanceIsShortOrItIsNotForSaleAndNothingIsCharged()
    {
        var id = await Make(_seller);
        var none = await Buy(_buyer, id, 0, "compra-0003"); Assert.Equal((409, "not_for_sale"), (none.Status, Code(none.Body)));
        await Sell(_seller, id, "original", 100);
        var changed = await Buy(_buyer, id, 60, "compra-0004"); Assert.Equal((409, "price_changed"), (changed.Status, Code(changed.Body))); Assert.Equal(100, changed.Body.GetProperty("price").GetInt64());
        await Sell(_seller, id, "original", 5000);
        var poor = await Buy(_buyer, id, 5000, "compra-0005"); Assert.Equal((409, "insufficient_balance"), (poor.Status, Code(poor.Body)));
        var own = await Buy(_seller, id, 5000, "compra-0006"); Assert.Equal((409, "own_object"), (own.Status, Code(own.Body)));
        var short_ = await Buy(_buyer, id, 5000, "x"); Assert.Equal(400, short_.Status);
        Assert.Equal(150, await Coins(_buyer)); Assert.Equal(_seller, (await Obj(id)).OwnerUserId); Assert.Equal(0, await _db.LedgerTransactions.CountAsync());
        _db.Wallets.Single(w => w.UserId == _buyer).IsBlocked = true; await _db.SaveChangesAsync(); await Sell(_seller, id, "original", 10);
        var blocked = await Buy(_buyer, id, 10, "compra-0007"); Assert.Equal((403, "wallet_blocked"), (blocked.Status, Code(blocked.Body)));
    }

    [Fact]
    public async Task TheBuyerGetsTheRightsMarkedForTheNextOwnerAndNothingMore()
    {
        var id = await Make(_seller);
        await Send(_seller, "properties", ids: [id], properties: $"{{\"nextOwnerRights\":{ItemRights.Modify}}}");
        await Sell(_seller, id, "original", 10);
        Assert.Equal(200, (await Buy(_buyer, id, 10, "compra-0008")).Status);
        var after = await Obj(id);
        Assert.True(ItemRights.Has(after.OwnerRights, Right.Modify)); Assert.False(ItemRights.IsCopyable(after.OwnerRights)); Assert.False(ItemRights.Has(after.OwnerRights, Right.Transfer));
        Assert.Equal(after.OwnerRights, ItemRights.Clamp(after.NextOwnerRights, after.OwnerRights)); // what it passes on again is inside what the buyer holds
    }

    [Fact]
    public async Task ALinkedSetChangesOwnerAsOnePiece()
    {
        var group = Guid.NewGuid(); var a = await Make(_seller, Cube(group)); var b = await Make(_seller, Cube(group));
        await Sell(_seller, a, "original", 20);
        Assert.Equal(200, (await Buy(_buyer, a, 20, "compra-0009")).Status);
        Assert.Equal(_buyer, (await Obj(a)).OwnerUserId); Assert.Equal(_buyer, (await Obj(b)).OwnerUserId);
    }

    [Fact]
    public async Task TheScriptsOfASoldObjectBelongToTheNewOwner()
    {
        var id = await Make(_seller); _db.ObjectScripts.Add(new ObjectScript { Id = Guid.NewGuid(), ObjectId = id, OwnerUserId = _seller, Name = "porta", Source = "function on_touch() end", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }); await _db.SaveChangesAsync();
        await Sell(_seller, id, "original", 10); Assert.Equal(200, (await Buy(_buyer, id, 10, "compra-0010")).Status);
        Assert.Equal(_buyer, (await _db.ObjectScripts.AsNoTracking().SingleAsync(s => s.ObjectId == id)).OwnerUserId);
    }

    // ---- a copy and the contents ---------------------------------------------------------------------------------------

    [Fact]
    public async Task BuyingACopyGivesAConstructionInTheInventoryAndLeavesTheObjectWithTheSeller()
    {
        var id = await Make(_seller); Assert.Equal(200, (await Sell(_seller, id, "copy", 30)).Status);
        var (status, body) = await Buy(_buyer, id, 30, "compra-0011");
        Assert.Equal(200, status); Assert.Equal("copy", body.GetProperty("saleType").GetString());
        Assert.Equal(_seller, (await Obj(id)).OwnerUserId); Assert.Equal("copy", BuildSharing.Sale((await Obj(id)).PermissionsJson).Type); // it can be sold again
        var item = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.OwnerUserId == _buyer); Assert.Equal("Caixa", item.DisplayName);
        Assert.Equal(120, await Coins(_buyer));
    }

    [Fact]
    public async Task ACopyCannotBeSoldWithoutTheRightToCopyAndTheEditorSaysSo()
    {
        var id = await Make(_seller);
        await Send(_seller, "properties", ids: [id], properties: $"{{\"ownerRights\":{ItemRights.Modify | ItemRights.Transfer}}}");
        var refused = await Sell(_seller, id, "copy", 30);
        Assert.Equal((403, "no_copy"), (refused.Status, Code(refused.Body)));
    }

    [Fact]
    public async Task BuyingTheContentsGivesCopiesOfWhatTheObjectCarries()
    {
        var sound = Guid.NewGuid(); var image = Guid.NewGuid();
        var content = $"[{{\"assetId\":\"{sound}\",\"name\":\"Campainha\",\"category\":\"sounds\"}},{{\"assetId\":\"{image}\",\"name\":\"Quadro\",\"category\":\"textures\"}}]";
        var id = await Make(_seller); _db.BuildObjects.Single(o => o.Id == id).ContentJson = content; await _db.SaveChangesAsync(); Assert.Equal(200, (await Sell(_seller, id, "contents", 40)).Status);
        var (status, body) = await Buy(_buyer, id, 40, "compra-0012");
        Assert.Equal(200, status); Assert.Equal(2, body.GetProperty("delivered").GetArrayLength());
        var items = await _db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == _buyer).Select(i => i.DisplayName).ToListAsync();
        Assert.Equal(["Campainha", "Quadro"], items.Order().ToArray()); Assert.Equal(_seller, (await Obj(id)).OwnerUserId);
        var empty = await Make(_seller); await Sell(_seller, empty, "contents", 5);
        var nothing = await Buy(_buyer, empty, 5, "compra-0013"); Assert.Equal((409, "nothing_to_sell"), (nothing.Status, Code(nothing.Body)));
        Assert.Equal(110, await Coins(_buyer));
    }
}
