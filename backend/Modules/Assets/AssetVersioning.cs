using System.Security.Cryptography;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Api.Modules.Social;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Assets;

public sealed record VersionResult(Guid AssetId, int Version, string Name, bool Changed);
public sealed record VersionLine(int Version, DateTimeOffset At, long Size, string MimeType, string Notes, bool Current);

/// <summary>
/// Versions of an asset. The creator publishes a new version (a new file for the same asset) with a note on what changed; the asset's current version is what everybody gets
/// from then on (placed objects, previews, the inventory list), and the creator can go back to an earlier version if the new one turns out wrong. Versions are never rewritten
/// or deleted, numbering only goes up, and the people who hold the item are told when a version is published, with the note.
/// </summary>
public static class AssetVersioning
{
    public const int MaxVersions = 50, MaxNotes = 400, MaxHolderNotices = 500;

    /// <summary>The file kinds a category may be updated with: a model stays a model, a picture a picture.</summary>
    private static string[] Extensions(string category) => category switch
    {
        "models" or "animations" or "clothing" or "accessories" or "objects" => [".glb", ".gltf"], "textures" => [".png", ".jpg", ".jpeg", ".webp"], "sounds" => [".ogg", ".wav"], _ => []
    };

    public static string CleanNotes(string? notes) { var text = new string([.. (notes ?? string.Empty).Where(c => !char.IsControl(c) || c == '\n')]).Trim(); return text.Length > MaxNotes ? text[..MaxNotes] : text; }

    /// <summary>Publishes a new version and makes it the current one. Only the creator may. The file goes through the same checks as an upload and must be the same kind of file as the asset.</summary>
    public static Task<InventoryOutcome> PublishAsync(LifeVerseDbContext db, AssetStorageService storage, Guid owner, Guid assetId, string fileName, byte[] bytes, string? notes, DateTimeOffset now, CancellationToken ct) => OneAtATime(db, assetId, ct, () => PublishCoreAsync(db, storage, owner, assetId, fileName, bytes, notes, now, ct));
    public static Task<InventoryOutcome> ActivateAsync(LifeVerseDbContext db, Guid owner, Guid assetId, int version, DateTimeOffset now, CancellationToken ct) => OneAtATime(db, assetId, ct, () => ActivateCoreAsync(db, owner, assetId, version, now, ct));

