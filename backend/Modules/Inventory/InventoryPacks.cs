using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

public sealed record LostObject(Guid Id, Guid IslandId, string Name, string Shape, Guid? GroupId, Guid? ParentId, float X, float Y, float Z, DateTimeOffset DeletedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// Getting back objects that left the world. Objects <b>returned</b> arrive in the inventory by themselves as a construction (marked "returned"); objects that were
/// <b>deleted</b> stay recoverable for a while, and recovering them puts them in the inventory as one construction (marked "recovered") — never straight back into the world,
/// so every rule about where and what may be placed is checked again when they are. An object can be recovered once: a second try, even at the same moment, is refused.
/// </summary>
public static class InventoryRecovery
{
    public const int RetentionDays = 30, MaxPerRecovery = 200, MaxListed = 200;

    private static IQueryable<BuildObject> Lost(LifeVerseDbContext db, Guid user, DateTimeOffset since) =>
        db.BuildObjects.Where(o => o.OwnerUserId == user && o.DeletedAt != null && o.DeletedAt >= since
            && db.BuildOperations.Where(p => p.ObjectId == o.Id).OrderByDescending(p => p.Revision).ThenByDescending(p => p.CreatedAt).Select(p => p.Operation).FirstOrDefault() == "delete"); // the last thing that happened to it was a deletion (not a return, not a recovery)

    public static async Task<List<LostObject>> ListAsync(LifeVerseDbContext db, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await Lost(db, user, now.AddDays(-RetentionDays)).AsNoTracking().OrderByDescending(o => o.DeletedAt).ThenBy(o => o.Id).Take(MaxListed).ToListAsync(ct);
        return [.. rows.Select(o => new LostObject(o.Id, o.IslandId, o.Name, o.PrimitiveType, o.GroupId, o.ParentObjectId, o.PositionX, o.PositionY, o.PositionZ, o.DeletedAt!.Value, o.DeletedAt!.Value.AddDays(RetentionDays)))];
    }

    /// <summary>
    /// The linksets among the objects being recovered. Objects deleted one by one lose their group as the others go (a linkset of one is dissolved), but each keeps the
    /// pointer to its parent, so the tree is read from those pointers: connected objects share one group, a parent outside the selection is dropped, and when several of them
    /// have no parent in the selection the later ones hang from the first, so a recovered linkset always has exactly one root.
    /// </summary>
    public static Dictionary<Guid, (Guid? Group, Guid? Parent)> Relink(IReadOnlyList<BuildObject> objects)
    {
        var ids = objects.Select(o => o.Id).ToHashSet(); var order = objects.Select((o, i) => (o.Id, i)).ToDictionary(x => x.Id, x => x.i); var union = objects.ToDictionary(o => o.Id, o => o.Id);
        Guid Find(Guid x) { while (union[x] != x) { union[x] = union[union[x]]; x = union[x]; } return x; }
        foreach (var o in objects) if (o.ParentObjectId is { } p && ids.Contains(p) && p != o.Id) union[Find(o.Id)] = Find(p);
        foreach (var same in objects.Where(o => o.GroupId is not null).GroupBy(o => o.GroupId)) { var first = same.First().Id; foreach (var o in same) union[Find(o.Id)] = Find(first); } // still in the same linkset when deleted
        var result = new Dictionary<Guid, (Guid?, Guid?)>();
        foreach (var component in objects.GroupBy(o => Find(o.Id)))
        {
            var members = component.OrderBy(o => order[o.Id]).ToList();
            if (members.Count < 2) { result[members[0].Id] = (null, null); continue; }
            var group = members.Where(m => m.GroupId is not null).GroupBy(m => m.GroupId).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? Guid.NewGuid();
            bool Inside(BuildObject m) => m.ParentObjectId is { } p && ids.Contains(p) && p != m.Id && Find(p) == Find(m.Id);
            var roots = members.Where(m => !Inside(m)).ToList(); var top = roots.Count > 0 ? roots[0] : members[0];
            foreach (var m in members) result[m.Id] = (group, m.Id == top.Id ? null : Inside(m) ? m.ParentObjectId : top.Id);
        }
        return result;
    }
    /// <summary>Puts the chosen deleted objects in the inventory as one construction. All of them or none: if one is not recoverable (not yours, not deleted, returned already, too old, recovered already) nothing happens.</summary>
    public static async Task<InventoryOutcome> RecoverAsync(LifeVerseDbContext db, AssetStorageService storage, Guid user, Guid[]? ids, DateTimeOffset now, CancellationToken ct)
    {
        var wanted = (ids ?? []).Distinct().ToList(); if (wanted.Count == 0 || wanted.Count > MaxPerRecovery) return InventoryOutcome.Fail("invalid_recovery");
        var objects = await Lost(db, user, now.AddDays(-RetentionDays)).Where(o => wanted.Contains(o.Id)).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct);
        if (objects.Count != wanted.Count) return InventoryOutcome.Fail("not_found");
        var inventory = await db.InventoryItems.CountAsync(i => i.OwnerUserId == user && i.DeletedAt == null, ct); if (inventory >= ItemTransfers.MaxItemsPerPerson) return InventoryOutcome.Fail("inventory_full");

        var links = Relink(objects);
        var sources = objects.Select(x => new PackageSourceDto(x.PrimitiveType, x.Name, x.Description, x.PositionX, x.PositionY, x.PositionZ, x.RotationX, x.RotationY, x.RotationZ, x.ScaleX, x.ScaleY, x.ScaleZ, x.MaterialJson, x.GeometryJson, x.PhysicsJson, x.ContentJson, links[x.Id].Group, x.Id, links[x.Id].Parent, x.PivotX, x.PivotY, x.PivotZ)).ToList();
        var first = objects[0].Name; var package = PackageDocument.Create(objects.Count == 1 ? $"Recuperado: {first}" : $"Recuperado: {first} (+{objects.Count - 1})", sources);
        var item = await BuildPackageStore.SaveAsync(db, storage, user, package, now, ct, ObjectRights.ForPackage(objects), "recovered");
        foreach (var o in objects)
        {
            o.Revision++; // a second recovery of the same object would write the same revision of its history: the database refuses it
            db.BuildOperations.Add(new BuildOperation { Id = Guid.NewGuid(), IslandId = o.IslandId, ObjectId = o.Id, ActorUserId = user, Operation = "recover", PayloadJson = JsonSerializer.Serialize(new { packageItemId = item.Id }), Revision = o.Revision, CreatedAt = now });
        }
        ItemRightsService.Audit(db, user, "recover", "ok", item.Id, item.AssetId, user, null, $"{objects.Count} deleted object(s) brought back to the inventory", now);
        try { await db.SaveChangesAsync(ct); }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException || RaceFailures.IsRace(exception)) { db.ChangeTracker.Clear(); return InventoryOutcome.Fail("already_recovered"); }
        return InventoryOutcome.Done(new { itemId = item.Id, name = package.Name, objects = objects.Count });
    }
}

