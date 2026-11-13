using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Land;

/// <summary>
/// Paying in two phases. <b>Reserve</b>: the buyer's money moves into a hold and that is saved on its own. <b>Settle</b>: in the same unit of work that moves the title (or starts the contract) the hold
/// is paid out to the receivers. If the second phase fails, or never finishes, the hold is <b>released</b>: the buyer gets the money back. So the money never reaches the seller without the title
/// moving, and never stays lost when it does not. A hold the process left behind is given back by <see cref="ReleaseStaleAsync"/>.
/// </summary>
public static class PaymentHolds
{
    public const string HoldAccount = "land_hold";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Phase one. Takes the money out of the payer's wallet into a hold and saves it. A hold already open for the same key (a retry after a crash) is picked up again.</summary>
    public static async Task<(PaymentHold? Hold, string? Error)> ReserveAsync(LifeVerseDbContext db, string key, string purpose, Guid payer, long amount, Guid target, DateTimeOffset now, CancellationToken ct)
    {
        if (amount <= 0) return (null, "bad_price");
        var open = await db.PaymentHolds.Where(h => h.Key == key && h.Status == "held").FirstOrDefaultAsync(ct);
        if (open is not null) return open.PayerUserId == payer && open.Amount == amount && open.Purpose == purpose && open.TargetId == target ? (open, null) : (null, "key_in_use");
        var wallet = await LandCommerce.WalletOfAsync(db, payer, now, ct);
        if (wallet.IsBlocked) return (null, "wallet_blocked");
        if (wallet.AvailableCoins < amount) return (null, "insufficient_balance");
        wallet.AvailableCoins -= amount; wallet.UpdatedAt = now;
        var hold = new PaymentHold { Id = Guid.NewGuid(), Key = key, Purpose = purpose, PayerUserId = payer, Amount = amount, TargetId = target, Status = "held", CreatedAt = now };
        db.PaymentHolds.Add(hold);
        await MoveAsync(db, $"hold:{hold.Id}", purpose + "_hold", payer, amount, fromHold: false, new { holdId = hold.Id, target }, now, ct);
        return await TrySaveAsync(db, hold, ct);
    }

    /// <summary>Phase two, inside the caller's unit of work: pays the receivers out of the hold and closes it. The caller saves.</summary>
    public static async Task<LedgerTransaction> SettleAsync(LifeVerseDbContext db, PaymentHold hold, string key, string type, IReadOnlyList<(Guid? Receiver, long Amount)> parts, object metadata, DateTimeOffset now, CancellationToken ct)
    {
        if (parts.Sum(p => p.Amount) != hold.Amount) throw new InvalidOperationException("The parts must add up to what was reserved.");
        hold.Status = "settled"; hold.ClosedAt = now;
        return await LandCommerce.LedgerAsync(db, key, type, hold.PayerUserId, hold.PayerUserId, parts, metadata, now, ct, HoldAccount);
    }

    /// <summary>Gives the money back to the payer and closes the hold. Safe to call twice: only a hold still open is released. Returns whether it was.</summary>
    public static async Task<bool> ReleaseAsync(LifeVerseDbContext db, Guid holdId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; // start from what is really saved: the failed attempt left nothing worth keeping
        var hold = await db.PaymentHolds.SingleOrDefaultAsync(h => h.Id == holdId, ct); if (hold is null || hold.Status != "held") return false;
        var wallet = await LandCommerce.WalletOfAsync(db, hold.PayerUserId, now, ct); wallet.AvailableCoins += hold.Amount; wallet.UpdatedAt = now;
        hold.Status = "released"; hold.Reason = reason.Length > 64 ? reason[..64] : reason; hold.ClosedAt = now;
        await MoveAsync(db, $"release:{hold.Id}", hold.Purpose + "_release", hold.PayerUserId, hold.Amount, fromHold: true, new { holdId = hold.Id, reason }, now, ct);
        try { await db.SaveChangesAsync(ct); return true; }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached; // somebody closed it first (settled, or another release): nothing more to do
            return false;
        }
    }

    /// <summary>Gives back the holds that stayed open (the server stopped between the phases). Returns how many.</summary>
    public static async Task<int> ReleaseStaleAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var stale = await db.PaymentHolds.AsNoTracking().Where(h => h.Status == "held" && h.CreatedAt < now - StaleAfter).Select(h => h.Id).Take(100).ToListAsync(ct);
        var released = 0; foreach (var id in stale) if (await ReleaseAsync(db, id, "stale", now, ct)) released++;
        return released;
    }

    /// <summary>Saves the second phase. When it cannot be saved the hold is released, so the payer is paid back and the failure is reported as usual.</summary>
    public static async Task<CommerceResult> FinishAsync(LifeVerseDbContext db, PaymentHold hold, object? data, DateTimeOffset now, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return CommerceResult.Done(data); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            await ReleaseAsync(db, hold.Id, "conflict", now, ct);
            return CommerceResult.Fail("conflict");
        }
    }

    /// <summary>One ledger transaction between the payer's account and the hold account (into the hold, or back out).</summary>
    private static async Task MoveAsync(LifeVerseDbContext db, string key, string type, Guid payer, long amount, bool fromHold, object metadata, DateTimeOffset now, CancellationToken ct)
    {
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = type, Status = "completed", Amount = amount, InitiatorUserId = payer, MetadataJson = JsonSerializer.Serialize(metadata), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        var user = await LandCommerce.AccountAsync(db, payer, "user", now, ct); var hold = await LandCommerce.AccountAsync(db, null, HoldAccount, now, ct);
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = fromHold ? hold.Id : user.Id, Debit = amount, Credit = 0, CreatedAt = now });
        db.LedgerEntries.Add(new LedgerEntry { Id = Guid.NewGuid(), TransactionId = transaction.Id, AccountId = fromHold ? user.Id : hold.Id, Debit = 0, Credit = amount, CreatedAt = now });
    }

    private static async Task<(PaymentHold? Hold, string? Error)> TrySaveAsync(LifeVerseDbContext db, PaymentHold hold, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return (hold, null); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
        {
            foreach (var entry in db.ChangeTracker.Entries().ToList()) entry.State = EntityState.Detached;
            return (null, "conflict");
        }
    }
}
