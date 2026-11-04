using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>
/// Textures on build objects. An object's material JSON may name image assets (<c>texture</c>, <c>normalMap</c>, on the whole
/// object or on single faces under <c>faces</c>), a built-in pattern (<c>builtin</c>), UV settings and a light. These helpers make
/// sure the numbers are sane and that an object can only use images its creator really has, like meshes do for 3D models.
/// </summary>
public static class MaterialAssets
{
    public static readonly string[] BuiltInTextures = ["checker", "bricks", "planks", "stripes", "noise", "tiles"];
    private static readonly string[] ImageKeys = ["texture", "normalMap"];
    public const int MaxTexturesPerObject = 8;

    private static readonly (string Key, float Min, float Max)[] Ranges =
    [
        ("uvRepeatX", .01f, 100f), ("uvRepeatY", .01f, 100f), ("uvOffsetX", -100f, 100f), ("uvOffsetY", -100f, 100f), ("uvRotation", -360f, 360f),
        ("emissionStrength", 0f, 16f), ("roughness", 0f, 1f), ("metallic", 0f, 1f), ("transparency", 0f, 1f)
    ];

    /// <summary>Every image asset the material refers to, across the object and its faces.</summary>
    public static HashSet<Guid> TextureIds(string? materialJson)
    {
        var ids = new HashSet<Guid>();
        if (string.IsNullOrWhiteSpace(materialJson)) return ids;
        try
        {
            using var document = JsonDocument.Parse(materialJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return ids;
            Collect(document.RootElement, ids);
            if (document.RootElement.TryGetProperty("faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
                foreach (var face in faces.EnumerateArray()) if (face.ValueKind == JsonValueKind.Object) Collect(face, ids);
        }
        catch (JsonException) { }
        return ids;
    }

    private static void Collect(JsonElement element, HashSet<Guid> ids)
    {
        foreach (var key in ImageKeys)
            if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)) ids.Add(id);
    }

    /// <summary>Checks everything that does not need the database: numeric ranges, the built-in names, the light and how many textures.</summary>
    public static string? ValidateSettings(string? materialJson)
    {
        if (string.IsNullOrWhiteSpace(materialJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(materialJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (CheckLayer(root) is { } error) return error;
            if (root.TryGetProperty("faces", out var faces) && faces.ValueKind == JsonValueKind.Array)
            {
                if (faces.GetArrayLength() > 32) return "A material can style at most 32 faces.";
                foreach (var face in faces.EnumerateArray()) if (face.ValueKind == JsonValueKind.Object && CheckLayer(face) is { } faceError) return faceError;
            }
            if (TextureIds(materialJson).Count > MaxTexturesPerObject) return $"An object can use at most {MaxTexturesPerObject} textures.";
            if (root.TryGetProperty("light", out var light) && light.ValueKind == JsonValueKind.Object)
            {
                if (!InRange(light, "energy", 0f, 8f) || !InRange(light, "range", .5f, 30f)) return "Light intensity must be 0 to 8 and range 0.5 to 30 metres.";
            }
        }
        catch (JsonException) { return "Material settings are not valid JSON."; }
        return null;
    }

    private static string? CheckLayer(JsonElement layer)
    {
        foreach (var (key, min, max) in Ranges) if (!InRange(layer, key, min, max)) return $"Material setting '{key}' must be between {min} and {max}.";
        if (layer.TryGetProperty("builtin", out var builtin) && (builtin.ValueKind != JsonValueKind.String || !BuiltInTextures.Contains(builtin.GetString(), StringComparer.Ordinal))) return "Unknown built-in texture.";
        foreach (var key in ImageKeys)
            if (layer.TryGetProperty(key, out var value) && (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out _))) return $"'{key}' must be the id of an image asset.";
        return null;
    }

    private static bool InRange(JsonElement element, string key, float min, float max) =>
        !element.TryGetProperty(key, out var value) || (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number) && float.IsFinite(number) && number >= min && number <= max);

    /// <summary>
    /// Null when the material is acceptable, otherwise the reason. Images the object already uses are always fine;
    /// a new one must be a PNG, JPEG or WebP the actor owns or has in the inventory.
    /// </summary>
    public static async Task<string?> ValidateAsync(LifeVerseDbContext db, Guid actor, string? materialJson, string? existingMaterialJson, CancellationToken ct)
    {
        if (ValidateSettings(materialJson) is { } settings) return settings;
        var existing = TextureIds(existingMaterialJson);
        foreach (var assetId in TextureIds(materialJson))
        {
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.DeletedAt == null, ct);
            if (asset is null) return "A texture does not exist.";
            var mime = await db.AssetVersions.AsNoTracking().Where(v => v.AssetId == assetId && v.Version == asset.CurrentVersion).Select(v => v.MimeType).FirstOrDefaultAsync(ct);
            if (!IsImage(mime)) return "A texture must be a PNG, JPEG or WebP image.";
            if (existing.Contains(assetId)) continue;
            var hasIt = asset.OwnerUserId == actor || await db.InventoryItems.AsNoTracking().AnyAsync(x => x.OwnerUserId == actor && x.AssetId == assetId && x.DeletedAt == null && x.Status == "active", ct);
            if (!hasIt) return "You do not have that texture in your inventory.";
        }
        return null;
    }

    public static bool IsImage(string? mime) => mime is "image/png" or "image/jpeg" or "image/webp";
}
