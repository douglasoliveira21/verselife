namespace LifeVerse.Api.Modules.Economy;

/// <summary>
/// The fees of the platform, in basis points (500 = 5 %): on marketplace sales and on sales of objects in the world. The administrator sets them in the configuration (Economy:Fees:MarketplaceBps, Economy:Fees:ObjectSaleBps),
/// read once when the server starts; a sale already done keeps the fee it was made with (it is in its ledger entry). The fee is taken from what the seller receives, the buyer pays the price, and clients ask for the rates (GET /api/v1/economy/fees) to show them before a purchase is confirmed.
/// </summary>
public static class EconomyFees
{
    public const long DefaultBps = 500, MaxBps = 5000;
    public static long MarketplaceBps { get; private set; } = DefaultBps;
    public static long ObjectSaleBps { get; private set; } = DefaultBps;

    public static void Configure(IConfiguration configuration) { MarketplaceBps = Read(configuration, "Economy:Fees:MarketplaceBps"); ObjectSaleBps = Read(configuration, "Economy:Fees:ObjectSaleBps"); }
    public static long Read(IConfiguration configuration, string key) => long.TryParse(configuration[key], out var bps) && bps is >= 0 and <= MaxBps ? bps : DefaultBps;

    /// <summary>The fee of a price at a rate, rounded down to whole coins (so a small price may carry no fee).</summary>
    public static long Of(long price, long bps) => price * bps / 10000;

    public static IEndpointRouteBuilder MapFees(this RouteGroupBuilder economy)
    {
        economy.MapGet("/fees", () => Results.Ok(new { marketplaceBps = MarketplaceBps, objectSaleBps = ObjectSaleBps }));
        return economy;
    }
}
