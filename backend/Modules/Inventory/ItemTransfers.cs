using LifeVerse.Api.Data;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record OfferView(Guid Id, Guid ItemId, string ItemName, Guid FromUserId, string FromName, Guid ToUserId, string ToName, string Kind, string Message, bool KeepCopy, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, Guid? ResultItemId);
public sealed record OfferResult(bool Ok, string? Error, ItemOffer? Offer = null, InventoryItem? Item = null);

/// <summary>
/// Copying, giving and handing over items. An item is never duplicated and never lost on the way: a copy needs the Copy right; a transfer is an offer the
/// receiver must accept, and accepting changes the owner of the same item in one step (or, when the giver keeps their own, makes exactly one copy). Two answers
/// at the same moment cannot both be applied, and the same request sent twice is the same offer.
/// </summary>
public static class ItemTransfers
{
    public const int MaxOutgoing = 20, MaxIncoming = 50, MaxItemsPerPerson = 20_000, MessageLength = 200;
    public static readonly TimeSpan OfferLife = TimeSpan.FromDays(7);

    // ---- copying ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes a copy of an item in the inventory of the person asking, who must hold the Copy right. The copy has the same rights the original has for its holder
    /// (when the holder copies it) or the rights a new owner would get (when a member of a group copies the group's item). Where it came from is kept.
    /// </summary>
    public static async Task<OfferResult> CopyAsync(LifeVerseDbContext db, Guid actor, Guid itemId, DateTimeOffset now, CancellationToken ct)
    {
        var access = await ItemRightsService.RequireAsync(db, actor, itemId, Right.Copy, "copy", now, ct); if (!access.Ok) return new OfferResult(false, access.Error);
        var source = access.Item!;
        if (await db.InventoryItems.CountAsync(i => i.OwnerUserId == actor && i.DeletedAt == null, ct) >= MaxItemsPerPerson) return new OfferResult(false, "inventory_full");
        var rights = source.OwnerUserId == actor ? new ItemPermissions(source.OwnerRights, ItemRights.None, source.NextOwnerRights).Normalized() : new ItemPermissions(source.OwnerRights, source.GroupRights, source.NextOwnerRights).ForNextOwner();
        var copy = NewItem(source, actor, rights, "Cópia de " + source.DisplayName, now); copy.CopiedFromItemId = source.Id;
        db.InventoryItems.Add(copy); ItemRightsService.RecordOwner(db, copy, actor, null, "copy", now);
        ItemRightsService.Audit(db, actor, "copy", "ok", source.Id, source.AssetId, source.OwnerUserId, actor, $"copy {copy.Id}", now);
        await db.SaveChangesAsync(ct); return new OfferResult(true, null, null, copy);
    }

    private static InventoryItem NewItem(InventoryItem from, Guid owner, ItemPermissions rights, string name, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = from.AssetId, DisplayName = name.Length > 160 ? name[..160] : name, OwnerRights = rights.Owner, GroupRights = ItemRights.None, NextOwnerRights = rights.NextOwner, CreatedAt = now, UpdatedAt = now,
    };

