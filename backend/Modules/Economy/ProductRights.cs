using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Economy;

/// <summary>What a listing decided: the rights the buyer will get, what the buyer may pass on, and (for a resale) the item that is for sale.</summary>
public sealed record ListingPlan(int Buyer, int BuyerNext, InventoryItem? Item);
public sealed record ListingResult(string? Error, ListingPlan? Plan)
{
    public bool Ok => Error is null;
}

public sealed record DeliveryResult(string? Error, InventoryItem? Item);

/// <summary>
/// The rights side of the marketplace. A product made by its creator says what the buyer gets (and what the buyer can pass on); a resale puts one item up for
/// sale and the buyer gets what a next owner of that item gets. What is bought always arrives with rights chosen here by the server, never by the buyer.
/// </summary>
public static class ProductRights
{
    public const string Listed = "listed";

    /// <summary>
    /// Works out the rights of a listing. For a new listing by the creator they come from the request (or the old permissions text, or the standard set) and
    /// the pass-on set must fit inside what the buyer gets. For a resale the seller must hold the item with Transfer and Resell; its next-owner rights are
    /// what the buyer will get, and an item that cannot be copied is locked while it is for sale.
    /// </summary>
    public static async Task<ListingResult> PlanAsync(LifeVerseDbContext db, Guid seller, PublishProductRequest request, MarketplaceProduct? existing, DateTimeOffset now, CancellationToken ct)
    {
        if (existing?.SourceItemId is not null) return new ListingResult(null, new ListingPlan(existing.BuyerRights, existing.BuyerNextRights, null)); // a resale keeps the rights of its item
        if (existing is null && request.ItemId is { } itemId)
        {
            var access = await ItemRightsService.RequireAsync(db, seller, itemId, Right.Transfer, "list_for_sale", now, ct); if (!access.Ok) return new ListingResult(access.Error, null);
            if (access.Item!.OwnerUserId != seller) return await Deny(db, seller, access.Item, "only the holder can sell an item", now, ct);
            if (!ItemRights.Has(access.Held, Right.Resell)) return await Deny(db, seller, access.Item, "the item cannot be resold", now, ct);
            if (await db.MarketplaceProducts.AnyAsync(p => p.SourceItemId == itemId && p.Status == "active", ct)) return new ListingResult("already_listed", null);
            if (!ItemRights.IsCopyable(access.Item.OwnerRights) && await Worlds.ObjectRights.InUseAsync(db, seller, access.Item.AssetId, null, ct)) return await Deny(db, seller, access.Item, "it is part of an object in the world; return the object first", now, ct);
            var item = await db.InventoryItems.FirstAsync(i => i.Id == itemId, ct);
            if (item.Revision != access.Item.Revision || item.Status != ItemRightsService.Active || item.DeletedAt is not null || item.OwnerUserId != seller) return new ListingResult("item_busy", null); // it changed between the check and now
            var arrives = new ItemPermissions(item.OwnerRights, ItemRights.None, item.NextOwnerRights).ForNextOwner();
            if (!ItemRights.IsCopyable(item.OwnerRights)) { item.Status = Listed; item.Revision++; item.UpdatedAt = now; } // the only one there is: it cannot be used or offered while it waits for a buyer
            return new ListingResult(null, new ListingPlan(arrives.Owner, arrives.NextOwner, item));
        }
        var hasNew = request.BuyerRights is not null || request.NextOwnerRights is not null || !string.IsNullOrWhiteSpace(request.PermissionsJson);
        var buyer = request.BuyerRights ?? (existing is not null && !hasNew ? existing.BuyerRights : string.IsNullOrWhiteSpace(request.PermissionsJson) ? ItemRights.Standard : ItemRights.FromJson(request.PermissionsJson, ItemRights.Standard));
        var next = request.NextOwnerRights ?? (existing is not null && request.BuyerRights is null && !hasNew ? existing.BuyerNextRights : buyer);
        if ((buyer & ~ItemRights.All) != 0 || (next & ~ItemRights.All) != 0) return new ListingResult("bad_rights", null);
        buyer = ItemRights.Normalize(buyer); if (!ItemRights.IsWithin(next, buyer)) return new ListingResult("next_exceeds_buyer", null);
        return new ListingResult(null, new ListingPlan(buyer, ItemRights.Normalize(next), null));
    }

