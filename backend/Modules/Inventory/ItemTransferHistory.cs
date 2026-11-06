using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

/// <summary>What the history of deliveries is asked for: sent, received or both; which outcomes; a word in the item or person's name; a period; a page.</summary>
public sealed record TransferQuery(string? Direction = null, string? Status = null, string? Search = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 25);

public sealed record TransferLine(Guid Id, Guid ItemId, string ItemName, string Direction, Guid OtherUserId, string OtherName, string Kind, bool KeepCopy, string Status, string Message, DateTimeOffset At, DateTimeOffset? AnsweredAt, Guid? ResultItemId);

/// <summary>
/// Everything a person gave or was given, answered or not: the history of deliveries. Only the two people in a delivery see it, each in their own list.
/// Items and accounts that are gone show as "item removido" and "conta removida", so the record stays whole.
/// </summary>
public static class ItemTransferHistory
{
    public const int MaxPageSize = 100;
    public static readonly string[] Statuses = ["pending", "accepted", "rejected", "cancelled", "expired"];

    public static async Task<(int Total, List<TransferLine> Lines)> QueryAsync(LifeVerseDbContext db, Guid user, TransferQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize <= 0 ? 25 : q.PageSize, 1, MaxPageSize); var direction = (q.Direction ?? "all").ToLowerInvariant();
        var offers = db.ItemOffers.AsNoTracking().Where(o => direction == "in" ? o.ToUserId == user : direction == "out" ? o.FromUserId == user : o.ToUserId == user || o.FromUserId == user);
        var statuses = (q.Status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant()).Where(Statuses.Contains).ToList();
        if (statuses.Count > 0) offers = offers.Where(o => statuses.Contains(o.Status));
        if (q.From is { } from) offers = offers.Where(o => o.CreatedAt >= from);
        if (q.To is { } to) offers = offers.Where(o => o.CreatedAt <= to);
        var terms = (q.Search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(4).Select(t => t.ToLowerInvariant()).ToList();
        foreach (var term in terms)
            offers = offers.Where(o => db.InventoryItems.Any(i => i.Id == o.ItemId && i.DisplayName.ToLower().Contains(term))
                || db.Users.Any(u => (u.Id == o.FromUserId || u.Id == o.ToUserId) && u.Id != user && u.Username.ToLower().Contains(term)));
        var total = await offers.CountAsync(ct);
        var rows = await offers.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct); if (rows.Count == 0) return (total, []);
        var people = rows.SelectMany(o => new[] { o.FromUserId, o.ToUserId }).Distinct().ToList(); var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var itemIds = rows.Select(o => o.ItemId).Distinct().ToList(); var items = await db.InventoryItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.DisplayName, ct);
        return (total, [.. rows.Select(o =>
        {
            var incoming = o.ToUserId == user && o.FromUserId != user; var other = incoming ? o.FromUserId : o.ToUserId;
            return new TransferLine(o.Id, o.ItemId, items.GetValueOrDefault(o.ItemId, "item removido"), incoming ? "in" : "out", other, names.GetValueOrDefault(other, "conta removida"), o.Kind, o.KeepCopy, o.Status, o.Message, o.CreatedAt, o.RespondedAt, o.ResultItemId);
        })]);
    }
}

/// <summary>Tells the people in a delivery, live, that something happened (the same pipe the friend requests use). Nothing here can fail a delivery: a person who is offline simply finds it in the list.</summary>
public static class ItemOfferNotifier
{
    public static async Task OfferedAsync(NotificationHub hub, LifeVerseDbContext db, ItemOffer offer, CancellationToken ct)
    {
        try
        {
            var from = await db.Users.AsNoTracking().Where(u => u.Id == offer.FromUserId).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "alguém"; var item = await db.InventoryItems.AsNoTracking().Where(i => i.Id == offer.ItemId).Select(i => i.DisplayName).FirstOrDefaultAsync(ct) ?? "um item";
            await hub.NotifyAsync(offer.ToUserId, "item_offer", new { offerId = offer.Id, fromName = from, itemName = item, kind = offer.Kind }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }

    /// <summary>The giver is told how the receiver answered ("accepted", "rejected"), and the receiver is told when the giver took the offer back or it ran out.</summary>
    public static async Task AnsweredAsync(NotificationHub hub, LifeVerseDbContext db, ItemOffer offer, string answer, CancellationToken ct)
    {
        try
        {
            var item = await db.InventoryItems.AsNoTracking().Where(i => i.Id == offer.ItemId).Select(i => i.DisplayName).FirstOrDefaultAsync(ct) ?? "um item";
            var receiverAnswered = answer is "accepted" or "rejected"; var tell = receiverAnswered ? offer.FromUserId : offer.ToUserId; var other = receiverAnswered ? offer.ToUserId : offer.FromUserId;
            var name = await db.Users.AsNoTracking().Where(u => u.Id == other).Select(u => u.Username).FirstOrDefaultAsync(ct) ?? "alguém";
            await hub.NotifyAsync(tell, "item_offer_answer", new { offerId = offer.Id, byName = name, itemName = item, answer }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }
}