    // ---- offering -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Offers an item to another person. Only the holder, with the Transfer right, can offer; the receiver must exist and not be blocked either way; the same
    /// idempotency key gives back the same offer. The item is locked until the offer is answered (unless the giver keeps their own and sends a copy).
    /// </summary>
    public static async Task<OfferResult> OfferAsync(LifeVerseDbContext db, Guid from, Guid itemId, Guid to, string? kind, string? message, bool keepCopy, string? idempotencyKey, DateTimeOffset now, CancellationToken ct)
    {
        if (idempotencyKey is not { Length: >= 8 and <= 128 }) return new OfferResult(false, "bad_key");
        kind = (kind ?? "gift").Trim().ToLowerInvariant(); if (kind is not ("gift" or "transfer")) return new OfferResult(false, "bad_kind");
        if (await db.ItemOffers.AsNoTracking().FirstOrDefaultAsync(o => o.FromUserId == from && o.IdempotencyKey == idempotencyKey, ct) is { } same) return new OfferResult(same.ItemId == itemId && same.ToUserId == to, same.ItemId == itemId && same.ToUserId == to ? null : "key_reused", same);
        if (to == from) return new OfferResult(false, "to_self");
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == to && u.DeletedAt == null && u.Status == "active", ct)) return new OfferResult(false, "recipient_not_found");
        if (await db.UserBlocks.AsNoTracking().AnyAsync(b => (b.UserId == from && b.BlockedUserId == to) || (b.UserId == to && b.BlockedUserId == from), ct)) return new OfferResult(false, "recipient_not_found"); // blocked either way: as if the person did not exist
        var access = await ItemRightsService.RequireAsync(db, from, itemId, Right.Transfer, "offer", now, ct); if (!access.Ok) return new OfferResult(false, access.Error);
        if (access.Item!.OwnerUserId != from) return await Denied(db, from, access.Item, "offer", "not_allowed", "only the holder can hand an item over", now, ct);
        if (keepCopy && !ItemRights.Has(access.Held, Right.Copy)) return await Denied(db, from, access.Item, "offer", "keep_copy_needs_copy", "the item cannot be copied", now, ct);
        if (!ItemRights.IsCopyable(access.Item.OwnerRights) && await Worlds.ObjectRights.InUseAsync(db, from, access.Item.AssetId, null, ct)) return await Denied(db, from, access.Item, "offer", "item_in_use", "it is part of an object in the world; return the object first", now, ct); // giving it away would leave a copy behind
        if (await db.ItemOffers.CountAsync(o => o.FromUserId == from && o.Status == "pending", ct) >= MaxOutgoing) return new OfferResult(false, "too_many_offers");
        if (await db.ItemOffers.CountAsync(o => o.ToUserId == to && o.Status == "pending", ct) >= MaxIncoming) return new OfferResult(false, "recipient_full");

        var item = await db.InventoryItems.FirstAsync(i => i.Id == itemId, ct);
        if (!keepCopy) { item.Status = ItemRightsService.Offered; item.Revision++; item.UpdatedAt = now; }
        var text = new string([.. (message ?? string.Empty).Where(c => !char.IsControl(c) || c == ' ')]).Trim(); if (text.Length > MessageLength) text = text[..MessageLength];
        var offer = new ItemOffer { Id = Guid.NewGuid(), ItemId = item.Id, AssetId = item.AssetId, FromUserId = from, ToUserId = to, Kind = kind, Message = text, KeepCopy = keepCopy, Status = "pending", IdempotencyKey = idempotencyKey, CreatedAt = now, ExpiresAt = now + OfferLife };
        db.ItemOffers.Add(offer); ItemRightsService.Audit(db, from, "transfer_offer", "ok", item.Id, item.AssetId, from, to, $"{kind}{(keepCopy ? " keeping a copy" : string.Empty)}", now);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException)
        {
            db.ChangeTracker.Clear(); // another request got there first: the same key gives its offer, anything else means the item is taken
            return await db.ItemOffers.AsNoTracking().FirstOrDefaultAsync(o => o.FromUserId == from && o.IdempotencyKey == idempotencyKey, ct) is { } won ? new OfferResult(true, null, won) : new OfferResult(false, "item_busy");
        }
        return new OfferResult(true, null, offer);
    }

    private static async Task<OfferResult> Denied(LifeVerseDbContext db, Guid actor, InventoryItem item, string action, string error, string detail, DateTimeOffset now, CancellationToken ct)
    {
        ItemRightsService.Audit(db, actor, "denied", "denied", item.Id, item.AssetId, item.OwnerUserId, null, $"{action}: {detail}", now); await db.SaveChangesAsync(ct); return new OfferResult(false, error);
    }

    // ---- answering ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The receiver accepts. The same item changes owner in one step (its rights become the "next owner" rights, the group sharing and the folder are gone), or,
    /// when the giver kept theirs, one copy is made. Accepting twice, or at the same time as a cancellation, applies only once.
    /// </summary>
    public static async Task<OfferResult> AcceptAsync(LifeVerseDbContext db, Guid actor, Guid offerId, DateTimeOffset now, CancellationToken ct)
    {
        var offer = await db.ItemOffers.FirstOrDefaultAsync(o => o.Id == offerId && o.ToUserId == actor, ct); if (offer is null) return new OfferResult(false, "not_found");
        if (offer.Status == "accepted") return new OfferResult(true, null, offer, offer.ResultItemId is { } made ? await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == made, ct) : await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == offer.ItemId, ct)); // already done
        if (offer.Status != "pending") return new OfferResult(false, "offer_closed", offer);
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == offer.ItemId, ct);
        var gone = item is null || item.DeletedAt is not null || item.OwnerUserId != offer.FromUserId || (!offer.KeepCopy && item.Status != ItemRightsService.Offered) || (offer.KeepCopy && (item.Status != ItemRightsService.Active || !ItemRights.Has(item.OwnerRights, Right.Copy) || !ItemRights.Has(item.OwnerRights, Right.Transfer)));
        if (offer.ExpiresAt <= now) return await CloseAsync(db, offer, item, "expired", offer.FromUserId, actor, "transfer_cancel", "expired", now, ct, ok: false, error: "offer_expired");
        if (gone) return await CloseAsync(db, offer, item, "cancelled", offer.FromUserId, actor, "transfer_cancel", "the item is no longer available", now, ct, ok: false, error: "item_unavailable");

        var arrives = new ItemPermissions(item!.OwnerRights, item.GroupRights, item.NextOwnerRights).ForNextOwner(); var reason = offer.Kind == "gift" ? "gift" : "transfer"; InventoryItem result;
        if (!offer.KeepCopy && await db.InventoryItems.CountAsync(i => i.OwnerUserId == actor && i.DeletedAt == null, ct) >= MaxItemsPerPerson) return new OfferResult(false, "inventory_full", offer);
        if (offer.KeepCopy)
        {
            if (await db.InventoryItems.CountAsync(i => i.OwnerUserId == actor && i.DeletedAt == null, ct) >= MaxItemsPerPerson) return new OfferResult(false, "inventory_full", offer);
            result = NewItem(item, actor, arrives, item.DisplayName, now); result.CopiedFromItemId = item.Id; db.InventoryItems.Add(result); offer.ResultItemId = result.Id;
        }
        else
        {
            item.OwnerUserId = actor; item.OwnerRights = arrives.Owner; item.GroupRights = ItemRights.None; item.NextOwnerRights = arrives.NextOwner; item.GroupId = null; item.FolderId = null; item.IsFavorite = false; item.ShowOwnerHistory = false;
            item.Status = ItemRightsService.Active; item.Revision++; item.UpdatedAt = now; result = item;
        }
        offer.Status = "accepted"; offer.RespondedAt = now; offer.Revision++;
        ItemRightsService.RecordOwner(db, result, actor, offer.FromUserId, reason, now);
        ItemRightsService.Audit(db, actor, offer.Kind == "gift" ? "gift" : "transfer_accept", "ok", result.Id, result.AssetId, offer.FromUserId, actor, offer.KeepCopy ? $"copy of {item.Id}" : "moved", now);
        try { await db.SaveChangesAsync(ct); return new OfferResult(true, null, offer, result); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException) { db.ChangeTracker.Clear(); return new OfferResult(false, "stale"); } // another answer won; nothing here was applied
    }

    /// <summary>The receiver says no.</summary>
    public static Task<OfferResult> RejectAsync(LifeVerseDbContext db, Guid actor, Guid offerId, DateTimeOffset now, CancellationToken ct) => AnswerAsync(db, offerId, actor, receiver: true, "rejected", "transfer_reject", now, ct);

    /// <summary>The giver takes the offer back.</summary>
    public static Task<OfferResult> CancelAsync(LifeVerseDbContext db, Guid actor, Guid offerId, DateTimeOffset now, CancellationToken ct) => AnswerAsync(db, offerId, actor, receiver: false, "cancelled", "transfer_cancel", now, ct);

    private static async Task<OfferResult> AnswerAsync(LifeVerseDbContext db, Guid offerId, Guid actor, bool receiver, string status, string action, DateTimeOffset now, CancellationToken ct)
    {
        var offer = await db.ItemOffers.FirstOrDefaultAsync(o => o.Id == offerId && (receiver ? o.ToUserId == actor : o.FromUserId == actor), ct); if (offer is null) return new OfferResult(false, "not_found");
        if (offer.Status != "pending") return offer.Status == status ? new OfferResult(true, null, offer) : new OfferResult(false, "offer_closed", offer);
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == offer.ItemId, ct);
        return await CloseAsync(db, offer, item, status, offer.FromUserId, offer.ToUserId, action, status, now, ct, actor, ok: true, error: null);
    }

    /// <summary>Ends an offer without moving anything and gives the item back to its holder (if it was locked for this offer).</summary>
    private static async Task<OfferResult> CloseAsync(LifeVerseDbContext db, ItemOffer offer, InventoryItem? item, string status, Guid from, Guid to, string action, string detail, DateTimeOffset now, CancellationToken ct, Guid? actor = null, bool ok = true, string? error = null)
    {
        offer.Status = status; offer.RespondedAt = now; offer.Revision++;
        if (!offer.KeepCopy && item is { DeletedAt: null } && item.OwnerUserId == offer.FromUserId && item.Status == ItemRightsService.Offered) { item.Status = ItemRightsService.Active; item.Revision++; item.UpdatedAt = now; }
        ItemRightsService.Audit(db, actor ?? to, action, ok ? "ok" : "denied", offer.ItemId, offer.AssetId, from, to, detail, now);
        try { await db.SaveChangesAsync(ct); return new OfferResult(ok, error, offer); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException) { db.ChangeTracker.Clear(); return new OfferResult(false, "stale"); }
    }

    /// <summary>Closes the offers nobody answered in time and gives their items back. Returns how many were closed.</summary>
    public static async Task<int> ExpireAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var closed = 0;
        foreach (var id in await db.ItemOffers.AsNoTracking().Where(o => o.Status == "pending" && o.ExpiresAt <= now).OrderBy(o => o.ExpiresAt).Select(o => o.Id).Take(500).ToListAsync(ct))
        {
            var offer = await db.ItemOffers.FirstOrDefaultAsync(o => o.Id == id && o.Status == "pending", ct); if (offer is null) continue;
            var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == offer.ItemId, ct);
            if ((await CloseAsync(db, offer, item, "expired", offer.FromUserId, offer.ToUserId, "transfer_cancel", "expired", now, ct, offer.FromUserId)).Ok) closed++;
        }
        return closed;
    }

    // ---- reading ------------------------------------------------------------------------------------------------------------------

    /// <summary>The offers a person sent ("outgoing") or received ("incoming"), newest first. Offers that ran out are closed first, so what is listed is what can be answered.</summary>
    public static async Task<List<OfferView>> ListAsync(LifeVerseDbContext db, Guid user, bool incoming, bool pendingOnly, DateTimeOffset now, CancellationToken ct)
    {
        await ExpireAsync(db, now, ct);
        var query = db.ItemOffers.AsNoTracking().Where(o => incoming ? o.ToUserId == user : o.FromUserId == user); if (pendingOnly) query = query.Where(o => o.Status == "pending");
        var offers = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(100).ToListAsync(ct); if (offers.Count == 0) return [];
        var people = offers.SelectMany(o => new[] { o.FromUserId, o.ToUserId }).Distinct().ToList(); var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var itemIds = offers.Select(o => o.ItemId).Distinct().ToList(); var items = await db.InventoryItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.DisplayName, ct);
        return [.. offers.Select(o => new OfferView(o.Id, o.ItemId, items.GetValueOrDefault(o.ItemId, "item removido"), o.FromUserId, names.GetValueOrDefault(o.FromUserId, "conta removida"), o.ToUserId, names.GetValueOrDefault(o.ToUserId, "conta removida"), o.Kind, o.Message, o.KeepCopy, o.Status, o.CreatedAt, o.ExpiresAt, o.ResultItemId))];
    }
}

/// <summary>Releases the items of offers that nobody answered.</summary>
public sealed class ItemOfferWorker(IServiceScopeFactory scopes, ILogger<ItemOfferWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            try { using var scope = scopes.CreateScope(); var closed = await ItemTransfers.ExpireAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), DateTimeOffset.UtcNow, stoppingToken); if (closed > 0) logger.LogInformation("event=item_offers_expired count={Count}", closed); }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=item_offers_expire_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
