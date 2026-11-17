using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>What a gateway says about a charge: the reference it gave, "paid" / "refunded" / "failed", and the amount it really received, in centavos.</summary>
public sealed record WebhookEvent(string ProviderRef, string Status, long AmountCents);

/// <summary>A payment gateway. Real ones (Mercado Pago, Asaas, PagBank) implement this and are registered in <see cref="CoinPayments.Providers"/>; the coins never come from the client, only from <see cref="CoinPurchases.ApplyAsync"/> after <see cref="Verify"/>.</summary>
public interface IPaymentProvider
{
    string Name { get; }
    /// <summary>Whether the call really comes from the gateway (a signature over the raw body).</summary>
    bool Verify(string body, string? signature, IConfiguration configuration);
    WebhookEvent? Parse(string body);
    /// <summary>Creates the charge at the gateway and returns the reference it uses for it.</summary>
    Task<string> CreateChargeAsync(CoinOrder order, CancellationToken ct);
}

/// <summary>A provider for development and tests: no money moves, but the whole path (order, signed webhook, credit once, refund) runs exactly as with a real gateway. Never enabled in production.</summary>
public sealed class TestPaymentProvider : IPaymentProvider
{
    public string Name => "test";
    public static string Sign(string body, string secret) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    public bool Verify(string body, string? signature, IConfiguration configuration)
    {
        var secret = configuration["Economy:Payments:WebhookSecret"];
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(body, secret)), Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }
    public WebhookEvent? Parse(string body)
    {
        try { using var d = JsonDocument.Parse(body); var r = d.RootElement; return new WebhookEvent(r.GetProperty("providerRef").GetString()!, r.GetProperty("status").GetString()!, r.GetProperty("amountCents").GetInt64()); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
    public Task<string> CreateChargeAsync(CoinOrder order, CancellationToken ct) => Task.FromResult("test-" + order.Id.ToString("N"));
}

public static class CoinPayments
{
    public static readonly IReadOnlyList<IPaymentProvider> Providers = [new TestPaymentProvider()];
    /// <summary>The provider the configuration chose, if it exists and may be used here (the test one never in production).</summary>
    public static IPaymentProvider? Active(IConfiguration configuration, IHostEnvironment environment)
    {
        var name = configuration["Economy:Payments:Provider"]; if (string.IsNullOrWhiteSpace(name)) return null;
        var provider = Providers.FirstOrDefault(p => p.Name == name); if (provider is null) return null;
        return provider.Name == "test" && environment.IsProduction() ? null : provider;
    }
    public static IPaymentProvider? Named(string name, IConfiguration configuration, IHostEnvironment environment) => Active(configuration, environment) is { } p && p.Name == name ? p : null;
}

public enum PaymentOutcome { Credited, AlreadyApplied, Refunded, HeldForReview, AmountMismatch, Failed, UnknownOrder, Ignored }

/// <summary>The money side of buying VerseCoin: the order, and what a confirmed payment does to the wallet. Every step is one database transaction, repeated webhooks change nothing, and a payment whose amount is not the order's credits nothing.</summary>
public static class CoinPurchases
{
    public static readonly TimeSpan OrderLife = TimeSpan.FromMinutes(30);

    public static async Task<(CoinOrder Order, bool Existed)> CreateAsync(LifeVerseDbContext db, IPaymentProvider provider, Guid userId, CoinPackage pack, string method, string key, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.CoinOrders.SingleOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct);
        if (existing is not null) return (existing, true);
        var order = new CoinOrder { Id = Guid.NewGuid(), UserId = userId, PackageId = pack.Id, Coins = pack.TotalCoins, PriceCents = pack.PriceCents, Method = method, Provider = provider.Name, Status = "pending", IdempotencyKey = key, CreatedAt = now, ExpiresAt = now + OrderLife };
        order.ProviderRef = await provider.CreateChargeAsync(order, ct);
        db.CoinOrders.Add(order); await db.SaveChangesAsync(ct);
        return (order, false);
    }

    public static async Task<PaymentOutcome> ApplyAsync(LifeVerseDbContext db, IPaymentProvider provider, WebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.CoinOrders.SingleOrDefaultAsync(x => x.Provider == provider.Name && x.ProviderRef == evt.ProviderRef, ct);
        if (order is null) return PaymentOutcome.UnknownOrder;
        PaymentOutcome result;
        switch (evt.Status)
        {
            case "paid":
                if (order.Status == "paid" || order.Status is "refunded" or "chargeback_review") return PaymentOutcome.AlreadyApplied;
                if (evt.AmountCents != order.PriceCents) { order.Status = "failed"; order.Reason = $"amount_mismatch:{evt.AmountCents}"; result = PaymentOutcome.AmountMismatch; break; }
                var wallet = await EconomyEndpoints.EnsureWallet(db, order.UserId, ct);
                wallet.AvailableCoins += order.Coins; wallet.UpdatedAt = now; order.Status = "paid"; order.PaidAt = now;
                db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = $"coin-order:{order.Id}", Type = "coin_purchase", Status = "completed", Amount = order.Coins, InitiatorUserId = order.UserId, MetadataJson = JsonSerializer.Serialize(new { orderId = order.Id, order.PackageId, order.PriceCents, provider = provider.Name }), CreatedAt = now, CompletedAt = now });
                Land.PropertyNotices.Add(db, order.UserId, "coins_credited", $"Compra concluída: {EconomyEndpoints.Coins(order.Coins)}", $"Seu pagamento foi confirmado e {EconomyEndpoints.Coins(order.Coins)} já estão na sua carteira.", $"coin-order:{order.Id}", now);
                result = PaymentOutcome.Credited; break;
            case "refunded":
                if (order.Status != "paid") { if (order.Status == "pending") { order.Status = "failed"; order.Reason = "refunded_before_paid"; } return order.Status is "refunded" or "chargeback_review" ? PaymentOutcome.AlreadyApplied : PaymentOutcome.Ignored; }
                var held = await EconomyEndpoints.EnsureWallet(db, order.UserId, ct);
                var taken = Math.Min(held.AvailableCoins, order.Coins); held.AvailableCoins -= taken; held.UpdatedAt = now;
                if (taken < order.Coins) { held.IsBlocked = true; order.Status = "chargeback_review"; order.Reason = $"short:{order.Coins - taken}"; result = PaymentOutcome.HeldForReview; } else { order.Status = "refunded"; result = PaymentOutcome.Refunded; }
                Land.PropertyNotices.Add(db, order.UserId, "coins_refunded", "Compra de VerseCoin estornada", $"O pagamento do pedido foi estornado: {EconomyEndpoints.Coins(taken)} saíram da sua carteira." + (taken < order.Coins ? " Como parte já tinha sido gasta, sua carteira ficou em análise." : ""), $"coin-refund:{order.Id}", now);
                db.LedgerTransactions.Add(new LedgerTransaction { Id = Guid.NewGuid(), IdempotencyKey = $"coin-refund:{order.Id}", Type = "coin_refund", Status = "completed", Amount = taken, InitiatorUserId = order.UserId, MetadataJson = JsonSerializer.Serialize(new { orderId = order.Id, owed = order.Coins }), CreatedAt = now, CompletedAt = now });
                break;
            case "failed":
                if (order.Status != "pending") return PaymentOutcome.AlreadyApplied; order.Status = "failed"; order.Reason = "gateway_failed"; result = PaymentOutcome.Failed; break;
            default: return PaymentOutcome.Ignored;
        }
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { return PaymentOutcome.AlreadyApplied; }      // another delivery of the same webhook got there first
        catch (DbUpdateException) when (result == PaymentOutcome.Credited) { return PaymentOutcome.AlreadyApplied; }
        return result;
    }

    /// <summary>Pending orders past their time are closed. A payment the gateway confirms later still credits (the money was received), but nothing waits forever.</summary>
    public static async Task<int> ExpireAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var stale = await db.CoinOrders.Where(x => x.Status == "pending" && x.ExpiresAt < now).ToListAsync(ct);
        foreach (var order in stale) { order.Status = "expired"; order.Reason = "unpaid"; }
        if (stale.Count > 0) await db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
