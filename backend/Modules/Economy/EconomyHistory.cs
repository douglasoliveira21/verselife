using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>The history of a wallet: the entries a person is part of (as who paid or who was paid) with their direction, filters by period and kind, the totals, the reserved balance, and the receipt of one entry.</summary>
public static class EconomyHistory
{
    public static readonly string[] Kinds = ["transfer", "market", "property", "coins", "adjustment", "other"];
    public const int MaxDays = 365;

    /// <summary>The group an entry belongs to in the filters of the wallet.</summary>
    public static string Category(string type) => type switch
    {
        "user_transfer" or "script_payment" or "script_payout" => "transfer",
        "marketplace_purchase" or "object_purchase" => "market",
        "coin_purchase" or "coin_refund" => "coins",
        "admin_adjustment" => "adjustment",
        _ when type.StartsWith("land_", StringComparison.Ordinal) || type.StartsWith("island_", StringComparison.Ordinal) || type.Contains("rent", StringComparison.Ordinal) => "property",
        _ => "other"
    };

    private static IEnumerable<LedgerTransaction> Filter(IEnumerable<LedgerTransaction> rows, string? kind, string? direction, Guid me) =>
        rows.Where(x => (kind is null || Category(x.Type) == kind) && (direction is null || EconomyEndpoints.Direction(x, me) == direction));

    private static IQueryable<LedgerTransaction> Mine(LifeVerseDbContext db, Guid me, int? days, DateTimeOffset now)
    {
        var query = db.LedgerTransactions.AsNoTracking().Where(x => x.InitiatorUserId == me || x.CounterpartyUserId == me);
        if (days is { } d) { var since = now.AddDays(-Math.Clamp(d, 1, MaxDays)); query = query.Where(x => x.CreatedAt >= since); }
        return query;
    }

    public static async Task<IReadOnlyList<object>> ListAsync(LifeVerseDbContext db, Guid me, int? days, string? kind, string? direction, int take, DateTimeOffset now, CancellationToken ct)
    {
        if (kind is not null && !Kinds.Contains(kind)) kind = null; if (direction is not "in" and not "out") direction = null;
        var rows = await Mine(db, me, days, now).OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
        return [.. Filter(rows, kind, direction, me).Take(Math.Clamp(take, 1, 200)).Select(x => (object)new { x.Id, x.Type, kind = Category(x.Type), x.Status, x.Amount, x.CreatedAt, x.CompletedAt, x.InitiatorUserId, x.CounterpartyUserId, direction = EconomyEndpoints.Direction(x, me) })];
    }

    /// <summary>What came in, what went out and how many entries in the period, and the coins held for purchases and rentals that are still being concluded.</summary>
    public static async Task<object> SummaryAsync(LifeVerseDbContext db, Guid me, int days, DateTimeOffset now, CancellationToken ct)
    {
        var rows = (await Mine(db, me, days, now).Where(x => x.Status == "completed").ToListAsync(ct));
        long received = 0, spent = 0; foreach (var x in rows) { if (EconomyEndpoints.Direction(x, me) == "in") received += Math.Abs(x.Amount); else spent += Math.Abs(x.Amount); }
        var reserved = await db.PaymentHolds.AsNoTracking().Where(h => h.PayerUserId == me && h.Status == "held").SumAsync(h => (long?)h.Amount, ct) ?? 0;
        var wallet = await EconomyEndpoints.EnsureWallet(db, me, ct);
        return new { days, received, spent, count = rows.Count, reserved, available = wallet.AvailableCoins, total = wallet.AvailableCoins + reserved, wallet.IsBlocked };
    }

    /// <summary>The receipt of one entry (only for who is part of it): who, how much, when, the message if there was one, and the fee if the sale had one.</summary>
    public static async Task<object?> ReceiptAsync(LifeVerseDbContext db, Guid me, Guid id, CancellationToken ct)
    {
        var x = await db.LedgerTransactions.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && (t.InitiatorUserId == me || t.CounterpartyUserId == me), ct); if (x is null) return null;
        string? message = null; long? fee = null;
        try { using var d = JsonDocument.Parse(x.MetadataJson); if (d.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString(); if (d.RootElement.TryGetProperty("fee", out var f) && f.ValueKind == JsonValueKind.Number) fee = f.GetInt64(); } catch (JsonException) { }
        var otherId = x.InitiatorUserId == me ? x.CounterpartyUserId : x.InitiatorUserId;
        var other = otherId is { } o ? await db.Users.AsNoTracking().Where(u => u.Id == o).Select(u => u.Username).FirstOrDefaultAsync(ct) : null;
        return new { x.Id, x.Type, kind = Category(x.Type), x.Status, x.Amount, direction = EconomyEndpoints.Direction(x, me), x.CreatedAt, x.CompletedAt, counterparty = other, message, fee, code = x.Id.ToString("N")[..12].ToUpperInvariant() };
    }

    public static IEndpointRouteBuilder MapHistory(this RouteGroupBuilder economy)
    {
        economy.MapGet("/wallet/summary", async (int? days, HttpContext c, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct) => !EconomyEndpoints.User(c, out var me) ? Results.Unauthorized() : Results.Ok(await SummaryAsync(db, me, Math.Clamp(days ?? 30, 1, MaxDays), clock.GetUtcNow(), ct)));
        economy.MapGet("/wallet/transactions/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, CancellationToken ct) => !EconomyEndpoints.User(c, out var me) ? Results.Unauthorized() : await ReceiptAsync(db, me, id, ct) is { } receipt ? Results.Ok(receipt) : Results.NotFound());
        return economy;
    }
}
