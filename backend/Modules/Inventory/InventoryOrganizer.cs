using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

/// <summary>The rules for the tree of folders, as pure arithmetic on (id, parent) pairs: depth, subtrees and what would make a loop.</summary>
public static class FolderRules
{
    public const int MaxDepth = 8, MaxFolders = 500, MaxMove = 500, MaxNameLength = 120;
    public readonly record struct Node(Guid Id, Guid? ParentId);

    /// <summary>How many levels deep a folder is (1 for one at the top). A missing parent or a loop stops the count.</summary>
    public static int DepthOf(IReadOnlyDictionary<Guid, Guid?> parents, Guid? id)
    {
        var depth = 0; var seen = new HashSet<Guid>();
        while (id is { } at && parents.TryGetValue(at, out var up) && seen.Add(at)) { depth++; id = up; }
        return depth;
    }

    /// <summary>The folder and everything below it.</summary>
    public static HashSet<Guid> Subtree(IEnumerable<Node> nodes, Guid root)
    {
        var children = nodes.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!.Value, n => n.Id); var result = new HashSet<Guid> { root }; var queue = new Queue<Guid>([root]);
        while (queue.Count > 0) foreach (var child in children[queue.Dequeue()]) if (result.Add(child)) queue.Enqueue(child);
        return result;
    }

    /// <summary>How many levels the folder and what is below it take (1 for a folder with nothing inside).</summary>
    public static int Height(IEnumerable<Node> nodes, Guid root)
    {
        var list = nodes.ToList(); var children = list.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!.Value, n => n.Id); var best = 1; var seen = new HashSet<Guid> { root }; var queue = new Queue<(Guid Id, int Level)>([(root, 1)]);
        while (queue.Count > 0) { var (id, level) = queue.Dequeue(); best = Math.Max(best, level); foreach (var child in children[id]) if (seen.Add(child)) queue.Enqueue((child, level + 1)); }
        return best;
    }

    /// <summary>Null when a folder may go under <paramref name="target"/> (null: the top); otherwise "cycle" (into itself or below itself) or "too_deep".</summary>
    public static string? CheckMove(IReadOnlyCollection<Node> nodes, Guid folder, Guid? target)
    {
        if (target is null) return null;
        if (Subtree(nodes, folder).Contains(target.Value)) return "cycle";
        var parents = nodes.ToDictionary(n => n.Id, n => n.ParentId);
        return DepthOf(parents, target) + Height(nodes, folder) > MaxDepth ? "too_deep" : null;
    }

    public static string? CleanName(string? name)
    {
        name = (name ?? string.Empty).Trim();
        return name.Length is < 1 or > MaxNameLength || name.Any(char.IsControl) ? null : name;
    }
}

public sealed record InventoryOutcome(bool Ok, string? Error = null, object? Data = null)
{
    public static InventoryOutcome Done(object? data = null) => new(true, null, data);
    public static InventoryOutcome Fail(string error) => new(false, error);
}

/// <summary>
/// Organising the inventory: folders and subfolders, moving things in bulk, the bin (trash) and bringing things back from it. Everything here belongs to one
/// person and runs in one save, so a move or a deletion either happens whole or not at all.
/// </summary>
public static class InventoryOrganizer
{
    public const int TrashDays = 30, RecentDays = 14;


    // ---- one change to the tree at a time -----------------------------------------------------------------------------------------

    /// <summary>
    /// Changes to the shape of a person's folders (create, rename, move, delete, restore) are checked against the tree as it is. Two of them at once could each look fine
    /// and together make a loop, so on PostgreSQL they take turns for the same person (an advisory lock held until the change is saved).
    /// </summary>
    private static async Task<InventoryOutcome> LockedAsync(LifeVerseDbContext db, Guid owner, CancellationToken ct, Func<Task<InventoryOutcome>> body)
    {
        try { return await LockedCoreAsync(db, owner, ct, body); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return InventoryOutcome.Fail("stale"); } // somebody changed one of these items at the same moment (a delivery, a deletion): nothing was applied, try again
    }

