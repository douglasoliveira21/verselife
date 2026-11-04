using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Inventory;
using LifeVerse.Shared.Items;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Worlds;

public sealed record SetObjectRightsRequest(int Owner, int NextOwner, int ExpectedRevision);

/// <summary>
/// The rights of objects standing in the world. An object is made of things (a 3D model, textures, scripts and other contents), and it can do no more than they
/// allow: what it inherits is the strictest of its parts. Changing what an object is made of needs the Modify right; moving it does not. A thing that cannot be
/// copied can be in only one object at a time, so it is not duplicated by building.
/// </summary>
public static class ObjectRights
{
    public const int MaxParentDepth = 16;

    /// <summary>The assets an object is made of: its 3D model, the images of its material, and the things in its contents.</summary>
    public sealed record Parts(HashSet<Guid> Models, HashSet<Guid> Textures, HashSet<Guid> Contents)
    {
        public IEnumerable<Guid> All => Models.Concat(Textures).Concat(Contents).Distinct();
        public IEnumerable<Guid> Singles => Models.Concat(Contents).Distinct(); // the parts that are used up by being in an object (textures can be seen in many places)
    }

    public static Parts PartsOf(string primitiveType, string? geometryJson, string? materialJson, string? contentJson) => new(
        string.Equals(primitiveType, MeshAssets.PrimitiveType, StringComparison.OrdinalIgnoreCase) && MeshAssets.AssetIdOf(geometryJson) is { } model ? [model] : [],
        MaterialAssets.TextureIds(materialJson), [.. ContentAssets.AssetIds(contentJson)]);

