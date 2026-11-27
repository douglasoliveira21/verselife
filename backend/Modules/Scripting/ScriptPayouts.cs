using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record PayoutAuthorizationRequest(long MaxPerPayment, long MaxPerDay, int Days);
public sealed record ScriptPayoutCall(Guid ScriptId, Guid RecipientUserId, long Amount, string RequestId);

/// <summary>
/// Payments OUT of an owner's wallet started by a script (economy.pay). A script can never do it by itself: the owner has to authorize that script, with a limit for each payment and one for each day, until a date, and can take the authorization back at any time.
/// The money leaves only the wallet of the owner of the object (read from the database, not from the script), goes to a person who exists, in one transaction with an idempotency key, and every payout is in the ledger and told to the person who got it.
/// </summary>
public static partial class ScriptingEndpoints
{
    public const int MaxAuthorizationDays = 90;

    public static IEndpointRouteBuilder MapScriptPayouts(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/scripting/payout-authorizations").WithTags("Scripting").RequireAuthorization();
        group.MapGet("", ListPayoutAuthorizationsAsync);
        group.MapPut("/{scriptId:guid}", GrantPayoutAuthorizationAsync);
        group.MapDelete("/{scriptId:guid}", RevokePayoutAuthorizationAsync);
        return endpoints;
    }

    private static async Task<Guid?> ScriptOwnerAsync(LifeVerseDbContext db, Guid scriptId, CancellationToken ct) =>
        await (from script in db.ObjectScripts.AsNoTracking() join obj in db.BuildObjects.AsNoTracking() on script.ObjectId equals obj.Id where script.Id == scriptId && obj.DeletedAt == null select (Guid?)obj.OwnerUserId).SingleOrDefaultAsync(ct);

    private static async Task<IResult> ListPayoutAuthorizationsAsync(HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!EconomyEndpoints.User(context, out var me)) return Results.Unauthorized(); var now = clock.GetUtcNow();
        var rows = await db.ScriptPayoutAuthorizations.AsNoTracking().Where(x => x.OwnerUserId == me && x.RevokedAt == null && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Results.Ok(rows.Select(x => new { x.ScriptId, x.MaxPerPayment, x.MaxPerDay, x.ExpiresAt, x.CreatedAt }));
    }