    private static async Task<ListingResult> Deny(LifeVerseDbContext db, Guid seller, InventoryItem item, string why, DateTimeOffset now, CancellationToken ct)
    {
        ItemRightsService.Audit(db, seller, "denied", "denied", item.Id, item.AssetId, item.OwnerUserId, null, "list_for_sale: " + why, now); await db.SaveChangesAsync(ct); return new ListingResult("not_allowed", null);
    }

    /// <summary>Takes a resale off sale: the item it was keeping is released (a copyable item was never locked).</summary>
    public static async Task ReleaseAsync(LifeVerseDbContext db, MarketplaceProduct product, DateTimeOffset now, CancellationToken ct)
    {
        if (product.SourceItemId is not { } id) return;
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id && i.Status == Listed, ct);
        if (item is not null) { item.Status = ItemRightsService.Active; item.Revision++; item.UpdatedAt = now; }
    }

    /// <summary>The rights the buyer ends up with, as the listing promises: shown in the store and written to the item.</summary>
    public static ItemPermissions BuyerPermissions(MarketplaceProduct product) => new ItemPermissions(product.BuyerRights, ItemRights.None, product.BuyerNextRights).Normalized();

    /// <summary>
    /// Puts what was bought in the inventory of the person who gets it (the buyer, or the person it was bought for). A product from its creator makes a new item
    /// with the listed rights; a resale moves the seller's only item (it is gone from the seller) or, if it can be copied, makes a copy and leaves the seller's.
    /// The sold item is checked again at this moment: it must still belong to the seller and still be for sale.
    /// </summary>
    public static async Task<DeliveryResult> DeliverAsync(LifeVerseDbContext db, MarketplaceProduct product, Guid buyer, Guid recipient, DateTimeOffset now, CancellationToken ct)
    {
        var reason = recipient == buyer ? (product.SourceItemId is null ? "purchase" : "resale") : "gift";
        if (product.SourceItemId is not { } sourceId)
        {
            var rights = BuyerPermissions(product);
            var made = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = recipient, AssetId = product.AssetId, DisplayName = product.Name, OwnerRights = rights.Owner, GroupRights = ItemRights.None, NextOwnerRights = rights.NextOwner, CreatedAt = now, UpdatedAt = now };
            db.InventoryItems.Add(made); ItemRightsService.RecordOwner(db, made, recipient, product.SellerUserId, reason, now); return new DeliveryResult(null, made);
        }
        var source = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == sourceId, ct);
        if (source is null || source.DeletedAt is not null || source.OwnerUserId != product.SellerUserId) return new DeliveryResult("listing_unavailable", null);
        var arrives = new ItemPermissions(source.OwnerRights, ItemRights.None, source.NextOwnerRights).ForNextOwner();
        if (ItemRights.IsCopyable(source.OwnerRights))
        {
            if (source.Status != ItemRightsService.Active || !ItemRights.Has(source.OwnerRights, Right.Resell)) return new DeliveryResult("listing_unavailable", null);
            var copy = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = recipient, AssetId = source.AssetId, DisplayName = source.DisplayName, OwnerRights = arrives.Owner, GroupRights = ItemRights.None, NextOwnerRights = arrives.NextOwner, CopiedFromItemId = source.Id, CreatedAt = now, UpdatedAt = now };
            db.InventoryItems.Add(copy); ItemRightsService.RecordOwner(db, copy, recipient, product.SellerUserId, reason, now); return new DeliveryResult(null, copy);
        }
        if (source.Status != Listed) return new DeliveryResult("listing_unavailable", null);
        source.OwnerUserId = recipient; source.OwnerRights = arrives.Owner; source.GroupRights = ItemRights.None; source.NextOwnerRights = arrives.NextOwner; source.GroupId = null; source.FolderId = null; source.IsFavorite = false; source.ShowOwnerHistory = false;
        source.Status = ItemRightsService.Active; source.Revision++; source.UpdatedAt = now; product.Status = "sold"; product.UpdatedAt = now;
        ItemRightsService.RecordOwner(db, source, recipient, product.SellerUserId, reason, now); return new DeliveryResult(null, source);
    }
}
