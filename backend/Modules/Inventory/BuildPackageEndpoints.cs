using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeVerse.Api.Data;
using LifeVerse.Api.Modules.Assets;
using LifeVerse.Api.Modules.Worlds;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Inventory;

/// <summary>
/// "Salvar no inventário": a construction (a house, a sofa, a shop) saved as one inventory item of the category "packages". The item
/// holds a <see cref="PackageDto"/> — plain data listing the objects, their shapes, materials and contents — stored through the
/// same asset storage as every other inventory item. Placing it in the world is done by the client with ordinary build operations,
/// so every object goes through the island's own permission, limit and revision checks again.
/// </summary>
public static class BuildPackageEndpoints
{
    public const string Category = "packages";
    public const string MimeType = "application/vnd.lifeverse.build+json";

    public static IEndpointRouteBuilder MapBuildPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/inventory/build-packages").WithTags("Inventory").RequireAuthorization();
        group.MapPost("", SaveAsync);
        group.MapGet("/{itemId:guid}", ReadAsync);
        return endpoints;
    }

    private static bool TryUser(HttpContext context, out Guid id) => Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out id);

    /// <summary>Null when every object in the package is acceptable for this person, otherwise the reason.</summary>
    public static async Task<string?> ValidateAsync(LifeVerseDbContext db, Guid actor, PackageDto package, CancellationToken ct)
    {
        foreach (var o in package.Objects)
        {
            var request = new BuildObjectRequest(o.PrimitiveType, o.X, o.Y, o.Z, o.RotationX, o.RotationY, o.RotationZ, o.ScaleX, o.ScaleY, o.ScaleZ, o.MaterialJson, o.PhysicsJson, null, 0, o.GeometryJson, o.Name, o.Description, null, null, o.ContentJson);
            var error = BuildEndpoints.ValidateObject(request)
                ?? await MeshAssets.ValidateAsync(db, actor, o.PrimitiveType, o.GeometryJson, null, ct)
                ?? await MaterialAssets.ValidateAsync(db, actor, o.MaterialJson, null, ct)
                ?? await ContentAssets.ValidateAsync(db, actor, o.ContentJson, null, ct);
            if (error is not null) return $"{(string.IsNullOrWhiteSpace(o.Name) ? o.PrimitiveType : o.Name)}: {error}";
        }
        return null;
    }

    private static async Task<IResult> SaveAsync(HttpRequest request, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, TimeProvider clock, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(ct);
        var package = PackageDocument.Parse(body, out var parseError);
        if (package is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["package"] = [parseError ?? "Invalid construction."] });
        var error = await ValidateAsync(db, owner, package, ct);
        if (error is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["package"] = [error] });

        var item = await BuildPackageStore.SaveAsync(db, storage, owner, package, clock.GetUtcNow(), ct, await PackageRightsAsync(db, owner, package, ct));
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/inventory/build-packages/{item.Id}", new { itemId = item.Id, assetId = item.AssetId, name = package.Name, objects = package.Objects.Length });
    }

    /// <summary>The rights of a construction being saved: the strictest of what its objects are made of (a model or a script that was only given to the person cannot be passed on as freely as one they made).</summary>
    public static async Task<LifeVerse.Shared.Items.ItemPermissions> PackageRightsAsync(LifeVerseDbContext db, Guid owner, PackageDto package, CancellationToken ct)
    {
        var rights = new List<LifeVerse.Shared.Items.ItemPermissions>();
        foreach (var o in package.Objects) rights.Add(await ObjectRights.InheritAsync(db, owner, ObjectRights.PartsOf(o.PrimitiveType, o.GeometryJson, o.MaterialJson, o.ContentJson), ct));
        return LifeVerse.Shared.Items.ItemPermissions.Compose(rights);
    }

    /// <summary>Reads a construction from storage for the server to place it (no one is shown the raw document).</summary>
    public static async Task<(PackageDto? Package, string? Error)> LoadAsync(LifeVerseDbContext db, AssetStorageService storage, InventoryItem item, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.AssetId && x.DeletedAt == null && x.Category == Category, ct); if (asset is null) return (null, "not_a_package");
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == asset.Id && x.Version == asset.CurrentVersion, ct); if (version is null || version.MimeType != MimeType) return (null, "not_a_package");
        await using var stream = await storage.OpenReadAsync(version.StorageKey, ct); if (stream is null) return (null, "missing_data");
        using var reader = new StreamReader(stream, Encoding.UTF8); var package = PackageDocument.Parse(await reader.ReadToEndAsync(ct), out var error); return (package, error);
    }

    private static async Task<IResult> ReadAsync(Guid itemId, HttpContext context, LifeVerseDbContext db, AssetStorageService storage, CancellationToken ct)
    {
        if (!TryUser(context, out var owner)) return Results.Unauthorized();
        var item = await db.InventoryItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId && x.OwnerUserId == owner && x.DeletedAt == null && x.Status == "active", ct);
        if (item is null) return Results.NotFound();
        if (!LifeVerse.Shared.Items.ItemRights.IsCopyable(item.OwnerRights)) return Results.Json(new { error = "use_rez" }, statusCode: StatusCodes.Status403Forbidden); // reading the whole construction is copying it: an item that cannot be copied is placed by the server (the "rez" operation), never read out
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == item.AssetId && x.DeletedAt == null && x.Category == Category, ct);
        if (asset is null) return Results.NotFound();
        var version = await db.AssetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AssetId == asset.Id && x.Version == asset.CurrentVersion, ct);
        if (version is null || version.MimeType != MimeType) return Results.NotFound();
        var stream = await storage.OpenReadAsync(version.StorageKey, ct);
        return stream is null ? Results.NotFound() : Results.Stream(stream, "application/json");
    }
}

/// <summary>Writes a construction into someone's inventory (the asset, its stored bytes and the item). The caller saves the changes.</summary>
public static class BuildPackageStore
{
    public static async Task<InventoryItem> SaveAsync(LifeVerseDbContext db, AssetStorageService storage, Guid owner, PackageDto package, DateTimeOffset now, CancellationToken ct, LifeVerse.Shared.Items.ItemPermissions? rights = null)
    {
        var bytes = Encoding.UTF8.GetBytes(PackageDocument.Serialize(package));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var asset = new Asset { Id = Guid.NewGuid(), OwnerUserId = owner, CreatorUserId = owner, Name = package.Name, Category = BuildPackageEndpoints.Category, LicenseJson = "{\"source\":\"build-package\"}", Status = "ready", CreatedAt = now, UpdatedAt = now };
        var key = await storage.PutAsync(owner, hash, bytes, ct);
        var version = new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, Version = 1, ContentHash = hash, StorageKey = key, MimeType = BuildPackageEndpoints.MimeType, ByteSize = bytes.Length, ValidationJson = JsonSerializer.Serialize(new { objects = package.Objects.Length }), CreatedAt = now };
        var item = new InventoryItem { Id = Guid.NewGuid(), OwnerUserId = owner, AssetId = asset.Id, DisplayName = package.Name, CreatedAt = now, UpdatedAt = now }; if (rights is not null) { item.OwnerRights = LifeVerse.Shared.Items.ItemRights.Normalize(rights.Owner); item.NextOwnerRights = LifeVerse.Shared.Items.ItemRights.Clamp(rights.NextOwner, item.OwnerRights); } /* a construction is as free as the things it is made of */
        db.Assets.Add(asset); db.AssetVersions.Add(version); db.InventoryItems.Add(item);
        return item;
    }
}
