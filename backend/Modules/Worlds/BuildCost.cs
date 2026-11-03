using System.Text.Json;
using LifeVerse.Api.Modules.Assets;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// What an object costs the island, in points. Counting objects alone says little about how heavy a build is, so each object is
/// priced by its geometry, its physics, its textures and light, and its contents; islands and parcels have a budget in these points
/// as well as a limit on the number of objects.
/// </summary>
public static class BuildCost
{
    public const int DefaultIslandBudget = 200_000;
    public const int MaxObjectCost = 400;

    /// <summary>Rough triangle count of a shape with its settings (hollow doubles the walls, twist adds slices, a cut removes some).</summary>
    public static int Triangles(string primitiveType, string? geometryJson)
    {
        double hollow = 0, twist = 0, cutStart = 0, cutEnd = 1;
        if (!string.IsNullOrWhiteSpace(geometryJson))
            try
            {
                using var document = JsonDocument.Parse(geometryJson);
                double Read(string key, double fallback) => document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
                hollow = Read("hollow", 0); twist = Read("twist", 0); cutStart = Read("cutStart", 0); cutEnd = Read("cutEnd", 1);
            }
            catch (JsonException) { }
        var type = primitiveType.ToLowerInvariant();
        double basic = type switch
        {
            "cube" => 16, "prism" => 12, "pyramid" => 12, "cone" => 56, "cylinder" => 96, "tube" => 192, "plane" => 2,
            "sphere" => 2048, "hemisphere" => 1024, "torus" or "ring" => 1536, "mesh" => 4000, _ => 100
        };
        if (type is "cube" or "prism" or "pyramid" or "cone" or "cylinder" or "tube")
        {
            if (hollow > 0 && type is not "tube") basic *= 2;
            if (twist != 0) basic *= Math.Clamp(Math.Ceiling(Math.Abs(twist) / 10), 2, 18);
            basic *= Math.Clamp(cutEnd - cutStart, .05, 1);
        }
        return (int)Math.Max(2, Math.Round(basic));
    }

    /// <summary>The cost of one object: geometry (a point per 20 triangles), physics, textures and light, and contents.</summary>
    public static int Of(string primitiveType, string? geometryJson, string? materialJson, string? physicsJson, string? contentJson)
    {
        var geometry = Math.Max(1, Triangles(primitiveType, geometryJson) / 20);
        var physics = 0;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(physicsJson) ? "{}" : physicsJson);
            var solid = !(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("collision", out var c) && c.ValueKind == JsonValueKind.False);
            physics = solid ? (primitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase) ? 2 : 1) : 0;
        }
        catch (JsonException) { physics = 1; }
        var textures = MaterialAssets.TextureIds(materialJson).Count * 3;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("builtin", out _)) textures += 1;
                if (document.RootElement.TryGetProperty("light", out _)) textures += 5;
                if (document.RootElement.TryGetProperty("faces", out var faces) && faces.ValueKind == JsonValueKind.Array) textures += Math.Min(faces.GetArrayLength(), 32);
            }
        }
        catch (JsonException) { }
        var contents = Math.Min(ContentAssets.AssetIds(contentJson).Count, ContentAssets.MaxEntries);
        return Math.Min(MaxObjectCost, geometry + physics + textures + contents);
    }

    /// <summary>A limit stored in an island's resource limits JSON (for example "cost"), or the default.</summary>
    public static int Limit(string? resourceLimitsJson, string key, int fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(resourceLimitsJson) ? "{}" : resourceLimitsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out var value) && value.TryGetInt32(out var number) ? Math.Clamp(number, 1, 100_000_000) : fallback;
        }
        catch (JsonException) { return fallback; }
    }
}
