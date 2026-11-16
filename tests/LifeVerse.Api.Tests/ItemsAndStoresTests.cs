using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Discovery;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LifeVerse.Api.Tests;

/// <summary>Items and shops as the search window shows them: filters by price, rights and creator, the item and shop details, who may see what, and opening shops.</summary>
public sealed class ItemsAndStoresTests : IDisposable
{
    private readonly LifeVerseDbContext _db = new(new DbContextOptionsBuilder<LifeVerseDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid _seller, _creator, _buyer, _other;

    public ItemsAndStoresTests() => (_seller, _creator, _buyer, _other) = (User("vendedora"), User("criador"), User("comprador"), User("outra"));

    public void Dispose() => _db.Dispose();

    private Guid User(string name)
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new User { Id = id, Username = name, NormalizedUsername = name.ToUpperInvariant(), Email = $"{name}@t.local", NormalizedEmail = $"{name}@T.LOCAL", PasswordHash = "x", CreatedAt = _now, UpdatedAt = _now });
        _db.Avatars.Add(new Avatar { Id = Guid.NewGuid(), UserId = id, DisplayName = name, BaseModel = "human_a", CreatedAt = _now, UpdatedAt = _now }); _db.SaveChanges(); return id;
    }

    private MarketplaceProduct Product(string name, long price, int rights = ItemRights.Standard, string category = "roupas", Guid? seller = null, Guid? creator = null, string status = "active")
    {
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = seller ?? _seller, CreatorUserId = creator ?? _creator, Name = name, Category = "models", CreatedAt = _now, UpdatedAt = _now };
        _db.Assets.Add(asset); _db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = Guid.NewGuid().ToString("N"), StorageKey = "k", MimeType = "model/gltf-binary", ByteSize = 2048 });
        var product = new MarketplaceProduct { Id = Guid.NewGuid(), SellerUserId = seller ?? _seller, AssetId = asset.Id, Name = name, Description = "Descrição de " + name, Category = category, PriceCoins = price, BuyerRights = rights, BuyerNextRights = ItemRights.Transfer, Status = status, Version = 1, CreatedAt = _now, UpdatedAt = _now };
        _db.MarketplaceProducts.Add(product); _db.SaveChanges(); return product;
    }

    private async Task<SearchPage> Find(SearchRequest request, Guid? who = null) { await SearchIndexer.RunAsync(_db, _now, default); return await GlobalSearch.SearchAsync(_db, who ?? _buyer, request, _now, default); }
    private async Task<JsonElement> Item(Guid viewer, Guid id) => JsonSerializer.SerializeToElement(await MarketDirectory.ItemAsync(_db, viewer, id, _now, default));
    private async Task<JsonElement> Store(Guid viewer, Guid id) => JsonSerializer.SerializeToElement(await MarketDirectory.StoreAsync(_db, viewer, id, _now, default));

    // ---- search filters -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ItemsCarryPriceRightsAndCreatorAndAreFilteredByThem()
    {
        Product("Camisa listrada", 0); Product("Vestido longo", 300, ItemRights.Copy | ItemRights.Modify); Product("Chapéu", 80, ItemRights.Transfer, creator: _other);
        var all = await Find(new SearchRequest("", ["product"])); Assert.Equal(3, all.Total);
        var dress = all.Items.Single(i => i.Title == "Vestido longo"); Assert.Equal(300, dress.PriceCoins); Assert.Equal(ItemRights.Copy | ItemRights.Modify, dress.Rights);
        Assert.Equal(["Vestido longo"], (await Find(new SearchRequest("", ["product"], MinPrice: 100))).Items.Select(i => i.Title));
        Assert.Equal(["Chapéu"], (await Find(new SearchRequest("", ["product"], MinPrice: 10, MaxPrice: 100))).Items.Select(i => i.Title));
        Assert.Equal(["Camisa listrada"], (await Find(new SearchRequest("", ["product"], Free: true))).Items.Select(i => i.Title)); Assert.Equal(2, (await Find(new SearchRequest("", ["product"], Free: false))).Total);
        Assert.Equal(["Camisa listrada", "Vestido longo"], (await Find(new SearchRequest("", ["product"], Rights: ItemRights.Copy | ItemRights.Modify))).Items.Select(i => i.Title).OrderBy(t => t));
        Assert.Equal(["Camisa listrada", "Chapéu"], (await Find(new SearchRequest("", ["product"], Rights: ItemRights.Transfer))).Items.Select(i => i.Title).OrderBy(t => t));
        Assert.Equal(["Camisa listrada", "Vestido longo"], (await Find(new SearchRequest("", ["product"], Rights: ItemRights.Copy))).Items.Select(i => i.Title).OrderBy(t => t));
        Assert.Equal(["Chapéu"], (await Find(new SearchRequest("", ["product"], Creator: "OUTRA"))).Items.Select(i => i.Title)); // not case sensitive
        Assert.Equal(3, (await Find(new SearchRequest("", ["product"], Owner: _seller))).Total); Assert.Equal(0, (await Find(new SearchRequest("", ["product"], Owner: _buyer))).Total);
    }

    [Fact]
    public async Task ItemsCanBeOrderedByPriceAndFoundByTheNameOfTheirCreator()
    {
        Product("Camisa", 50); Product("Casaco", 500); Product("Calça", 5);
        Assert.Equal(["Calça", "Camisa", "Casaco"], (await Find(new SearchRequest("", ["product"], Sort: "price_asc"))).Items.Select(i => i.Title));
        Assert.Equal(["Casaco", "Camisa", "Calça"], (await Find(new SearchRequest("", ["product"], Sort: "price_desc"))).Items.Select(i => i.Title));
        Assert.Equal(3, (await Find(new SearchRequest("criador", ["product"]))).Total); // the creator's name is searchable text
        Product("Gorro", 20, status: "paused"); Assert.Equal(3, (await Find(new SearchRequest("", ["product"]))).Total); // not for sale: not listed
    }

    // ---- the item ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheItemTellsCreatorSellerRightsAndWhatTheViewerCanDo()
    {
        var p = Product("Vestido longo", 300, ItemRights.Copy | ItemRights.Modify);
        var d = await Item(_buyer, p.Id);
        Assert.Equal("criador", d.GetProperty("creator").GetProperty("displayName").GetString()); Assert.Equal("vendedora", d.GetProperty("seller").GetProperty("name").GetString()); Assert.Equal("user", d.GetProperty("seller").GetProperty("kind").GetString());
        Assert.True(d.GetProperty("rights").GetProperty("copy").GetBoolean()); Assert.False(d.GetProperty("rights").GetProperty("transfer").GetBoolean()); Assert.True(d.GetProperty("nextRights").GetProperty("transfer").GetBoolean());
        Assert.True(d.GetProperty("canBuy").GetBoolean()); Assert.False(d.GetProperty("owned").GetBoolean()); Assert.Equal("model/gltf-binary", d.GetProperty("asset").GetProperty("mimeType").GetString()); Assert.Equal(300, d.GetProperty("priceCoins").GetInt64());
        Assert.False((await Item(_seller, p.Id)).GetProperty("canBuy").GetBoolean()); Assert.True((await Item(_seller, p.Id)).GetProperty("isOwn").GetBoolean()); // not your own
    }

    [Fact]
    public async Task AFreeItemIsTakenOnceAndPausedOrBlockedOnesAreNotFound()
    {
        var free = Product("Camisa", 0); Assert.True((await Item(_buyer, free.Id)).GetProperty("canBuy").GetBoolean());
        _db.MarketplaceOrders.Add(new MarketplaceOrder { Id = Guid.NewGuid(), ProductId = free.Id, BuyerUserId = _buyer, SellerUserId = _seller, PriceCoins = 0, Status = "completed", IdempotencyKey = "k123456789", CreatedAt = _now }); await _db.SaveChangesAsync();
        var d = await Item(_buyer, free.Id); Assert.True(d.GetProperty("owned").GetBoolean()); Assert.False(d.GetProperty("canBuy").GetBoolean());
        var paused = Product("Pausado", 10, status: "paused"); Assert.Null(await MarketDirectory.ItemAsync(_db, _buyer, paused.Id, _now, default)); Assert.NotNull(await MarketDirectory.ItemAsync(_db, _seller, paused.Id, _now, default));
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _other, BlockedUserId = _seller, CreatedAt = _now }); await _db.SaveChangesAsync();
        Assert.Null(await MarketDirectory.ItemAsync(_db, _other, free.Id, _now, default)); Assert.Null(await MarketDirectory.ItemAsync(_db, _buyer, Guid.NewGuid(), _now, default));
    }

    // ---- shops ---------------------------------------------------------------------------------------------------------------------

    private async Task<Guid> OpenShop(string name = "Moda da Vendedora", string category = "moda", Guid? owner = null)
    {
        var outcome = await MarketDirectory.OpenAsync(_db, owner ?? _seller, new StoreChange(name, "Roupas lindas.", category, "https://x.com/logo.png", "https://x.com/banner.png", true), _now, default); Assert.True(outcome.Ok, outcome.Error); return outcome.Id!.Value;
    }

    [Theory]
    [InlineData("ab", "moda", "bad_name")]
    [InlineData("Loja", "nada", "bad_category")]
    [InlineData("Loja", "moda", "bad_image")]
    public async Task OpeningAShopIsValidated(string name, string category, string error)
    {
        var logo = error == "bad_image" ? "http://x.com/a.png" : null;
        Assert.Equal(error, (await MarketDirectory.OpenAsync(_db, _seller, new StoreChange(name, "d", category, logo, null, true), _now, default)).Error);
    }

    [Fact]
    public async Task ShopsAreLimitedAndNamesAreNotRepeatedByTheSamePerson()
    {
        await OpenShop("Loja A"); Assert.Equal("name_taken", (await MarketDirectory.OpenAsync(_db, _seller, new StoreChange("loja a", "", "moda", null, null, true), _now, default)).Error);
        await OpenShop("Loja B"); await OpenShop("Loja C"); Assert.Equal("too_many_stores", (await MarketDirectory.OpenAsync(_db, _seller, new StoreChange("Loja D", "", "moda", null, null, true), _now, default)).Error);
        await OpenShop("Loja A", owner: _other); // another person may use the name
    }

    [Fact]
    public async Task AVirtualShopIsFoundOnlyWhileOpenAndSellingAndNeverHasAPlace()
    {
        var shop = await OpenShop();
        Assert.Equal(0, (await Find(new SearchRequest("vendedora", ["store"]))).Total); // nothing to sell yet
        Product("Vestido", 300); await SearchIndexer.RunAsync(_db, _now.AddMinutes(1), default); await SearchIndexer.RunAsync(_db, _now.AddMinutes(2), default);
        var page = await GlobalSearch.SearchAsync(_db, _buyer, new SearchRequest("moda", ["store"]), _now, default); var hit = Assert.Single(page.Items);
        Assert.Equal(shop, hit.Id); Assert.Null(hit.IslandId); Assert.Equal("moda", hit.Category); Assert.StartsWith("Loja virtual", hit.Subtitle); Assert.Equal("https://x.com/logo.png", hit.ThumbnailUrl);
        Assert.True((await MarketDirectory.ChangeAsync(_db, _seller, shop, new StoreChange(null, null, null, null, null, false), _now.AddMinutes(3), default)).Ok);
        await SearchIndexer.RunAsync(_db, _now.AddMinutes(3), default); Assert.Equal(0, (await GlobalSearch.SearchAsync(_db, _buyer, new SearchRequest("moda", ["store"]), _now, default)).Total);
    }

    [Fact]
    public async Task TheShopDetailHasTheOwnerTheCountAndTheBestSellersFirst()
    {
        var shop = await OpenShop(); var best = Product("Mais vendido", 100); var other = Product("Menos vendido", 90); Product("Pausado", 10, status: "paused");
        for (var i = 0; i < 3; i++) _db.MarketplaceOrders.Add(new MarketplaceOrder { Id = Guid.NewGuid(), ProductId = best.Id, BuyerUserId = _buyer, SellerUserId = _seller, PriceCoins = 100, Status = "completed", IdempotencyKey = "key-" + i + "-xxxxxxxx", CreatedAt = _now }); await _db.SaveChangesAsync();
        var d = await Store(_buyer, shop);
        Assert.Equal("virtual", d.GetProperty("kind").GetString()); Assert.Equal("Moda", d.GetProperty("categoryLabel").GetString()); Assert.Equal(2, d.GetProperty("productCount").GetInt32()); Assert.Equal("vendedora", d.GetProperty("owner").GetProperty("displayName").GetString());
        Assert.Equal(["Mais vendido", "Menos vendido"], d.GetProperty("featured").EnumerateArray().Select(f => f.GetProperty("name").GetString())); Assert.False(d.GetProperty("isOwner").GetBoolean()); Assert.True((await Store(_seller, shop)).GetProperty("isOwner").GetBoolean());
        Assert.Equal(other.Id.ToString(), d.GetProperty("featured")[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task AClosedBlockedOrDeletedShopIsNotFoundButTheOwnerSeesItsOwn()
    {
        var shop = await OpenShop(); Assert.NotNull(await MarketDirectory.StoreAsync(_db, _buyer, shop, _now, default));
        await MarketDirectory.ChangeAsync(_db, _seller, shop, new StoreChange(null, null, null, null, null, false), _now, default);
        Assert.Null(await MarketDirectory.StoreAsync(_db, _buyer, shop, _now, default)); Assert.NotNull(await MarketDirectory.StoreAsync(_db, _seller, shop, _now, default));
        await MarketDirectory.ChangeAsync(_db, _seller, shop, new StoreChange(null, null, null, null, null, true), _now, default);
        _db.UserBlocks.Add(new UserBlock { Id = Guid.NewGuid(), UserId = _buyer, BlockedUserId = _seller, CreatedAt = _now }); await _db.SaveChangesAsync(); Assert.Null(await MarketDirectory.StoreAsync(_db, _buyer, shop, _now, default));
        Assert.Equal("not_found", (await MarketDirectory.CloseForGoodAsync(_db, _other, shop, _now, default)).Error); Assert.True((await MarketDirectory.CloseForGoodAsync(_db, _seller, shop, _now, default)).Ok);
        Assert.Null(await MarketDirectory.StoreAsync(_db, _seller, shop, _now, default)); Assert.Empty(await MarketDirectory.MyStoresAsync(_db, _seller, default));
    }

    [Fact]
    public async Task OnlyTheOwnerChangesAShop()
    {
        var shop = await OpenShop();
        Assert.Equal("not_found", (await MarketDirectory.ChangeAsync(_db, _other, shop, new StoreChange("Roubada", null, null, null, null, null), _now, default)).Error);
        Assert.Equal("bad_name", (await MarketDirectory.ChangeAsync(_db, _seller, shop, new StoreChange("x", null, null, null, null, null), _now, default)).Error);
        Assert.True((await MarketDirectory.ChangeAsync(_db, _seller, shop, new StoreChange("Novo nome", "Nova descrição", "decoracao", null, null, null), _now, default)).Ok);
        Assert.Equal("Novo nome", (await Store(_buyer, shop)).GetProperty("name").GetString());
    }

[Fact]    public async Task VirtualAndPhysicalShopsCanBeToldApart()    {        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha Comercio", Slug = "com2", IsPublic = true, ShowInSearch = true, CreatedAt = _now, UpdatedAt = _now }; _db.Islands.Add(island);        var product = Product("Espada", 200); _db.WorldStores.Add(new WorldStore { Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = _seller, ProductId = product.Id, DisplayName = "Armas do Porto", CreatedAt = _now, UpdatedAt = _now }); await _db.SaveChangesAsync();        await OpenShop("Moda Virtual"); await SearchIndexer.RunAsync(_db, _now.AddMinutes(1), default); await SearchIndexer.RunAsync(_db, _now.AddMinutes(2), default);        Assert.Equal(["Armas do Porto"], (await GlobalSearch.SearchAsync(_db, _buyer, new SearchRequest("", ["store"], Located: true), _now, default)).Items.Select(i => i.Title));        Assert.Equal(["Moda Virtual"], (await GlobalSearch.SearchAsync(_db, _buyer, new SearchRequest("", ["store"], Located: false), _now, default)).Items.Select(i => i.Title));        Assert.Equal(2, (await GlobalSearch.SearchAsync(_db, _buyer, new SearchRequest("", ["store"]), _now, default)).Total);    }
    [Fact]
    public async Task AShopInTheWorldIsPhysicalAndHasItsPlace()
    {
        var island = new Island { Id = Guid.NewGuid(), OwnerUserId = _seller, Name = "Ilha Comércio", Slug = "com", IsPublic = true, ShowInSearch = true, CreatedAt = _now, UpdatedAt = _now }; _db.Islands.Add(island);
        var product = Product("Espada", 200); var store = new WorldStore { Id = Guid.NewGuid(), IslandId = island.Id, OwnerUserId = _seller, ProductId = product.Id, DisplayName = "Armas do Porto", CreatedAt = _now, UpdatedAt = _now }; _db.WorldStores.Add(store); await _db.SaveChangesAsync();
        var d = await Store(_buyer, store.Id); Assert.Equal("physical", d.GetProperty("kind").GetString()); Assert.Equal("Ilha Comércio", d.GetProperty("island").GetProperty("name").GetString()); Assert.Equal(1, d.GetProperty("productCount").GetInt32());
        Assert.Equal("Armas do Porto", (await Item(_buyer, product.Id)).GetProperty("store").GetProperty("Name").GetString() ?? (await Item(_buyer, product.Id)).GetProperty("store").GetProperty("name").GetString());
        island.IsPublic = false; await _db.SaveChangesAsync(); Assert.Null(await MarketDirectory.StoreAsync(_db, _buyer, store.Id, _now, default)); Assert.NotNull(await MarketDirectory.StoreAsync(_db, _seller, store.Id, _now, default));
    }
}
