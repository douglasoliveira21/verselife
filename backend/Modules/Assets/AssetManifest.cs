using System.IdentityModel.Tokens.Jwt;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Assets;

public sealed record ManifestAsset(Guid AssetId, string Kind, string Hash, long Size, string Mime, double Distance);
public sealed class ManifestRow { public string PrimitiveType { get; set; } = ""; public string GeometryJson { get; set; } = "{}"; public string MaterialJson { get; set; } = "{}"; public float PositionX { get; set; } public float PositionZ { get; set; } }
public sealed record AssetManifest(Guid IslandId, int Count, long TotalBytes, IReadOnlyList<ManifestAsset> Assets);

/// <summary>
/// What an island needs to show its objects: every 3D model its meshes use and every image its materials use (and its projectors), with the hash and size of the
/// current version and how far from a point (the arrival point by default) the nearest object using it stands. The client uses it to know the amount of work before
/// it starts, to keep what it already has on disk (the hash proves the bytes) and to load the nearest first.
/// </summary>
public static class AssetManifestEndpoints
{
    public const int MaxObjects = 20_000, MaxAssets = 2_000;

    public static IEndpointRouteBuilder MapAssetManifestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/worlds/islands/{islandId:guid}/assets/manifest", ManifestAsync).WithTags("Assets").RequireAuthorization();
        return endpoints;
    }

    internal static async Task<IResult> ManifestAsync(Guid islandId, float? x, float? z, HttpContext context, LifeVerseDbContext db, CancellationToken ct)
    {
        if (!Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var user)) return Results.Unauthorized();
        var island = await db.Islands.AsNoTracking().SingleOrDefaultAsync(i => i.Id == islandId && i.DeletedAt == null, ct);
        if (island is null) return Results.NotFound();
        var canEnter = island.IsPublic || island.OwnerUserId == user || await db.IslandPermissions.AnyAsync(p => p.IslandId == islandId && p.UserId == user && p.RevokedAt == null && (p.Permission == "enter" || p.Permission == "admin"), ct);
        if (!canEnter) return Results.Forbid();
        var originX = x is { } px && float.IsFinite(px) ? px : island.SpawnX; var originZ = z is { } pz && float.IsFinite(pz) ? pz : island.SpawnZ;

        // the JSON columns are jsonb on PostgreSQL, where string functions need a cast to text: filter there with SQL; the in-memory provider (tests) uses the same rule
        List<ManifestRow> objects;
        if (db.Database.IsNpgsql())
            objects = await db.Database.SqlQuery<ManifestRow>($"SELECT primitive_type AS \"PrimitiveType\", geometry_json::text AS \"GeometryJson\", material_json::text AS \"MaterialJson\", position_x AS \"PositionX\", position_z AS \"PositionZ\" FROM world.objects WHERE island_id = {islandId} AND deleted_at IS NULL AND (primitive_type = 'mesh' OR material_json::text LIKE '%\"texture\"%' OR material_json::text LIKE '%\"normalMap\"%' OR material_json::text LIKE '%\"projectorTexture\"%') LIMIT {MaxObjects}").ToListAsync(ct);
        else
            objects = await db.BuildObjects.AsNoTracking().Where(o => o.IslandId == islandId && o.DeletedAt == null && (o.PrimitiveType == MeshAssets.PrimitiveType || o.MaterialJson.Contains("\"texture\"") || o.MaterialJson.Contains("\"normalMap\"") || o.MaterialJson.Contains("\"projectorTexture\"")))
                .Select(o => new ManifestRow { PrimitiveType = o.PrimitiveType, GeometryJson = o.GeometryJson, MaterialJson = o.MaterialJson, PositionX = o.PositionX, PositionZ = o.PositionZ }).Take(MaxObjects).ToListAsync(ct);
        var nearest = new Dictionary<Guid, (bool Model, double Distance)>();
        void Note(Guid asset, bool model, double distance) { if (!nearest.TryGetValue(asset, out var known) || distance < known.Distance) nearest[asset] = (model, distance); }
        foreach (var item in objects)
        {
            var distance = Math.Sqrt(Math.Pow(item.PositionX - originX, 2) + Math.Pow(item.PositionZ - originZ, 2));
            if (item.PrimitiveType == MeshAssets.PrimitiveType && MeshAssets.AssetIdOf(item.GeometryJson) is { } model) Note(model, true, distance);
            foreach (var texture in MaterialAssets.TextureIds(item.MaterialJson)) Note(texture, false, distance);
        }
        if (nearest.Count == 0) return Results.Ok(new AssetManifest(islandId, 0, 0, []));
        var ids = nearest.Keys.Take(MaxAssets * 2).ToList();
        var assets = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id) && a.DeletedAt == null).Select(a => new { a.Id, a.CurrentVersion }).ToListAsync(ct);
        var versions = await db.AssetVersions.AsNoTracking().Where(v => ids.Contains(v.AssetId)).Select(v => new { v.AssetId, v.Version, v.ContentHash, v.ByteSize, v.MimeType }).ToListAsync(ct);
        var list = new List<ManifestAsset>();
        foreach (var asset in assets)
        {
            var version = versions.FirstOrDefault(v => v.AssetId == asset.Id && v.Version == asset.CurrentVersion);
            if (version is null) continue;
            var (isModel, distance) = nearest[asset.Id];
            if (isModel ? !version.MimeType.StartsWith("model/", StringComparison.Ordinal) : !MaterialAssets.IsImage(version.MimeType)) continue; // the same rule as the file endpoint
            list.Add(new ManifestAsset(asset.Id, isModel ? "model" : "texture", version.ContentHash, version.ByteSize, version.MimeType, Math.Round(distance, 1)));
        }
        var ordered = list.OrderBy(a => a.Distance).ThenBy(a => a.Size).Take(MaxAssets).ToList();
        return Results.Ok(new AssetManifest(islandId, ordered.Count, ordered.Sum(a => a.Size), ordered));
    }
}
