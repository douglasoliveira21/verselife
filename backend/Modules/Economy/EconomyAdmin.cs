using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

public sealed record WalletBlockRequest(bool Blocked, string Reason);
public sealed record WalletAdjustRequest(long Amount, string Reason, string IdempotencyKey);

/// <summary>The administration of the economy ("Economia VerseCoin"): the numbers of the circulation, a look at one wallet, blocking it, and adjustments that always carry a reason and leave a ledger entry and an audit record. Nothing here changes a balance silently.</summary>
public static class EconomyAdmin
{
    public static IEndpointRouteBuilder MapEconomyAdmin(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/economy").WithTags("Economy administration").RequireAuthorization();
        admin.MapGet("/summary", SummaryAsync); admin.MapGet("/wallets/{userId:guid}", WalletAsync);
        admin.MapPost("/wallets/{userId:guid}/block", BlockAsync); admin.MapPost("/wallets/{userId:guid}/adjust", AdjustAsync);
        return endpoints;
    }

    internal static async Task<object> SummaryDataAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddHours(-24);
        var inCirculation = await db.Wallets.SumAsync(x => (long?)x.AvailableCoins, ct) ?? 0;
        var issued = await db.LedgerTransactions.Where(x => x.Type == "coin_purchase" && x.Status == "completed").SumAsync(x => (long?)x.Amount, ct) ?? 0;
        var takenBack = await db.LedgerTransactions.Where(x => x.Type == "coin_refund" && x.Status == "completed").SumAsync(x => (long?)x.Amount, ct) ?? 0;
        var adjusted = await db.LedgerTransactions.Where(x => x.Type == "admin_adjustment" && x.Status == "completed").SumAsync(x => (long?)x.Amount, ct) ?? 0;
        return new
        {
            coinsInWallets = inCirculation, wallets = await db.Wallets.CountAsync(ct), blockedWallets = await db.Wallets.CountAsync(x => x.IsBlocked, ct),
            coinsSoldTotal = issued, coinsRefundedTotal = takenBack, adminAdjustmentsTotal = adjusted,
            transactionsLast24h = await db.LedgerTransactions.CountAsync(x => x.CreatedAt >= since, ct), volumeLast24h = await db.LedgerTransactions.Where(x => x.CreatedAt >= since).SumAsync(x => (long?)x.Amount, ct) ?? 0,
            pendingCoinOrders = await db.CoinOrders.CountAsync(x => x.Status == "pending", ct), ordersInReview = await db.CoinOrders.CountAsync(x => x.Status == "chargeback_review", ct),
            revenueCentsTotal = await db.CoinOrders.Where(x => x.Status == "paid").SumAsync(x => (long?)x.PriceCents, ct) ?? 0
        };
    }

    private static async Task<IResult> SummaryAsync(HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !AdminEndpoints.Admin(c, out _) ? Results.Forbid() : Results.Ok(await SummaryDataAsync(db, clock.GetUtcNow(), ct));

    private static async Task<IResult> WalletAsync(Guid userId, HttpContext c, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!AdminEndpoints.Admin(c, out _)) return Results.Forbid();
        var wallet = await db.Wallets.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct); if (wallet is null) return Results.NotFound();
        var recent = await db.LedgerTransactions.AsNoTracking().Where(x => x.InitiatorUserId == userId || x.CounterpartyUserId == userId).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
        return Results.Ok(new { wallet.UserId, wallet.AvailableCoins, wallet.IsBlocked, wallet.UpdatedAt, recent = recent.Select(x => new { x.Id, x.Type, x.Status, x.Amount, x.CreatedAt, direction = EconomyEndpoints.Direction(x, userId) }) });
    }

    private static async Task<IResult> BlockAsync(Guid userId, [FromBody] WalletBlockRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!AdminEndpoints.Admin(c, out var admin)) return Results.Forbid();
        if (request.Reason is not { Length: >= 3 and <= 200 }) return Results.BadRequest(new { error = "reason_required" });
        var wallet = await db.Wallets.SingleOrDefaultAsync(x => x.UserId == userId, ct); if (wallet is null) return Results.NotFound();
        wallet.IsBlocked = request.Blocked; wallet.UpdatedAt = clock.GetUtcNow();
        await AdminEndpoints.Audit(db, admin, request.Blocked ? "wallet_block" : "wallet_unblock", "wallet", wallet.Id, clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct); return Results.Ok(new { wallet.IsBlocked });
    }

    private static async Task<IResult> AdjustAsync(Guid userId, [FromBody] WalletAdjustRequest request, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!AdminEndpoints.Admin(c, out var admin)) return Results.Forbid();
        if (request.Amount == 0 || Math.Abs(request.Amount) > EconomyEndpoints.MaxPriceCoins || request.Reason is not { Length: >= 3 and <= 200 } || request.IdempotencyKey is not { Length: >= 8 and <= 128 }) return Results.BadRequest(new { error = "invalid_adjustment" });
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var key = $"admin-adjust:{request.IdempotencyKey}";
        if (await db.LedgerTransactions.AnyAsync(x => x.IdempotencyKey == key, ct)) return Results.Ok(new { status = "already_processed" });
        var wallet = await db.Wallets.SingleOrDefaultAsync(x => x.UserId == userId, ct); if (wallet is null) return Results.NotFound();
        if (wallet.AvailableCoins + request.Amount < 0) return Results.Conflict(new { error = "would_be_negative" });
        var now = clock.GetUtcNow(); wallet.AvailableCoins += request.Amount; wallet.UpdatedAt = now;
        db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = "admin_adjustment", Status = "completed", Amount = request.Amount, InitiatorUserId = userId, MetadataJson = JsonSerializer.Serialize(new { admin, reason = request.Reason }), CreatedAt = now, CompletedAt = now });
        await AdminEndpoints.Audit(db, admin, "wallet_adjust", "wallet", wallet.Id, now, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Ok(new { status = "completed", balance = wallet.AvailableCoins });
    }
}
