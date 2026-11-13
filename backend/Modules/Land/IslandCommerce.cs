using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Users;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// Selling, renting and handing over whole islands. The rules are those of the land commerce (one unit of work for the wallets, the ledger and what changes hands; a repeated key does nothing twice; the price the person saw
/// must still be the price), with what is different about an island: a sale moves the title of the island with every parcel and object its seller still holds; a rental never moves the title, it only gives the renter
/// the administrator permission, and the powers over the island's settings, for as long as the contract runs.
/// </summary>
public static class IslandCommerce
{
    public const long MaxPrice = LandCommerce.MaxPrice;

    private static Task<Island?> IslandAsync(LifeVerseDbContext db, Guid islandId, CancellationToken ct) => db.Islands.SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct);
    private static Task<bool> HasLiveLeaseAsync(LifeVerseDbContext db, Guid islandId, CancellationToken ct) => db.IslandLeases.AnyAsync(l => l.IslandId == islandId && (l.Status == "active" || l.Status == "grace"), ct);

    /// <summary>The kind of region the island is, from its size: 256 m and up is a full region, smaller ones are homesteads.</summary>
    public static string RegionOf(Island island) => island.Size >= 256 ? "full" : "homestead";

    private static void Record(LifeVerseDbContext db, Guid islandId, string type, Guid? from, Guid? to, long amount, object data, Guid? ledger, DateTimeOffset now) =>
        db.IslandTransactions.Add(new IslandTransaction { Id = Guid.NewGuid(), IslandId = islandId, Type = type, FromUserId = from, ToUserId = to, Amount = amount, DataJson = JsonSerializer.Serialize(data), LedgerTransactionId = ledger, CreatedAt = now });

    private static void Touch(Island island, DateTimeOffset now) => island.UpdatedAt = now;

    private static async Task<CommerceResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }

    // ---- selling -----------------------------------------------------------------------------------------------------------

    public static async Task<CommerceResult> OfferForSaleAsync(LifeVerseDbContext db, Guid islandId, Guid actor, SaleOffer offer, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (island.IsWelcome) return CommerceResult.Fail("welcome_island"); // the island every new avatar arrives on belongs to the platform's flow, not to the market
        if (offer.Price is < 1 or > MaxPrice) return CommerceResult.Fail("bad_price");
        if (offer.BuyerUserId == actor) return CommerceResult.Fail("bad_buyer");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        if (HasPendingTransfer(island, clock.GetUtcNow())) return CommerceResult.Fail("transfer_pending"); // the island is already promised to somebody
        var now = clock.GetUtcNow(); var description = (offer.Description ?? string.Empty).Trim(); if (description.Length > 2000) description = description[..2000];
        island.SalePrice = offer.Price; island.SaleBuyerUserId = offer.BuyerUserId; island.SaleIncludesObjects = offer.IncludeObjects; island.SaleDescription = description; island.RentPrice = null; Touch(island, now);
        Record(db, islandId, "sale_listed", actor, null, offer.Price, new { exclusive = offer.BuyerUserId is not null, offer.IncludeObjects }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_sale_listed", "A ilha foi colocada à venda", "Propriedade", null, new { price = offer.Price, exclusive = offer.BuyerUserId is not null }, now);
        return await SaveAsync(db, ct, new { islandId, price = offer.Price });
    }

    public static async Task<CommerceResult> CancelSaleAsync(LifeVerseDbContext db, Guid islandId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (island.SalePrice is null) return CommerceResult.Fail("not_for_sale");
        var now = clock.GetUtcNow(); var before = island.SalePrice; island.SalePrice = null; island.SaleBuyerUserId = null; island.SaleDescription = string.Empty; Touch(island, now);
        Record(db, islandId, "sale_cancelled", actor, null, 0, new { }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_sale_cancelled", "A venda da ilha foi retirada", "Propriedade", new { price = before }, null, now);
        return await SaveAsync(db, ct);
    }

    /// <summary>Gives the island, with the parcels and objects its owner still holds, to somebody else. Contracts already running stay (rent on a parcel goes to the new owner of the parcel).</summary>
    private static async Task<(int Parcels, int ObjectsMoved, Guid[] ObjectsToReturn)> HandOverAsync(LifeVerseDbContext db, Island island, Guid from, Guid to, bool includeObjects, DateTimeOffset now, CancellationToken ct)
    {
        island.OwnerUserId = to; island.SalePrice = null; island.SaleBuyerUserId = null; island.SaleDescription = string.Empty; island.RentPrice = null; Touch(island, now);
        var parcels = await db.IslandParcels.Where(p => p.IslandId == island.Id && p.DeletedAt == null && p.OwnerUserId == from && p.GroupId == null).ToListAsync(ct);
        var live = await db.ParcelRentals.Where(r => (r.Status == "active" || r.Status == "grace") && parcels.Select(p => p.Id).Contains(r.ParcelId)).ToListAsync(ct);
        foreach (var parcel in parcels)
        {
            var rented = live.Any(r => r.ParcelId == parcel.Id);
            parcel.OwnerUserId = to; parcel.SalePrice = null; parcel.SaleBuyerUserId = null; parcel.TransferToUserId = null; parcel.TransferExpiresAt = null; if (!rented) parcel.RentPrice = null; parcel.Revision++; parcel.UpdatedAt = now;
            db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = to, Operation = "island_transfer", PayloadJson = JsonSerializer.Serialize(new { from, to }), Revision = parcel.Revision, CreatedAt = now });
        }
        foreach (var rental in live) rental.LandlordUserId = to;
        // the platform plan that keeps the island running goes with it: otherwise the old owner would go on paying (and renewing) for an island that is no longer theirs
        foreach (var plan in await db.IslandRentals.Where(r => r.IslandId == island.Id && r.Status == "active" && r.UserId == from).ToListAsync(ct)) plan.UserId = to;
        var objects = await db.BuildObjects.Where(o => o.IslandId == island.Id && o.DeletedAt == null && o.OwnerUserId == from).ToListAsync(ct);
        if (includeObjects) { foreach (var item in objects) { item.OwnerUserId = to; item.Revision++; } return (parcels.Count, objects.Count, []); }
        return (parcels.Count, 0, [.. objects.Select(o => o.Id)]);
    }

    private static async Task ReturnAsync(LifeVerseDbContext db, AssetStorageService? storage, Island island, Guid owner, Guid[] ids, DateTimeOffset now, CancellationToken ct)
    {
        if (storage is null || ids.Length == 0) return;
        for (var i = 0; i < ids.Length; i += BuildPackageLimits.MaxReturnAtOnce) await BuildOperations.ReturnForLandAsync(island, owner, ids[i..Math.Min(ids.Length, i + BuildPackageLimits.MaxReturnAtOnce)], db, storage, now, ct);
    }

    /// <summary>The purchase. <paramref name="expectedPrice"/> is the price the buyer saw: if it changed since, nothing is charged.</summary>
    public static async Task<CommerceResult> BuyAsync(LifeVerseDbContext db, AssetStorageService? storage, Guid islandId, Guid buyer, string? key, long expectedPrice, TimeProvider clock, CancellationToken ct)
    {
        if (!LandCommerce.KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { islandId, alreadyDone = true });
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.SalePrice is not { } price) return CommerceResult.Fail("not_for_sale");
        if (island.OwnerUserId == buyer) return CommerceResult.Fail("own_island");
        if (island.SaleBuyerUserId is { } exclusive && exclusive != buyer) return CommerceResult.Fail("not_for_you");
        if (price != expectedPrice) return CommerceResult.Fail("price_changed");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow(); var seller = island.OwnerUserId; var includeObjects = island.SaleIncludesObjects;
        var buyerWallet = await LandCommerce.WalletOfAsync(db, buyer, now, ct); var sellerWallet = await LandCommerce.WalletOfAsync(db, seller, now, ct);
        if (buyerWallet.IsBlocked || sellerWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (buyerWallet.AvailableCoins < price) return CommerceResult.Fail("insufficient_balance");
        var (hold, holdError) = await PaymentHolds.ReserveAsync(db, key!, "island_sale", buyer, price, islandId, now, ct); if (hold is null) return CommerceResult.Fail(holdError!);
        sellerWallet.AvailableCoins += price; sellerWallet.UpdatedAt = now;
        var ledger = await PaymentHolds.SettleAsync(db, hold, key!, "island_sale", [(seller, price)], new { islandId, seller }, now, ct);
        var (parcels, moved, toReturn) = await HandOverAsync(db, island, seller, buyer, includeObjects, now, ct);
        Record(db, islandId, "sale", buyer, seller, price, new { parcels, objectsMoved = moved, objectsReturned = toReturn.Length }, ledger.Id, now);
        PropertyNotices.Add(db, seller, "sold", $"A sua ilha {island.Name} foi vendida por {LandCommerce.Money(price)}", $"Comprador: {await PropertyNotices.NameAsync(db, buyer, ct)}. O valor já está na sua carteira e a ilha, os terrenos e os objetos incluídos passaram para ele.", $"sold:{islandId}:{ledger.Id}", now);
        IslandAudit.Add(db, islandId, buyer, "administração", "island_sold", "A ilha foi vendida", "Propriedade", new { owner = seller }, new { owner = buyer, price, parcels }, now);
        var saved = await PaymentHolds.FinishAsync(db, hold, new { islandId, price, parcels, objectsMoved = moved }, now, ct);
        if (!saved.Ok) return saved;
        await ReturnAsync(db, storage, island, seller, toReturn, now, ct); // what the seller keeps goes back to the seller's inventory
        return saved;
    }

    // ---- handing over (a gift that the other person has to accept) ----------------------------------------------------------------

    /// <summary>How long the other person has to answer.</summary>
    public static readonly TimeSpan TransferWindow = TimeSpan.FromDays(7);

    /// <summary>Whether the island has a transfer waiting for an answer (one that ran out counts as none).</summary>
    public static bool HasPendingTransfer(Island island, DateTimeOffset now) => island.TransferToUserId is not null && island.TransferExpiresAt > now;

    private static void ClearTransfer(Island island) { island.TransferToUserId = null; island.TransferExpiresAt = null; }

    /// <summary>
    /// The owner offers the island, with its parcels and objects, to another person, for nothing. Nothing moves yet: the person has <see cref="TransferWindow"/> to accept or turn it down,
    /// and the owner can take the offer back. Not while it is rented or for sale, and the island cannot be put on the market while an offer waits.
    /// </summary>
    public static async Task<CommerceResult> TransferAsync(LifeVerseDbContext db, Guid islandId, Guid actor, string? toName, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (island.IsWelcome) return CommerceResult.Fail("welcome_island");
        if (string.IsNullOrWhiteSpace(toName)) return CommerceResult.Fail("bad_buyer");
        if (await ProfileService.FindByNameAsync(db, toName.Trim(), ct) is not { } target) return CommerceResult.Fail("person_not_found");
        if (target == actor) return CommerceResult.Fail("bad_buyer");
        if (island.SalePrice is not null) return CommerceResult.Fail("for_sale");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow();
        if (HasPendingTransfer(island, now)) return CommerceResult.Fail("transfer_pending");
        island.TransferToUserId = target; island.TransferExpiresAt = now + TransferWindow; island.RentPrice = null; Touch(island, now);
        Record(db, islandId, "transfer_offered", actor, target, 0, new { expiresAt = island.TransferExpiresAt }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_transfer_offered", "A ilha foi oferecida em transferência", "Propriedade", null, new { to = target, expiresAt = island.TransferExpiresAt }, now);
        PropertyNotices.Add(db, target, "island_transfer_offer", $"{await PropertyNotices.NameAsync(db, actor, ct)} quer transferir a ilha {island.Name} para você", $"É um presente: a ilha, os terrenos e os objetos dele passam para você. Aceite ou recuse em Minhas Propriedades até {island.TransferExpiresAt:dd/MM/yyyy HH:mm}.", $"island_transfer_offer:{islandId}:{now.UtcTicks}", now);
        return await SaveAsync(db, ct, new { islandId, expiresAt = island.TransferExpiresAt });
    }

    /// <summary>The owner takes the offer back.</summary>
    public static async Task<CommerceResult> CancelTransferAsync(LifeVerseDbContext db, Guid islandId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        var now = clock.GetUtcNow(); if (island.TransferToUserId is not { } target) return CommerceResult.Fail("no_transfer");
        var live = HasPendingTransfer(island, now); ClearTransfer(island); Touch(island, now);
        Record(db, islandId, "transfer_cancelled", actor, target, 0, new { }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_transfer_cancelled", "A transferência da ilha foi retirada", "Propriedade", new { to = target }, null, now);
        if (live) PropertyNotices.Add(db, target, "island_transfer_cancelled", $"A transferência da ilha {island.Name} foi retirada", "O dono desistiu de oferecer a ilha a você.", $"island_transfer_cancelled:{islandId}:{now.UtcTicks}", now);
        return await SaveAsync(db, ct, new { islandId });
    }

    /// <summary>The person the island was offered to turns it down.</summary>
    public static async Task<CommerceResult> DeclineTransferAsync(LifeVerseDbContext db, Guid islandId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow(); if (island.TransferToUserId != actor) return CommerceResult.Fail("no_transfer");
        ClearTransfer(island); Touch(island, now);
        Record(db, islandId, "transfer_declined", actor, island.OwnerUserId, 0, new { }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_transfer_declined", "A transferência da ilha foi recusada", "Propriedade", null, new { by = actor }, now);
        PropertyNotices.Add(db, island.OwnerUserId, "island_transfer_declined", $"{await PropertyNotices.NameAsync(db, actor, ct)} recusou a ilha {island.Name}", "A ilha continua sua.", $"island_transfer_declined:{islandId}:{now.UtcTicks}", now);
        return await SaveAsync(db, ct, new { islandId });
    }

    /// <summary>The person the island was offered to accepts: only now does the title move, with the parcels, the objects and the hosting plan.</summary>
    public static async Task<CommerceResult> AcceptTransferAsync(LifeVerseDbContext db, Guid islandId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow(); if (island.TransferToUserId != actor) return CommerceResult.Fail("no_transfer");
        if (!HasPendingTransfer(island, now)) { ClearTransfer(island); await SaveAsync(db, ct); return CommerceResult.Fail("transfer_expired"); }
        if (island.SalePrice is not null) return CommerceResult.Fail("for_sale");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        var giver = island.OwnerUserId;
        ClearTransfer(island);
        var (parcels, moved, _) = await HandOverAsync(db, island, giver, actor, true, now, ct);
        Record(db, islandId, "transfer", giver, actor, 0, new { parcels, objectsMoved = moved }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_transferred", "A propriedade da ilha foi transferida", "Propriedade", new { owner = giver }, new { owner = actor, parcels }, now);
        PropertyNotices.Add(db, actor, "island_received", $"Você recebeu a ilha {island.Name}", $"{await PropertyNotices.NameAsync(db, giver, ct)} transferiu a ilha para você, com {parcels} terreno(s) e os objetos dele. Ela já está em Minhas Propriedades.", $"island_received:{islandId}:{now.UtcTicks}", now);
        PropertyNotices.Add(db, giver, "island_given", $"{await PropertyNotices.NameAsync(db, actor, ct)} aceitou a ilha {island.Name}", "A ilha, os terrenos e os objetos seus passaram para ele.", $"island_given:{islandId}:{now.UtcTicks}", now);
        return await SaveAsync(db, ct, new { islandId, parcels });
    }

    // ---- renting -------------------------------------------------------------------------------------------------------------

    public static async Task<CommerceResult> OfferForRentAsync(LifeVerseDbContext db, Guid islandId, Guid actor, RentOffer offer, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (island.IsWelcome) return CommerceResult.Fail("welcome_island");
        if (offer.PricePerPeriod is < 1 or > MaxPrice || offer.DepositCoins is < 0 or > MaxPrice) return CommerceResult.Fail("bad_price");
        if (offer.PeriodDays is < 1 or > 365 || offer.MinPeriods < 1 || offer.MaxPeriods < offer.MinPeriods || offer.MaxPeriods > 120 || offer.GraceDays is < 0 or > 60) return CommerceResult.Fail("bad_terms");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        if (HasPendingTransfer(island, clock.GetUtcNow())) return CommerceResult.Fail("transfer_pending");
        var now = clock.GetUtcNow();
        island.RentPrice = offer.PricePerPeriod; island.RentPeriodDays = offer.PeriodDays; island.RentMinPeriods = offer.MinPeriods; island.RentMaxPeriods = offer.MaxPeriods; island.RentDeposit = offer.DepositCoins; island.RentGraceDays = offer.GraceDays; island.RentAutoRenewAllowed = offer.AutoRenewAllowed;
        island.SalePrice = null; island.SaleBuyerUserId = null; Touch(island, now);
        Record(db, islandId, "rent_listed", actor, null, offer.PricePerPeriod, new { offer.PeriodDays, offer.MinPeriods, offer.MaxPeriods, offer.DepositCoins }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_rent_listed", "A ilha foi oferecida para aluguel", "Propriedade", null, new { price = offer.PricePerPeriod, days = offer.PeriodDays }, now);
        return await SaveAsync(db, ct, new { islandId });
    }

    public static async Task<CommerceResult> CancelRentOfferAsync(LifeVerseDbContext db, Guid islandId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        if (island.RentPrice is null) return CommerceResult.Fail("not_for_rent");
        var now = clock.GetUtcNow(); island.RentPrice = null; Touch(island, now);
        Record(db, islandId, "rent_cancelled", actor, null, 0, new { }, null, now);
        IslandAudit.Add(db, islandId, actor, "administração", "island_rent_cancelled", "A oferta de aluguel da ilha foi retirada", "Propriedade", null, null, now);
        return await SaveAsync(db, ct);
    }

    private static async Task<IslandPermission> GrantAdminAsync(LifeVerseDbContext db, Island island, Guid tenant, DateTimeOffset expires, DateTimeOffset now, CancellationToken ct)
    {
        var permission = new IslandPermission { Id = Guid.NewGuid(), IslandId = island.Id, UserId = tenant, Permission = "admin", PrincipalType = "user", GrantedByUserId = island.OwnerUserId, CreatedAt = now, ExpiresAt = expires };
        db.IslandPermissions.Add(permission); await Task.CompletedTask; return permission;
    }

    public static async Task<CommerceResult> RentAsync(LifeVerseDbContext db, Guid islandId, Guid tenant, int periods, string? key, long expectedPrice, TimeProvider clock, CancellationToken ct, bool autoRenew = false)
    {
        if (!LandCommerce.KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { islandId, alreadyDone = true });
        var island = await IslandAsync(db, islandId, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.RentPrice is not { } price) return CommerceResult.Fail("not_for_rent");
        if (island.OwnerUserId == tenant) return CommerceResult.Fail("own_island");
        if (price != expectedPrice) return CommerceResult.Fail("price_changed");
        if (periods < island.RentMinPeriods || periods > island.RentMaxPeriods) return CommerceResult.Fail("bad_periods");
        if (await HasLiveLeaseAsync(db, islandId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow(); var rent = price * periods; var deposit = island.RentDeposit; var total = rent + deposit;
        var tenantWallet = await LandCommerce.WalletOfAsync(db, tenant, now, ct); var landlordWallet = await LandCommerce.WalletOfAsync(db, island.OwnerUserId, now, ct);
        if (tenantWallet.IsBlocked || landlordWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (tenantWallet.AvailableCoins < total) return CommerceResult.Fail("insufficient_balance");
        var (hold, holdError) = await PaymentHolds.ReserveAsync(db, key!, "island_rental", tenant, total, islandId, now, ct); if (hold is null) return CommerceResult.Fail(holdError!);
        landlordWallet.AvailableCoins += rent; landlordWallet.UpdatedAt = now;
        var ledger = await PaymentHolds.SettleAsync(db, hold, key!, "island_rental", [(island.OwnerUserId, rent), (null, deposit)], new { islandId, periods }, now, ct);
        var expires = now.AddDays((double)island.RentPeriodDays * periods);
        var permission = await GrantAdminAsync(db, island, tenant, expires, now, ct);
        var lease = new IslandLease { Id = Guid.NewGuid(), IslandId = islandId, LandlordUserId = island.OwnerUserId, TenantUserId = tenant, PricePerPeriod = price, PeriodDays = island.RentPeriodDays, DepositCoins = deposit, GraceDays = island.RentGraceDays, Periods = periods, AutoRenew = autoRenew && island.RentAutoRenewAllowed, Status = "active", StartsAt = now, ExpiresAt = expires, CreatedAt = now, PermissionId = permission.Id };
        db.IslandLeases.Add(lease); island.RentPrice = null; Touch(island, now);
        Record(db, islandId, "rent_start", tenant, island.OwnerUserId, rent, new { periods, deposit, leaseId = lease.Id }, ledger.Id, now);
        PropertyNotices.Add(db, island.OwnerUserId, "rented", $"A sua ilha {island.Name} foi alugada", $"Locatário: {await PropertyNotices.NameAsync(db, tenant, ct)}, {periods} período(s), até {expires.ToLocalTime():dd/MM/yyyy}. Você recebeu {LandCommerce.Money(rent)}.", $"rented:{lease.Id}", now);
        IslandAudit.Add(db, islandId, tenant, "administração", "island_rented", "A ilha foi alugada", "Propriedade", null, new { tenant, periods, expires }, now);
        return await PaymentHolds.FinishAsync(db, hold, new { leaseId = lease.Id, expiresAt = expires, paid = total }, now, ct);
    }

    public static async Task<CommerceResult> RenewAsync(LifeVerseDbContext db, Guid leaseId, Guid tenant, int periods, string? key, TimeProvider clock, CancellationToken ct)
    {
        if (!LandCommerce.KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { leaseId, alreadyDone = true });
        var lease = await db.IslandLeases.SingleOrDefaultAsync(l => l.Id == leaseId && l.TenantUserId == tenant && (l.Status == "active" || l.Status == "grace"), ct);
        if (lease is null) return CommerceResult.Fail("not_found");
        if (periods is < 1 or > 120) return CommerceResult.Fail("bad_periods");
        return await ChargeRenewalAsync(db, lease, periods, key!, "island_renewal", clock.GetUtcNow(), ct);
    }

    private static async Task<CommerceResult> ChargeRenewalAsync(LifeVerseDbContext db, IslandLease lease, int periods, string key, string type, DateTimeOffset now, CancellationToken ct)
    {
        var tenant = lease.TenantUserId; var cost = lease.PricePerPeriod * periods;
        var tenantWallet = await LandCommerce.WalletOfAsync(db, tenant, now, ct); var landlordWallet = await LandCommerce.WalletOfAsync(db, lease.LandlordUserId, now, ct);
        if (tenantWallet.IsBlocked || landlordWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (tenantWallet.AvailableCoins < cost) return CommerceResult.Fail("insufficient_balance");
        tenantWallet.AvailableCoins -= cost; landlordWallet.AvailableCoins += cost; tenantWallet.UpdatedAt = landlordWallet.UpdatedAt = now;
        var ledger = await LandCommerce.LedgerAsync(db, key, type, tenant, tenant, [(lease.LandlordUserId, cost)], new { leaseId = lease.Id, periods }, now, ct);
        var from = type == "island_autorenewal" || lease.ExpiresAt > now ? lease.ExpiresAt : now; // the clock renewing right on time does not eat into the new period
        lease.ExpiresAt = from.AddDays((double)lease.PeriodDays * periods); lease.Periods += periods; lease.Status = "active"; lease.GraceUntil = null;
        var permission = lease.PermissionId is { } pid ? await db.IslandPermissions.SingleOrDefaultAsync(p => p.Id == pid, ct) : null;
        if (permission is null) { permission = new IslandPermission { Id = Guid.NewGuid(), IslandId = lease.IslandId, UserId = tenant, Permission = "admin", PrincipalType = "user", GrantedByUserId = lease.LandlordUserId, CreatedAt = now }; db.IslandPermissions.Add(permission); lease.PermissionId = permission.Id; }
        permission.RevokedAt = null; permission.ExpiresAt = lease.ExpiresAt;
        Record(db, lease.IslandId, "rent_payment", tenant, lease.LandlordUserId, cost, new { periods, leaseId = lease.Id, auto = type == "island_autorenewal" }, ledger.Id, now);
        return await SaveAsync(db, ct, new { leaseId = lease.Id, expiresAt = lease.ExpiresAt, paid = cost });
    }

    /// <summary>The tenant may leave at any time; the owner may only close a contract that has run out. The deposit goes back to the tenant, the tenant loses the administrator permission and the objects the tenant owns on the island go back to the tenant's inventory.</summary>
    public static async Task<CommerceResult> EndAsync(LifeVerseDbContext db, AssetStorageService? storage, Guid leaseId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var lease = await db.IslandLeases.SingleOrDefaultAsync(l => l.Id == leaseId && (l.Status == "active" || l.Status == "grace"), ct);
        if (lease is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow();
        if (lease.TenantUserId == actor) return await FinishAsync(db, storage, lease, "tenant", now, ct);
        if (lease.LandlordUserId == actor) return lease.ExpiresAt <= now ? await FinishAsync(db, storage, lease, "landlord", now, ct) : CommerceResult.Fail("still_running");
        return CommerceResult.Fail("forbidden");
    }

    private static async Task<CommerceResult> FinishAsync(LifeVerseDbContext db, AssetStorageService? storage, IslandLease lease, string reason, DateTimeOffset now, CancellationToken ct)
    {
        lease.Status = "ended"; lease.EndReason = reason; lease.EndedAt = now; lease.GraceUntil = null;
        if (lease.PermissionId is { } pid && await db.IslandPermissions.SingleOrDefaultAsync(p => p.Id == pid, ct) is { } permission) permission.RevokedAt = now;
        Guid? ledgerId = null;
        if (lease.DepositCoins > 0)
        {
            var tenantWallet = await LandCommerce.WalletOfAsync(db, lease.TenantUserId, now, ct); tenantWallet.AvailableCoins += lease.DepositCoins; tenantWallet.UpdatedAt = now;
            var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = $"island-deposit-refund:{lease.Id}", Type = "island_deposit_refund", Status = "completed", Amount = lease.DepositCoins, InitiatorUserId = lease.TenantUserId, MetadataJson = JsonSerializer.Serialize(new { leaseId = lease.Id }), CreatedAt = now, CompletedAt = now };
            db.LedgerTransactions.Add(transaction); ledgerId = transaction.Id;
            var escrow = await LandCommerce.AccountAsync(db, null, "land_escrow", now, ct); var tenantAccount = await LandCommerce.AccountAsync(db, lease.TenantUserId, "user", now, ct);
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = escrow.Id, Debit = lease.DepositCoins, Credit = 0, CreatedAt = now });
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = tenantAccount.Id, Debit = 0, Credit = lease.DepositCoins, CreatedAt = now });
        }
        Record(db, lease.IslandId, "rent_end", lease.LandlordUserId, lease.TenantUserId, lease.DepositCoins, new { reason, leaseId = lease.Id }, ledgerId, now);
        IslandAudit.Add(db, lease.IslandId, lease.TenantUserId, "administração", "island_rent_ended", "O aluguel da ilha terminou", "Propriedade", null, new { reason }, now);
        if (reason == "expired") { var endedName = await db.Islands.AsNoTracking().Where(i => i.Id == lease.IslandId).Select(i => i.Name).FirstOrDefaultAsync(ct) ?? "a ilha"; PropertyNotices.Add(db, lease.TenantUserId, "rent_ended", $"O aluguel da ilha {endedName} terminou", "O prazo de tolerância acabou. Os seus objetos voltaram para o seu inventário e a caução foi devolvida.", $"ended:{lease.Id}:t", now); PropertyNotices.Add(db, lease.LandlordUserId, "rent_ended_owner", $"O aluguel da ilha {endedName} terminou", "O contrato não foi renovado e a ilha está livre de novo.", $"ended:{lease.Id}:l", now); }
        var saved = await SaveAsync(db, ct, new { leaseId = lease.Id, refunded = lease.DepositCoins });
        if (!saved.Ok || storage is null) return saved;
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == lease.IslandId, ct); if (island is null) return saved;
        var ids = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == island.Id && o.DeletedAt == null && o.OwnerUserId == lease.TenantUserId).Select(o => o.Id).ToArrayAsync(ct);
        await ReturnAsync(db, storage, island, lease.TenantUserId, ids, now, ct);
        return CommerceResult.Done(new { leaseId = lease.Id, refunded = lease.DepositCoins, objectsReturned = ids.Length });
    }

    /// <summary>What the clock does to the island leases: one that ran out goes to its grace period (or ends at once without one), one whose grace ran out ends.</summary>
    public static async Task<int> ProcessDueAsync(LifeVerseDbContext db, AssetStorageService? storage, DateTimeOffset now, CancellationToken ct)
    {
        var done = 0;
        foreach (var due in await db.IslandLeases.AsNoTracking().Where(l => (l.Status == "active" && l.ExpiresAt <= now) || (l.Status == "grace" && l.GraceUntil <= now)).Select(l => l.Id).ToListAsync(ct))
        {
            var lease = await db.IslandLeases.SingleAsync(l => l.Id == due, ct);
            var island = await db.Islands.AsNoTracking().Where(i => i.Id == lease.IslandId).Select(i => i.Name).FirstOrDefaultAsync(ct) ?? "a ilha";
            if (lease.Status == "active" && lease.AutoRenew)
            {
                var renewed = await ChargeRenewalAsync(db, lease, 1, $"island-auto:{lease.Id}:{lease.ExpiresAt.UtcTicks}", "island_autorenewal", now, ct);
                if (renewed.Ok) { done++; continue; }
                db.ChangeTracker.Clear(); lease = await db.IslandLeases.SingleAsync(l => l.Id == due, ct);
                PropertyNotices.Add(db, lease.TenantUserId, "renewal_failed", $"Não consegui renovar o aluguel da ilha {island}", "O saldo da carteira não deu para cobrar mais um período. O contrato entrou em tolerância: renove antes que termine.", $"renewfail:{lease.Id}:{lease.ExpiresAt.UtcTicks}", now);
            }
            if (lease.Status == "active" && lease.GraceDays > 0)
            {
                lease.Status = "grace"; lease.GraceUntil = lease.ExpiresAt.AddDays(lease.GraceDays);
                if (lease.GraceUntil > now)
                {
                    PropertyNotices.Add(db, lease.TenantUserId, "rent_grace", $"O aluguel da ilha {island} venceu: você está no prazo de tolerância", $"Renove até {lease.GraceUntil.Value.ToLocalTime():dd/MM/yyyy HH:mm}; depois dele o contrato termina.", $"grace:{lease.Id}", now);
                    PropertyNotices.Add(db, lease.LandlordUserId, "rent_grace_owner", $"O aluguel da ilha {island} venceu e está em tolerância", $"Se não for renovado até {lease.GraceUntil.Value.ToLocalTime():dd/MM/yyyy HH:mm}, termina sozinho.", $"grace:{lease.Id}:l", now);
                    await db.SaveChangesAsync(ct); done++; continue;
                }
            }
            await FinishAsync(db, storage, lease, "expired", now, ct); done++;
        }
        return done;
    }
}
