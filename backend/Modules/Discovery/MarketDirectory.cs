using LifeVerse.Api.Data;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Discovery;

public static class StoreRules
{
    public static readonly (string Key, string Label)[] Categories = [("moda", "Moda"), ("avatares", "Avatares"), ("construcao", "Construção"), ("decoracao", "Decoração"), ("animacoes", "Animações"), ("veiculos", "Veículos"), ("tecnologia", "Tecnologia"), ("servicos", "Serviços"), ("outros", "Outros")];
    public const int MinName = 3, MaxName = 60, MaxDescription = 600, MaxPerOwner = 3, Featured = 6;
    public static string LabelOf(string key) => Categories.FirstOrDefault(c => c.Key == key).Label ?? "Outros";
}

public sealed record StoreChange(string? Name, string? Description, string? Category, string? LogoUrl, string? BannerUrl, bool? IsOpen);
public sealed record StoreOutcome(bool Ok, string? Error = null, Guid? Id = null);

/// <summary>
/// What the search window shows when an item or a shop is chosen: the item with its creator, seller, price, what the buyer may do with it (copy, modify, transfer), whether the viewer already has it and may buy it;
/// the shop with its owner, kind (virtual or in the world, with the place), how many things it sells and a few to show. And the shops of a person: opening, changing, closing. Buying itself is the economy's purchase
/// (idempotent, checked on the server); this only says whether it makes sense to offer it.
/// </summary>
public static class MarketDirectory
{
    private static async Task<bool> BlockedAsync(LifeVerseDbContext db, Guid a, Guid b, CancellationToken ct) =>
        a != b && await db.UserBlocks.AsNoTracking().AnyAsync(x => x.UserId == a && x.BlockedUserId == b || x.UserId == b && x.BlockedUserId == a, ct);

    private static object Flags(int mask) => new { copy = ItemRights.Has(mask, Right.Copy), modify = ItemRights.Has(mask, Right.Modify), transfer = ItemRights.Has(mask, Right.Transfer), resell = ItemRights.Has(mask, Right.Resell) };

    private static async Task<object?> PersonAsync(LifeVerseDbContext db, Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => u.Id == id && u.DeletedAt == null).Select(u => (object)new { id = u.Id, username = u.Username, displayName = u.Avatar != null ? u.Avatar.DisplayName : u.Username }).SingleOrDefaultAsync(ct);

