using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>Tells the people involved, through the live notification channel, that their balance changed ("wallet_changed" with the balance as the database has it after the operation), so the top bar and the wallet update by themselves. It never fails an operation: the money has already moved when this is called.</summary>
public static class EconomyPush
{
    public const string EventType = "wallet_changed";

    public static async Task WalletChangedAsync(IServiceProvider? services, LifeVerseDbContext db, CancellationToken ct, params Guid[] users)
    {
        try
        {
            if (services?.GetService<NotificationHub>() is not { } hub) return;
            foreach (var user in users.Distinct())
            {
                if (hub.ConnectionCount(user) == 0) continue;
                var balance = await db.Wallets.AsNoTracking().Where(w => w.UserId == user).Select(w => (long?)w.AvailableCoins).SingleOrDefaultAsync(ct);
                if (balance is { } b) await hub.NotifyAsync(user, EventType, new { balance = b }, ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }
}
