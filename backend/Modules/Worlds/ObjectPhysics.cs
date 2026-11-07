using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Api.Modules.Worlds;

/// <summary>
/// The limits of an object's physics settings (<c>PhysicsJson</c>). The client writes them with <c>LifeVerse.Shared.Building.PhysicsRules</c>; the API does not
/// reference that project, so the rules live here too and a test keeps both in step.
/// </summary>
public static class ObjectPhysics
{
    public const float MinMass = .01f, MaxMass = 10_000f;

    /// <summary>Null when the settings are acceptable, otherwise why not.</summary>
    public static string? Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonNode? node;
        try { node = JsonNode.Parse(json); } catch (JsonException) { return "Physics settings must be valid JSON."; }
        if (node is not JsonObject o) return "Physics settings must be an object.";
        foreach (var key in new[] { "collision", "trigger" }) if (o[key] is { } flag && !(flag is JsonValue v && v.TryGetValue<bool>(out _))) return $"Physics '{key}' must be true or false.";
        string? mode = null;
        if (o["mode"] is { } m) { if (m is not JsonValue mv || !mv.TryGetValue<string>(out mode) || mode is not ("static" or "physical")) return "Physics 'mode' must be static or physical."; }
        var shape = "box"; if (o["shape"] is { } s) { if (s is not JsonValue sv || !sv.TryGetValue<string>(out shape) || shape is not ("none" or "box" or "convex" or "triangle")) return "Physics 'shape' must be none, box, convex or triangle."; }
        if (Number(o, "mass", MinMass, MaxMass) is { } massError) return massError;
        if (Number(o, "friction", 0f, 1f) is { } frictionError) return frictionError;
        if (Number(o, "bounce", 0f, 1f) is { } bounceError) return bounceError;
        if (mode == "physical" && (Flag(o, "collision", true) == false || Flag(o, "trigger", false))) return "A physical object needs solid collision (not a ghost, not a trigger).";
        if (mode == "physical" && shape == "triangle") return "Triangle mesh collision is static only; use box or convex for a physical object.";
        return null;
    }

    public static bool IsPhysical(string? json)
    {
        try { return !string.IsNullOrWhiteSpace(json) && JsonNode.Parse(json) is JsonObject o && o["mode"] is JsonValue m && m.TryGetValue<string>(out var s) && s == "physical"; }
        catch (JsonException) { return false; }
    }

    private static bool Flag(JsonObject o, string key, bool fallback) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static string? Number(JsonObject o, string key, float min, float max)
    {
        if (o[key] is not { } node) return null;
        if (node is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < min || number > max) return $"Physics '{key}' must be between {min} and {max}.";
        return null;
    }
}