    private static async Task<InventoryOutcome> LockedCoreAsync(LifeVerseDbContext db, Guid owner, CancellationToken ct, Func<Task<InventoryOutcome>> body)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true) return await body();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({owner.ToString()}))", ct);
        var result = await body(); if (result.Ok) await transaction.CommitAsync(ct); return result;
    }

    public static Task<InventoryOutcome> CreateFolderAsync(LifeVerseDbContext db, Guid owner, string? name, Guid? parentId, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => CreateFolderCoreAsync(db, owner, name, parentId, now, ct));
    public static Task<InventoryOutcome> UpdateFolderAsync(LifeVerseDbContext db, Guid owner, Guid folderId, string? name, Guid? parentId, bool toTop, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => UpdateFolderCoreAsync(db, owner, folderId, name, parentId, toTop, now, ct));
    public static Task<InventoryOutcome> DeleteFolderAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => DeleteFolderCoreAsync(db, owner, folderId, now, ct));
    public static Task<InventoryOutcome> RestoreFolderAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => RestoreFolderCoreAsync(db, owner, folderId, now, ct));
    public static Task<InventoryOutcome> MoveAsync(LifeVerseDbContext db, Guid owner, Guid[]? itemIds, Guid[]? folderIds, Guid? target, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => MoveCoreAsync(db, owner, itemIds, folderIds, target, now, ct));
    public static Task<InventoryOutcome> PurgeFolderAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct) => LockedAsync(db, owner, ct, () => PurgeFolderCoreAsync(db, owner, folderId, now, ct));

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase);

    private static async Task<List<InventoryFolder>> LiveFoldersAsync(LifeVerseDbContext db, Guid owner, CancellationToken ct) =>
        await db.InventoryFolders.Where(f => f.OwnerUserId == owner && f.DeletedAt == null).ToListAsync(ct);

    private static List<FolderRules.Node> NodesOf(IEnumerable<InventoryFolder> folders) => folders.Select(f => new FolderRules.Node(f.Id, f.ParentId)).ToList();

    // ---- folders ---------------------------------------------------------------------------------------------------------------

    private static async Task<InventoryOutcome> CreateFolderCoreAsync(LifeVerseDbContext db, Guid owner, string? name, Guid? parentId, DateTimeOffset now, CancellationToken ct)
    {
        if (FolderRules.CleanName(name) is not { } clean) return InventoryOutcome.Fail("invalid_name");
        var live = await LiveFoldersAsync(db, owner, ct);
        if (parentId is { } parent && live.All(f => f.Id != parent)) return InventoryOutcome.Fail("not_found");
        if (live.Count >= FolderRules.MaxFolders) return InventoryOutcome.Fail("folder_limit");
        if (parentId is not null && FolderRules.DepthOf(live.ToDictionary(f => f.Id, f => f.ParentId), parentId) + 1 > FolderRules.MaxDepth) return InventoryOutcome.Fail("too_deep");
        if (live.Any(f => f.ParentId == parentId && SameName(f.Name, clean))) return InventoryOutcome.Fail("name_taken");
        var folder = new InventoryFolder { Id = Guid.NewGuid(), OwnerUserId = owner, ParentId = parentId, Name = clean, CreatedAt = now, UpdatedAt = now };
        db.InventoryFolders.Add(folder); await db.SaveChangesAsync(ct); return InventoryOutcome.Done(folder);
    }

    /// <summary>Renames a folder and/or moves it under another (or to the top with <paramref name="toTop"/>). A folder cannot go into itself or into what is inside it, and the tree stays within its depth.</summary>
    private static async Task<InventoryOutcome> UpdateFolderCoreAsync(LifeVerseDbContext db, Guid owner, Guid folderId, string? name, Guid? parentId, bool toTop, DateTimeOffset now, CancellationToken ct)
    {
        var live = await LiveFoldersAsync(db, owner, ct); var folder = live.FirstOrDefault(f => f.Id == folderId); if (folder is null) return InventoryOutcome.Fail("not_found");
        var cleanName = folder.Name; if (name is not null) { if (FolderRules.CleanName(name) is not { } clean) return InventoryOutcome.Fail("invalid_name"); cleanName = clean; }
        var destination = toTop ? null : parentId ?? folder.ParentId;
        if (destination is { } d && live.All(f => f.Id != d)) return InventoryOutcome.Fail("not_found");
        if (destination != folder.ParentId && FolderRules.CheckMove(NodesOf(live), folderId, destination) is { } problem) return InventoryOutcome.Fail(problem);
        if (live.Any(f => f.Id != folderId && f.ParentId == destination && SameName(f.Name, cleanName))) return InventoryOutcome.Fail("name_taken");
        folder.Name = cleanName; folder.ParentId = destination; folder.UpdatedAt = now; await db.SaveChangesAsync(ct); return InventoryOutcome.Done(folder);
    }

    /// <summary>
    /// Deleting a folder sends it, its subfolders and everything inside to the bin in one step; restoring the folder brings all of it back. Items that cannot be
    /// binned right now (they are in an offer) are taken out of the folder instead of being lost with it.
    /// </summary>
    private static async Task<InventoryOutcome> DeleteFolderCoreAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct)
    {
        var live = await LiveFoldersAsync(db, owner, ct); if (live.All(f => f.Id != folderId)) return InventoryOutcome.Fail("not_found");
        var ids = FolderRules.Subtree(NodesOf(live), folderId);
        foreach (var folder in live.Where(f => ids.Contains(f.Id))) { folder.DeletedAt = now; folder.UpdatedAt = now; }
        var items = await db.InventoryItems.Where(i => i.OwnerUserId == owner && i.FolderId != null && ids.Contains(i.FolderId.Value) && i.DeletedAt == null && i.Status != "consumed" && i.Status != "purged").ToListAsync(ct); var binned = 0;
        foreach (var item in items)
        {
            if (item.Status == "active") { item.DeletedAt = now; item.DeletedWithFolderId = folderId; binned++; } else item.FolderId = null;
            item.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct); return InventoryOutcome.Done(new { folders = ids.Count, items = binned });
    }

    private static async Task<InventoryOutcome> RestoreFolderCoreAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct)
    {
        var folder = await db.InventoryFolders.FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerUserId == owner && f.DeletedAt != null, ct); if (folder is null) return InventoryOutcome.Fail("not_found");
        var stamp = folder.DeletedAt; var all = await db.InventoryFolders.Where(f => f.OwnerUserId == owner).ToListAsync(ct); var live = all.Where(f => f.DeletedAt == null).ToList();
        var together = FolderRules.Subtree(all.Where(f => f.DeletedAt == stamp).Select(f => new FolderRules.Node(f.Id, f.ParentId)), folderId); // what was binned in the same step
        if (live.Count + together.Count > FolderRules.MaxFolders) return InventoryOutcome.Fail("folder_limit");
        var parentAlive = folder.ParentId is { } p && live.Any(f => f.Id == p); var destination = parentAlive ? folder.ParentId : null; var name = folder.Name;
        for (var n = 2; live.Any(f => f.ParentId == destination && SameName(f.Name, name)); n++) name = $"{folder.Name} ({n})";
        folder.Name = name; folder.ParentId = destination;
        foreach (var f in all.Where(f => together.Contains(f.Id))) { f.DeletedAt = null; f.UpdatedAt = now; }
        var items = await db.InventoryItems.Where(i => i.OwnerUserId == owner && i.DeletedWithFolderId == folderId && i.Status == "active").ToListAsync(ct);
        foreach (var item in items) { item.DeletedAt = null; item.DeletedWithFolderId = null; item.UpdatedAt = now; if (item.FolderId is { } at && !together.Contains(at)) item.FolderId = null; }
        await db.SaveChangesAsync(ct); return InventoryOutcome.Done(new { folders = together.Count, items = items.Count, folder });
    }

    // ---- moving ----------------------------------------------------------------------------------------------------------------

    /// <summary>Moves items and folders to a folder (null: the top level) in one step: if anything cannot be moved, nothing is.</summary>
    private static async Task<InventoryOutcome> MoveCoreAsync(LifeVerseDbContext db, Guid owner, Guid[]? itemIds, Guid[]? folderIds, Guid? target, DateTimeOffset now, CancellationToken ct)
    {
        var itemList = (itemIds ?? []).Distinct().ToList(); var folderList = (folderIds ?? []).Distinct().ToList();
        if (itemList.Count + folderList.Count is 0 or > FolderRules.MaxMove) return InventoryOutcome.Fail("invalid_move");
        var live = await LiveFoldersAsync(db, owner, ct);
        if (target is { } t && live.All(f => f.Id != t)) return InventoryOutcome.Fail("not_found");
        var items = itemList.Count == 0 ? [] : await db.InventoryItems.Where(i => i.OwnerUserId == owner && itemList.Contains(i.Id) && i.DeletedAt == null && i.Status == "active").ToListAsync(ct);
        if (items.Count != itemList.Count) return InventoryOutcome.Fail("not_found");
        var moving = live.Where(f => folderList.Contains(f.Id)).ToList(); if (moving.Count != folderList.Count) return InventoryOutcome.Fail("not_found");
        var nodes = NodesOf(live);
        foreach (var folder in moving) if (FolderRules.CheckMove(nodes, folder.Id, target) is { } problem) return InventoryOutcome.Fail(problem);
        var names = moving.Select(f => f.Name).ToList(); if (names.Count != names.Distinct(StringComparer.CurrentCultureIgnoreCase).Count()) return InventoryOutcome.Fail("name_taken");
        foreach (var folder in moving) if (live.Any(f => f.ParentId == target && f.Id != folder.Id && !folderList.Contains(f.Id) && SameName(f.Name, folder.Name))) return InventoryOutcome.Fail("name_taken");
        foreach (var folder in moving) { folder.ParentId = target; folder.UpdatedAt = now; }
        foreach (var item in items) { item.FolderId = target; item.UpdatedAt = now; }
        await db.SaveChangesAsync(ct); return InventoryOutcome.Done(new { items = items.Count, folders = moving.Count });
    }

    /// <summary>Null when the folder may be used as the place of an item (it is the person's own and not in the bin).</summary>
    public static async Task<bool> OwnFolderAsync(LifeVerseDbContext db, Guid owner, Guid folderId, CancellationToken ct) =>
        await db.InventoryFolders.AnyAsync(f => f.Id == folderId && f.OwnerUserId == owner && f.DeletedAt == null, ct);

    // ---- the bin ---------------------------------------------------------------------------------------------------------------

    /// <summary>Gets rid of one binned item for good: it stays on record (history keeps pointing at it) but is gone from every list and cannot come back.</summary>
    public static async Task<InventoryOutcome> PurgeItemAsync(LifeVerseDbContext db, Guid owner, Guid itemId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == owner && i.DeletedAt != null && i.Status == "active", ct); if (item is null) return InventoryOutcome.Fail("not_found");
        item.Status = "purged"; item.DeletedWithFolderId = null; item.UpdatedAt = now; item.Revision++;
        ItemRightsService.Audit(db, owner, "purge", "ok", item.Id, item.AssetId, owner, null, "removed from the bin", now);
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (RaceFailures.IsRace(exception)) { return InventoryOutcome.Fail("item_busy"); }
        return InventoryOutcome.Done();
    }

    private static async Task<InventoryOutcome> PurgeFolderCoreAsync(LifeVerseDbContext db, Guid owner, Guid folderId, DateTimeOffset now, CancellationToken ct)
    {
        var folder = await db.InventoryFolders.FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerUserId == owner && f.DeletedAt != null, ct); if (folder is null) return InventoryOutcome.Fail("not_found");
        var stamp = folder.DeletedAt; var binned = await db.InventoryFolders.Where(f => f.OwnerUserId == owner && f.DeletedAt == stamp).ToListAsync(ct);
        var together = FolderRules.Subtree(binned.Select(f => new FolderRules.Node(f.Id, f.ParentId)), folderId);
        var items = await db.InventoryItems.Where(i => i.OwnerUserId == owner && i.DeletedWithFolderId == folderId && i.Status == "active").ToListAsync(ct);
        foreach (var item in items) { item.Status = "purged"; item.DeletedWithFolderId = null; item.UpdatedAt = now; item.Revision++; ItemRightsService.Audit(db, owner, "purge", "ok", item.Id, item.AssetId, owner, null, "removed from the bin with its folder", now); }
        db.InventoryFolders.RemoveRange(binned.Where(f => together.Contains(f.Id)));
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (RaceFailures.IsRace(exception)) { return InventoryOutcome.Fail("item_busy"); }
        return InventoryOutcome.Done(new { folders = together.Count, items = items.Count });
    }

    /// <summary>Empties the bin of one person, or (with a cut-off) only what has been in it since before the cut-off. Returns how many items and folders went.</summary>
    public static async Task<(int Items, int Folders)> EmptyTrashAsync(LifeVerseDbContext db, Guid? owner, DateTimeOffset now, DateTimeOffset? olderThan, CancellationToken ct)
    {
        var items = await db.InventoryItems.Where(i => (owner == null || i.OwnerUserId == owner) && i.DeletedAt != null && i.Status == "active" && (olderThan == null || i.DeletedAt <= olderThan)).ToListAsync(ct);
        foreach (var item in items) { item.Status = "purged"; item.DeletedWithFolderId = null; item.UpdatedAt = now; item.Revision++; ItemRightsService.Audit(db, item.OwnerUserId, "purge", "ok", item.Id, item.AssetId, item.OwnerUserId, null, olderThan is null ? "bin emptied" : "bin expired", now); }
        var folders = await db.InventoryFolders.Where(f => (owner == null || f.OwnerUserId == owner) && f.DeletedAt != null && (olderThan == null || f.DeletedAt <= olderThan)).ToListAsync(ct);
        db.InventoryFolders.RemoveRange(folders);
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return (0, 0); }
        return (items.Count, folders.Count);
    }

    /// <summary>What has been in the bin for longer than the retention is removed for good.</summary>
    public static Task<(int Items, int Folders)> ExpireTrashAsync(LifeVerseDbContext db, DateTimeOffset now, CancellationToken ct) => EmptyTrashAsync(db, null, now, now.AddDays(-TrashDays), ct);

    // ---- recents ---------------------------------------------------------------------------------------------------------------

    /// <summary>Notes that an item was just used (placed, worn, opened), so it shows among the recent ones.</summary>
    public static async Task TouchAsync(LifeVerseDbContext db, Guid owner, Guid itemId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == owner && i.Status == "active", ct); if (item is null) return;
        item.LastUsedAt = now; await db.SaveChangesAsync(ct);
    }
}

/// <summary>Takes the bin's old contents away for good.</summary>
public sealed class InventoryTrashWorker(IServiceScopeFactory scopes, ILogger<InventoryTrashWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { using var scope = scopes.CreateScope(); var (items, folders) = await InventoryOrganizer.ExpireTrashAsync(scope.ServiceProvider.GetRequiredService<LifeVerseDbContext>(), DateTimeOffset.UtcNow, stoppingToken); if (items + folders > 0) logger.LogInformation("event=inventory_trash_expired items={Items} folders={Folders}", items, folders); }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "event=inventory_trash_expire_failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
