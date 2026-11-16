using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Users;

public sealed record AttachRequest(Guid InventoryItemId, string? Point, string? Mode, float[]? Position, float[]? Rotation, float[]? Scale);
public sealed record AttachTransformRequest(float[]? Position, float[]? Rotation, float[]? Scale, float? Sway = null);
public sealed record LookAttachment(Guid InventoryItemId, string Point, float[] Position, float[] Rotation, float[] Scale, int Order = 0, float Sway = 0);
public sealed record AttachMoveRequest(string? Point, string? Mode);
public sealed record AttachmentResult(bool Ok, string? Error = null, object? Data = null);

/// <summary>
/// Objects of the inventory worn on the body. The server decides everything: the object must be the person's own and ready to use, the point must exist on this avatar, how many fit is limited (in total, at a
/// point, on the screen), and where an object sits (position, rotation, scale) is limited and can only be changed when the person holds the right to modify it. A removed attachment keeps its row (not equipped), so
/// putting it back finds it where it was. Placement is always relative to the point, never to the world.
/// </summary>
public static class AvatarAttachments
{
    public static AttachmentPoints.Limits Limits { get; set; } = AttachmentPoints.DefaultLimits;

    private static readonly string[] AvatarProfiles = [AttachmentPoints.Humanoid];

    // ---- reading -----------------------------------------------------------------------------------------------------------------------------

    public static object View(AvatarAttachment a, string itemName = "", int rights = ItemRights.All) => new
    {
        id = a.Id, inventoryItemId = a.InventoryItemId, assetId = a.AssetId, name = itemName, point = a.AttachmentPoint, pointName = AttachmentPoints.Find(a.AttachmentPoint)?.Name ?? a.AttachmentPoint,
        position = new[] { (float)a.PositionX, (float)a.PositionY, (float)a.PositionZ }, rotation = new[] { a.RotationX, a.RotationY, a.RotationZ }, scale = new[] { (float)a.ScaleX, (float)a.ScaleY, (float)a.ScaleZ },
        equipped = a.Equipped, order = a.SortOrder, profileVersion = a.SkeletonProfileVersion, revision = a.Revision, canModify = ItemRights.Has(rights, Right.Modify), updatedAt = a.UpdatedAt,
        originalPoint = OriginalPoint(a), originalPointName = OriginalPoint(a) is { } o ? AttachmentPoints.Find(o)?.Name ?? o : null, sway = Sway(a)
    };

    /// <summary>The catalog this avatar can use (so a client never has to know the list): every point with its region, bone and Second Life name, and the limits.</summary>
    public static object Catalog(IEnumerable<string>? profiles = null) => new
    {
        version = AttachmentPoints.SkeletonProfileVersion, regions = AttachmentPoints.Regions,
        points = AttachmentPoints.For(profiles ?? AvatarProfiles).Select(p => new { id = p.Id, key = p.Key, name = p.Name, region = p.Region, bone = p.Bone, hud = p.IsHud, offset = p.Offset, secondLife = p.SecondLife }),
        limits = new { total = Limits.MaxTotal, perPoint = Limits.MaxPerPoint, hud = Limits.MaxHud, maxOffset = Limits.MaxOffset, minScale = Limits.MinScale, maxScale = Limits.MaxScale }
    };

