using LifeVerse.Api.Data;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

/// <summary>An item and what the person asking may do with it.</summary>
public sealed record ItemAccess(bool Ok, string? Error, InventoryItem? Item, int Held);

public sealed record RightsView(Guid ItemId, Guid AssetId, string Name, Guid OwnerUserId, string Content, bool IsOwner, ItemPermissions Permissions, int YourRights, string YourRightsText, string Letters, bool Copyable, Guid? GroupId, bool ShowOwnerHistory, string Status, long Revision);
public sealed record OwnerLine(DateTimeOffset At, string Reason, string? Holder, string? From);

/// <summary>
/// The one place where the rights of an item are decided: who holds which rights, how they are changed, who is told the history, and what is written to
/// the audit trail. Everything else (copy, transfer, purchase, build, network commands) asks here, so the answer is the same everywhere and is always
/// given by the server, never taken from what the game says.
/// </summary>
public static class ItemRightsService
{
    public const string Active = "active", Offered = "offered";

    // ---- who may do what ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The rights a person holds over an item: the owner's set for the owner, the group's set for a member of the group the item is shared with, nothing for
    /// anyone else. An item that is removed, or waiting for an answer to an offer, can be used by nobody.
    /// </summary>
    public static async Task<int> HeldAsync(LifeVerseDbContext db, InventoryItem item, Guid user, CancellationToken ct)
    {
        if (item.DeletedAt is not null || item.Status != Active) return ItemRights.None;
        if (item.OwnerUserId == user) return ItemRights.Normalize(item.OwnerRights);
        if (item.GroupId is { } group && item.GroupRights != 0 && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == group && m.UserId == user, ct)) return ItemRights.Clamp(item.GroupRights, item.OwnerRights);
        return ItemRights.None;
    }

    public static async Task<bool> CanAsync(LifeVerseDbContext db, InventoryItem item, Guid user, Right right, CancellationToken ct) => ItemRights.Has(await HeldAsync(db, item, user, ct), right);

    /// <summary>
    /// Loads the item and checks that the person holds the right. A refusal is written to the audit trail (once, with the reason) before it is returned, so
    /// attempts to do what is not allowed leave a mark. The item is returned (not tracked for changes) only when the person may act.
    /// </summary>
    public static async Task<ItemAccess> RequireAsync(LifeVerseDbContext db, Guid user, Guid itemId, Right right, string action, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.DeletedAt == null, ct);
        if (item is null) return new ItemAccess(false, "not_found", null, ItemRights.None);
        var held = await HeldAsync(db, item, user, ct);
        if (held == ItemRights.None && item.OwnerUserId != user && !(item.GroupId is not null && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == item.GroupId && m.UserId == user, ct))) return new ItemAccess(false, "not_found", null, ItemRights.None); // not theirs: it does not exist for them
        if (item.Status != Active && item.OwnerUserId == user) return await DeniedAsync(db, user, item, action, "item_busy", $"status {item.Status}", now, ct);
        if (!ItemRights.Has(held, right)) return await DeniedAsync(db, user, item, action, "not_allowed", $"missing {right}; held {ItemRights.Letters(held)}", now, ct);
        return new ItemAccess(true, null, item, held);
    }

    private static async Task<ItemAccess> DeniedAsync(LifeVerseDbContext db, Guid user, InventoryItem item, string action, string error, string detail, DateTimeOffset now, CancellationToken ct)
    {
        Audit(db, user, "denied", "denied", item.Id, item.AssetId, item.OwnerUserId, null, $"{action}: {detail}", now);
        await db.SaveChangesAsync(ct);
        return new ItemAccess(false, error, null, ItemRights.None);
    }

    // ---- reading the rights ---------------------------------------------------------------------------------------------------------------

    /// <summary>The rights of an item as its holder (or someone it is shared with) sees them. Anyone else gets nothing, as if it did not exist.</summary>
    public static async Task<RightsView?> ViewAsync(LifeVerseDbContext db, Guid user, Guid itemId, CancellationToken ct)
    {
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.DeletedAt == null, ct); if (item is null) return null;
        var isOwner = item.OwnerUserId == user; var shared = !isOwner && item.GroupId is { } g && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == g && m.UserId == user, ct);
        if (!isOwner && !shared) return null;
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == item.AssetId, ct); var held = isOwner ? ItemRights.Normalize(item.OwnerRights) : ItemRights.Clamp(item.GroupRights, item.OwnerRights);
        var permissions = isOwner ? new ItemPermissions(item.OwnerRights, item.GroupRights, item.NextOwnerRights).Normalized() : new ItemPermissions(held, 0, 0); // a group member sees their own set, not what the owner passes on
        return new RightsView(item.Id, item.AssetId, item.DisplayName, item.OwnerUserId, ContentRules.FromCategory(asset?.Category).ToString().ToLowerInvariant(), isOwner, permissions, held, ItemRights.Describe(held), ItemRights.Letters(held), ItemRights.IsCopyable(held), item.GroupId, item.ShowOwnerHistory, item.Status, item.Revision);
    }

    /// <summary>The rights of something made of several items (an object with parts, a package): only what every part allows.</summary>
    public static async Task<ItemPermissions> ComposeAsync(LifeVerseDbContext db, IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        var parts = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.OwnerRights, i.GroupRights, i.NextOwnerRights }).ToListAsync(ct);
        return ItemPermissions.Compose(parts.Select(p => new ItemPermissions(p.OwnerRights, p.GroupRights, p.NextOwnerRights)));
    }

    // ---- changing the rights --------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The holder sets the three sets of rights. The group's and the next owner's sets must fit inside the holder's. The holder can give up rights of their own
    /// (and so what they pass on) but can only get back rights they do not hold if they are also the one who made the asset. A change made on an out-of-date
    /// view of the item (<paramref name="expectedRevision"/>) is refused.
    /// </summary>
    public static async Task<(bool Ok, string? Error)> SetPermissionsAsync(LifeVerseDbContext db, Guid actor, Guid itemId, ItemPermissions requested, long? expectedRevision, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == actor && i.DeletedAt == null, ct); if (item is null) return (false, "not_found");
        if (item.Status != Active) return await Refuse(db, actor, item, "rights_change", "item_busy", "waiting for an answer", now, ct);
        if (expectedRevision is { } expected && expected != item.Revision) return await Refuse(db, actor, item, "rights_change", "stale", $"expected {expected}, is {item.Revision}", now, ct);
        var requestedOwner = ItemRights.Normalize(requested.Owner); var creator = await db.Assets.AsNoTracking().Where(a => a.Id == item.AssetId).Select(a => a.CreatorUserId).FirstOrDefaultAsync(ct) == actor;
        var ceiling = creator ? ItemRights.All : ItemRights.Normalize(item.OwnerRights);
        if (!ItemRights.IsWithin(requested.Owner, ceiling) || (requested.Owner & ~ItemRights.All) != 0) return await Refuse(db, actor, item, "rights_change", "cannot_raise_rights", $"asked {ItemRights.Letters(requested.Owner)}, may {ItemRights.Letters(ceiling)}", now, ct);
        if (!ItemRights.IsWithin(requested.Group, requestedOwner) || !ItemRights.IsWithin(requested.NextOwner, requestedOwner) || (requested.Group & ~ItemRights.All) != 0 || (requested.NextOwner & ~ItemRights.All) != 0) return await Refuse(db, actor, item, "rights_change", "exceeds_owner", "group or next owner above the holder", now, ct);
        var before = $"{ItemRights.Letters(item.OwnerRights)}/{ItemRights.Letters(item.GroupRights)}/{ItemRights.Letters(item.NextOwnerRights)}"; var normalized = requested.Normalized();
        item.OwnerRights = normalized.Owner; item.GroupRights = item.GroupId is null ? ItemRights.None : normalized.Group; item.NextOwnerRights = normalized.NextOwner; item.Revision++; item.UpdatedAt = now;
        Audit(db, actor, "rights_change", "ok", item.Id, item.AssetId, actor, null, $"{before} -> {ItemRights.Letters(item.OwnerRights)}/{ItemRights.Letters(item.GroupRights)}/{ItemRights.Letters(item.NextOwnerRights)}", now);
        return await Save(db, ct);
    }

    /// <summary>Shares the item with one of the holder's groups (or stops sharing with null). Members of the group then hold the group's set of rights.</summary>
    public static async Task<(bool Ok, string? Error)> ShareWithGroupAsync(LifeVerseDbContext db, Guid actor, Guid itemId, Guid? groupId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == actor && i.DeletedAt == null, ct); if (item is null) return (false, "not_found");
        if (item.Status != Active) return await Refuse(db, actor, item, "share_group", "item_busy", "waiting for an answer", now, ct);
        if (groupId is { } group && !await db.GroupMembers.AnyAsync(m => m.GroupId == group && m.UserId == actor, ct) || groupId is { } g2 && !await db.SocialGroups.AnyAsync(s => s.Id == g2 && s.DeletedAt == null, ct)) return await Refuse(db, actor, item, "share_group", "not_in_group", "not a member of that group", now, ct);
        var before = item.GroupId; item.GroupId = groupId; if (groupId is null) item.GroupRights = ItemRights.None; item.Revision++; item.UpdatedAt = now;
        Audit(db, actor, "share_group", "ok", item.Id, item.AssetId, actor, null, $"{before?.ToString() ?? "none"} -> {groupId?.ToString() ?? "none"}", now);
        return await Save(db, ct);
    }

    /// <summary>The holder decides whether other people can see the history of who held the item.</summary>
    public static async Task<(bool Ok, string? Error)> SetHistoryVisibilityAsync(LifeVerseDbContext db, Guid actor, Guid itemId, bool visible, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == actor && i.DeletedAt == null, ct); if (item is null) return (false, "not_found");
        item.ShowOwnerHistory = visible; item.Revision++; item.UpdatedAt = now;
        Audit(db, actor, "history_setting", "ok", item.Id, item.AssetId, actor, null, visible ? "shown" : "hidden", now);
        return await Save(db, ct);
    }

    private static async Task<(bool Ok, string? Error)> Refuse(LifeVerseDbContext db, Guid actor, InventoryItem item, string action, string error, string detail, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); Audit(db, actor, action, "denied", item.Id, item.AssetId, item.OwnerUserId, null, $"{error}: {detail}", now); await db.SaveChangesAsync(ct); return (false, error);
    }

    private static async Task<(bool Ok, string? Error)> Save(LifeVerseDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return (true, null); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return (false, "stale"); } // somebody else changed the item at the same moment: nothing here was applied
    }

    // ---- history and audit ----------------------------------------------------------------------------------------------------------------

    /// <summary>Adds a line to the history of owners (not saved here: it goes with the change it describes).</summary>
    public static void RecordOwner(LifeVerseDbContext db, InventoryItem item, Guid holder, Guid? from, string reason, DateTimeOffset now) =>
        db.ItemOwnerRecords.Add(new ItemOwnerRecord { Id = Guid.NewGuid(), ItemId = item.Id, AssetId = item.AssetId, UserId = holder, FromUserId = from, Reason = reason, At = now });

    /// <summary>Adds a line to the audit trail (not saved here: it goes with the change it describes, so both exist or neither does).</summary>
    public static void Audit(LifeVerseDbContext db, Guid actor, string action, string outcome, Guid? itemId, Guid? assetId, Guid? from, Guid? to, string detail, DateTimeOffset now) =>
        db.ItemAudit.Add(new ItemAuditEntry { Id = Guid.NewGuid(), At = now, ActorUserId = actor, Action = action, Outcome = outcome, ItemId = itemId, AssetId = assetId, FromUserId = from, ToUserId = to, Detail = detail.Length > 1000 ? detail[..1000] : detail });

    /// <summary>
    /// The history of who held an item. The holder always sees all of it; a member of the group it is shared with sees it only when the holder allows it. To other
    /// people the names of the ones in between stay hidden: they see who made it and who holds it, and for the rest only that there was "a previous owner".
    /// </summary>
    public static async Task<List<OwnerLine>?> HistoryAsync(LifeVerseDbContext db, Guid viewer, Guid itemId, CancellationToken ct)
    {
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.DeletedAt == null, ct); if (item is null) return null;
        var isOwner = item.OwnerUserId == viewer; var shared = !isOwner && item.GroupId is { } g && await db.GroupMembers.AsNoTracking().AnyAsync(m => m.GroupId == g && m.UserId == viewer, ct);
        if (!isOwner && !(shared && item.ShowOwnerHistory)) return null;
        var lines = await db.ItemOwnerRecords.AsNoTracking().Where(r => r.ItemId == itemId).OrderBy(r => r.At).ThenBy(r => r.Id).ToListAsync(ct); if (lines.Count == 0) return [];
        var ids = lines.SelectMany(l => new[] { (Guid?)l.UserId, l.FromUserId }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        var creator = lines[0].UserId;
        string? Name(Guid? id) => id is not { } who ? null : who == viewer || isOwner || who == creator || who == item.OwnerUserId ? names.GetValueOrDefault(who) ?? "conta removida" : "proprietário anterior";
        return [.. lines.Select(l => new OwnerLine(l.At, l.Reason, Name(l.UserId), Name(l.FromUserId)))];
    }
}
