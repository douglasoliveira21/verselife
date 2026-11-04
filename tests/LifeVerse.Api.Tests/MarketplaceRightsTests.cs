using System.Security.Claims;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Products with rights: what the buyer gets, free products, gifts, resale of an item, and that nothing is delivered twice or without being paid.</summary>
public sealed class MarketplaceRightsTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options);
    private readonly FakeTime _clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly User _maker, _fan, _other, _third;
    private int _key;

    public MarketplaceRightsTests() { _maker = NewUser("criador", 0); _fan = NewUser("fa", 10_000); _other = NewUser("outro", 10_000); _third = NewUser("terceiro", 10_000); }
    public void Dispose() => _db.Dispose();
    private string Key() => "compra-" + (++_key).ToString("D6");

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider { public DateTimeOffset Now = start; public override DateTimeOffset GetUtcNow() => Now; }

    private User NewUser(string username, long coins)
    {
        var now = _clock?.Now ?? DateTimeOffset.UtcNow; var u = new User { Id = Guid.NewGuid(), Username = username, NormalizedUsername = username.ToUpperInvariant(), Email = $"{username}@t.local", NormalizedEmail = $"{username}@T.LOCAL", PasswordHash = "x", CreatedAt = now, UpdatedAt = now };
        _db.Users.Add(u); _db.Wallets.Add(new Wallet { Id = Guid.NewGuid(), UserId = u.Id, AvailableCoins = coins, CreatedAt = now, UpdatedAt = now }); _db.SaveChanges(); return u;
    }

    private static DefaultHttpContext As(User user) => new() { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.Id.ToString())], "test")) };
    private static int Status(IResult r) => r is IStatusCodeHttpResult { StatusCode: { } code } ? code : r is ForbidHttpResult ? 403 : 0;
    private static T? Value<T>(IResult r) where T : class => (r as IValueHttpResult)?.Value as T;
    private async Task<long> Coins(User u) => (await _db.Wallets.AsNoTracking().SingleAsync(w => w.UserId == u.Id)).AvailableCoins;

    private Asset NewAsset(User owner, string category = "mesh")
    {
        var a = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner.Id, CreatorUserId = owner.Id, Name = "Obra", Category = category, CreatedAt = _clock.Now, UpdatedAt = _clock.Now }; _db.Assets.Add(a); _db.SaveChanges(); return a;
    }

    private InventoryItem NewItem(User owner, int rights, int next, Asset? asset = null)
    {
        asset ??= NewAsset(_maker); var i = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner.Id, AssetId = asset.Id, DisplayName = "Item", OwnerRights = rights, NextOwnerRights = next, CreatedAt = _clock.Now, UpdatedAt = _clock.Now };
        _db.InventoryItems.Add(i); _db.SaveChanges(); return i;
    }

    private PublishProductRequest Req(Guid asset, long price = 500, int? buyer = null, int? next = null, Guid? item = null, bool? gift = null, string? json = null) => new(asset, "Produto", "Descrição", "arte", price, null, json, buyer, next, item, gift);
    private async Task<(int Status, MarketplaceProduct? Product)> Publish(User who, PublishProductRequest request) { var r = await EconomyEndpoints.PublishAsync(request, As(who), _db, _clock, default); return (Status(r), Value<MarketplaceProduct>(r)); }
    private async Task<(int Status, MarketplaceOrder? Order)> Buy(User who, Guid product, Guid? forWhom = null, string? key = null)
    {
        var r = await EconomyEndpoints.PurchaseAsync(product, new PurchaseRequest(key ?? Key(), forWhom), As(who), _db, _clock, default); return (Status(r), Value<MarketplaceOrder>(r));
    }
    private async Task<InventoryItem> Delivered(MarketplaceOrder order) => await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == order.InventoryItemId);

    // ---- a product from its creator -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheBuyerGetsTheRightsTheProductPromises()
    {
        var asset = NewAsset(_maker); var (status, product) = await Publish(_maker, Req(asset.Id, 1000, buyer: ItemRights.Copy | ItemRights.Modify | ItemRights.Transfer, next: ItemRights.Modify | ItemRights.Transfer)); Assert.Equal(201, status);
        Assert.Equal((7, 6), (product!.BuyerRights, product.BuyerNextRights)); Assert.Contains("\"copy\":true", product.PermissionsJson);
        var (code, order) = await Buy(_fan, product.Id); Assert.Equal(200, code); var item = await Delivered(order!);
        Assert.Equal((_fan.Id, 7, 6, 0, "active"), (item.OwnerUserId, item.OwnerRights, item.NextOwnerRights, item.GroupRights, item.Status));
        var line = await _db.ItemOwnerRecords.SingleAsync(r => r.ItemId == item.Id); Assert.Equal(("purchase", _maker.Id, _fan.Id), (line.Reason, line.FromUserId, line.UserId));
        var audit = await _db.ItemAudit.SingleAsync(a => a.Action == "purchase"); Assert.Equal((item.Id, _maker.Id, _fan.Id), (audit.ItemId, audit.FromUserId, audit.ToUserId)); Assert.Contains("1000 coins", audit.Detail);
        Assert.Equal((9000, 950), (await Coins(_fan), await Coins(_maker))); // 5% fee
    }

    [Fact]
    public async Task WhatIsPassedOnMustFitInsideWhatTheBuyerGetsAndBitsMustBeKnown()
    {
        var asset = NewAsset(_maker);
        Assert.Equal(400, (await Publish(_maker, Req(asset.Id, buyer: ItemRights.Copy, next: ItemRights.All))).Status); Assert.Equal(400, (await Publish(_maker, Req(asset.Id, buyer: 1 << 9))).Status); Assert.Equal(400, (await Publish(_maker, Req(asset.Id, buyer: 3, next: 1 << 9))).Status);
        Assert.Equal(400, (await Publish(_maker, Req(Guid.NewGuid()))).Status); Assert.Equal(400, (await Publish(_other, Req(asset.Id))).Status); Assert.Equal(400, (await Publish(_maker, Req(asset.Id, price: -1))).Status); // not the creator's asset; negative price
        Assert.Empty(_db.MarketplaceProducts);
    }

    [Fact]
    public async Task WithoutSayingTheBuyerGetsTheStandardRightsAndOldPermissionTextIsStillRead()
    {
        var asset = NewAsset(_maker); var plain = (await Publish(_maker, Req(asset.Id))).Product!; Assert.Equal((7, 7), (plain.BuyerRights, plain.BuyerNextRights));
        var old = (await Publish(_maker, Req(asset.Id, json: "{\"copy\":false,\"modify\":true,\"transfer\":true}"))).Product!; Assert.Equal((6, 6), (old.BuyerRights, old.BuyerNextRights));
        var closed = (await Publish(_maker, Req(asset.Id, buyer: 0, next: 0))).Product!; Assert.Equal((0, 0), (closed.BuyerRights, closed.BuyerNextRights)); // nothing at all is a choice too
        var item = await Delivered((await Buy(_fan, closed.Id)).Order!); Assert.Equal((0, 0), (item.OwnerRights, item.NextOwnerRights));
    }

    [Fact]
    public async Task TheStoreListShowsWhatTheBuyerWouldGet()
    {
        var asset = NewAsset(_maker); await Publish(_maker, Req(asset.Id, buyer: ItemRights.Copy | ItemRights.Transfer, next: ItemRights.Transfer));
        var result = await EconomyEndpoints.ProductsAsync(null, null, 1, 24, _db, default); var json = System.Text.Json.JsonSerializer.Serialize(Value<object>(result), new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Contains("\"rights\":\"C-T-\"", json); Assert.Contains("Copiar · Transferir", json); Assert.Contains("\"isResale\":false", json); Assert.Contains("\"nextRightsText\":\"Transferir\"", json);
    }

    [Fact]
    public async Task ChangingTheRightsOfAProductAffectsOnlyThoseWhoBuyAfterwardsAndIsAudited()
    {
        var asset = NewAsset(_maker); var product = (await Publish(_maker, Req(asset.Id, buyer: 15, next: 15))).Product!; var first = await Delivered((await Buy(_fan, product.Id)).Order!);
        var update = await EconomyEndpoints.UpdateProductAsync(product.Id, Req(asset.Id, 500, buyer: ItemRights.Modify, next: 0), As(_maker), _db, _clock, default); Assert.Equal(200, Status(update));
        var second = await Delivered((await Buy(_other, product.Id)).Order!);
        Assert.Equal((15, 15), ((await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == first.Id)).OwnerRights, first.NextOwnerRights)); Assert.Equal((2, 0), (second.OwnerRights, second.NextOwnerRights));
        Assert.Contains(_db.ItemAudit, a => a.Action == "product_rights" && a.Detail.Contains("CMTR/CMTR -> -M--/----"));
        Assert.Equal(400, Status(await EconomyEndpoints.UpdateProductAsync(product.Id, Req(asset.Id, 500, buyer: 1, next: 7), As(_maker), _db, _clock, default))); // the rule holds on update too
        Assert.Equal(404, Status(await EconomyEndpoints.UpdateProductAsync(product.Id, Req(asset.Id, 500), As(_fan), _db, _clock, default))); // only the seller
    }

    // ---- free products and gifts ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AFreeProductIsTakenOncePerPersonAndMovesNoCoins()
    {
        var asset = NewAsset(_maker); var free = (await Publish(_maker, Req(asset.Id, 0, buyer: 3, next: 1))).Product!;
        var (code, order) = await Buy(_fan, free.Id); Assert.Equal(200, code); Assert.Equal(0, order!.PriceCoins); Assert.Equal((10_000, 0), (await Coins(_fan), await Coins(_maker)));
        Assert.Equal(409, (await Buy(_fan, free.Id)).Status); Assert.Equal(1, await _db.InventoryItems.CountAsync(i => i.OwnerUserId == _fan.Id && i.AssetId == asset.Id)); // not a way to fill an inventory
        Assert.Equal(200, (await Buy(_other, free.Id)).Status);
    }

    [Fact]
    public async Task APaidProductCanBeBoughtAgainButNeverTwiceWithTheSameKey()
    {
        var asset = NewAsset(_maker); var product = (await Publish(_maker, Req(asset.Id, 100))).Product!; var key = Key();
        var a = await Buy(_fan, product.Id, key: key); var again = await Buy(_fan, product.Id, key: key); Assert.Equal(200, again.Status); Assert.Equal(a.Order!.Id, again.Order!.Id); Assert.Equal(1, await _db.InventoryItems.CountAsync(i => i.AssetId == asset.Id));
        Assert.Equal(9900, await Coins(_fan)); Assert.Equal(409, (await Buy(_other, product.Id, key: key)).Status); // somebody else's key is not an answer
        Assert.Equal(200, (await Buy(_fan, product.Id)).Status); Assert.Equal(2, await _db.InventoryItems.CountAsync(i => i.OwnerUserId == _fan.Id));
    }

    [Fact]
    public async Task ABuyerCanBuyAGiftForSomeoneElseWhenTheProductAllowsIt()
    {
        var asset = NewAsset(_maker); var product = (await Publish(_maker, Req(asset.Id, 200, buyer: 7, next: 7))).Product!;
        var (code, order) = await Buy(_fan, product.Id, _other.Id); Assert.Equal(200, code); var item = await Delivered(order!);
        Assert.Equal((_other.Id, _other.Id), (item.OwnerUserId, order!.RecipientUserId)); Assert.Equal(9800, await Coins(_fan)); Assert.Equal(10_000, await Coins(_other)); Assert.Equal(0, await _db.InventoryItems.CountAsync(i => i.OwnerUserId == _fan.Id));
        Assert.Equal("gift", (await _db.ItemOwnerRecords.SingleAsync(r => r.ItemId == item.Id)).Reason); Assert.Equal(_other.Id, (await _db.ItemAudit.SingleAsync(a => a.Action == "purchase")).ToUserId);
    }

    [Fact]
    public async Task GiftsAreRefusedWhenNotAllowedOrWhenTheRecipientIsNotReachable()
    {
        var asset = NewAsset(_maker); var closed = (await Publish(_maker, Req(asset.Id, 200, gift: false))).Product!; var open = (await Publish(_maker, Req(asset.Id, 200))).Product!;
        Assert.Equal(403, (await Buy(_fan, closed.Id, _other.Id)).Status); Assert.Equal(404, (await Buy(_fan, open.Id, Guid.NewGuid())).Status); Assert.Equal(404, (await Buy(_fan, open.Id, _maker.Id)).Status); // not to the seller
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _other.Id, BlockedUserId = _fan.Id, CreatedAt = _clock.Now }); await _db.SaveChangesAsync(); Assert.Equal(404, (await Buy(_fan, open.Id, _other.Id)).Status);
        _other.DeletedAt = _clock.Now; await _db.SaveChangesAsync(); Assert.Equal(404, (await Buy(_fan, open.Id, _other.Id)).Status);
        Assert.Equal((10_000, 0), (await Coins(_fan), await Coins(_maker))); Assert.Empty(_db.MarketplaceOrders);
    }

    [Fact]
    public async Task NothingIsDeliveredWithoutPayment()
    {
        var asset = NewAsset(_maker); var product = (await Publish(_maker, Req(asset.Id, 50_000))).Product!;
        var (code, _) = await Buy(_fan, product.Id); Assert.Equal(409, code); Assert.Empty(_db.MarketplaceOrders); Assert.Equal(0, await _db.InventoryItems.CountAsync(i => i.AssetId == asset.Id)); Assert.Equal(10_000, await Coins(_fan));
        Assert.Equal(400, (await Buy(_maker, product.Id)).Status); // and the seller does not buy their own
    }

    // ---- reselling an item ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyAnItemWithTheResellRightCanBeListedAndOnlyByItsHolder()
    {
        var noResell = NewItem(_fan, ItemRights.Standard, ItemRights.Standard); var resell = NewItem(_fan, ItemRights.All, 6);
        Assert.Equal(403, (await Publish(_fan, Req(noResell.AssetId, 300, item: noResell.Id))).Status); Assert.Contains(_db.ItemAudit, a => a.Action == "denied" && a.Detail.Contains("cannot be resold"));
        Assert.Equal(404, (await Publish(_other, Req(resell.AssetId, 300, item: resell.Id))).Status); Assert.Equal(404, (await Publish(_fan, Req(Guid.NewGuid(), 300, item: Guid.NewGuid()))).Status);
        Assert.Empty(_db.MarketplaceProducts); Assert.Equal("active", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == resell.Id)).Status);
    }

    [Fact]
    public async Task ReSellingAnItemThatCannotBeCopiedLocksItAndMovesItToTheBuyer()
    {
        var item = NewItem(_fan, ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, ItemRights.Modify | ItemRights.Transfer);
        var (status, product) = await Publish(_fan, Req(item.AssetId, 1000, item: item.Id)); Assert.Equal(201, status); Assert.Equal((item.Id, item.AssetId, 6, 6), (product!.SourceItemId, product.AssetId, product.BuyerRights, product.BuyerNextRights));
        Assert.Equal("listed", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).Status);
        Assert.Equal("item_busy", (await ItemTransfers.OfferAsync(_db, _fan.Id, item.Id, _other.Id, "gift", null, false, Key(), _clock.Now, default)).Error); // while it is for sale it cannot also be given away
        Assert.Equal("item_busy", (await ItemRightsService.RequireAsync(_db, _fan.Id, item.Id, Right.Modify, "edit", _clock.Now, default)).Error);
        var (code, order) = await Buy(_other, product.Id); Assert.Equal(200, code); var moved = await Delivered(order!);
        Assert.Equal((item.Id, _other.Id, 6, 6, "active"), (moved.Id, moved.OwnerUserId, moved.OwnerRights, moved.NextOwnerRights, moved.Status)); Assert.Equal(1, await _db.InventoryItems.CountAsync(i => i.AssetId == item.AssetId));
        Assert.Equal(("sold", 10_950), ((await _db.MarketplaceProducts.AsNoTracking().SingleAsync()).Status, await Coins(_fan))); Assert.Equal(9000, await Coins(_other));
        Assert.Equal("resale", (await _db.ItemOwnerRecords.SingleAsync(r => r.ItemId == item.Id)).Reason); Assert.Equal(404, (await Buy(_maker, product.Id)).Status); // the last one is gone
    }

    [Fact]
    public async Task ReSellingAnItemThatCanBeCopiedSellsCopiesAndKeepsTheOriginal()
    {
        var item = NewItem(_fan, ItemRights.All, ItemRights.Copy | ItemRights.Modify | ItemRights.Transfer); var product = (await Publish(_fan, Req(item.AssetId, 400, item: item.Id))).Product!;
        Assert.Equal("active", (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).Status); // not locked: copies are sold
        var first = await Delivered((await Buy(_other, product.Id)).Order!); var second = await Delivered((await Buy(_third, product.Id)).Order!);
        Assert.NotEqual(first.Id, second.Id); Assert.Equal((item.Id, 7), (first.CopiedFromItemId, first.OwnerRights)); Assert.Equal(_fan.Id, (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id)).OwnerUserId); Assert.Equal(3, await _db.InventoryItems.CountAsync(i => i.AssetId == item.AssetId));
        Assert.Equal("active", (await _db.MarketplaceProducts.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(409, (await Publish(_fan, Req(item.AssetId, 400, item: item.Id))).Status);
    }

    [Fact]
    public async Task AResaleStopsWhenTheSellerNoLongerHoldsTheItemAndNoCoinsMove()
    {
        var item = NewItem(_fan, ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, 6); var product = (await Publish(_fan, Req(item.AssetId, 700, item: item.Id))).Product!;
        var tracked = await _db.InventoryItems.SingleAsync(i => i.Id == item.Id); tracked.DeletedAt = _clock.Now; await _db.SaveChangesAsync(); // removed by code that does not know about the sale
        var (code, _) = await Buy(_other, product.Id); Assert.Equal(409, code); Assert.Empty(_db.MarketplaceOrders); Assert.Equal((10_000, 10_000), (await Coins(_other), await Coins(_fan)));
        var item2 = NewItem(_fan, ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, 6); var p2 = (await Publish(_fan, Req(item2.AssetId, 700, item: item2.Id))).Product!;
        var t2 = await _db.InventoryItems.SingleAsync(i => i.Id == item2.Id); t2.OwnerUserId = _maker.Id; await _db.SaveChangesAsync(); // it changed hands some other way
        Assert.Equal(409, (await Buy(_other, p2.Id)).Status); Assert.Equal(_maker.Id, (await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item2.Id)).OwnerUserId); Assert.Empty(_db.MarketplaceOrders);
    }

    [Fact]
    public async Task TakingAResaleOffSaleReleasesTheItem()
    {
        var item = NewItem(_fan, ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, 6); var product = (await Publish(_fan, Req(item.AssetId, 700, item: item.Id))).Product!;
        Assert.Equal(404, Status(await EconomyEndpoints.DeactivateAsync(product.Id, As(_other), _db, _clock, default)));
        Assert.Equal(204, Status(await EconomyEndpoints.DeactivateAsync(product.Id, As(_fan), _db, _clock, default))); var back = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id); Assert.Equal("active", back.Status);
        Assert.Equal(404, (await Buy(_other, product.Id)).Status); Assert.True((await ItemRightsService.RequireAsync(_db, _fan.Id, item.Id, Right.Modify, "edit", _clock.Now, default)).Ok);
        var again = await Publish(_fan, Req(item.AssetId, 800, item: item.Id)); Assert.Equal(201, again.Status); // and it can be put up again
    }

    [Fact]
    public async Task TheRightsOfAResaleAreThoseOfItsItemAndCannotBeEdited()
    {
        var item = NewItem(_fan, ItemRights.Modify | ItemRights.Transfer | ItemRights.Resell, ItemRights.Transfer); var product = (await Publish(_fan, Req(item.AssetId, 700, item: item.Id))).Product!;
        var update = await EconomyEndpoints.UpdateProductAsync(product.Id, Req(item.AssetId, 650, buyer: 15, next: 15, item: null), As(_fan), _db, _clock, default); Assert.Equal(200, Status(update));
        var stored = await _db.MarketplaceProducts.AsNoTracking().SingleAsync(); Assert.Equal((650, 4, 4), (stored.PriceCoins, stored.BuyerRights, stored.BuyerNextRights)); // the price changed; the rights did not
    }

    [Fact]
    public async Task AChainOfResalesAlwaysHandsOverTheNextOwnerRights()
    {
        var item = NewItem(_fan, 14, 14);
        // the first buyer can resell it (the creator allowed Resell for the next owner), the second cannot (the first removed it)
        var p1 = (await Publish(_fan, Req(item.AssetId, 100, item: item.Id))).Product!; Assert.Equal(200, (await Buy(_other, p1.Id)).Status); var held = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal((_other.Id, 14, 14), (held.OwnerUserId, held.OwnerRights, held.NextOwnerRights));
        var down = await ItemRightsService.SetPermissionsAsync(_db, _other.Id, item.Id, new ItemPermissions(14, 0, ItemRights.Modify | ItemRights.Transfer), null, _clock.Now, default); Assert.True(down.Ok);
        var p2 = (await Publish(_other, Req(item.AssetId, 200, item: item.Id))).Product!; Assert.Equal(200, (await Buy(_third, p2.Id)).Status); var last = await _db.InventoryItems.AsNoTracking().SingleAsync(i => i.Id == item.Id);
        Assert.Equal((_third.Id, 6), (last.OwnerUserId, last.OwnerRights)); Assert.Equal(403, (await Publish(_third, Req(item.AssetId, 300, item: item.Id))).Status); // the last holder cannot sell it again
        Assert.Equal(2, await _db.ItemOwnerRecords.CountAsync(r => r.ItemId == item.Id && r.Reason == "resale"));
    }
}