    /// <summary>
    /// The rights of an object made of these parts: for each part the person made themselves, nothing is taken away; for each part they were given or bought,
    /// the rights of the item they hold (the most they have if they hold several). The strictest wins.
    /// </summary>
    public static async Task<ItemPermissions> InheritAsync(LifeVerseDbContext db, Guid actor, Parts parts, CancellationToken ct)
    {
        var rights = new List<ItemPermissions>();
        foreach (var id in parts.All)
        {
            var asset = await db.Assets.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.OwnerUserId, a.CreatorUserId }).FirstOrDefaultAsync(ct); if (asset is null || (asset.CreatorUserId == actor && asset.OwnerUserId == actor)) continue;
            var held = await db.InventoryItems.AsNoTracking().Where(i => i.AssetId == id && i.OwnerUserId == actor && i.DeletedAt == null).Select(i => new { i.OwnerRights, i.NextOwnerRights }).ToListAsync(ct); if (held.Count == 0) continue;
            var best = held.OrderByDescending(h => System.Numerics.BitOperations.PopCount((uint)h.OwnerRights)).ThenByDescending(h => h.OwnerRights).First(); rights.Add(new ItemPermissions(best.OwnerRights, 0, best.NextOwnerRights));
        }
        var composed = ItemPermissions.Compose(rights); return new ItemPermissions(composed.Owner, 0, composed.NextOwner);
    }

    /// <summary>
    /// Null when the parts are acceptable. A part that cannot be copied can be in only one object of the person at a time (the model and the contents; the
    /// images of a material can be seen in many places): putting it in a second one would be copying it.
    /// </summary>
    public static async Task<string?> ValidateSinglesAsync(LifeVerseDbContext db, Guid actor, Parts now, Parts? before, Guid? exceptObject, CancellationToken ct)
    {
        var already = before?.Singles.ToHashSet() ?? [];
        foreach (var id in now.Singles.Where(i => !already.Contains(i)))
        {
            var asset = await db.Assets.AsNoTracking().Where(a => a.Id == id).Select(a => new { a.OwnerUserId, a.CreatorUserId }).FirstOrDefaultAsync(ct); if (asset is null || (asset.CreatorUserId == actor && asset.OwnerUserId == actor)) continue;
            var items = await db.InventoryItems.AsNoTracking().Where(i => i.AssetId == id && i.OwnerUserId == actor && i.DeletedAt == null).Select(i => i.OwnerRights).ToListAsync(ct);
            if (items.Count == 0 || items.Any(r => ItemRights.IsCopyable(r))) continue; // copyable: as many objects as wanted
            if (await InUseAsync(db, actor, id, exceptObject, ct)) return "no_copy_in_use";
        }
        return null;
    }

    /// <summary>Whether a thing can be in several objects: the person made it themselves, or holds an item of it that can be copied.</summary>
    public static async Task<bool> CanBeInManyAsync(LifeVerseDbContext db, Guid actor, Guid assetId, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().Where(a => a.Id == assetId).Select(a => new { a.OwnerUserId, a.CreatorUserId }).FirstOrDefaultAsync(ct); if (asset is null || (asset.CreatorUserId == actor && asset.OwnerUserId == actor)) return true;
        var items = await db.InventoryItems.AsNoTracking().Where(i => i.AssetId == assetId && i.OwnerUserId == actor && i.DeletedAt == null).Select(i => i.OwnerRights).ToListAsync(ct);
        return items.Count == 0 || items.Any(r => ItemRights.IsCopyable(r));
    }

    /// <summary>Whether a model or a content of this asset is already in a live object of the person.</summary>
    public static async Task<bool> InUseAsync(LifeVerseDbContext db, Guid owner, Guid assetId, Guid? exceptObject, CancellationToken ct)
    {
        var except = exceptObject ?? Guid.Empty; // the settings columns are JSON in PostgreSQL, which cannot be searched as text: the owner's live objects are read and looked through here
        var rows = await db.BuildObjects.AsNoTracking().Where(o => o.OwnerUserId == owner && o.DeletedAt == null && o.Id != except).Select(o => new { o.PrimitiveType, o.GeometryJson, o.ContentJson }).ToListAsync(ct);
        return rows.Any(o => ContentAssets.AssetIds(o.ContentJson).Contains(assetId) || (string.Equals(o.PrimitiveType, MeshAssets.PrimitiveType, StringComparison.OrdinalIgnoreCase) && MeshAssets.AssetIdOf(o.GeometryJson) == assetId));
    }

    /// <summary>Rights an object really has: its own, cut by those of every object it is a part of (a child of an object that cannot be modified cannot be modified either).</summary>
    public static async Task<ItemPermissions> EffectiveAsync(LifeVerseDbContext db, BuildObject obj, CancellationToken ct)
    {
        var chain = new List<ItemPermissions> { new(obj.OwnerRights, 0, obj.NextOwnerRights) }; var next = obj.ParentObjectId; var seen = new HashSet<Guid> { obj.Id };
        for (var depth = 0; next is { } parent && depth < MaxParentDepth && seen.Add(parent); depth++)
        {
            var row = await db.BuildObjects.AsNoTracking().Where(o => o.Id == parent && o.DeletedAt == null).Select(o => new { o.OwnerRights, o.NextOwnerRights, o.ParentObjectId }).FirstOrDefaultAsync(ct); if (row is null) break;
            chain.Add(new ItemPermissions(row.OwnerRights, 0, row.NextOwnerRights)); next = row.ParentObjectId;
        }
        var composed = ItemPermissions.Compose(chain); return new ItemPermissions(composed.Owner, 0, composed.NextOwner);
    }

    /// <summary>The rights of a group of objects saved or returned together (a package): only what every object allows.</summary>
    public static ItemPermissions ForPackage(IEnumerable<BuildObject> objects)
    {
        var composed = ItemPermissions.Compose(objects.Select(o => new ItemPermissions(o.OwnerRights, 0, o.NextOwnerRights))); return new ItemPermissions(composed.Owner, 0, composed.NextOwner);
    }

    // ---- what counts as modifying -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the update changes what the object is (shape, size, material, contents, name…) and not only where it stands or how it is turned. Moving and
    /// turning never need the Modify right. JSON is compared by meaning, so sending the same settings written differently is not a change.
    /// </summary>
    public static bool ChangesWhatItIs(BuildObject item, BuildObjectRequest o) =>
        !string.Equals(item.PrimitiveType, o.PrimitiveType, StringComparison.OrdinalIgnoreCase)
        || Differs(item.ScaleX, o.ScaleX) || Differs(item.ScaleY, o.ScaleY) || Differs(item.ScaleZ, o.ScaleZ)
        || (o.PivotX is { } px && Differs(item.PivotX, px)) || (o.PivotY is { } py && Differs(item.PivotY, py)) || (o.PivotZ is { } pz && Differs(item.PivotZ, pz))
        || (o.Name is not null && !string.Equals(BuildEndpoints.CleanName(o.Name, item.PrimitiveType), item.Name, StringComparison.Ordinal))
        || (o.Description is not null && !string.Equals(BuildEndpoints.CleanDescription(o.Description), item.Description, StringComparison.Ordinal))

        || !SameJson(item.MaterialJson, o.MaterialJson) || !SameJson(item.GeometryJson, o.GeometryJson) || !SameJson(DefaultPhysics(item.PhysicsJson), DefaultPhysics(o.PhysicsJson)) || !SameJson(item.ContentJson, o.ContentJson);

    /// <summary>An object with no physics settings collides, which is what the editor sends when it has none: the two are the same thing, not a change.</summary>
    private static string? DefaultPhysics(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "{\"collision\":true}";
        try { using var d = JsonDocument.Parse(json); return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.EnumerateObject().Any() ? "{\"collision\":true}" : json; }
        catch (JsonException) { return json; }
    }

    private static bool Differs(float a, float b) => Math.Abs(a - b) > 1e-4f;

    /// <summary>A missing value means "unchanged" (the update keeps what is stored); two texts are the same when they mean the same.</summary>
    public static bool SameJson(string? stored, string? sent)
    {
        if (string.IsNullOrWhiteSpace(sent)) return true;
        try { using var a = JsonDocument.Parse(string.IsNullOrWhiteSpace(stored) ? "null" : stored); using var b = JsonDocument.Parse(sent); return Equal(a.RootElement, b.RootElement); }
        catch (JsonException) { return false; }
    }

    private static bool Equal(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return (a.ValueKind is JsonValueKind.True or JsonValueKind.False) && (b.ValueKind is JsonValueKind.True or JsonValueKind.False) && a.ValueKind == b.ValueKind;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var left = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value); var right = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                return left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out var other) && Equal(p.Value, other));
            case JsonValueKind.Array:
                var xs = a.EnumerateArray().ToList(); var ys = b.EnumerateArray().ToList(); return xs.Count == ys.Count && xs.Zip(ys).All(pair => Equal(pair.First, pair.Second));
            case JsonValueKind.Number: return a.TryGetDouble(out var x) && b.TryGetDouble(out var y) && Math.Abs(x - y) <= 1e-6 * Math.Max(1, Math.Abs(x));
            case JsonValueKind.String: return a.GetString() == b.GetString();
            default: return true;
        }
    }


    // ---- changing the rights of an object -----------------------------------------------------------------------------------------

    public static IEndpointRouteBuilder MapObjectRightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/objects/{objectId:guid}/rights", ViewAsync).WithTags("Build").RequireAuthorization();
        endpoints.MapPut("/api/v1/worlds/islands/{islandId:guid}/objects/{objectId:guid}/rights", SetAsync).WithTags("Build").RequireAuthorization();
        return endpoints;
    }

    private static bool TryUser(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    private static async Task<IResult> ViewAsync(Guid islandId, Guid objectId, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var obj = await db.BuildObjects.AsNoTracking().FirstOrDefaultAsync(o => o.Id == objectId && o.IslandId == islandId && o.DeletedAt == null, ct); if (obj is null) return Results.NotFound();
        var island = await db.Islands.AsNoTracking().FirstOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct); if (island is null || (!island.IsPublic && island.OwnerUserId != user && !await db.IslandPermissions.AsNoTracking().AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null, ct))) return Results.NotFound();
        var effective = await EffectiveAsync(db, obj, ct); var mine = obj.OwnerUserId == user;
        return Results.Ok(new { objectId = obj.Id, isOwner = mine, rights = ItemRights.Letters(effective.Owner), rightsText = ItemRights.Describe(effective.Owner), owner = effective.Owner, nextOwner = mine ? effective.NextOwner : (int?)null, nextOwnerText = mine ? ItemRights.Describe(effective.NextOwner) : null, revision = obj.Revision });
    }

    /// <summary>The owner gives up rights of the object (or of what it passes on). Rights can only go down here: they come back only by making the object again from parts one has the rights to.</summary>
    internal static async Task<IResult> SetAsync(Guid islandId, Guid objectId, SetObjectRightsRequest body, HttpContext context, LifeVerseDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var user)) return Results.Unauthorized();
        var obj = await db.BuildObjects.FirstOrDefaultAsync(o => o.Id == objectId && o.IslandId == islandId && o.OwnerUserId == user && o.DeletedAt == null, ct); if (obj is null) return Results.NotFound();
        var now = clock.GetUtcNow();
        if (body.ExpectedRevision != obj.Revision) return Results.Json(new { code = "revision_conflict", currentRevision = obj.Revision }, statusCode: StatusCodes.Status409Conflict);
        if ((body.Owner & ~ItemRights.All) != 0 || (body.NextOwner & ~ItemRights.All) != 0 || !ItemRights.IsWithin(body.Owner, obj.OwnerRights) || !ItemRights.IsWithin(body.NextOwner, ItemRights.Normalize(body.Owner)))
        {
            ItemRightsService.Audit(db, user, "denied", "denied", null, null, user, null, $"object {obj.Id} rights: asked {ItemRights.Letters(body.Owner)}/{ItemRights.Letters(body.NextOwner)}, holds {ItemRights.Letters(obj.OwnerRights)}", now); await db.SaveChangesAsync(ct);
            return Results.Json(new { code = "cannot_raise_rights" }, statusCode: StatusCodes.Status400BadRequest);
        }
        var before = $"{ItemRights.Letters(obj.OwnerRights)}/{ItemRights.Letters(obj.NextOwnerRights)}";
        obj.OwnerRights = ItemRights.Normalize(body.Owner); obj.NextOwnerRights = ItemRights.Clamp(Math.Min(body.NextOwner, ItemRights.All) & obj.NextOwnerRights, obj.OwnerRights); obj.Revision++; obj.UpdatedAt = now;
        ItemRightsService.Audit(db, user, "object_rights", "ok", null, null, user, null, $"object {obj.Id}: {before} -> {ItemRights.Letters(obj.OwnerRights)}/{ItemRights.Letters(obj.NextOwnerRights)}", now);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "revision_conflict" }, statusCode: StatusCodes.Status409Conflict); }
        return Results.Ok(new { objectId = obj.Id, rights = ItemRights.Letters(obj.OwnerRights), nextOwner = obj.NextOwnerRights, revision = obj.Revision });
    }
}