    private static async Task<Avatar?> AvatarOfAsync(LifeVerseDbContext db, Guid user, CancellationToken ct) => await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == user, ct);

    /// <summary>What the avatar wears and what it has worn (and may wear again). Rows whose object left the person (given away, deleted, on offer) are taken off first.</summary>
    public static async Task<object?> ListAsync(LifeVerseDbContext db, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        if (await AvatarOfAsync(db, user, ct) is not { } avatar) return null;
        await ReconcileAsync(db, avatar.Id, user, now, ct); var profiles = await ProfilesAsync(db, avatar, ct); await ReconcileProfilesAsync(db, avatar, profiles, now, ct);
        var rows = await db.AvatarAttachments.AsNoTracking().Where(a => a.AvatarId == avatar.Id).OrderBy(a => a.AttachmentPoint).ThenBy(a => a.SortOrder).ToListAsync(ct);
        var ids = rows.Select(r => r.InventoryItemId).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return new { profiles, limits = new { total = Limits.MaxTotal, perPoint = Limits.MaxPerPoint, hud = Limits.MaxHud }, attachments = rows.Select(r => View(r, items.GetValueOrDefault(r.InventoryItemId)?.DisplayName ?? string.Empty, items.GetValueOrDefault(r.InventoryItemId)?.OwnerRights ?? 0)) };
    }

    /// <summary>The equipped attachments of any avatar, for the island servers and for other players: only what is needed to draw them, and only what the person still holds (an item given away or on offer is not shown).</summary>
    public static async Task<List<object>> WornAsync(LifeVerseDbContext db, Guid avatarId, CancellationToken ct)
    {
        var avatar = await db.Avatars.AsNoTracking().SingleOrDefaultAsync(a => a.Id == avatarId, ct); if (avatar is null) return []; var owner = avatar.UserId;
        await ReconcileProfilesAsync(db, avatar, await ProfilesAsync(db, avatar, ct), DateTimeOffset.UtcNow, ct);
        var rows = await (from a in db.AvatarAttachments.AsNoTracking() join i in db.InventoryItems.AsNoTracking() on a.InventoryItemId equals i.Id
                          where a.AvatarId == avatarId && a.Equipped && i.OwnerUserId == owner && i.Status == "active" && i.DeletedAt == null orderby a.AttachmentPoint, a.SortOrder select new { a, i.DisplayName }).ToListAsync(ct);
        return [.. rows.Select(r => View(r.a, r.DisplayName))];
    }

    /// <summary>What can be worn on the body: a 3D model, a picture (shown as a flat sign) or a saved construction (a set of shapes).</summary>
    public static bool IsAttachableMime(string? mime) => mime is not null && (mime.StartsWith("model/", StringComparison.Ordinal) || MaterialAssets.IsImage(mime) || mime == BuildPackageEndpoints.MimeType);

    /// <summary>Whether a file is a picture or a construction (and not a model): those are seen as what is worn, too.</summary>
    public static bool IsPictureOrConstruction(string? mime) => mime is not null && (MaterialAssets.IsImage(mime) || mime == BuildPackageEndpoints.MimeType);

    /// <summary>Whether a worn construction uses this asset (a picture on one of its shapes, a model of one of its meshes): whoever sees the construction must be able to get it.</summary>
    public static async Task<bool> WornConstructionUsesAsync(LifeVerseDbContext db, AssetStorageService storage, Guid assetId, CancellationToken ct)
    {
        var needle = assetId.ToString().ToLowerInvariant();
        var worn = await (from a in db.AvatarAttachments.AsNoTracking() where a.Equipped join v in db.AssetVersions.AsNoTracking() on a.AssetId equals v.AssetId join s in db.Assets.AsNoTracking() on a.AssetId equals s.Id
                          where v.Version == s.CurrentVersion && v.MimeType == BuildPackageEndpoints.MimeType select v.StorageKey).Distinct().Take(50).ToListAsync(ct);
        foreach (var key in worn)
        {
            var stream = await storage.OpenReadAsync(key, ct); if (stream is null) continue;
            using (stream) { using var reader = new StreamReader(stream); if ((await reader.ReadToEndAsync(ct)).Contains(needle, StringComparison.OrdinalIgnoreCase)) return true; }
        }
        return false;
    }

    /// <summary>Whether somebody wears this asset on the body: everybody on an island may then get the file, because they all see it.</summary>
    public static Task<bool> AssetIsWornAsync(LifeVerseDbContext db, Guid assetId, CancellationToken ct) => db.AvatarAttachments.AsNoTracking().AnyAsync(a => a.Equipped && a.AssetId == assetId, ct);

    /// <summary>Takes off what cannot be worn any more: the item is gone, belongs to somebody else, is on offer, or the asset was removed.</summary>
    public static async Task<int> ReconcileAsync(LifeVerseDbContext db, Guid avatarId, Guid user, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.AvatarAttachments.Where(a => a.AvatarId == avatarId).ToListAsync(ct); if (rows.Count == 0) return 0;
        var ids = rows.Select(r => r.InventoryItemId).ToList();
        var good = (await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id) && i.OwnerUserId == user && i.Status == "active" && i.DeletedAt == null).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
        var removed = 0;
        foreach (var row in rows.Where(r => !good.Contains(r.InventoryItemId))) { if (row.Equipped) removed++; db.AvatarAttachments.Remove(row); }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return removed;
    }


    // ---- profiles: the points a body has -------------------------------------------------------------------------------------------------

    /// <summary>The skeleton profiles this avatar has: "humanoid" always, and a tail, wings or fingers when the custom body it wears really has them.</summary>
    public static async Task<List<string>> ProfilesAsync(LifeVerseDbContext db, Avatar avatar, CancellationToken ct)
    {
        var profiles = new List<string> { AttachmentPoints.Humanoid };
        if (await AvatarModels.ActiveAsync(db, avatar, ct) is { } model) profiles.AddRange(AttachmentProfiles.FromReport(model.ReportJson));
        return profiles;
    }

    private static JsonElement? SettingsOf(AvatarAttachment a) { try { return JsonDocument.Parse(string.IsNullOrEmpty(a.SettingsJson) ? "{}" : a.SettingsJson).RootElement.Clone(); } catch (JsonException) { return null; } }
    /// <summary>The point the person chose when the body they wear now does not have it (the attachment waits at a nearby point and goes back when the body has it again).</summary>
    public static string? OriginalPoint(AvatarAttachment a) => SettingsOf(a) is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty("originalPoint", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null;
    /// <summary>How much a worn object sways with the body (0: not at all, 1: a lot): a light secondary movement, never a physics body.</summary>
    public static float Sway(AvatarAttachment a) => SettingsOf(a) is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty("sway", out var w) && w.TryGetSingle(out var v) ? Math.Clamp(v, 0f, 1f) : 0f;

    private static void WriteSettings(AvatarAttachment a, string? originalPoint, bool keepOriginal, float? sway)
    {
        var map = new Dictionary<string, object>();
        var original = keepOriginal ? originalPoint ?? OriginalPoint(a) : null; if (original is not null) map["originalPoint"] = original;
        var s = sway ?? Sway(a); if (s > 0) map["sway"] = Math.Round(s, 3);
        a.SettingsJson = JsonSerializer.Serialize(map);
    }

    /// <summary>
    /// After the body changed: an object at a point the body does not have goes to the nearest point it does (its placement is kept, and the point the person chose is remembered); an object that was moved earlier goes back
    /// when the body has its point again. Nothing the person set is overwritten. Returns what was moved (key, from, to).
    /// </summary>
    public static async Task<List<(Guid Id, string From, string To)>> ReconcileProfilesAsync(LifeVerseDbContext db, Avatar avatar, IReadOnlyCollection<string> profiles, DateTimeOffset now, CancellationToken ct)
    {
        var moved = new List<(Guid, string, string)>();
        foreach (var row in await db.AvatarAttachments.Where(a => a.AvatarId == avatar.Id).ToListAsync(ct))
        {
            if (OriginalPoint(row) is { } wanted && AttachmentPoints.Resolve(wanted, profiles) is { } back) { moved.Add((row.Id, row.AttachmentPoint, back.Key)); row.AttachmentPoint = back.Key; WriteSettings(row, null, false, null); row.UpdatedAt = now; row.Revision++; continue; }
            if (AttachmentPoints.Find(row.AttachmentPoint) is not { } point || AttachmentPoints.Resolve(point.Key, profiles) is not null) continue;
            if (AttachmentPoints.Fallback(point, profiles) is not { } to) continue;
            moved.Add((row.Id, point.Key, to.Key)); WriteSettings(row, OriginalPoint(row) ?? point.Key, true, null); row.AttachmentPoint = to.Key; row.UpdatedAt = now; row.Revision++;
        }
        if (moved.Count > 0) await db.SaveChangesAsync(ct);
        return moved;
    }

    // ---- outfits: what is worn, put aside and put back -------------------------------------------------------------------------------------

    /// <summary>What is worn now, as a look keeps it (the items, the points and the placements).</summary>
    public static async Task<List<LookAttachment>> SnapshotAsync(LifeVerseDbContext db, Guid avatarId, CancellationToken ct)
    {
        var rows = await db.AvatarAttachments.AsNoTracking().Where(a => a.AvatarId == avatarId && a.Equipped).OrderBy(a => a.AttachmentPoint).ThenBy(a => a.SortOrder).ToListAsync(ct);
        return [.. rows.Select(r => new LookAttachment(r.InventoryItemId, OriginalPoint(r) ?? r.AttachmentPoint, [(float)r.PositionX, (float)r.PositionY, (float)r.PositionZ], [(float)r.RotationX, (float)r.RotationY, (float)r.RotationZ], [(float)r.ScaleX, (float)r.ScaleY, (float)r.ScaleZ], r.SortOrder, Sway(r)))];
    }

    /// <summary>
    /// Puts a look's objects on the body (the objects worn now come off first). Everything goes through the same checks as wearing something by hand: the person must still hold the item, the point must exist on this body (or its
    /// nearest one is used), the limits hold, and a placement is only taken for an item they may modify. What cannot be put back is returned by name, never a reason to refuse the whole look. The caller saves.
    /// </summary>
    public static async Task<List<string>> RestoreAsync(LifeVerseDbContext db, Avatar avatar, IReadOnlyList<LookAttachment> wanted, DateTimeOffset now, CancellationToken ct)
    {
        var skipped = new List<string>(); var profiles = await ProfilesAsync(db, avatar, ct);
        var rows = await db.AvatarAttachments.Where(a => a.AvatarId == avatar.Id).ToListAsync(ct);
        foreach (var row in rows) row.Equipped = false;
        var total = 0; var perPoint = new Dictionary<string, int>(); var huds = 0;
        foreach (var w in wanted.Take(Limits.MaxTotal * 2))
        {
            var (item, error) = await UsableAsync(db, avatar.UserId, w.InventoryItemId, ct);
            var label = item?.DisplayName ?? w.InventoryItemId.ToString("N")[..8];
            if (item is null) { skipped.Add("attachment:" + label); continue; }
            var asked = AttachmentPoints.Find(w.Point); if (asked is null) { skipped.Add("attachment:" + label); continue; }
            var point = AttachmentPoints.Resolve(asked.Key, profiles) ?? AttachmentPoints.Fallback(asked, profiles); if (point is null) { skipped.Add("attachment:" + label); continue; }
            var canPlace = ItemRights.Has(item.OwnerRights, Right.Modify) && AttachmentPoints.CheckTransform(Limits, w.Position, w.Rotation, w.Scale) is null;
            if (total >= Limits.MaxTotal || (point.IsHud ? huds >= Limits.MaxHud : perPoint.GetValueOrDefault(point.Key) >= Limits.MaxPerPoint)) { skipped.Add("attachment:" + label); continue; }
            var row = rows.SingleOrDefault(a => a.InventoryItemId == item.Id);
            if (row is null) { row = new AvatarAttachment { Id = Guid.NewGuid(), AvatarId = avatar.Id, InventoryItemId = item.Id, AssetId = item.AssetId, AttachmentPoint = point.Key, CreatedAt = now, SkeletonProfileVersion = AttachmentPoints.SkeletonProfileVersion }; db.AvatarAttachments.Add(row); rows.Add(row); }
            row.AssetId = item.AssetId; row.AttachmentPoint = point.Key; row.Equipped = true; row.SortOrder = w.Order; row.UpdatedAt = now; row.Revision++;
            if (canPlace) Place(row, w.Position, w.Rotation, w.Scale); else Place(row, [0, 0, 0], [0, 0, 0], [1, 1, 1]);
            WriteSettings(row, point.Key != asked.Key ? asked.Key : null, point.Key != asked.Key, canPlace ? Math.Clamp(w.Sway, 0f, 1f) : 0f);
            total++; perPoint[point.Key] = perPoint.GetValueOrDefault(point.Key) + 1; if (point.IsHud) huds++;
        }
        return skipped;
    }
    // ---- writing -----------------------------------------------------------------------------------------------------------------------------

    private static async Task<(InventoryItem? Item, string? Error)> UsableAsync(LifeVerseDbContext db, Guid user, Guid itemId, CancellationToken ct)
    {
        var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId && i.DeletedAt == null, ct);
        if (item is null || item.OwnerUserId != user) return (null, "item_not_found");
        if (item.Status != "active") return (null, "item_unavailable");
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(a => a.Id == item.AssetId && a.DeletedAt == null, ct);
        if (asset is null || asset.Status != "ready") return (null, "asset_unavailable");
        var mime = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion).Select(v => v.MimeType).SingleOrDefaultAsync(ct);
        return IsAttachableMime(mime) ? (item, null) : (null, "not_attachable");
    }

    private static bool Hud(string key) => AttachmentPoints.Find(key)?.IsHud == true;

    /// <summary>Equips an item at a point. "add" keeps what is there; "replace" takes off what is at that point first. An item already worn is not worn twice: move it instead.</summary>
    public static async Task<AttachmentResult> AttachAsync(LifeVerseDbContext db, Guid user, AttachRequest r, DateTimeOffset now, CancellationToken ct)
    {
        if (await AvatarOfAsync(db, user, ct) is not { } avatar) return new(false, "avatar_not_found");
        var mode = (r.Mode ?? "add").Trim().ToLowerInvariant(); if (mode is not ("add" or "replace")) return new(false, "bad_mode");
        var profiles = await ProfilesAsync(db, avatar, ct);
        var point = AttachmentPoints.Resolve(r.Point, profiles); if (point is null) return new(false, AttachmentPoints.Find(r.Point) is null ? "unknown_point" : "point_unavailable");
        var (item, error) = await UsableAsync(db, user, r.InventoryItemId, ct); if (item is null) return new(false, error);
        var transform = AttachmentPoints.CheckTransform(Limits, r.Position, r.Rotation, r.Scale); if (transform is not null) return new(false, transform);
        var customized = r.Position is not null || r.Rotation is not null || r.Scale is not null;
        if (customized && !ItemRights.Has(item.OwnerRights, Right.Modify)) return new(false, "no_modify_right");

        var all = await db.AvatarAttachments.Where(a => a.AvatarId == avatar.Id).ToListAsync(ct);
        var row = all.SingleOrDefault(a => a.InventoryItemId == item.Id);
        if (row is { Equipped: true }) return new(false, "already_attached");
        if (mode == "replace") foreach (var other in all.Where(a => a.Equipped && a.AttachmentPoint == point.Key && a.Id != row?.Id)) other.Equipped = false;

        var equipped = all.Where(a => a.Equipped).ToList();
        if (equipped.Count >= Limits.MaxTotal) return new(false, "too_many_attachments");
        var here = equipped.Count(a => a.AttachmentPoint == point.Key);
        if (point.IsHud ? equipped.Count(a => Hud(a.AttachmentPoint)) >= Limits.MaxHud : here >= Limits.MaxPerPoint) return new(false, point.IsHud ? "too_many_huds" : "too_many_at_point");

        if (row is null)
        {
            row = new AvatarAttachment { Id = Guid.NewGuid(), AvatarId = avatar.Id, InventoryItemId = item.Id, AssetId = item.AssetId, AttachmentPoint = point.Key, CreatedAt = now, UpdatedAt = now, SkeletonProfileVersion = AttachmentPoints.SkeletonProfileVersion };
            db.AvatarAttachments.Add(row);
        }
        else if (row.AttachmentPoint != point.Key && !customized) { Place(row, point.Offset, [0, 0, 0], [1, 1, 1]); } // on another point the old placement would not make sense: start from the point's own
        row.AttachmentPoint = point.Key; row.AssetId = item.AssetId; row.Equipped = true; row.SortOrder = equipped.Where(a => a.AttachmentPoint == point.Key).Select(a => a.SortOrder + 1).DefaultIfEmpty(0).Max();
        if (customized) Place(row, r.Position ?? [(float)row.PositionX, (float)row.PositionY, (float)row.PositionZ], r.Rotation ?? [(float)row.RotationX, (float)row.RotationY, (float)row.RotationZ], r.Scale ?? [(float)row.ScaleX, (float)row.ScaleY, (float)row.ScaleZ]);
        else if (db.Entry(row).State == EntityState.Added) Place(row, [0, 0, 0], [0, 0, 0], [1, 1, 1]);
        row.UpdatedAt = now; row.Revision++;
        await db.SaveChangesAsync(ct);
        return new(true, null, View(row, item.DisplayName, item.OwnerRights));
    }

    private static void Place(AvatarAttachment a, float[] position, float[] rotation, float[] scale)
    {
        (a.PositionX, a.PositionY, a.PositionZ) = (position[0], position[1], position[2]);
        (a.RotationX, a.RotationY, a.RotationZ) = (rotation[0], rotation[1], rotation[2]);
        (a.ScaleX, a.ScaleY, a.ScaleZ) = (scale[0], scale[1], scale[2]);
    }

    private static async Task<(AvatarAttachment? Row, InventoryItem? Item)> OwnAsync(LifeVerseDbContext db, Guid user, Guid id, CancellationToken ct)
    {
        var row = await (from a in db.AvatarAttachments join av in db.Avatars on a.AvatarId equals av.Id where a.Id == id && av.UserId == user select a).SingleOrDefaultAsync(ct);
        return row is null ? (null, null) : (row, await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == row.InventoryItemId && i.OwnerUserId == user, ct));
    }

    /// <summary>Changes where the object sits (position, rotation, scale, relative to the point). Needs the right to modify the item and an object that is worn.</summary>
    public static async Task<AttachmentResult> TransformAsync(LifeVerseDbContext db, Guid user, Guid id, AttachTransformRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (row, item) = await OwnAsync(db, user, id, ct); if (row is null || item is null) return new(false, "not_found");
        if (!row.Equipped) return new(false, "not_attached");
        if (!ItemRights.Has(item.OwnerRights, Right.Modify)) return new(false, "no_modify_right");
        if (r.Position is null && r.Rotation is null && r.Scale is null && r.Sway is null) return new(false, "nothing_to_change");
        if (r.Sway is { } sway && (!float.IsFinite(sway) || sway < 0 || sway > 1)) return new(false, "bad_sway");
        var bad = AttachmentPoints.CheckTransform(Limits, r.Position, r.Rotation, r.Scale); if (bad is not null) return new(false, bad);
        Place(row, r.Position ?? [(float)row.PositionX, (float)row.PositionY, (float)row.PositionZ], r.Rotation ?? [(float)row.RotationX, (float)row.RotationY, (float)row.RotationZ], r.Scale ?? [(float)row.ScaleX, (float)row.ScaleY, (float)row.ScaleZ]); if (r.Sway is { } s) WriteSettings(row, null, true, s);
        row.UpdatedAt = now; row.Revision++;
        await db.SaveChangesAsync(ct); return new(true, null, View(row, item.DisplayName, item.OwnerRights));
    }

    /// <summary>Puts the placement back to the point's own (position, rotation, scale; any part can be left out to leave it as it is).</summary>
    public static async Task<AttachmentResult> ResetAsync(LifeVerseDbContext db, Guid user, Guid id, string? what, DateTimeOffset now, CancellationToken ct)
    {
        var (row, item) = await OwnAsync(db, user, id, ct); if (row is null || item is null) return new(false, "not_found");
        if (!row.Equipped) return new(false, "not_attached");
        if (!ItemRights.Has(item.OwnerRights, Right.Modify)) return new(false, "no_modify_right");
        var part = (what ?? "all").Trim().ToLowerInvariant(); if (part is not ("all" or "position" or "rotation" or "scale")) return new(false, "bad_part");
        if (part is "all" or "position") (row.PositionX, row.PositionY, row.PositionZ) = (0, 0, 0);
        if (part is "all" or "rotation") (row.RotationX, row.RotationY, row.RotationZ) = (0, 0, 0);
        if (part is "all" or "scale") (row.ScaleX, row.ScaleY, row.ScaleZ) = (1, 1, 1);
        row.UpdatedAt = now; row.Revision++;
        await db.SaveChangesAsync(ct); return new(true, null, View(row, item.DisplayName, item.OwnerRights));
    }

    /// <summary>Moves a worn object to another point. Taking an object to another point does not need the right to modify it. The placement starts over from the new point.</summary>
    public static async Task<AttachmentResult> MoveAsync(LifeVerseDbContext db, Guid user, Guid id, AttachMoveRequest r, DateTimeOffset now, CancellationToken ct)
    {
        var (row, item) = await OwnAsync(db, user, id, ct); if (row is null || item is null) return new(false, "not_found");
        if (!row.Equipped) return new(false, "not_attached");
        var mode = (r.Mode ?? "add").Trim().ToLowerInvariant(); if (mode is not ("add" or "replace")) return new(false, "bad_mode");
        var holder = await db.Avatars.AsNoTracking().SingleAsync(a => a.Id == row.AvatarId, ct); var profiles = await ProfilesAsync(db, holder, ct);
        var point = AttachmentPoints.Resolve(r.Point, profiles); if (point is null) return new(false, AttachmentPoints.Find(r.Point) is null ? "unknown_point" : "point_unavailable");
        if (point.Key == row.AttachmentPoint) return new(true, null, View(row, item.DisplayName, item.OwnerRights));
        var all = await db.AvatarAttachments.Where(a => a.AvatarId == row.AvatarId && a.Equipped && a.Id != row.Id).ToListAsync(ct);
        if (mode == "replace") foreach (var other in all.Where(a => a.AttachmentPoint == point.Key)) other.Equipped = false;
        var rest = all.Where(a => a.Equipped).ToList();
        if (point.IsHud ? rest.Count(a => Hud(a.AttachmentPoint)) >= Limits.MaxHud : rest.Count(a => a.AttachmentPoint == point.Key) >= Limits.MaxPerPoint) return new(false, point.IsHud ? "too_many_huds" : "too_many_at_point");
        row.AttachmentPoint = point.Key; Place(row, [0, 0, 0], [0, 0, 0], [1, 1, 1]);
        row.SortOrder = rest.Where(a => a.AttachmentPoint == point.Key).Select(a => a.SortOrder + 1).DefaultIfEmpty(0).Max();
        row.UpdatedAt = now; row.Revision++;
        await db.SaveChangesAsync(ct); return new(true, null, View(row, item.DisplayName, item.OwnerRights));
    }

    /// <summary>Takes the object off the body. The item stays in the inventory and the placement is kept for the next time.</summary>
    public static async Task<AttachmentResult> DetachAsync(LifeVerseDbContext db, Guid user, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var (row, item) = await OwnAsync(db, user, id, ct); if (row is null) return new(false, "not_found");
        if (row.Equipped) { row.Equipped = false; row.UpdatedAt = now; row.Revision++; await db.SaveChangesAsync(ct); }
        return new(true, null, View(row, item?.DisplayName ?? string.Empty, item?.OwnerRights ?? 0));
    }

    /// <summary>Takes everything off (or everything at one point).</summary>
    public static async Task<AttachmentResult> DetachAllAsync(LifeVerseDbContext db, Guid user, string? point, DateTimeOffset now, CancellationToken ct)
    {
        if (await AvatarOfAsync(db, user, ct) is not { } avatar) return new(false, "avatar_not_found");
        if (point is not null && AttachmentPoints.Find(point) is null) return new(false, "unknown_point");
        var rows = await db.AvatarAttachments.Where(a => a.AvatarId == avatar.Id && a.Equipped && (point == null || a.AttachmentPoint == point.ToLower())).ToListAsync(ct);
        foreach (var row in rows) { row.Equipped = false; row.UpdatedAt = now; row.Revision++; }
        await db.SaveChangesAsync(ct); return new(true, null, new { removed = rows.Count });
    }

    // ---- http --------------------------------------------------------------------------------------------------------------------------------

    public static IEndpointRouteBuilder MapAvatarAttachmentEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/avatar/attachments");
        g.MapGet("/points", async (HttpContext c, LifeVerseDbContext db, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : await AvatarOfAsync(db, me, ct) is not { } avatar ? Results.NotFound() : Results.Ok(Catalog(await ProfilesAsync(db, avatar, ct))));
        g.MapGet("", async (HttpContext c, LifeVerseDbContext db, TimeProvider t, CancellationToken ct) => !Who(c, out var me) ? Results.Unauthorized() : await ListAsync(db, me, t.GetUtcNow(), ct) is { } view ? Results.Ok(view) : Results.NotFound());
        g.MapPost("", async (AttachRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => await Run(c, log, "attachment_attached", me => AttachAsync(db, me, r, t.GetUtcNow(), ct), created: true));
        g.MapPatch("/{id:guid}", async (Guid id, AttachTransformRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => await Run(c, log, "attachment_transformed", me => TransformAsync(db, me, id, r, t.GetUtcNow(), ct)));
        g.MapPost("/{id:guid}/reset", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, string? part, CancellationToken ct) => await Run(c, log, "attachment_reset", me => ResetAsync(db, me, id, part, t.GetUtcNow(), ct)));
        g.MapPost("/{id:guid}/move", async (Guid id, AttachMoveRequest r, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => await Run(c, log, "attachment_moved", me => MoveAsync(db, me, id, r, t.GetUtcNow(), ct)));
        g.MapDelete("/{id:guid}", async (Guid id, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => await Run(c, log, "attachment_detached", me => DetachAsync(db, me, id, t.GetUtcNow(), ct)));
        g.MapDelete("", async (string? point, HttpContext c, LifeVerseDbContext db, TimeProvider t, ILoggerFactory log, CancellationToken ct) => await Run(c, log, "attachment_detached_all", me => DetachAllAsync(db, me, point, t.GetUtcNow(), ct)));
        return group;
    }

    private static bool Who(HttpContext context, out Guid user) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out user);

    private static async Task<IResult> Run(HttpContext context, ILoggerFactory log, string action, Func<Guid, Task<AttachmentResult>> operation, bool created = false)
    {
        if (!Who(context, out var me)) return Results.Unauthorized();
        var r = await operation(me);
        log.CreateLogger("LifeVerse.Audit").LogInformation("audit action={Action} user={User} result={Result}", action, me, r.Ok ? "ok" : r.Error);
        if (r.Ok) return created ? Results.Json(r.Data, statusCode: 201) : Results.Ok(r.Data ?? new { });
        return r.Error switch
        {
            "not_found" or "item_not_found" or "avatar_not_found" => Results.NotFound(new { error = r.Error }),
            "no_modify_right" => Results.Json(new { error = r.Error }, statusCode: 403),
            "already_attached" or "too_many_attachments" or "too_many_at_point" or "too_many_huds" or "item_unavailable" or "asset_unavailable" or "not_attachable" or "not_attached" => Results.Conflict(new { error = r.Error }),
            _ => Results.BadRequest(new { error = r.Error })
        };
    }
}
