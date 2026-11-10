using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>
/// "mesh" build objects show an imported 3D model: the object's geometry JSON carries the asset id
/// (<c>{"assetId":"..."}</c>). Everything else (position, scale, permissions, scripts, undo) is the ordinary object
/// machinery. These helpers make sure a mesh can only reference a model its creator actually has.
/// </summary>
public static class MeshAssets
{
    public const string PrimitiveType = "mesh";

    public static Guid? AssetIdOf(string? geometryJson)
    {
        if (string.IsNullOrWhiteSpace(geometryJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(geometryJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("assetId", out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id) ? id : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Null when the object is acceptable, otherwise the reason. <paramref name="existingAssetId"/> is the model the object already shows.</summary>
    public static async Task<string?> ValidateAsync(LifeVerseDbContext db, Guid actor, string primitiveType, string? geometryJson, Guid? existingAssetId, CancellationToken ct)
    {
        if (!string.Equals(primitiveType, PrimitiveType, StringComparison.OrdinalIgnoreCase)) return null;
        if (AssetIdOf(geometryJson) is not { } assetId) return "A mesh needs the id of a 3D model in its geometry (assetId).";
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.DeletedAt == null, ct);
        if (asset is null) return "The 3D model does not exist.";
        var mime = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId && v.Version == asset.CurrentVersion).Select(v => v.MimeType).FirstOrDefaultAsync(ct);
        if (mime is null || !mime.StartsWith("model/", StringComparison.Ordinal)) return "The asset is not a 3D model.";
        if (existingAssetId == assetId) return null; // an edit that keeps the same model is always fine
        var hasIt = asset.OwnerUserId == actor || await db.InventoryItems.AsNoTracking().AnyAsync(x => x.OwnerUserId == actor && x.AssetId == assetId && x.DeletedAt == null && x.Status == "active", ct);
        return hasIt ? null : "You do not have that 3D model in your inventory.";
    }

    /// <summary>
    /// Whether a live object of the island names the asset in its geometry (a model) or its material (an image). The two columns are JSON, which PostgreSQL
    /// cannot lower-case as text, so there the search is done on their text form; the in-memory database used by the tests compares the strings.
    /// </summary>
    private static async Task<bool> UsedAsync(LifeVerseDbContext db, Guid islandId, string needle, bool geometry, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var pattern = "%" + needle + "%";
            return geometry
                ? await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM world.objects WHERE island_id = {islandId} AND deleted_at IS NULL AND primitive_type = 'mesh' AND geometry_json::text ILIKE {pattern} LIMIT 1").AnyAsync(ct)
                : await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM world.objects WHERE island_id = {islandId} AND deleted_at IS NULL AND material_json::text ILIKE {pattern} LIMIT 1").AnyAsync(ct);
        }
        return geometry
            ? await db.BuildObjects.AsNoTracking().AnyAsync(o => o.IslandId == islandId && o.DeletedAt == null && o.PrimitiveType == PrimitiveType && o.GeometryJson.ToLower().Contains(needle), ct)
            : await db.BuildObjects.AsNoTracking().AnyAsync(o => o.IslandId == islandId && o.DeletedAt == null && o.MaterialJson.ToLower().Contains(needle), ct);
    }

    public static IEndpointRouteBuilder MapMeshAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/assets/{assetId:guid}/file", IslandAssetAsync).WithTags("Assets").RequireAuthorization();
        return endpoints;
    }

    /// <summary>Anyone who may enter an island can fetch the models its meshes use and the images its materials use, so they can see them.</summary>
    internal static async Task<IResult> IslandAssetAsync(Guid islandId, Guid assetId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(x => x.Id == islandId && x.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var canEnter = island.IsPublic || island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(x => x.IslandId == islandId && x.UserId == user && x.RevokedAt == null && (x.Permission == "enter" || x.Permission == "admin"), ct);
        if (!canEnter) return Results.Forbid();
        var needle = assetId.ToString().ToLowerInvariant();
        var usedAsMesh = await UsedAsync(db, islandId, needle, geometry: true, ct);
        var usedAsTexture = !usedAsMesh && await UsedAsync(db, islandId, needle, geometry: false, ct);
        if (!usedAsMesh && !usedAsTexture) return Results.NotFound();
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.DeletedAt == null, ct);
        if (asset is null) return Results.NotFound();
        var version = await db.AssetVersions.AsNoTracking().SingleAsync(x => x.AssetId == assetId && x.Version == asset.CurrentVersion, ct);
        if (usedAsMesh ? !version.MimeType.StartsWith("model/", StringComparison.Ordinal) : !MaterialAssets.IsImage(version.MimeType)) return Results.NotFound();
        var stream = await storage.OpenReadAsync(version.StorageKey, ct);
        // the hash is the entity tag: a client that already has these bytes asks again with it and gets "not modified" instead of the file
        return stream is null ? Results.NotFound() : Results.File(stream, version.MimeType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\"" + version.ContentHash + "\""));
    }
}