    /// <summary>Publishing and going back take turns for the same asset (on PostgreSQL, an advisory lock until saved): two at once could each pick the same number, or leave the current version pointing at the older of the two.</summary>
    private static async Task<InventoryOutcome> OneAtATime(LifeVerseDbContext db, Guid assetId, CancellationToken ct, Func<Task<InventoryOutcome>> body)
    {
        if (db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) != true) return await body();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"asset-versions:" + assetId}))", ct);
        var result = await body(); if (result.Ok) await transaction.CommitAsync(ct); return result;
    }

    private static async Task<InventoryOutcome> PublishCoreAsync(LifeVerseDbContext db, AssetStorageService storage, Guid owner, Guid assetId, string fileName, byte[] bytes, string? notes, DateTimeOffset now, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId && a.OwnerUserId == owner && a.DeletedAt == null, ct); if (asset is null) return InventoryOutcome.Fail("not_found");
        var allowed = Extensions(asset.Category); var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (allowed.Length == 0) return InventoryOutcome.Fail("not_versionable"); if (!allowed.Contains(extension)) return InventoryOutcome.Fail("wrong_kind");
        var versions = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId).Select(v => new { v.Version, v.ContentHash }).ToListAsync(ct);
        if (versions.Count >= MaxVersions) return InventoryOutcome.Fail("version_limit");
        var inspection = await AssetValidator.InspectAsync(fileName, string.Empty, bytes, ct); if (!inspection.Valid) return new InventoryOutcome(false, "invalid_file", inspection.Error);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (versions.Any(v => v.ContentHash == hash && v.Version == asset.CurrentVersion)) return InventoryOutcome.Fail("unchanged");
        var next = versions.Max(v => v.Version) + 1; var key = await storage.PutAsync(owner, hash, bytes, ct);
        db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = assetId, Version = next, ContentHash = hash, StorageKey = key, MimeType = inspection.MimeType, ByteSize = bytes.Length, Width = inspection.Width, Height = inspection.Height, DurationSeconds = inspection.DurationSeconds, ValidationJson = inspection.MetadataJson, Notes = CleanNotes(notes), CreatedAt = now });
        asset.CurrentVersion = next; asset.UpdatedAt = now;
        ItemRightsService.Audit(db, owner, "version_publish", "ok", null, assetId, owner, null, $"{asset.Name}: version {next}", now);
        try { await db.SaveChangesAsync(ct); } catch (Exception exception) when (exception is DbUpdateConcurrencyException || RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return InventoryOutcome.Fail("stale"); } // two versions published at the same moment: one wins, the other is told
        return InventoryOutcome.Done(new VersionResult(assetId, next, asset.Name, true));
    }

    /// <summary>Makes an existing version the current one again (going back after a bad update, or forward again). The version keeps its number; nothing is rewritten.</summary>
    private static async Task<InventoryOutcome> ActivateCoreAsync(LifeVerseDbContext db, Guid owner, Guid assetId, int version, DateTimeOffset now, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == assetId && a.OwnerUserId == owner && a.DeletedAt == null, ct); if (asset is null) return InventoryOutcome.Fail("not_found");
        if (!await db.AssetVersions.AnyAsync(v => v.AssetId == assetId && v.Version == version, ct)) return InventoryOutcome.Fail("not_found");
        if (asset.CurrentVersion == version) return InventoryOutcome.Done(new VersionResult(assetId, version, asset.Name, false));
        asset.CurrentVersion = version; asset.UpdatedAt = now; ItemRightsService.Audit(db, owner, "version_activate", "ok", null, assetId, owner, null, $"{asset.Name}: back to version {version}", now);
        await db.SaveChangesAsync(ct); return InventoryOutcome.Done(new VersionResult(assetId, version, asset.Name, true));
    }

    /// <summary>The versions of an asset, newest first, for its creator and for whoever holds an item of it.</summary>
    public static async Task<InventoryOutcome> HistoryAsync(LifeVerseDbContext db, Guid user, Guid assetId, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId && a.DeletedAt == null, ct); if (asset is null) return InventoryOutcome.Fail("not_found");
        var isOwner = asset.OwnerUserId == user; if (!isOwner && !await db.InventoryItems.AnyAsync(i => i.AssetId == assetId && i.OwnerUserId == user && i.DeletedAt == null && i.Status != "consumed" && i.Status != "purged", ct)) return InventoryOutcome.Fail("not_found");
        var lines = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId).OrderByDescending(v => v.Version).Select(v => new VersionLine(v.Version, v.CreatedAt, v.ByteSize, v.MimeType, v.Notes, v.Version == asset.CurrentVersion)).ToListAsync(ct);
        return InventoryOutcome.Done(new { assetId, name = asset.Name, category = asset.Category, current = asset.CurrentVersion, isOwner, versions = lines });
    }

    /// <summary>"Already seen": the holder looked at the versions, so the update mark of this item goes away until the next version.</summary>
    public static async Task<InventoryOutcome> AcknowledgeAsync(LifeVerseDbContext db, Guid user, Guid itemId, DateTimeOffset now, CancellationToken ct)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == user && i.DeletedAt == null && i.Status == "active", ct); if (item is null) return InventoryOutcome.Fail("not_found");
        item.UpdateAckAt = now; await db.SaveChangesAsync(ct); return InventoryOutcome.Done();
    }

    /// <summary>The holders of the asset (not its creator) who are online are told a version was published or restored.</summary>
    public static async Task NotifyHoldersAsync(NotificationHub hub, LifeVerseDbContext db, Guid assetId, int version, CancellationToken ct)
    {
        try
        {
            var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, ct); if (asset is null) return;
            var notes = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId && v.Version == version).Select(v => v.Notes).FirstOrDefaultAsync(ct) ?? string.Empty;
            var holders = await db.InventoryItems.AsNoTracking().Where(i => i.AssetId == assetId && i.OwnerUserId != asset.OwnerUserId && i.DeletedAt == null && i.Status != "consumed" && i.Status != "purged").Select(i => i.OwnerUserId).Distinct().Take(MaxHolderNotices).ToListAsync(ct);
            foreach (var holder in holders) await hub.NotifyAsync(holder, "asset_updated", new { assetId, name = asset.Name, version, notes }, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { }
    }
}
