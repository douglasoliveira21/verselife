using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// The platform plan that keeps an island running, managed from the island's own window: what the island has now, which plans can be bought, buying a first plan and renewing it.
/// Only the owner pays (a renter of the island does not). Money goes from the owner's wallet to the platform in one ledger transaction, a repeated key does nothing twice,
/// and a plan can be renewed while it runs and during the week of recovery after it ends.
/// </summary>
public static class IslandHosting
{
    /// <summary>The most a plan can be paid ahead: no one pays years in advance by clicking a button too many times.</summary>
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(365);
    public const string PlatformAccount = "platform_revenue";

    /// <summary>active while it runs, recovery during the week after it ends (still renewable), ended after that.</summary>
    public static string StatusOf(DateTimeOffset expiresAt, DateTimeOffset recoveryUntil, DateTimeOffset now) => expiresAt > now ? "active" : recoveryUntil > now ? "recovery" : "ended";
    public static string StatusOf(IslandRental rental, DateTimeOffset now) => StatusOf(rental.ExpiresAt, rental.RecoveryUntil, now);

    private static async Task<IslandRental?> LatestAsync(LifeVerseDbContext db, Guid islandId, CancellationToken ct) =>
        (await db.IslandRentals.Where(r => r.IslandId == islandId && r.Status == "active").ToListAsync(ct)).OrderByDescending(r => r.ExpiresAt).FirstOrDefault();

    private static object PlanView(IslandRentalPlan p) => new { id = p.Id, name = p.Name, price = p.PriceCoins, days = p.DurationDays, maxSize = p.MaxSize, maxObjects = p.MaxObjects, maxPlayers = p.MaxPlayers };

