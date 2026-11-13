using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

public sealed record PublishProductRequest(Guid AssetId, string Name, string Description, string Category, long PriceCoins, string? ImageUrl, string? PermissionsJson, int? BuyerRights = null, int? NextOwnerRights = null, Guid? ItemId = null, bool? AllowGift = null);
public sealed record PurchaseRequest(string IdempotencyKey, Guid? RecipientUserId = null);
public sealed record TransferRequest(Guid RecipientUserId, long AmountCoins, string IdempotencyKey);
public sealed record RentalRequest(Guid PlanId, string IdempotencyKey);
public sealed record StoreRequest(Guid ProductId, Guid? ObjectId, string DisplayName);

public static class EconomyEndpoints
{
    internal const long FeeBps = 500;
    /// <summary>The most a product can cost. The fee is computed as price x 5 %: far below this, nothing can overflow.</summary>
    public const long MaxPriceCoins = 1_000_000_000_000;
    public static IEndpointRouteBuilder MapEconomyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var economy = endpoints.MapGroup("/api/v1/economy").WithTags("Economy").RequireAuthorization();
        economy.MapGet("/wallet", WalletAsync); economy.MapGet("/wallet/transactions", TransactionsAsync); economy.MapPost("/transfer", TransferAsync);
        economy.MapGet("/products", ProductsAsync); economy.MapPost("/products", PublishAsync); economy.MapPatch("/products/{id:guid}", UpdateProductAsync); economy.MapPost("/products/{id:guid}/deactivate", DeactivateAsync); economy.MapPost("/products/{id:guid}/purchase", PurchaseAsync); economy.MapGet("/orders", OrdersAsync);
        economy.MapGet("/rental-plans", RentalPlansAsync); economy.MapPost("/islands/{islandId:guid}/rent", RentIslandAsync); economy.MapPost("/rentals/{rentalId:guid}/renew", RenewRentalAsync);
        economy.MapGet("/islands/{islandId:guid}/stores", ListStoresAsync); economy.MapPost("/islands/{islandId:guid}/stores", CreateStoreAsync); economy.MapDelete("/islands/{islandId:guid}/stores/{storeId:guid}", DeleteStoreAsync);
        return endpoints;
    }
    private static async Task<IResult> WalletAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var wallet = await EnsureWallet(db, me, ct); return Results.Ok(new { wallet.Id, wallet.AvailableCoins, wallet.IsBlocked, wallet.UpdatedAt }); }
    private static async Task<IResult> TransactionsAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); return Results.Ok(await db.LedgerTransactions.AsNoTracking().Where(x => x.InitiatorUserId == me).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct)); }
    internal static async Task<IResult> ProductsAsync(string? q, string? category, int page, int pageSize, LifeVerseDbContext db, CancellationToken ct) { page = Math.Max(1, page); pageSize = Math.Clamp(pageSize == 0 ? 24 : pageSize, 1, 100); var query = db.MarketplaceProducts.AsNoTracking().Where(x => x.Status == "active"); if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.ToLower().Contains(q.ToLower()) || x.Description.ToLower().Contains(q.ToLower())); if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category); var total = await query.CountAsync(ct); var rows = await query.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct); var groupIds = rows.Where(p => p.GroupId != null).Select(p => p.GroupId!.Value).Distinct().ToList(); var groupNames = await db.SocialGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct); var assetIds = rows.Select(p => p.AssetId).Distinct().ToList(); var assetInfo = await db.Assets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => new { a.CurrentVersion, a.UpdatedAt }, ct); return Results.Ok(new { page, pageSize, total, items = rows.Select(p => new { p.Id, assetVersion = assetInfo.TryGetValue(p.AssetId, out var av) ? av.CurrentVersion : 1, assetUpdatedAt = assetInfo.TryGetValue(p.AssetId, out var au) ? (DateTimeOffset?)au.UpdatedAt : null, p.SellerUserId, p.GroupId, groupName = p.GroupId is { } owner ? groupNames.GetValueOrDefault(owner) : null, p.AssetId, p.Name, p.Description, p.Category, p.PriceCoins, p.PermissionsJson, buyerRights = p.BuyerRights, buyerNextRights = p.BuyerNextRights, rights = LifeVerse.Shared.Items.ItemRights.Letters(p.BuyerRights), rightsText = LifeVerse.Shared.Items.ItemRights.Describe(p.BuyerRights), nextRightsText = LifeVerse.Shared.Items.ItemRights.Describe(p.BuyerNextRights), isResale = p.SourceItemId != null, p.AllowGift, p.ImageUrl, p.Status, p.Version, p.CreatedAt, p.UpdatedAt }) }); }
    private static async Task<IResult> ListStoresAsync(Guid islandId, HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (!await db.Islands.AnyAsync(x => x.Id == islandId && x.DeletedAt == null && (x.IsPublic || x.OwnerUserId == me), ct)) return Results.Forbid(); return Results.Ok(await db.WorldStores.AsNoTracking().Where(x => x.IslandId == islandId && x.DeletedAt == null).Join(db.MarketplaceProducts, s => s.ProductId, p => p.Id, (s, p) => new { store = s, product = p }).Where(x => x.product.Status == "active").ToListAsync(ct)); }
    private static async Task<IResult> CreateStoreAsync(Guid islandId, StoreRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); if (!await db.Islands.AnyAsync(x => x.Id == islandId && x.OwnerUserId == me && x.DeletedAt == null, ct)) return Results.Forbid(); if (!await db.MarketplaceProducts.AnyAsync(x => x.Id == request.ProductId && x.SellerUserId == me && x.Status == "active", ct) || request.DisplayName.Trim().Length is < 1 or > 160) return Results.BadRequest(); var item = new WorldStore { Id = Guid.NewGuid(), IslandId = islandId, OwnerUserId = me, ProductId = request.ProductId, ObjectId = request.ObjectId, DisplayName = request.DisplayName.Trim(), CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow() }; db.WorldStores.Add(item); await db.SaveChangesAsync(ct); return Results.Created($"/api/v1/economy/islands/{islandId}/stores/{item.Id}", item); }
    private static async Task<IResult> DeleteStoreAsync(Guid islandId, Guid storeId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var item = await db.WorldStores.SingleOrDefaultAsync(x => x.Id == storeId && x.IslandId == islandId && x.OwnerUserId == me && x.DeletedAt == null, ct); if (item is null) return Results.NotFound(); item.DeletedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); return Results.NoContent(); }
    private static async Task<IResult> OrdersAsync(HttpContext context, LifeVerseDbContext db, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); return Results.Ok(await db.MarketplaceOrders.AsNoTracking().Where(x => x.BuyerUserId == me || x.SellerUserId == me).OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct)); }
    private static async Task<IResult> TransferAsync(TransferRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me) || me == request.RecipientUserId || request.AmountCoins <= 0 || request.IdempotencyKey.Length is < 8 or > 128) return Results.BadRequest(); await using var tx = await db.Database.BeginTransactionAsync(ct); if (await db.LedgerTransactions.AnyAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct)) return Results.Ok(new { status = "already_processed" }); var recipient = await db.Users.AnyAsync(x => x.Id == request.RecipientUserId && x.DeletedAt == null, ct); if (!recipient) return Results.NotFound(); var senderWallet = await EnsureWallet(db, me, ct); var recipientWallet = await EnsureWallet(db, request.RecipientUserId, ct); if (senderWallet.IsBlocked || recipientWallet.IsBlocked || senderWallet.AvailableCoins < request.AmountCoins) return Results.Conflict(new { error = "transfer_rejected" }); var now = clock.GetUtcNow(); senderWallet.AvailableCoins -= request.AmountCoins; recipientWallet.AvailableCoins += request.AmountCoins; senderWallet.UpdatedAt = recipientWallet.UpdatedAt = now; db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = request.IdempotencyKey, Type = "user_transfer", Status = "completed", Amount = request.AmountCoins, InitiatorUserId = me, MetadataJson = JsonSerializer.Serialize(new { recipientUserId = request.RecipientUserId }), CreatedAt = now, CompletedAt = now }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(new { status = "completed", amountCoins = request.AmountCoins }); }
    private static async Task<IResult> RentalPlansAsync(LifeVerseDbContext db, CancellationToken ct) => Results.Ok(await db.IslandRentalPlans.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct));
    private static async Task<IResult> RentIslandAsync(Guid islandId, RentalRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); return LifeVerse.Api.Modules.Land.LandEndpoints.Answer(await LifeVerse.Api.Modules.Land.IslandHosting.SubscribeAsync(db, islandId, me, request.PlanId, request.IdempotencyKey, clock, ct)); }
    private static async Task<IResult> RenewRentalAsync(Guid rentalId, RentalRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) { if (!User(context, out var me)) return Results.Unauthorized(); var rental = await db.IslandRentals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rentalId && x.UserId == me, ct); if (rental is null) return Results.NotFound(); return LifeVerse.Api.Modules.Land.LandEndpoints.Answer(await LifeVerse.Api.Modules.Land.IslandHosting.RenewAsync(db, rental.IslandId, me, request.IdempotencyKey, clock, ct)); }
    private static IResult RightsError(string? error) => error switch
    {
        "not_found" => Results.NotFound(new { error }),
        "not_allowed" => Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden),
        "item_busy" or "already_listed" or "listing_unavailable" or "stale" => Results.Conflict(new { error }),
        _ => Results.BadRequest(new { error }),
    };

    internal static async Task<IResult> PublishAsync(PublishProductRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        if (request.PriceCoins is < 0 or > MaxPriceCoins || request.Name.Trim().Length is < 1 or > 160) return Results.BadRequest();
        if (request.ItemId is null && !await db.Assets.AnyAsync(x => x.Id == request.AssetId && x.OwnerUserId == me && x.DeletedAt == null, ct)) return Results.BadRequest();
        var now = clock.GetUtcNow(); var listing = await ProductRights.PlanAsync(db, me, request, null, now, ct); if (!listing.Ok) return RightsError(listing.Error);
        var plan = listing.Plan!;
        var item = new MarketplaceProduct
        {
            Id = Guid.NewGuid(), SellerUserId = me, AssetId = plan.Item?.AssetId ?? request.AssetId, Name = request.Name.Trim(), Description = request.Description[..Math.Min(4000, request.Description.Length)], Category = request.Category.Trim()[..Math.Min(48, request.Category.Trim().Length)],
            PriceCoins = request.PriceCoins, ImageUrl = request.ImageUrl, PermissionsJson = LifeVerse.Shared.Items.ItemRights.ToJson(plan.Buyer), BuyerRights = plan.Buyer, BuyerNextRights = plan.BuyerNext, SourceItemId = plan.Item?.Id, AllowGift = request.AllowGift ?? true, Status = "active", Version = 1, CreatedAt = now, UpdatedAt = now,
        };
        db.MarketplaceProducts.Add(item);
        Inventory.ItemRightsService.Audit(db, me, "product_publish", "ok", plan.Item?.Id, item.AssetId, me, null, $"product {item.Id} price {item.PriceCoins} buyer {LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerRights)} next {LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerNextRights)}{(plan.Item is null ? string.Empty : " resale")}", now);
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (Inventory.RaceFailures.IsRace(exception)) { return RightsError("item_busy"); }
        return Results.Created($"/api/v1/economy/products/{item.Id}", item);
    }

    internal static async Task<IResult> UpdateProductAsync(Guid id, PublishProductRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var item = await db.MarketplaceProducts.SingleOrDefaultAsync(x => x.Id == id && x.SellerUserId == me && x.GroupId == null && x.Status == "active", ct); if (item is null) return Results.NotFound();
        if (request.PriceCoins is < 0 or > MaxPriceCoins || request.Name.Trim().Length is < 1 or > 160) return Results.BadRequest();
        var now = clock.GetUtcNow(); var listing = await ProductRights.PlanAsync(db, me, request, item, now, ct); if (!listing.Ok) return RightsError(listing.Error);
        var before = $"{LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerRights)}/{LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerNextRights)}"; var plan = listing.Plan!;
        item.Name = request.Name.Trim(); item.Description = request.Description[..Math.Min(4000, request.Description.Length)]; item.Category = request.Category.Trim()[..Math.Min(48, request.Category.Trim().Length)];
        item.PriceCoins = request.PriceCoins; item.ImageUrl = request.ImageUrl; item.BuyerRights = plan.Buyer; item.BuyerNextRights = plan.BuyerNext; item.PermissionsJson = LifeVerse.Shared.Items.ItemRights.ToJson(plan.Buyer); if (request.AllowGift is { } gift) item.AllowGift = gift;
        item.Version++; item.UpdatedAt = now;
        var after = $"{LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerRights)}/{LifeVerse.Shared.Items.ItemRights.Letters(item.BuyerNextRights)}";
        if (before != after) Inventory.ItemRightsService.Audit(db, me, "product_rights", "ok", item.SourceItemId, item.AssetId, me, null, $"product {item.Id}: {before} -> {after} (only for those who buy from now on)", now);
        await db.SaveChangesAsync(ct); return Results.Ok(item);
    }

    internal static async Task<IResult> DeactivateAsync(Guid id, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        var item = await db.MarketplaceProducts.SingleOrDefaultAsync(x => x.Id == id && x.SellerUserId == me, ct); if (item is null) return Results.NotFound();
        if (item.Status == "active") await ProductRights.ReleaseAsync(db, item, clock.GetUtcNow(), ct); // a resale gives its item back
        if (item.Status != "sold") item.Status = "inactive";
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (Inventory.RaceFailures.IsRace(exception)) { return RightsError("stale"); }
        return Results.NoContent();
    }

    internal static async Task<IResult> PurchaseAsync(Guid id, PurchaseRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!User(context, out var me)) return Results.Unauthorized();
        if (request.IdempotencyKey.Length is < 8 or > 128) return Results.BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.MarketplaceOrders.SingleOrDefaultAsync(x => x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return existing.BuyerUserId == me ? Results.Ok(existing) : Results.Conflict(new { error = "key_reused" });
        var product = await db.MarketplaceProducts.SingleOrDefaultAsync(x => x.Id == id && x.Status == "active", ct); if (product is null) return Results.NotFound();
        if (product.SellerUserId == me) return Results.BadRequest(new { error = "cannot_buy_own_product" });
        var recipient = request.RecipientUserId ?? me;
        if (recipient != me)
        {
            if (!product.AllowGift) return Results.Json(new { error = "gift_not_allowed" }, statusCode: StatusCodes.Status403Forbidden);
            if (recipient == product.SellerUserId || !await db.Users.AnyAsync(u => u.Id == recipient && u.DeletedAt == null && u.Status == "active", ct) || await db.UserBlocks.AnyAsync(b => (b.UserId == me && b.BlockedUserId == recipient) || (b.UserId == recipient && b.BlockedUserId == me), ct)) return Results.NotFound(new { error = "recipient_not_found" });
        }
        if (product.PriceCoins == 0 && product.SourceItemId is null && await db.MarketplaceOrders.AnyAsync(o => o.ProductId == id && o.Status == "completed" && (o.RecipientUserId ?? o.BuyerUserId) == recipient, ct)) return Results.Conflict(new { error = "already_owned" }); // a free product is taken once by each person
        var buyer = await EnsureWallet(db, me, ct); var seller = product.GroupId is null ? await EnsureWallet(db, product.SellerUserId, ct) : null;
        if (buyer.IsBlocked || seller?.IsBlocked == true) return Results.Forbid();
        if (buyer.AvailableCoins < product.PriceCoins) return Results.Conflict(new { error = "insufficient_balance" });
        var now = clock.GetUtcNow();
        var delivery = await ProductRights.DeliverAsync(db, product, me, recipient, now, ct); if (delivery.Error is not null) return Results.Conflict(new { error = delivery.Error }); // checked before any coin moves
        var item = delivery.Item!;
        try
        {
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = request.IdempotencyKey, Type = "marketplace_purchase", Status = "completed", Amount = product.PriceCoins, InitiatorUserId = me, MetadataJson = JsonSerializer.Serialize(new { productId = id }), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction); var fee = product.PriceCoins * FeeBps / 10000; buyer.AvailableCoins -= product.PriceCoins;
        if (seller is not null) { seller.AvailableCoins += product.PriceCoins - fee; seller.UpdatedAt = now; }
        buyer.UpdatedAt = now;
        /* what a group sells goes to the chest of the group, below */
        var buyerAccount = await EnsureAccount(db, me, "user"); var sellerAccount = product.GroupId is { } soldBy ? await Groups.GroupService.TreasuryAccountAsync(db, soldBy, now, ct) : await EnsureAccount(db, product.SellerUserId, "user"); var treasury = await EnsureAccount(db, null, "treasury");
        db.LedgerEntries.AddRange(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = buyerAccount.Id, Debit = product.PriceCoins, Credit = 0, CreatedAt = now }, new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = sellerAccount.Id, Debit = 0, Credit = product.PriceCoins - fee, CreatedAt = now }, new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = treasury.Id, Debit = 0, Credit = fee, CreatedAt = now });
        var order = new MarketplaceOrder { Id = Guid.NewGuid(), ProductId = product.Id, BuyerUserId = me, SellerUserId = product.SellerUserId, PriceCoins = product.PriceCoins, Status = "completed", IdempotencyKey = request.IdempotencyKey, TransactionId = transaction.Id, InventoryItemId = item.Id, RecipientUserId = recipient == me ? null : recipient, CreatedAt = now, CompletedAt = now };
        db.MarketplaceOrders.Add(order); if (product.GroupId is { } soldFor) await Groups.GroupService.CreditSaleAsync(db, soldFor, product.PriceCoins - fee, order.Id, me, product.Name, transaction.Id, now, ct);
        Inventory.ItemRightsService.Audit(db, me, "purchase", "ok", item.Id, item.AssetId, product.SellerUserId, recipient, $"product {product.Id} {product.PriceCoins} coins; holds {LifeVerse.Shared.Items.ItemRights.Letters(item.OwnerRights)}, passes on {LifeVerse.Shared.Items.ItemRights.Letters(item.NextOwnerRights)}{(product.SourceItemId is null ? string.Empty : " (resale)")}", now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(order);
        }
        catch (Exception exception) when (Inventory.RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return Results.Conflict(new { error = "stale" }); } // somebody else got there first (the last unit, the same coins, the same account): nothing was charged and nothing was delivered
    }

    internal static async Task<Wallet> EnsureWallet(LifeVerseDbContext db, Guid userId, CancellationToken ct) { var wallet = await db.Wallets.SingleOrDefaultAsync(x => x.UserId == userId, ct); if (wallet is not null) return wallet; wallet = new Wallet { Id = Guid.NewGuid(), UserId = userId, AvailableCoins = 0, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }; db.Wallets.Add(wallet); await db.SaveChangesAsync(ct); return wallet; }
    // Two first-ever purchases may both create the (user-less) platform account; the oldest one is the one used, so a duplicate never breaks later purchases.
    internal static async Task<LedgerAccount> EnsureAccount(LifeVerseDbContext db, Guid? userId, string type) { var account = await db.LedgerAccounts.Where(x => x.UserId == userId && x.AccountType == type).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(); if (account is not null) return account; account = new LedgerAccount { Id = Guid.NewGuid(), UserId = userId, AccountType = type, CreatedAt = DateTimeOffset.UtcNow }; db.LedgerAccounts.Add(account); await db.SaveChangesAsync(); return account; }
    private static bool User(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out id);
}