    private static async Task<IResult> GrantPayoutAuthorizationAsync(Guid scriptId, [FromBody] PayoutAuthorizationRequest request, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!EconomyEndpoints.User(context, out var me)) return Results.Unauthorized();
        if (request.MaxPerPayment < 1 || request.MaxPerPayment > MaxScriptPayment || request.MaxPerDay < request.MaxPerPayment || request.MaxPerDay > MaxScriptPayment * 100 || request.Days is < 1 or > MaxAuthorizationDays) return Results.BadRequest(new { error = "invalid_authorization" });
        if (await ScriptOwnerAsync(db, scriptId, ct) != me) return Results.NotFound();
        var now = clock.GetUtcNow();
        foreach (var old in await db.ScriptPayoutAuthorizations.Where(x => x.ScriptId == scriptId && x.RevokedAt == null).ToListAsync(ct)) old.RevokedAt = now;       // one at a time: a new one replaces the old
        var auth = new ScriptPayoutAuthorization { Id = Guid.NewGuid(), ScriptId = scriptId, OwnerUserId = me, MaxPerPayment = request.MaxPerPayment, MaxPerDay = request.MaxPerDay, ExpiresAt = now.AddDays(request.Days), CreatedAt = now };
        db.ScriptPayoutAuthorizations.Add(auth); await db.SaveChangesAsync(ct);
        return Results.Ok(new { auth.ScriptId, auth.MaxPerPayment, auth.MaxPerDay, auth.ExpiresAt });
    }

    private static async Task<IResult> RevokePayoutAuthorizationAsync(Guid scriptId, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!EconomyEndpoints.User(context, out var me)) return Results.Unauthorized(); var now = clock.GetUtcNow();
        var active = await db.ScriptPayoutAuthorizations.Where(x => x.ScriptId == scriptId && x.OwnerUserId == me && x.RevokedAt == null).ToListAsync(ct);
        foreach (var auth in active) auth.RevokedAt = now; await db.SaveChangesAsync(ct);
        return Results.Ok(new { revoked = active.Count });
    }

    /// <summary>The island asks for a payout of a script. Everything that decides is checked here, none of it comes from the script.</summary>
    internal static async Task<IResult> ScriptPayoutAsync(Guid instanceId, ScriptPayoutCall call, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Platform.PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct); if (server is null) return Results.NotFound();
        if (call.Amount < 1 || call.Amount > MaxScriptPayment || call.RequestId.Length is < 8 or > 100) return Results.BadRequest(new ScriptPaymentResult("rejected", "bad_amount"));
        var target = await (from script in db.ObjectScripts.AsNoTracking() join obj in db.BuildObjects.AsNoTracking() on script.ObjectId equals obj.Id
                            where script.Id == call.ScriptId && obj.IslandId == server.IslandId && obj.DeletedAt == null select new { obj.Name, obj.OwnerUserId }).SingleOrDefaultAsync(ct);
        if (target is null) return Results.NotFound();
        if (target.OwnerUserId == call.RecipientUserId) return Results.Json(new ScriptPaymentResult("rejected", "own_object"), statusCode: StatusCodes.Status409Conflict);
        var now = clock.GetUtcNow(); var key = $"scriptpayout-{call.ScriptId:N}-{call.RequestId}";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var done = await db.LedgerTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (done is not null) return Results.Ok(new ScriptPaymentResult("already_processed", null, done.Id, done.Amount));
        var auth = await db.ScriptPayoutAuthorizations.AsNoTracking().Where(x => x.ScriptId == call.ScriptId && x.OwnerUserId == target.OwnerUserId && x.RevokedAt == null && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (auth is null) return Results.Json(new ScriptPaymentResult("rejected", "not_authorized"), statusCode: StatusCodes.Status403Forbidden);
        if (call.Amount > auth.MaxPerPayment) return Results.Json(new ScriptPaymentResult("rejected", "over_payment_limit"), statusCode: StatusCodes.Status409Conflict);
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var scriptText = call.ScriptId.ToString();
        var today = await db.LedgerTransactions.Where(x => x.Type == "script_payout" && x.InitiatorUserId == target.OwnerUserId && x.CreatedAt >= start && JsonColumn.Text(x.MetadataJson).Contains(scriptText)).SumAsync(x => (long?)x.Amount, ct) ?? 0;
        if (today + call.Amount > auth.MaxPerDay) return Results.Json(new ScriptPaymentResult("rejected", "over_daily_limit"), statusCode: StatusCodes.Status409Conflict);
        if (!await db.Users.AnyAsync(x => x.Id == call.RecipientUserId && x.DeletedAt == null, ct)) return Results.NotFound();
        var owner = await EconomyEndpoints.EnsureWallet(db, target.OwnerUserId, ct); var receiver = await EconomyEndpoints.EnsureWallet(db, call.RecipientUserId, ct);
        if (owner.IsBlocked || receiver.IsBlocked) return Results.Json(new ScriptPaymentResult("rejected", "wallet_blocked"), statusCode: StatusCodes.Status409Conflict);
        if (owner.AvailableCoins < call.Amount) return Results.Json(new ScriptPaymentResult("rejected", "insufficient_balance"), statusCode: StatusCodes.Status409Conflict);
        owner.AvailableCoins -= call.Amount; receiver.AvailableCoins += call.Amount; owner.UpdatedAt = receiver.UpdatedAt = now;
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = "script_payout", Status = "completed", Amount = call.Amount, InitiatorUserId = target.OwnerUserId, CounterpartyUserId = call.RecipientUserId, MetadataJson = JsonSerializer.Serialize(new { scriptId = call.ScriptId, objectName = target.Name }), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        Land.PropertyNotices.Add(db, call.RecipientUserId, "payment_received", $"Pagamento recebido: {EconomyEndpoints.Coins(call.Amount)}", $"“{target.Name}” pagou {EconomyEndpoints.Coins(call.Amount)} para você.", $"scriptpayout:{transaction.Id}", now);
        db.ScriptLogs.Add(new ScriptLogLine { Id = Guid.NewGuid(), ScriptId = call.ScriptId, Level = "info", EventName = "payout", Text = $"paid VC$ {call.Amount} to {call.RecipientUserId.ToString()[..8]}…", CreatedAt = now });
        if (!await EconomyConcurrency.CommitAsync(db, tx, ct)) return Results.Json(new ScriptPaymentResult("rejected", EconomyConcurrency.TryAgain), statusCode: StatusCodes.Status409Conflict); await EconomyPush.WalletChangedAsync(context.RequestServices, db, ct, target.OwnerUserId, call.RecipientUserId);
        return Results.Ok(new ScriptPaymentResult("completed", null, transaction.Id, call.Amount));
    }
}
