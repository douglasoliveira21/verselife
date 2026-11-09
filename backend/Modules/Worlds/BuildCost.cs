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
        if (LifeVerse.Shared.Building.PrimMesh.IsRound(type) && !string.IsNullOrWhiteSpace(geometryJson) && geometryJson.Trim() != "{}") return Math.Clamp(LifeVerse.Shared.Building.PrimMesh.Build(type, LifeVerse.Shared.Building.PrimParams.Parse(geometryJson)).TriangleCount, 2, 20_000); // a shaped sphere or torus costs what its mesh really has
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

    /// <summary>The parts an object's cost is made of, so it is clear what makes a build heavy.</summary>
    public sealed record CostParts(int Geometry, int Physics, int Collision, int Textures, int Effects, int Scripts, int Animations, int Content)
    {
        public int Total => Geometry + Physics + Collision + Textures + Effects + Scripts + Animations + Content;
    }

    public const int ScriptCost = 8, AnimationCost = 3;

    /// <summary>The cost of one object: geometry (a point per 20 triangles), solid physics, the complexity of its collision shape, textures, light and see-through or glowing materials, and what it carries (scripts weigh most, then animations).</summary>
    public static int Of(string primitiveType, string? geometryJson, string? materialJson, string? physicsJson, string? contentJson) =>
        ObjectPhysics.IsTemporary(physicsJson) ? 1 : // a temporary object barely weighs on the budget
        Math.Min(MaxObjectCost, Breakdown(primitiveType, geometryJson, materialJson, physicsJson, contentJson).Total);

    public static CostParts Breakdown(string primitiveType, string? geometryJson, string? materialJson, string? physicsJson, string? contentJson)
    {
        var geometry = Math.Max(1, Triangles(primitiveType, geometryJson) / 20);
        var physics = 0; var solid = true; var collisionProfile = "box";
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(physicsJson) ? "{}" : physicsJson);
            solid = !(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("collision", out var c) && c.ValueKind == JsonValueKind.False);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("shape", out var profile) && profile.ValueKind == JsonValueKind.String) collisionProfile = profile.GetString() ?? "box";
            if (collisionProfile == "none") solid = false;
            physics = solid ? (primitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase) ? 2 : 1) + (ObjectPhysics.IsPhysical(physicsJson) ? 2 : 0) : 0; // a body that moves by physics costs more than one that stands still
        }
        catch (JsonException) { physics = 1; }
        // a hollow, twisted or cut shape needs a collision mesh of its own instead of a simple box
        var collision = solid ? collisionProfile switch { "convex" => 3, "triangle" => 8, _ => ComplexCollision(geometryJson) ? 1 : 0 } : 0;
        var textures = MaterialAssets.TextureIds(materialJson).Count * 3; var effects = 0;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(materialJson) ? "{}" : materialJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = document.RootElement;
                if (root.TryGetProperty("builtin", out _)) textures += 1;
                if (root.TryGetProperty("light", out _)) textures += 5;
                if (root.TryGetProperty("projector", out _) || root.TryGetProperty("projectorTexture", out _) || root.TryGetProperty("projectorBuiltin", out _)) textures += 4; // a projector adds a shadowless spot light and an image
                if (root.TryGetProperty("flex", out _)) effects += 3; // a flexible object is animated every frame
                if (root.TryGetProperty("faces", out var faces) && faces.ValueKind == JsonValueKind.Array) textures += Math.Min(faces.GetArrayLength(), 32);
                if (root.TryGetProperty("transparency", out var alpha) && alpha.ValueKind == JsonValueKind.Number && alpha.TryGetDouble(out var a) && a < .999) effects += 1; // see-through surfaces cost more to draw
                if (root.TryGetProperty("emission", out _)) effects += 1;
            }
        }
        catch (JsonException) { }
        int scripts = 0, animations = 0, content = 0;
        foreach (var category in ContentCategories(contentJson).Take(ContentAssets.MaxEntries))
        {
            if (category == "scripts") scripts += ScriptCost; else if (category == "animations") animations += AnimationCost; else content += 1;
        }
        return new CostParts(geometry, physics, collision, textures, effects, scripts, animations, content);
    }

    private static bool ComplexCollision(string? geometryJson)
    {
        if (string.IsNullOrWhiteSpace(geometryJson)) return false;
        try
        {
            using var document = JsonDocument.Parse(geometryJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            double Read(string key, double fallback) => document.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
            return Read("hollow", 0) > 0 || Read("twist", 0) != 0 || Read("cutStart", 0) > 0 || Read("cutEnd", 1) < 1;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The category of every well-formed entry of an object's content (scripts, sounds, textures…), in order.</summary>
    public static IEnumerable<string> ContentCategories(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) yield break;
        JsonDocument? document = null;
        try { document = JsonDocument.Parse(contentJson); } catch (JsonException) { }
        if (document is null) yield break;
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) yield break;
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("assetId", out var asset) || !Guid.TryParse(asset.GetString(), out _)) continue;
                yield return entry.TryGetProperty("category", out var category) && category.ValueKind == JsonValueKind.String ? category.GetString() ?? "" : "";
            }
        }
    }

    public static bool HasScripts(string? contentJson) => ContentCategories(contentJson).Any(c => c == "scripts");

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
