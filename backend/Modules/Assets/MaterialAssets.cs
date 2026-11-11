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
    public static readonly string[] BuiltInTextures = ["plywood", "checker", "bricks", "planks", "stripes", "noise", "tiles"];
    private static readonly string[] ImageKeys = ["texture", "normalMap", "specularMap", "ormMap", "emissiveMap", "projectorTexture"];
    public const int MaxTexturesPerObject = 8;

    private static readonly (string Key, float Min, float Max)[] Ranges =
    [
        ("uvRepeatX", .01f, 100f), ("uvRepeatY", .01f, 100f), ("uvOffsetX", -100f, 100f), ("uvOffsetY", -100f, 100f), ("uvRotation", -360f, 360f),
        ("emissionStrength", 0f, 16f), ("roughness", 0f, 1f), ("metallic", 0f, 1f), ("transparency", 0f, 1f), ("alphaCutoff", 0f, 1f), ("glossiness", 0f, 1f), ("environment", 0f, 1f), ("normalStrength", 0f, 2f), ("specularStrength", 0f, 2f), ("aoStrength", 0f, 1f)
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
            if (ValidateResources(root) is { } resourceError) return resourceError;
            if (root.TryGetProperty("light", out var light) && light.ValueKind == JsonValueKind.Object)
            {
                if (!InRange(light, "energy", 0f, 8f) || !InRange(light, "range", .5f, 30f)) return "Light intensity must be 0 to 8 and range 0.5 to 30 metres.";
            }
        }
        catch (JsonException) { return "Material settings are not valid JSON."; }
        return null;
    }

    /// <summary>Flexible path, light falloff and projector settings: the ranges the client also clamps to. A projector needs a light.</summary>
    private static string? ValidateResources(JsonElement root)
    {
        if (root.TryGetProperty("light", out var light) && light.ValueKind == JsonValueKind.Object && !InRange(light, "falloff", .1f, 4f)) return "Light falloff must be 0.1 to 4.";
        if (root.TryGetProperty("light", out var shadowLight) && shadowLight.ValueKind == JsonValueKind.Object && shadowLight.TryGetProperty("shadow", out var shadow) && shadow.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return "Light shadow must be true or false.";
        if (root.TryGetProperty("reflection", out var reflection))
        {
            if (reflection.ValueKind != JsonValueKind.Object || !InRange(reflection, "intensity", 0f, 1f) || !InRange(reflection, "range", 2f, 30f)) return "Reflection intensity must be 0 to 1 and its range 2 to 30 metres.";
        }
        if (root.TryGetProperty("animation", out var animation))
        {
            if (animation.ValueKind != JsonValueKind.Object || !InRange(animation, "speed", .1f, 4f)) return "Mesh animation speed must be 0.1 to 4.";
            if (animation.TryGetProperty("clip", out var clip) && (clip.ValueKind != JsonValueKind.String || (clip.GetString()?.Length ?? 0) is 0 or > 64)) return "The animation clip needs a name of 1 to 64 letters.";
            foreach (var flag in new[] { "playing", "loop" }) if (animation.TryGetProperty(flag, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return $"Mesh animation '{flag}' must be true or false.";
        }
        if (root.TryGetProperty("flex", out var flex))
        {
            if (flex.ValueKind != JsonValueKind.Object) return "Flexible path settings must be an object.";
            foreach (var (key, min, max) in new[] { ("softness", 0f, 3f), ("gravity", -10f, 10f), ("drag", 0f, 10f), ("wind", 0f, 10f), ("tension", 0f, 10f), ("forceX", -10f, 10f), ("forceY", -10f, 10f), ("forceZ", -10f, 10f) })
                if (!InRange(flex, key, min, max)) return $"Flexible path setting '{key}' must be between {min} and {max}.";
        }
        var hasProjector = root.TryGetProperty("projector", out var projector) || root.TryGetProperty("projectorTexture", out _) || root.TryGetProperty("projectorBuiltin", out _);
        if (hasProjector)
        {
            if (!root.TryGetProperty("light", out var owner) || owner.ValueKind != JsonValueKind.Object) return "A projector needs the light to be on.";
            if (root.TryGetProperty("projector", out projector) && (projector.ValueKind != JsonValueKind.Object || !InRange(projector, "fov", 10f, 120f) || !InRange(projector, "focus", 0f, 30f) || !InRange(projector, "ambiance", 0f, 1f))) return "Projector field of view must be 10 to 120, focus 0 to 30 and ambiance 0 to 1.";
            if (root.TryGetProperty("projectorBuiltin", out var builtin) && (builtin.ValueKind != JsonValueKind.String || !BuiltInTextures.Contains(builtin.GetString(), StringComparer.Ordinal))) return "Unknown built-in texture.";
        }
        return null;
    }

    private static readonly string[] MediaImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];
    private static readonly string[] MediaVideoExtensions = [".ogv"];

    /// <summary>
    /// The media of a surface (an image or a video from the web): its kind, an https address on a public host with an extension that kind plays, and the flags. Addresses that reach the
    /// person's own network (localhost, private ranges, bare IP numbers), carry a user name or are very long are refused, so a surface can never be used to probe somebody's computer.
    /// </summary>
    public static string? ValidateMedia(JsonElement layer)
    {
        if (!layer.TryGetProperty("media", out var media)) return null;
        if (media.ValueKind != JsonValueKind.Object) return "Media settings must be an object.";
        var type = media.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        if (type is not ("image" or "video")) return "Media type must be image or video.";
        var url = media.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() ?? string.Empty : string.Empty;
        if (url.Length is 0 or > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return "Media must come from a public https address.";
        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal) || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal) || !host.Contains('.') || System.Net.IPAddress.TryParse(host.Trim('[', ']'), out _)) return "Media must come from a public host name, not an address on a private network.";
        var extensions = type == "image" ? MediaImageExtensions : MediaVideoExtensions;
        if (!extensions.Any(e => uri.AbsolutePath.EndsWith(e, StringComparison.OrdinalIgnoreCase))) return type == "image" ? "An image must be a PNG, JPG or WebP file." : "A video must be an Ogg Theora (.ogv) file.";
        foreach (var flag in new[] { "playing", "loop", "glow" }) if (media.TryGetProperty(flag, out var value) && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return $"Media '{flag}' must be true or false.";
        return null;
    }

    private static string? CheckLayer(JsonElement layer)
    {
        if (ValidateMedia(layer) is { } mediaError) return mediaError;
        if (layer.TryGetProperty("alphaMode", out var alphaMode) && (alphaMode.ValueKind != JsonValueKind.String || alphaMode.GetString() is not ("none" or "blend" or "mask"))) return "Alpha mode must be none, blend or mask.";
        if (layer.TryGetProperty("mapping", out var mapping) && (mapping.ValueKind != JsonValueKind.String || mapping.GetString() is not ("default" or "planar"))) return "Mapping must be default or planar.";
        if (layer.TryGetProperty("shading", out var shading) && (shading.ValueKind != JsonValueKind.String || shading.GetString() is not ("pbr" or "blinnphong"))) return "Shading must be pbr or blinnphong.";
        if (layer.TryGetProperty("specularColor", out var specular) && (specular.ValueKind != JsonValueKind.String || specular.GetString() is not { Length: 7 } hex || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit))) return "Specular color must be like #RRGGBB.";
        if (layer.TryGetProperty("mapUv", out var mapUv)) { if (mapUv.ValueKind != JsonValueKind.Object) return "Map transforms must be an object."; foreach (var map in mapUv.EnumerateObject()) { if (map.Name is not ("texture" or "normalMap" or "specularMap" or "ormMap" or "emissiveMap") || map.Value.ValueKind != JsonValueKind.Object) return "Unknown map transform."; foreach (var (key, min, max) in new[] { ("repeatX", .01f, 100f), ("repeatY", .01f, 100f), ("offsetX", -100f, 100f), ("offsetY", -100f, 100f), ("rotation", -360f, 360f) }) if (!InRange(map.Value, key, min, max)) return $"Map transform '{key}' must be between {min} and {max}."; } }
        foreach (var flag in new[] { "fullBright", "doubleSided", "flipU", "flipV", "noTexture" }) if (layer.TryGetProperty(flag, out var on) && on.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return $"Material setting '{flag}' must be true or false.";
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
