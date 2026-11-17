using LifeVerse.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>A pack of VerseCoin (VC$) that can be bought with real money. Prices are in centavos of real, the coins an integer.</summary>
public sealed record CoinPackage(string Id, string Name, long Coins, long PriceCents, int BonusPercent)
{
    public long BonusCoins => Coins * BonusPercent / 100;
    public long TotalCoins => Coins + BonusCoins;
}

public sealed record CoinOrderRequest(string PackageId, string Method, string IdempotencyKey);

/// <summary>
/// The packs of VerseCoin and the start of buying them. The packs come from the configuration (Economy:CoinPackages, so the administrator sets packs, prices and
/// bonuses); without it the default list is offered. Coins are only ever credited from a confirmed payment of a gateway (webhook checked with the provider): while no
/// gateway is configured (Economy:Payments:Provider) an order is refused with "payments_not_configured" and nothing is credited. See CoinPurchases for the money side.
/// </summary>
public static class CoinPackages
{
    public static readonly CoinPackage[] Defaults =
    [
        new("inicial", "Inicial", 500, 500, 0), new("basico", "Básico", 1000, 1000, 0), new("intermediario", "Intermediário", 2500, 2500, 10),
        new("avancado", "Avançado", 5000, 5000, 15), new("premium", "Premium", 10000, 10000, 20)
    ];
    public static readonly string[] Methods = ["pix", "card", "mercadopago", "asaas", "pagbank"];

    public static IReadOnlyList<CoinPackage> For(IConfiguration configuration)
    {
        var configured = configuration.GetSection("Economy:CoinPackages").Get<CoinPackage[]>();
        return configured is { Length: > 0 } && configured.All(p => p.Coins > 0 && p.PriceCents > 0 && p.BonusPercent is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(p.Id)) ? configured : Defaults;
    }

    public static IEndpointRouteBuilder MapCoinPackages(this RouteGroupBuilder economy)
    {
        economy.MapGet("/packages", (IConfiguration configuration, IHostEnvironment environment) => Results.Ok(new
        {
            paymentsEnabled = CoinPayments.Active(configuration, environment) is not null,
            methods = Methods,
            packages = For(configuration).Select(p => new { p.Id, p.Name, p.Coins, p.BonusPercent, bonusCoins = p.BonusCoins, totalCoins = p.TotalCoins, p.PriceCents })
        }));
        economy.MapPost("/coin-orders", CreateOrderAsync);
        economy.MapGet("/coin-orders/{id:guid}", async (Guid id, HttpContext context, LifeVerseDbContext db, CancellationToken ct) =>
        {
            if (!EconomyEndpoints.User(context, out var me)) return Results.Unauthorized();
            var order = await db.CoinOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == me, ct);
            return order is null ? Results.NotFound() : Results.Ok(Describe(order));
        });
        return economy;
    }

    private static object Describe(CoinOrder o) => new { o.Id, o.PackageId, o.Coins, o.PriceCents, o.Method, o.Status, o.CreatedAt, o.ExpiresAt, o.PaidAt };

    private static async Task<IResult> CreateOrderAsync([FromBody] CoinOrderRequest request, HttpContext context, LifeVerseDbContext db, IConfiguration configuration, IHostEnvironment environment, TimeProvider clock, CancellationToken ct)
    {
        if (!EconomyEndpoints.User(context, out var me)) return Results.Unauthorized();
        if (request.IdempotencyKey is not { Length: >= 8 and <= 128 } || !Methods.Contains(request.Method)) return Results.BadRequest(new { error = "invalid_order" });
        var pack = For(configuration).FirstOrDefault(p => p.Id == request.PackageId); if (pack is null) return Results.NotFound(new { error = "package_not_found" });
        if (CoinPayments.Active(configuration, environment) is not { } provider) return Results.Json(new { error = "payments_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        var wallet = await EconomyEndpoints.EnsureWallet(db, me, ct); if (wallet.IsBlocked) return Results.Conflict(new { error = "wallet_blocked" });
        var (order, existed) = await CoinPurchases.CreateAsync(db, provider, me, pack, request.Method, request.IdempotencyKey, clock.GetUtcNow(), ct);
        return existed ? Results.Ok(Describe(order)) : Results.Created($"/api/v1/economy/coin-orders/{order.Id}", Describe(order));
    }

    /// <summary>The gateway calls this (no login: the signature over the body is the proof). Whatever is not understood or not signed is refused before anything is read.</summary>
    public static IEndpointRouteBuilder MapCoinWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/economy/payment-webhooks/{provider}", async (string provider, HttpContext context, LifeVerseDbContext db, IConfiguration configuration, IHostEnvironment environment, TimeProvider clock, CancellationToken ct) =>
        {
            if (CoinPayments.Named(provider, configuration, environment) is not { } gateway) return Results.NotFound();
            using var reader = new StreamReader(context.Request.Body, System.Text.Encoding.UTF8); var body = await reader.ReadToEndAsync(ct);
            if (!gateway.Verify(body, context.Request.Headers["X-Signature"].FirstOrDefault(), configuration)) return Results.Unauthorized();
            if (gateway.Parse(body) is not { } evt) return Results.BadRequest();
            var outcome = await CoinPurchases.ApplyAsync(db, gateway, evt, clock.GetUtcNow(), ct);
            return outcome == PaymentOutcome.UnknownOrder ? Results.NotFound() : Results.Ok(new { outcome = outcome.ToString() });
        }).AllowAnonymous();
        return endpoints;
    }
}