    /// <summary>What the island window shows: the plan it has, the plans on offer and the owner's balance. Only the owner and the island's staff may look.</summary>
    public static async Task<CommerceResult> StateAsync(LifeVerseDbContext db, Guid islandId, Guid viewer, TimeProvider clock, CancellationToken ct)
    {
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return CommerceResult.Fail("not_found");
        var now = clock.GetUtcNow();
        if (!await LandOverview.IsStaffAsync(db, island, viewer, now, ct)) return CommerceResult.Fail("forbidden");
        var current = await LatestAsync(db, islandId, ct); IslandRentalPlan? plan = current is null ? null : await db.IslandRentalPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == current.PlanId, ct);
        var plans = (await db.IslandRentalPlans.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct)).OrderBy(p => p.PriceCoins).ToList();
        var balance = await db.Wallets.AsNoTracking().Where(w => w.UserId == island.OwnerUserId).Select(w => (long?)w.AvailableCoins).FirstOrDefaultAsync(ct) ?? 0;
        var isOwner = island.OwnerUserId == viewer;
        var status = current is null ? "none" : StatusOf(current, now);
        return CommerceResult.Done(new
        {
            isOwner, status, canSubscribe = isOwner && (status is "none" or "ended"), canRenew = isOwner && current is not null && (status == "active" || status == "recovery"),
            balance = isOwner ? balance : (long?)null,
            current = current is null || plan is null ? null : new { id = current.Id, plan = PlanView(plan), startsAt = current.StartsAt, expiresAt = current.ExpiresAt, recoveryUntil = current.RecoveryUntil, status },
            plans = plans.Select(PlanView),
        });
    }

    private static LedgerTransaction Charge(LifeVerseDbContext db, string key, string type, Guid payer, long amount, object metadata, DateTimeOffset now, LedgerAccount payerAccount, LedgerAccount platform)
    {
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = type, Status = "completed", Amount = amount, InitiatorUserId = payer, MetadataJson = JsonSerializer.Serialize(metadata), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = payerAccount.Id, Debit = amount, Credit = 0, CreatedAt = now });
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = platform.Id, Debit = 0, Credit = amount, CreatedAt = now });
        return transaction;
    }

    /// <summary>Buys the first plan of an island (or a new one after the last ended). With a plan already running, renew it instead.</summary>
    public static async Task<CommerceResult> SubscribeAsync(LifeVerseDbContext db, Guid islandId, Guid actor, Guid planId, string? key, TimeProvider clock, CancellationToken ct)
    {
        if (!LandCommerce.KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { islandId, alreadyDone = true });
        var island = await db.Islands.SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        var plan = await db.IslandRentalPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.IsActive, ct); if (plan is null) return CommerceResult.Fail("plan_not_found");
        var now = clock.GetUtcNow();
        if (await LatestAsync(db, islandId, ct) is { } existing && StatusOf(existing, now) != "ended") return CommerceResult.Fail("already_hosted");
        var wallet = await LandCommerce.WalletOfAsync(db, actor, now, ct); if (wallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (wallet.AvailableCoins < plan.PriceCoins) return CommerceResult.Fail("insufficient_balance");
        wallet.AvailableCoins -= plan.PriceCoins; wallet.UpdatedAt = now;
        var payerAccount = await LandCommerce.AccountAsync(db, actor, "user", now, ct); var platform = await LandCommerce.AccountAsync(db, null, PlatformAccount, now, ct);
        Charge(db, key!, "island_rental", actor, plan.PriceCoins, new { islandId, planId }, now, payerAccount, platform);
        var rental = new IslandRental { Id = Guid.NewGuid(), IslandId = islandId, UserId = actor, PlanId = plan.Id, StartsAt = now, ExpiresAt = now.AddDays(plan.DurationDays), RecoveryUntil = now.AddDays(plan.DurationDays + 7), Status = "active" };
        db.IslandRentals.Add(rental);
        PropertyNotices.Add(db, actor, "hosting_started", $"A ilha {island.Name} tem hospedagem: {plan.Name}", $"Vale até {rental.ExpiresAt.ToLocalTime():dd/MM/yyyy HH:mm}. Foram cobrados {LandCommerce.Money(plan.PriceCoins)}.", $"hosting_started:{rental.Id}", now);
        return await SaveAsync(db, ct, new { islandId, planId = plan.Id, expiresAt = rental.ExpiresAt });
    }

    /// <summary>Pays one more period of the plan the island has, while it runs or during its week of recovery. The time is added after the end (or after now, if it already ended).</summary>
    public static async Task<CommerceResult> RenewAsync(LifeVerseDbContext db, Guid islandId, Guid actor, string? key, TimeProvider clock, CancellationToken ct)
    {
        if (!LandCommerce.KeyOk(key)) return CommerceResult.Fail("bad_key");
        if (await db.LedgerTransactions.AnyAsync(t => t.IdempotencyKey == key, ct)) return CommerceResult.Done(new { islandId, alreadyDone = true });
        var island = await db.Islands.SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null) return CommerceResult.Fail("not_found");
        if (island.OwnerUserId != actor) return CommerceResult.Fail("forbidden");
        var now = clock.GetUtcNow();
        var rental = await LatestAsync(db, islandId, ct); if (rental is null) return CommerceResult.Fail("no_hosting");
        if (StatusOf(rental, now) == "ended") return CommerceResult.Fail("hosting_ended"); // too late: choose a plan again
        var plan = await db.IslandRentalPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == rental.PlanId, ct); if (plan is null) return CommerceResult.Fail("plan_not_found");
        var from = rental.ExpiresAt > now ? rental.ExpiresAt : now; var until = from.AddDays(plan.DurationDays);
        if (until - now > MaxAhead) return CommerceResult.Fail("too_far");
        var wallet = await LandCommerce.WalletOfAsync(db, actor, now, ct); if (wallet.IsBlocked) return CommerceResult.Fail("wallet_blocked");
        if (wallet.AvailableCoins < plan.PriceCoins) return CommerceResult.Fail("insufficient_balance");
        wallet.AvailableCoins -= plan.PriceCoins; wallet.UpdatedAt = now;
        var payerAccount = await LandCommerce.AccountAsync(db, actor, "user", now, ct); var platform = await LandCommerce.AccountAsync(db, null, PlatformAccount, now, ct);
        Charge(db, key!, "island_rental_renewal", actor, plan.PriceCoins, new { islandId, rentalId = rental.Id, planId = plan.Id }, now, payerAccount, platform);
        rental.ExpiresAt = until; rental.RecoveryUntil = until.AddDays(7);
        return await SaveAsync(db, ct, new { islandId, expiresAt = rental.ExpiresAt });
    }

    private static async Task<CommerceResult> SaveAsync(LifeVerseDbContext db, CancellationToken ct, object data)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return CommerceResult.Fail("conflict");
        }
    }
}
