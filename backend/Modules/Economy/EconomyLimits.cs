using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>Limits on what one person can send to others: per transfer and per day (UTC). They come from the configuration (Economy:Limits:MaxTransfer, Economy:Limits:DailyTransfer), so the administrator can change them; a limit stops a stolen account from emptying a wallet in one go.</summary>
public static class EconomyLimits
{
    public const long DefaultMaxTransfer = 1_000_000, DefaultDailyTransfer = 5_000_000;

    public static (long Max, long Daily) For(IConfiguration configuration) =>
        (long.TryParse(configuration["Economy:Limits:MaxTransfer"], out var m) && m > 0 ? m : DefaultMaxTransfer, long.TryParse(configuration["Economy:Limits:DailyTransfer"], out var d) && d > 0 ? d : DefaultDailyTransfer);

    public static async Task<bool> ExceededAsync(LifeVerseDbContext db, IConfiguration configuration, Guid user, long amount, DateTimeOffset now, CancellationToken ct)
    {
        var (max, daily) = For(configuration);
        if (amount > max) return true;
        var start = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var sent = await db.LedgerTransactions.Where(x => x.InitiatorUserId == user && x.Type == "user_transfer" && x.CreatedAt >= start).SumAsync(x => (long?)x.Amount, ct) ?? 0;
        return sent + amount > daily;
    }
}