public sealed record PackAsset(Guid AssetId, string Name, string Category, string Role, bool Held);
public sealed record PackPart(int Index, string Name, string Shape, int? Parent, bool Linked, int Scripts, int Animations, int OtherContent, bool HasPivot);

/// <summary>What is inside a saved construction, without handing it over: its parts, and the things (models, pictures, scripts, animations, sounds) the parts are made of, with which of them the holder has.</summary>
public static class PackContents
{
    public static async Task<InventoryOutcome> ReadAsync(LifeVerseDbContext db, AssetStorageService storage, Guid user, Guid itemId, CancellationToken ct)
    {
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerUserId == user && i.DeletedAt == null && i.Status != "consumed" && i.Status != "purged", ct); if (item is null) return InventoryOutcome.Fail("not_found");
        var (package, error) = await BuildPackageEndpoints.LoadAsync(db, storage, item, ct); if (package is null) return InventoryOutcome.Fail(error ?? "not_a_package");
        var parts = new List<PackPart>(); var roles = new Dictionary<Guid, string>();
        for (var i = 0; i < package.Objects.Length; i++)
        {
            var o = package.Objects[i]; var categories = BuildCost.ContentCategories(o.ContentJson).Take(ContentAssets.MaxEntries).ToList();
            parts.Add(new PackPart(i, o.Name, o.PrimitiveType, o.Parent, o.Group is not null, categories.Count(c => c == "scripts"), categories.Count(c => c == "animations"), categories.Count(c => c is not ("scripts" or "animations")), o.PivotX != 0 || o.PivotY != 0 || o.PivotZ != 0));
            var made = ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson);
            foreach (var id in made.Models) roles.TryAdd(id, "model"); foreach (var id in made.Textures) roles.TryAdd(id, "texture"); foreach (var id in made.Contents) roles.TryAdd(id, "content");
        }
        var assets = new List<PackAsset>();
        if (roles.Count > 0)
        {
            var ids = roles.Keys.ToList(); var known = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).Select(a => new { a.Id, a.Name, a.Category, a.OwnerUserId, a.CreatorUserId }).ToDictionaryAsync(a => a.Id, ct);
            var mine = (await db.InventoryItems.AsNoTracking().Where(i => i.OwnerUserId == user && i.DeletedAt == null && ids.Contains(i.AssetId)).Select(i => i.AssetId).ToListAsync(ct)).ToHashSet();
            foreach (var (id, role) in roles) assets.Add(known.TryGetValue(id, out var a) ? new PackAsset(id, a.Name, a.Category, role, mine.Contains(id) || (a.CreatorUserId == user && a.OwnerUserId == user)) : new PackAsset(id, "peça removida", string.Empty, role, false));
        }
        return InventoryOutcome.Done(new
        {
            itemId = item.Id, name = package.Name, objects = parts.Count, linksets = package.Objects.Where(o => o.Group is not null).Select(o => o.Group).Distinct().Count(), withScripts = parts.Count(p => p.Scripts > 0), withAnimations = parts.Count(p => p.Animations > 0),
            parts, assets = assets.OrderBy(a => a.Held).ThenBy(a => a.Name).ToList(), missing = assets.Count(a => !a.Held)
        });
    }
}

