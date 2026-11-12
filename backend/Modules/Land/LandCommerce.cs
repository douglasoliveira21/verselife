using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

public sealed record CommerceResult(bool Ok, string? Error = null, object? Data = null)
{
    public static CommerceResult Done(object? data = null) => new(true, null, data);
    public static CommerceResult Fail(string error) => new(false, error);
}

public sealed record SaleOffer(long Price, Guid? BuyerUserId, string? Description, bool IncludeObjects);
public sealed record RentOffer(long PricePerPeriod, int PeriodDays, int MinPeriods, int MaxPeriods, long DepositCoins, int GraceDays, bool AutoRenewAllowed);

/// <summary>
/// Buying, selling and renting land. Every money movement is one unit of work: the wallets, the ledger, the parcel and the objects change
/// together or not at all, and two people reaching for the same parcel (or one wallet being spent twice) cannot both win, because the parcel
/// revision and the wallet balance are concurrency tokens. A repeated request with the same key does nothing the second time.
/// </summary>
public static class LandCommerce
{
    public const long MaxPrice = 1_000_000_000;

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static async Task<IslandParcel?> ParcelAsync(LifeVerseDbContext db, Guid parcelId, CancellationToken ct) =>
        await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == parcelId && p.DeletedAt == null, ct);

    internal static async Task<Wallet> WalletOfAsync(LifeVerseDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var wallet = await db.Wallets.SingleOrDefaultAsync(w => w.UserId == userId, ct);
        if (wallet is not null) return wallet;
        wallet = new Wallet { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now, UpdatedAt = now };
        db.Wallets.Add(wallet);
        return wallet;
    }

    internal static async Task<LedgerAccount> AccountAsync(LifeVerseDbContext db, Guid? userId, string type, DateTimeOffset now, CancellationToken ct)
    {
        var local = db.LedgerAccounts.Local.FirstOrDefault(a => a.UserId == userId && a.AccountType == type);
        if (local is not null) return local;
        var account = await db.LedgerAccounts.Where(a => a.UserId == userId && a.AccountType == type).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).FirstOrDefaultAsync(ct);
        if (account is not null) return account;
        account = new LedgerAccount { Id = Guid.NewGuid(), UserId = userId, AccountType = type, CreatedAt = now };
        db.LedgerAccounts.Add(account);
        return account;
    }

    /// <summary>One ledger transaction: the payer is debited the sum of the parts and each receiver credited its part. A null receiver is the land escrow (deposits).</summary>
    internal static async Task<LedgerTransaction> LedgerAsync(LifeVerseDbContext db, string key, string type, Guid initiator, Guid payer, IReadOnlyList<(Guid? Receiver, long Amount)> parts, object metadata, DateTimeOffset now, CancellationToken ct)
    {
        var total = parts.Sum(p => p.Amount);
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = type, Status = "completed", Amount = total, InitiatorUserId = initiator, MetadataJson = JsonSerializer.Serialize(metadata), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        var payerAccount = await AccountAsync(db, payer, "user", now, ct);
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = payerAccount.Id, Debit = total, Credit = 0, CreatedAt = now });
        foreach (var (receiver, amount) in parts.Where(p => p.Amount > 0))
        {
            var account = receiver is null ? await AccountAsync(db, null, "land_escrow", now, ct) : await AccountAsync(db, receiver, "user", now, ct);
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = account.Id, Debit = 0, Credit = amount, CreatedAt = now });
        }
        return transaction;
    }

    private static void Record(LifeVerseDbContext db, Guid parcelId, string type, Guid? from, Guid? to, long amount, object data, Guid? ledger, DateTimeOffset now) =>
        db.ParcelTransactions.Add(new ParcelTransaction { Id = Guid.NewGuid(), ParcelId = parcelId, Type = type, FromUserId = from, ToUserId = to, Amount = amount, DataJson = JsonSerializer.Serialize(data), LedgerTransactionId = ledger, CreatedAt = now });

    private static void Touch(LifeVerseDbContext db, IslandParcel parcel, Guid actor, string operation, object data, DateTimeOffset now)
    {
        parcel.Revision++; parcel.UpdatedAt = now;
        db.ParcelChanges.Add(new ParcelChange { Id = Guid.NewGuid(), ParcelId = parcel.Id, ActorUserId = actor, Operation = operation, PayloadJson = JsonSerializer.Serialize(data), Revision = parcel.Revision, CreatedAt = now });
    }

    private static async Task<CommerceResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object? data = null)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            // someone else got there first (the parcel or a wallet changed under us): nothing of ours was saved
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }

    private static Task<bool> HasLiveRentalAsync(LifeVerseDbContext db, Guid parcelId, CancellationToken ct) => db.ParcelRentals.AnyAsync(r => r.ParcelId == parcelId && (r.Status == "active" || r.Status == "grace"), ct);

    internal static bool KeyOk(string? key) => key is { Length: >= 8 and <= 128 };

    // ---- selling -----------------------------------------------------------------------------------------------

    public static async Task<CommerceResult> OfferForSaleAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, SaleOffer offer, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (parcel.GroupId is not null) return CommerceResult.Fail("group_owned"); // what a group owns is not sold by one person; the group's treasury comes later
        if (offer.Price is < 1 or > MaxPrice) return CommerceResult.Fail("bad_price");
        if (offer.BuyerUserId == actor) return CommerceResult.Fail("bad_buyer");
        if (await HasLiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow();
        parcel.SalePrice = offer.Price; parcel.SaleBuyerUserId = offer.BuyerUserId; parcel.SaleIncludesObjects = offer.IncludeObjects;
        parcel.SaleDescription = (offer.Description ?? string.Empty).Trim()[..Math.Min(2000, (offer.Description ?? string.Empty).Trim().Length)];
        parcel.RentPrice = null; // a parcel is either offered for sale or for rent
        Touch(db, parcel, actor, "list_sale", new { price = offer.Price, exclusive = offer.BuyerUserId is not null }, now);
        Record(db, parcelId, "sale_listed", actor, null, offer.Price, new { exclusive = offer.BuyerUserId is not null, offer.IncludeObjects }, null, now);
        return await SaveAsync(db, ct, new { parcelId, price = offer.Price });
    }

    public static async Task<CommerceResult> CancelSaleAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (parcel.SalePrice is null) return CommerceResult.Fail("not_for_sale");
        var now = clock.GetUtcNow();
        parcel.SalePrice = null; parcel.SaleBuyerUserId = null; parcel.SaleDescription = string.Empty;
        Touch(db, parcel, actor, "cancel_sale", new { }, now);
        Record(db, parcelId, "sale_cancelled", actor, null, 0, new { }, null, now);
        return await SaveAsync(db, ct);
    }

    /// <summary>The purchase. <paramref name="expectedPrice"/> is the price the buyer saw: if it changed since, nothing is charged.</summary>
    public static async Task<CommerceResult> BuyAsync(LifeVerseDbContext db, Guid parcelId, Guid buyer, string? key, long expectedPrice, TimeProvider clock, CancellationToken ct)
    {
        if (!KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { parcelId, alreadyDone = true });
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (parcel.SalePrice is not { } price) return CommerceResult.Fail("not_for_sale");
        if (parcel.OwnerUserId == buyer) return CommerceResult.Fail("own_parcel");
        if (parcel.SaleBuyerUserId is { } exclusive && exclusive != buyer) return CommerceResult.Fail("not_for_you");
        if (price != expectedPrice) return CommerceResult.Fail("price_changed");
        if (await HasLiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow();
        var seller = parcel.OwnerUserId;
        var buyerWallet = await WalletOfAsync(db, buyer, now, ct); var sellerWallet = await WalletOfAsync(db, seller, now, ct);
        if (buyerWallet.IsBlocked || sellerWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (buyerWallet.AvailableCoins < price) return CommerceResult.Fail("insufficient_balance");

        buyerWallet.AvailableCoins -= price; sellerWallet.AvailableCoins += price; buyerWallet.UpdatedAt = sellerWallet.UpdatedAt = now;
        var ledger = await LedgerAsync(db, key!, "land_sale", buyer, buyer, [(seller, price)], new { parcelId, seller }, now, ct);

        // the objects the seller left on the parcel go with it, when the sale says so
        var moved = 0;
        if (parcel.SaleIncludesObjects)
        {
            var facts = (await LandService.LoadFactsAsync(db, parcel.IslandId, ct)).FirstOrDefault(f => f.Id == parcelId);
            if (facts is not null)
            {
                var inBox = await db.BuildObjects.Where(o => o.IslandId == parcel.IslandId && o.DeletedAt == null && o.OwnerUserId == seller && o.PositionX >= facts.MinX && o.PositionX < facts.MaxX && o.PositionZ >= facts.MinZ && o.PositionZ < facts.MaxZ).ToListAsync(ct);
                foreach (var item in inBox.Where(o => facts.Contains(o.PositionX, o.PositionZ)))
                {
                    item.OwnerUserId = buyer; item.Revision++; moved++;
                    db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = parcel.IslandId, ObjectId = item.Id, ActorUserId = buyer, Operation = "land_transfer", PayloadJson = JsonSerializer.Serialize(new { from = seller, to = buyer, parcelId }), Revision = item.Revision, CreatedAt = now });
                }
            }
        }
        parcel.OwnerUserId = buyer; parcel.GroupId = null;
        parcel.SalePrice = null; parcel.SaleBuyerUserId = null; parcel.SaleDescription = string.Empty;
        Touch(db, parcel, buyer, "sold", new { seller, buyer, price, objectsMoved = moved }, now);
        Record(db, parcelId, "sale", buyer, seller, price, new { objectsMoved = moved }, ledger.Id, now);
        return await SaveAsync(db, ct, new { parcelId, price, objectsMoved = moved });
    }

    // ---- renting -----------------------------------------------------------------------------------------------

    public static async Task<CommerceResult> OfferForRentAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, RentOffer offer, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (parcel.GroupId is not null) return CommerceResult.Fail("group_owned");
        if (offer.PricePerPeriod is < 1 or > MaxPrice || offer.DepositCoins is < 0 or > MaxPrice) return CommerceResult.Fail("bad_price");
        if (offer.PeriodDays is < 1 or > 365 || offer.MinPeriods < 1 || offer.MaxPeriods < offer.MinPeriods || offer.MaxPeriods > 120 || offer.GraceDays is < 0 or > 60) return CommerceResult.Fail("bad_terms");
        if (await HasLiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow();
        parcel.RentPrice = offer.PricePerPeriod; parcel.RentPeriodDays = offer.PeriodDays; parcel.RentMinPeriods = offer.MinPeriods; parcel.RentMaxPeriods = offer.MaxPeriods;
        parcel.RentDeposit = offer.DepositCoins; parcel.RentGraceDays = offer.GraceDays; parcel.RentAutoRenewAllowed = offer.AutoRenewAllowed;
        parcel.SalePrice = null; parcel.SaleBuyerUserId = null;
        Touch(db, parcel, actor, "list_rent", new { offer.PricePerPeriod, offer.PeriodDays }, now);
        Record(db, parcelId, "rent_listed", actor, null, offer.PricePerPeriod, new { offer.PeriodDays, offer.MinPeriods, offer.MaxPeriods, offer.DepositCoins }, null, now);
        return await SaveAsync(db, ct, new { parcelId });
    }

    public static async Task<CommerceResult> CancelRentOfferAsync(LifeVerseDbContext db, Guid parcelId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (!await LandAuthority.CanManageAsync(db, parcel, actor, ct)) return CommerceResult.Fail("forbidden");
        if (parcel.RentPrice is null) return CommerceResult.Fail("not_for_rent");
        var now = clock.GetUtcNow();
        parcel.RentPrice = null;
        Touch(db, parcel, actor, "cancel_rent_offer", new { }, now);
        Record(db, parcelId, "rent_cancelled", actor, null, 0, new { }, null, now);
        return await SaveAsync(db, ct);
    }

    public static async Task<CommerceResult> RentAsync(LifeVerseDbContext db, Guid parcelId, Guid tenant, int periods, bool autoRenew, string? key, long expectedPrice, TimeProvider clock, CancellationToken ct)
    {
        if (!KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { parcelId, alreadyDone = true });
        var parcel = await ParcelAsync(db, parcelId, ct); if (parcel is null) return CommerceResult.Fail("not_found");
        if (parcel.RentPrice is not { } price) return CommerceResult.Fail("not_for_rent");
        if (parcel.OwnerUserId == tenant) return CommerceResult.Fail("own_parcel");
        if (price != expectedPrice) return CommerceResult.Fail("price_changed");
        if (periods < parcel.RentMinPeriods || periods > parcel.RentMaxPeriods) return CommerceResult.Fail("bad_periods");
        if (await HasLiveRentalAsync(db, parcelId, ct)) return CommerceResult.Fail("rented");
        var now = clock.GetUtcNow();
        var rent = price * periods; var deposit = parcel.RentDeposit; var total = rent + deposit;
        var tenantWallet = await WalletOfAsync(db, tenant, now, ct); var landlordWallet = await WalletOfAsync(db, parcel.OwnerUserId, now, ct);
        if (tenantWallet.IsBlocked || landlordWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (tenantWallet.AvailableCoins < total) return CommerceResult.Fail("insufficient_balance");
        tenantWallet.AvailableCoins -= total; landlordWallet.AvailableCoins += rent; tenantWallet.UpdatedAt = landlordWallet.UpdatedAt = now;
        var ledger = await LedgerAsync(db, key!, "land_rental", tenant, tenant, [(parcel.OwnerUserId, rent), (null, deposit)], new { parcelId, periods }, now, ct);
        var rental = new ParcelRental
        {
            Id = Guid.NewGuid(), ParcelId = parcelId, LandlordUserId = parcel.OwnerUserId, TenantUserId = tenant, PricePerPeriod = price, PeriodDays = parcel.RentPeriodDays, DepositCoins = deposit, GraceDays = parcel.RentGraceDays,
            Periods = periods, AutoRenew = autoRenew && parcel.RentAutoRenewAllowed, Status = "active", StartsAt = now, ExpiresAt = now.AddDays((double)parcel.RentPeriodDays * periods), CreatedAt = now
        };
        db.ParcelRentals.Add(rental);
        parcel.RentPrice = null;
        Touch(db, parcel, tenant, "rented", new { tenant, periods, rental.ExpiresAt }, now);
        Record(db, parcelId, "rent_start", tenant, parcel.OwnerUserId, rent, new { periods, deposit, rentalId = rental.Id }, ledger.Id, now);
        return await SaveAsync(db, ct, new { rentalId = rental.Id, expiresAt = rental.ExpiresAt, paid = total });
    }

    public static async Task<CommerceResult> RenewAsync(LifeVerseDbContext db, Guid rentalId, Guid tenant, int periods, string? key, TimeProvider clock, CancellationToken ct)
    {
        if (!KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { rentalId, alreadyDone = true });
        var rental = await db.ParcelRentals.SingleOrDefaultAsync(r => r.Id == rentalId && r.TenantUserId == tenant && (r.Status == "active" || r.Status == "grace"), ct);
        if (rental is null) return CommerceResult.Fail("not_found");
        if (periods is < 1 or > 120) return CommerceResult.Fail("bad_periods");
        return await ChargeRenewalAsync(db, rental, periods, key!, "land_renewal", clock.GetUtcNow(), ct);
    }

    private static async Task<CommerceResult> ChargeRenewalAsync(LifeVerseDbContext db, ParcelRental rental, int periods, string key, string type, DateTimeOffset now, CancellationToken ct)
    {
        var cost = rental.PricePerPeriod * periods;
        var tenantWallet = await WalletOfAsync(db, rental.TenantUserId, now, ct); var landlordWallet = await WalletOfAsync(db, rental.LandlordUserId, now, ct);
        if (tenantWallet.IsBlocked || landlordWallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (tenantWallet.AvailableCoins < cost) return CommerceResult.Fail("insufficient_balance");
        tenantWallet.AvailableCoins -= cost; landlordWallet.AvailableCoins += cost; tenantWallet.UpdatedAt = landlordWallet.UpdatedAt = now;
        var ledger = await LedgerAsync(db, key, type, rental.TenantUserId, rental.TenantUserId, [(rental.LandlordUserId, cost)], new { rentalId = rental.Id, periods }, now, ct);
        var from = rental.ExpiresAt > now ? rental.ExpiresAt : now;
        rental.ExpiresAt = from.AddDays((double)rental.PeriodDays * periods); rental.Periods += periods; rental.Status = "active"; rental.GraceUntil = null;
        Record(db, rental.ParcelId, "rent_payment", rental.TenantUserId, rental.LandlordUserId, cost, new { periods, rentalId = rental.Id, auto = type == "land_autorenew" }, ledger.Id, now);
        return await SaveAsync(db, ct, new { rentalId = rental.Id, expiresAt = rental.ExpiresAt, paid = cost });
    }

    /// <summary>The tenant may leave at any time; the landlord may only close a contract that has run out. The deposit goes back to the tenant.</summary>
    public static async Task<CommerceResult> EndAsync(LifeVerseDbContext db, AssetStorageService? storage, Guid rentalId, Guid actor, TimeProvider clock, CancellationToken ct)
    {
        var rental = await db.ParcelRentals.SingleOrDefaultAsync(r => r.Id == rentalId && (r.Status == "active" || r.Status == "grace"), ct);
        if (rental is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow();
        if (rental.TenantUserId == actor) return await FinishAsync(db, storage, rental, "tenant", now, ct);
        if (rental.LandlordUserId == actor) return rental.ExpiresAt <= now ? await FinishAsync(db, storage, rental, "landlord", now, ct) : CommerceResult.Fail("still_running");
        return CommerceResult.Fail("forbidden");
    }

    private static async Task<CommerceResult> FinishAsync(LifeVerseDbContext db, AssetStorageService? storage, ParcelRental rental, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var parcel = await db.IslandParcels.SingleOrDefaultAsync(p => p.Id == rental.ParcelId, ct);
        rental.Status = "ended"; rental.EndReason = reason; rental.EndedAt = now; rental.GraceUntil = null;
        Guid? ledgerId = null;
        if (rental.DepositCoins > 0)
        {
            var tenantWallet = await WalletOfAsync(db, rental.TenantUserId, now, ct);
            tenantWallet.AvailableCoins += rental.DepositCoins; tenantWallet.UpdatedAt = now;
            var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = $"land-deposit-refund:{rental.Id}", Type = "land_deposit_refund", Status = "completed", Amount = rental.DepositCoins, InitiatorUserId = rental.TenantUserId, MetadataJson = JsonSerializer.Serialize(new { rentalId = rental.Id, reason }), CreatedAt = now, CompletedAt = now };
            db.LedgerTransactions.Add(transaction); ledgerId = transaction.Id;
            var escrow = await AccountAsync(db, null, "land_escrow", now, ct); var tenantAccount = await AccountAsync(db, rental.TenantUserId, "user", now, ct);
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = escrow.Id, Debit = rental.DepositCoins, Credit = 0, CreatedAt = now });
            db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = tenantAccount.Id, Debit = 0, Credit = rental.DepositCoins, CreatedAt = now });
        }
        Record(db, rental.ParcelId, "rent_end", rental.LandlordUserId, rental.TenantUserId, rental.DepositCoins, new { reason, rentalId = rental.Id }, ledgerId, now);
        if (parcel is not null) Touch(db, parcel, rental.TenantUserId, "rent_ended", new { reason }, now);
        var saved = await SaveAsync(db, ct, new { rentalId = rental.Id, refunded = rental.DepositCoins });
        if (!saved.Ok || parcel is null || storage is null) return saved;

        // what the tenant left on the land goes back to the tenant's inventory; nothing is deleted for good
        var facts = (await LandService.LoadFactsAsync(db, parcel.IslandId, ct)).FirstOrDefault(f => f.Id == parcel.Id);
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == parcel.IslandId, ct);
        if (facts is null || island is null) return saved;
        var candidates = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == island.Id && o.DeletedAt == null && o.OwnerUserId == rental.TenantUserId && o.PositionX >= facts.MinX && o.PositionX < facts.MaxX && o.PositionZ >= facts.MinZ && o.PositionZ < facts.MaxZ).Select(o => new { o.Id, o.PositionX, o.PositionZ }).ToListAsync(ct);
        var ids = candidates.Where(o => facts.Contains(o.PositionX, o.PositionZ)).Select(o => o.Id).ToArray();
        for (var i = 0; i < ids.Length; i += BuildPackageLimits.MaxReturnAtOnce)
            await BuildOperations.ReturnForLandAsync(island, rental.TenantUserId, ids[i..Math.Min(ids.Length, i + BuildPackageLimits.MaxReturnAtOnce)], db, storage, now, ct);
        return CommerceResult.Done(new { rentalId = rental.Id, refunded = rental.DepositCoins, objectsReturned = ids.Length });
    }

    /// <summary>
    /// Run by the server every minute, so contracts expire even when nobody is online: an overdue rental renews by itself when the tenant chose
    /// that and can pay, otherwise it goes into the grace period; when the grace period is over the contract ends.
    /// </summary>
    public static async Task<int> ProcessDueAsync(LifeVerseDbContext db, AssetStorageService? storage, DateTimeOffset now, CancellationToken ct)
    {
        var done = 0;
        var dueIds = await db.ParcelRentals.Where(r => r.Status == "active" && r.ExpiresAt <= now).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in dueIds)
        {
            var rental = await db.ParcelRentals.SingleAsync(r => r.Id == id, ct);
            if (rental.AutoRenew)
            {
                var renewed = await ChargeRenewalAsync(db, rental, 1, $"land-auto:{rental.Id}:{rental.ExpiresAt.UtcTicks}", "land_autorenew", now, ct);
                if (renewed.Ok) { done++; continue; }
                db.ChangeTracker.Clear();
                rental = await db.ParcelRentals.SingleAsync(r => r.Id == rental.Id, ct);
            }
            rental.Status = "grace"; rental.GraceUntil = rental.ExpiresAt.AddDays(rental.GraceDays);
            await db.SaveChangesAsync(ct); done++;
        }
        var overIds = await db.ParcelRentals.Where(r => r.Status == "grace" && r.GraceUntil != null && r.GraceUntil <= now).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in overIds) { await FinishAsync(db, storage, await db.ParcelRentals.SingleAsync(r => r.Id == id, ct), "expired", now, ct); done++; }
        return done;
    }
}

/// <summary>Calls <see cref="LandCommerce.ProcessDueAsync"/> once a minute.</summary>
public sealed class LandRentalWorker(IServiceScopeFactory scopes, ILogger<LandRentalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>();
                var count = await LandCommerce.ProcessDueAsync(db, scope.ServiceProvider.GetService<AssetStorageService>(), DateTimeOffset.UtcNow, stoppingToken);
                count += await IslandCommerce.ProcessDueAsync(db, scope.ServiceProvider.GetService<AssetStorageService>(), DateTimeOffset.UtcNow, stoppingToken);
                if (count > 0) logger.LogInformation("event=land_rentals_processed count={Count}", count);
                var returned = await LandAutoReturn.RunAsync(db, scope.ServiceProvider.GetService<AssetStorageService>(), DateTimeOffset.UtcNow, stoppingToken);
                if (returned > 0) logger.LogInformation("event=land_auto_return count={Count}", returned);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=land_rentals_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
