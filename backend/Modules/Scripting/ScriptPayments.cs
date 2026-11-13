using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Economy;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Scripting;

public sealed record ScriptPaymentCall(Guid ScriptId, Guid PayerUserId, long Amount, string RequestId);
public sealed record ScriptPaymentResult(string Status, string? Error = null, Guid? TransactionId = null, long Amount = 0);

/// <summary>
/// A payment a script asked for and the player agreed to in the dialog the island showed. The script never touches a balance and never says who is paid: the money goes from the player (named by the island server, which saw them confirm)
/// to the OWNER of the object (read here from the database), in one database transaction with an idempotency key, so a repeated or retried request moves the money once. The script only hears the confirmed result.
/// </summary>
public static partial class ScriptingEndpoints
{
    public const long MaxScriptPayment = 100_000;

    internal static async Task<IResult> ScriptPaymentAsync(Guid instanceId, ScriptPaymentCall call, HttpContext context, IConfiguration config, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!Platform.PlatformEndpoints.HasValidServiceKey(context, config)) return Results.Unauthorized();
        var server = await db.ServerInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == instanceId && x.StoppedAt == null, ct);
        if (server is null) return Results.NotFound();
        if (call.Amount < 1 || call.Amount > MaxScriptPayment || call.RequestId.Length is < 8 or > 100) return Results.BadRequest(new ScriptPaymentResult("rejected", "bad_amount"));
        var target = await (from script in db.ObjectScripts.AsNoTracking() join obj in db.BuildObjects.AsNoTracking() on script.ObjectId equals obj.Id
                            where script.Id == call.ScriptId && obj.IslandId == server.IslandId && obj.DeletedAt == null select new { script.Id, ObjectId = obj.Id, obj.OwnerUserId }).SingleOrDefaultAsync(ct);
        if (target is null) return Results.NotFound();
        if (target.OwnerUserId == call.PayerUserId) return Results.Json(new ScriptPaymentResult("rejected", "own_object"), statusCode: StatusCodes.Status409Conflict);
        var key = $"scriptpay-{call.ScriptId:N}-{call.RequestId}";
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var done = await db.LedgerTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (done is not null) return Results.Ok(new ScriptPaymentResult("already_processed", null, done.Id, done.Amount));
        if (!await db.Users.AnyAsync(x => x.Id == call.PayerUserId && x.DeletedAt == null, ct) || !await db.Users.AnyAsync(x => x.Id == target.OwnerUserId && x.DeletedAt == null, ct)) return Results.NotFound();
        var payer = await EconomyEndpoints.EnsureWallet(db, call.PayerUserId, ct); var receiver = await EconomyEndpoints.EnsureWallet(db, target.OwnerUserId, ct);
        if (payer.IsBlocked || receiver.IsBlocked) return Results.Json(new ScriptPaymentResult("rejected", "wallet_blocked"), statusCode: StatusCodes.Status409Conflict);
        if (payer.AvailableCoins < call.Amount) return Results.Json(new ScriptPaymentResult("rejected", "insufficient_balance"), statusCode: StatusCodes.Status409Conflict);
        var now = clock.GetUtcNow(); payer.AvailableCoins -= call.Amount; receiver.AvailableCoins += call.Amount; payer.UpdatedAt = receiver.UpdatedAt = now;
        var transaction = new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = key, Type = "script_payment", Status = "completed", Amount = call.Amount, InitiatorUserId = call.PayerUserId, MetadataJson = JsonSerializer.Serialize(new { scriptId = call.ScriptId, objectId = target.ObjectId, islandId = server.IslandId, receiverUserId = target.OwnerUserId, requestId = call.RequestId }), CreatedAt = now, CompletedAt = now };
        db.LedgerTransactions.Add(transaction);
        db.ScriptLogs.Add(new ScriptLogLine { Id = Guid.NewGuid(), ScriptId = call.ScriptId, Level = "info", EventName = "payment", Text = $"payment of L$ {call.Amount} received from {call.PayerUserId.ToString()[..8]}…", CreatedAt = now });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Ok(new ScriptPaymentResult("completed", null, transaction.Id, call.Amount));
    }
}
