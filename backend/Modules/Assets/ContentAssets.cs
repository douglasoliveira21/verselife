using System.Text.Json;
using LifeVerse.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeVerse.Api.Modules.Assets;

/// <summary>
/// The contents of an object (its "Conteúdo" tab): references to things from the owner's inventory — scripts, textures, sounds,
/// animations, other objects — kept in the object's content JSON as <c>[{"assetId":"…","name":"…","category":"…"}]</c>.
/// The object only holds references, never copies of the data; these checks make sure a reference is well formed and that the
/// person adding it really has the item.
/// </summary>
public static class ContentAssets
{
    public const int MaxEntries = 64;
    public static readonly string[] Categories = ["objects", "textures", "sounds", "animations", "clothing", "accessories", "models", "packages", "scripts", "notecards"];

    public static List<Guid> AssetIds(string? contentJson)
    {
        var ids = new List<Guid>();
        if (string.IsNullOrWhiteSpace(contentJson)) return ids;
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return ids;
            foreach (var entry in document.RootElement.EnumerateArray())
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("assetId", out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)) ids.Add(id);
        }
        catch (JsonException) { }
        return ids;
    }

    /// <summary>Everything that does not need the database. Null is fine (an object may have no contents).</summary>
    public static string? ValidateSettings(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return "The contents must be a list.";
            if (root.GetArrayLength() > MaxEntries) return $"An object can hold at most {MaxEntries} items.";
            var seen = new HashSet<Guid>();
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) return "Every content entry must be an object.";
                if (!entry.TryGetProperty("assetId", out var asset) || asset.ValueKind != JsonValueKind.String || !Guid.TryParse(asset.GetString(), out var id)) return "A content entry needs an assetId.";
                if (!seen.Add(id)) return "The same item is listed twice.";
                if (!entry.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || name.GetString()!.Length > 120) return "A content entry needs a name of up to 120 characters.";
                if (!entry.TryGetProperty("category", out var category) || category.ValueKind != JsonValueKind.String || !Categories.Contains(category.GetString(), StringComparer.Ordinal)) return "A content entry has an unknown category.";
            }
        }
        catch (JsonException) { return "The contents are not valid JSON."; }
        return null;
    }

    /// <summary>Null when acceptable. Items already in the object are always fine; a new one must be the actor's own or in the actor's inventory.</summary>
    public static async Task<string?> ValidateAsync(LifeVerseDbContext db, Guid actor, string? contentJson, string? existingContentJson, CancellationToken ct)
    {
        if (ValidateSettings(contentJson) is { } settings) return settings;
        var existing = AssetIds(existingContentJson).ToHashSet();
        foreach (var assetId in AssetIds(contentJson))
        {
            var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == assetId && x.DeletedAt == null, ct);
            if (asset is null) return "An item in the contents does not exist.";
            if (existing.Contains(assetId)) continue;
            var hasIt = asset.OwnerUserId == actor || await db.InventoryItems.AsNoTracking().AnyAsync(x => x.OwnerUserId == actor && x.AssetId == assetId && x.DeletedAt == null, ct);
            if (!hasIt) return "You do not have that item in your inventory.";
        }
        return null;
    }
}
