using Microsoft.AspNetCore.Mvc;

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
/// gateway is configured (Economy:Payments:Provider) an order is refused with "payments_not_configured" and nothing is credited.
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
        economy.MapGet("/packages", (IConfiguration configuration) => Results.Ok(new
        {
            paymentsEnabled = !string.IsNullOrWhiteSpace(configuration["Economy:Payments:Provider"]),
            methods = Methods,
            packages = For(configuration).Select(p => new { p.Id, p.Name, p.Coins, p.BonusPercent, bonusCoins = p.BonusCoins, totalCoins = p.TotalCoins, p.PriceCents })
        }));
        economy.MapPost("/coin-orders", ([FromBody] CoinOrderRequest request, IConfiguration configuration) =>
        {
            if (request.IdempotencyKey is not { Length: >= 8 and <= 128 } || !Methods.Contains(request.Method)) return Results.BadRequest(new { error = "invalid_order" });
            if (For(configuration).All(p => p.Id != request.PackageId)) return Results.NotFound(new { error = "package_not_found" });
            return Results.Json(new { error = "payments_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });
        return economy;
    }
}