public sealed record UnpackFile(string Name, string? Error);

/// <summary>
/// Unpacking a zip of files into the inventory. Every file is checked first, with the same rules as an upload (kind, size, header, no scripts, no outside references)
/// and some more for zips (no paths that climb out, no files that expand absurdly, a limit on how many and how big in all); if any file fails, <b>nothing</b> is created.
/// Otherwise all the files become items in a new folder named after the zip, in one step.
/// </summary>
public static class PackUnzip
{
    public const int MaxEntries = 100; public const long MaxZipBytes = 100L * 1024 * 1024, MaxEntryBytes = 50L * 1024 * 1024, MaxTotalBytes = 300L * 1024 * 1024; public const int MaxRatio = 200;

    private static string? CategoryOf(string extension) => extension switch { ".glb" or ".gltf" => "models", ".png" or ".jpg" or ".jpeg" or ".webp" => "textures", ".ogg" or ".wav" or ".mp3" => "sounds", _ => null };

    private static string CleanName(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file); name = new string(name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim(); if (name.Length == 0) name = "arquivo"; return name[..Math.Min(name.Length, 160)];
    }

    public static async Task<(bool Ok, string? Error, object? Report)> UnpackAsync(LifeVerseDbContext db, AssetStorageService storage, Guid owner, string zipName, byte[] zip, DateTimeOffset now, CancellationToken ct, long quota = InventoryStorage.DefaultQuotaBytes)
    {
        if (zip.Length == 0 || zip.Length > MaxZipBytes) return (false, "invalid_zip", null);
        var files = new List<(string Path, byte[] Bytes, string Category, string Mime, int? Width, int? Height, string Metadata)>(); var problems = new List<UnpackFile>(); long total = 0;
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read, false);
            var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal) && !e.Name.StartsWith('.')).ToList(); // folders, macOS leftovers and hidden files are not files of the pack
            if (entries.Count == 0) return (false, "empty_zip", null);
            if (entries.Count > MaxEntries) return (false, "too_many_files", new { limit = MaxEntries, found = entries.Count });
            foreach (var entry in entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith('/') || path.Split('/').Any(s => s == "..") || path.Contains(':')) { problems.Add(new UnpackFile(path, "unsafe_path")); continue; }
                var extension = Path.GetExtension(entry.Name).ToLowerInvariant(); var category = CategoryOf(extension);
                if (category is null) { problems.Add(new UnpackFile(path, extension == ".zip" ? "nested_zip" : "unsupported_type")); continue; }
                if (entry.Length > MaxEntryBytes) { problems.Add(new UnpackFile(path, "file_too_large")); continue; }
                if (entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxRatio) { problems.Add(new UnpackFile(path, "suspicious_compression")); continue; }
                byte[] bytes;
                try
                {
                    using var stream = entry.Open(); using var memory = new MemoryStream(); var buffer = new byte[81920]; int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) > 0) { memory.Write(buffer, 0, read); if (memory.Length > MaxEntryBytes || total + memory.Length > MaxTotalBytes) break; } // what the file really holds, whatever its header says
                    if (memory.Length > MaxEntryBytes) { problems.Add(new UnpackFile(path, "file_too_large")); continue; }
                    if (total + memory.Length > MaxTotalBytes) return (false, "pack_too_large", new { limit = MaxTotalBytes });
                    bytes = memory.ToArray();
                }
                catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException) { problems.Add(new UnpackFile(path, "unreadable")); continue; }
                total += bytes.Length;
                var inspection = await AssetValidator.InspectAsync(entry.Name, string.Empty, bytes, ct, category);
                if (!inspection.Valid) { problems.Add(new UnpackFile(path, inspection.Error ?? "invalid_file")); continue; }
                files.Add((path, bytes, inspection.Category ?? category, inspection.MimeType, inspection.Width, inspection.Height, inspection.MetadataJson));
            }
        }
        catch (InvalidDataException) { return (false, "invalid_zip", null); }
        if (problems.Count > 0) return (false, "bad_files", new { problems = problems.Select(p => new { file = p.Name, error = p.Error }) });

        if (await db.InventoryItems.CountAsync(i => i.OwnerUserId == owner && i.DeletedAt == null, ct) + files.Count > ItemTransfers.MaxItemsPerPerson) return (false, "inventory_full", null);
        if (!await InventoryStorage.FitsAsync(db, owner, files.Sum(f => (long)f.Bytes.Length), quota, ct)) return (false, "storage_full", null); // checked before anything is stored
        var live = await db.InventoryFolders.Where(f => f.OwnerUserId == owner && f.DeletedAt == null).ToListAsync(ct); if (live.Count >= FolderRules.MaxFolders) return (false, "folder_limit", null);
        var baseName = FolderRules.CleanName(Path.GetFileNameWithoutExtension(zipName)) is { } clean ? new string(clean.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim() : "Pacote"; if (baseName.Length == 0) baseName = "Pacote";
        var folderName = baseName; for (var n = 2; live.Any(f => f.ParentId == null && string.Equals(f.Name, folderName, StringComparison.CurrentCultureIgnoreCase)); n++) folderName = $"{baseName} ({n})";
        var folder = new InventoryFolder { Id = Guid.NewGuid(), OwnerUserId = owner, ParentId = null, Name = folderName, CreatedAt = now, UpdatedAt = now }; db.InventoryFolders.Add(folder);

        var created = new List<object>();
        foreach (var (path, bytes, category, mime, width, height, metadata) in files)
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(); var name = CleanName(path);
            var existing = await db.AssetVersions.AsNoTracking().Include(v => v.Asset).FirstOrDefaultAsync(v => v.ContentHash == hash && v.Asset.DeletedAt == null, ct); Guid assetId;
            if (existing is not null) assetId = existing.AssetId; // the same bytes are already stored: the item points to them, as an upload of the same file does
            else
            {
                var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = name, Category = category, Description = string.Empty, LicenseJson = "{\"source\":\"user-upload\",\"moderation\":\"pending\"}", Status = "ready", CreatedAt = now, UpdatedAt = now };
                var key = await storage.PutAsync(owner, hash, bytes, ct);
                db.Assets.Add(asset); db.AssetVersions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = hash, StorageKey = key, MimeType = mime, ByteSize = bytes.Length, Width = width, Height = height, ValidationJson = metadata, CreatedAt = now }); assetId = asset.Id;
            }
            var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = assetId, DisplayName = name, FolderId = folder.Id, Origin = "unpacked", CreatedAt = now, UpdatedAt = now }; db.InventoryItems.Add(item);
            created.Add(new { itemId = item.Id, name, category });
        }
        ItemRightsService.Audit(db, owner, "unpack", "ok", null, null, owner, null, $"{files.Count} file(s) from \"{zipName}\" into folder \"{folderName}\"", now);
        await db.SaveChangesAsync(ct);
        return (true, null, new { folderId = folder.Id, folderName, count = created.Count, items = created });
    }
}