    public static async Task<object?> ItemAsync(LifeVerseDbContext db, Guid viewer, Guid productId, DateTimeOffset now, CancellationToken ct)
    {
        var p = await db.MarketplaceProducts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == productId, ct); if (p is null) return null;
        var own = p.SellerUserId == viewer; if (p.Status != "active" && !own) return null; if (await BlockedAsync(db, viewer, p.SellerUserId, ct)) return null;
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == p.AssetId, ct);
        var version = asset is null ? null : await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion).Select(v => new { v.MimeType, v.ByteSize }).SingleOrDefaultAsync(ct);
        var creator = asset is null ? null : await PersonAsync(db, asset.CreatorUserId, ct);
        object? seller;
        if (p.GroupId is { } g) { var group = await db.SocialGroups.AsNoTracking().Where(x => x.Id == g && x.DeletedAt == null).Select(x => new { x.Id, x.Name }).SingleOrDefaultAsync(ct); seller = group is null ? null : new { kind = "group", id = group.Id, name = group.Name }; }
        else { var person = await db.Users.AsNoTracking().Include(u => u.Avatar).Where(u => u.Id == p.SellerUserId).Select(u => new { u.Id, Name = u.Avatar != null ? u.Avatar.DisplayName : u.Username }).SingleOrDefaultAsync(ct); seller = person is null ? null : new { kind = "user", id = person.Id, name = person.Name }; }
        var owned = await db.MarketplaceOrders.AsNoTracking().AnyAsync(o => o.ProductId == p.Id && o.Status == "completed" && (o.RecipientUserId ?? o.BuyerUserId) == viewer, ct);
        var active = p.Status == "active";
        // where to see more of the seller: a shop in the world that sells this very product, else an open virtual shop of the seller
        var world = await (from s in db.WorldStores.AsNoTracking() join i in db.Islands.AsNoTracking() on s.IslandId equals i.Id where s.ProductId == p.Id && s.DeletedAt == null && i.IsPublic && i.DeletedAt == null select new { s.Id, Name = s.DisplayName, Kind = "physical" }).FirstOrDefaultAsync(ct);
        var shop = world is not null ? (object)world : await db.Storefronts.AsNoTracking().Where(s => s.OwnerUserId == p.SellerUserId && s.GroupId == p.GroupId && s.DeletedAt == null && s.IsOpen).Select(s => (object)new { s.Id, Name = s.Name, Kind = "virtual" }).FirstOrDefaultAsync(ct);
        return new
        {
            id = p.Id, name = p.Name, description = p.Description, category = p.Category, priceCoins = p.PriceCoins, imageUrl = string.IsNullOrEmpty(p.ImageUrl) ? null : p.ImageUrl, status = p.Status,
            creator, seller, rights = Flags(p.BuyerRights), nextRights = Flags(p.BuyerNextRights), allowGift = p.AllowGift, resale = p.SourceItemId is not null,
            asset = asset is null ? null : new { category = asset.Category, mimeType = version?.MimeType, byteSize = version?.ByteSize, version = asset.CurrentVersion, updatedAt = asset.UpdatedAt },
            owned, isOwn = own, available = active, canBuy = active && !own && !(p.PriceCoins == 0 && p.SourceItemId is null && owned), store = shop, updatedAt = p.UpdatedAt
        };
    }

    // ---- shops ---------------------------------------------------------------------------------------------------------------------

    public static async Task<object?> StoreAsync(LifeVerseDbContext db, Guid viewer, Guid storeId, DateTimeOffset now, CancellationToken ct)
    {
        var shop = await db.Storefronts.AsNoTracking().SingleOrDefaultAsync(s => s.Id == storeId && s.DeletedAt == null, ct);
        if (shop is not null)
        {
            var mine = shop.OwnerUserId == viewer; if (!mine && (!shop.IsOpen || await BlockedAsync(db, viewer, shop.OwnerUserId, ct))) return null;
            var products = db.MarketplaceProducts.AsNoTracking().Where(p => p.SellerUserId == shop.OwnerUserId && p.GroupId == shop.GroupId && p.Status == "active");
            var count = await products.CountAsync(ct);
            var sold = await db.MarketplaceOrders.AsNoTracking().Where(o => o.SellerUserId == shop.OwnerUserId && o.Status == "completed").GroupBy(o => o.ProductId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
            var top = (await products.OrderByDescending(p => p.UpdatedAt).Take(60).ToListAsync(ct)).OrderByDescending(p => sold.GetValueOrDefault(p.Id)).ThenByDescending(p => p.UpdatedAt).Take(StoreRules.Featured).ToList();
            return new
            {
                id = shop.Id, kind = "virtual", name = shop.Name, description = shop.Description, category = shop.Category, categoryLabel = StoreRules.LabelOf(shop.Category),
                logoUrl = shop.LogoUrl.Length > 0 ? shop.LogoUrl : null, bannerUrl = shop.BannerUrl.Length > 0 ? shop.BannerUrl : null, owner = await PersonAsync(db, shop.OwnerUserId, ct), island = (object?)null, x = (float?)null, z = (float?)null,
                productCount = count, featured = top.Select(p => new { id = p.Id, name = p.Name, priceCoins = p.PriceCoins, category = p.Category, imageUrl = string.IsNullOrEmpty(p.ImageUrl) ? null : p.ImageUrl }), isOwner = mine, open = shop.IsOpen, ownerId = shop.OwnerUserId
            };
        }
        var world = await (from s in db.WorldStores.AsNoTracking() join i in db.Islands.AsNoTracking() on s.IslandId equals i.Id join p in db.MarketplaceProducts.AsNoTracking() on s.ProductId equals p.Id
                           where s.Id == storeId && s.DeletedAt == null && i.DeletedAt == null && (i.IsPublic || i.OwnerUserId == viewer || s.OwnerUserId == viewer) select new { Store = s, IslandName = i.Name, Product = p }).SingleOrDefaultAsync(ct);
        if (world is null) return null;
        var spot = world.Store.ObjectId is { } o ? await db.BuildObjects.AsNoTracking().Where(b => b.Id == o).Select(b => new { b.PositionX, b.PositionZ }).SingleOrDefaultAsync(ct) : null;
        if (await BlockedAsync(db, viewer, world.Store.OwnerUserId, ct)) return null;
        var activeProduct = world.Product.Status == "active";
        return new
        {
            id = world.Store.Id, kind = "physical", name = world.Store.DisplayName, description = world.Product.Description, category = world.Product.Category, categoryLabel = world.Product.Category, logoUrl = string.IsNullOrEmpty(world.Product.ImageUrl) ? null : world.Product.ImageUrl, bannerUrl = (string?)null,
            owner = await PersonAsync(db, world.Store.OwnerUserId, ct), island = new { id = world.Store.IslandId, name = world.IslandName }, x = spot?.PositionX, z = spot?.PositionZ, productCount = activeProduct ? 1 : 0,
            featured = activeProduct ? new[] { new { id = world.Product.Id, name = world.Product.Name, priceCoins = world.Product.PriceCoins, category = world.Product.Category, imageUrl = string.IsNullOrEmpty(world.Product.ImageUrl) ? null : world.Product.ImageUrl } } : [], isOwner = world.Store.OwnerUserId == viewer, open = activeProduct, ownerId = world.Store.OwnerUserId
        };
    }

    public static async Task<List<object>> MyStoresAsync(LifeVerseDbContext db, Guid me, CancellationToken ct) =>
        [.. (await db.Storefronts.AsNoTracking().Where(s => s.OwnerUserId == me && s.DeletedAt == null).OrderBy(s => s.CreatedAt).ToListAsync(ct)).Select(s => (object)new { id = s.Id, name = s.Name, category = s.Category, open = s.IsOpen })];

    private static bool BadUrl(string? url) => !string.IsNullOrWhiteSpace(url) && (url.Trim().Length > 1024 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0);

    public static async Task<StoreOutcome> OpenAsync(LifeVerseDbContext db, Guid me, StoreChange c, DateTimeOffset now, CancellationToken ct)
    {
        var name = (c.Name ?? string.Empty).Trim(); if (name.Length < StoreRules.MinName || name.Length > StoreRules.MaxName) return new(false, "bad_name");
        var description = (c.Description ?? string.Empty).Trim(); if (description.Length > StoreRules.MaxDescription) return new(false, "bad_description");
        var category = string.IsNullOrWhiteSpace(c.Category) ? "outros" : c.Category.Trim().ToLowerInvariant(); if (!StoreRules.Categories.Any(x => x.Key == category)) return new(false, "bad_category");
        if (BadUrl(c.LogoUrl) || BadUrl(c.BannerUrl)) return new(false, "bad_image");
        if (await db.Storefronts.CountAsync(s => s.OwnerUserId == me && s.DeletedAt == null, ct) >= StoreRules.MaxPerOwner) return new(false, "too_many_stores");
        if (await db.Storefronts.AnyAsync(s => s.OwnerUserId == me && s.DeletedAt == null && s.Name.ToLower() == name.ToLower(), ct)) return new(false, "name_taken");
        var shop = new Storefront { Id = Guid.NewGuid(), OwnerUserId = me, Name = name, Description = description, Category = category, LogoUrl = (c.LogoUrl ?? string.Empty).Trim(), BannerUrl = (c.BannerUrl ?? string.Empty).Trim(), IsOpen = c.IsOpen ?? true, CreatedAt = now, UpdatedAt = now };
        db.Storefronts.Add(shop); await db.SaveChangesAsync(ct); return new(true, null, shop.Id);
    }

    public static async Task<StoreOutcome> ChangeAsync(LifeVerseDbContext db, Guid me, Guid id, StoreChange c, DateTimeOffset now, CancellationToken ct)
    {
        var shop = await db.Storefronts.SingleOrDefaultAsync(s => s.Id == id && s.OwnerUserId == me && s.DeletedAt == null, ct); if (shop is null) return new(false, "not_found");
        if (c.Name is not null) { var name = c.Name.Trim(); if (name.Length < StoreRules.MinName || name.Length > StoreRules.MaxName) return new(false, "bad_name"); if (await db.Storefronts.AnyAsync(s => s.Id != id && s.OwnerUserId == me && s.DeletedAt == null && s.Name.ToLower() == name.ToLower(), ct)) return new(false, "name_taken"); shop.Name = name; }
        if (c.Description is not null) { var d = c.Description.Trim(); if (d.Length > StoreRules.MaxDescription) return new(false, "bad_description"); shop.Description = d; }
        if (c.Category is not null) { var category = c.Category.Trim().ToLowerInvariant(); if (!StoreRules.Categories.Any(x => x.Key == category)) return new(false, "bad_category"); shop.Category = category; }
        if (BadUrl(c.LogoUrl) || BadUrl(c.BannerUrl)) return new(false, "bad_image");
        if (c.LogoUrl is not null) shop.LogoUrl = c.LogoUrl.Trim(); if (c.BannerUrl is not null) shop.BannerUrl = c.BannerUrl.Trim(); if (c.IsOpen is { } open) shop.IsOpen = open;
        shop.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, shop.Id);
    }

    public static async Task<StoreOutcome> CloseForGoodAsync(LifeVerseDbContext db, Guid me, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var shop = await db.Storefronts.SingleOrDefaultAsync(s => s.Id == id && s.OwnerUserId == me && s.DeletedAt == null, ct); if (shop is null) return new(false, "not_found");
        shop.DeletedAt = now; shop.UpdatedAt = now; await db.SaveChangesAsync(ct); return new(true, null, id);
    }
}
