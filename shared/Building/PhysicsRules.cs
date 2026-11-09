using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Shared.Building;

/// <summary>
/// How an object takes part in physics. <see cref="Physical"/> marks an object that is meant to move by physics (fall, be pushed) instead of standing still;
/// the settings are stored, validated and carried through saved constructions. The world does not simulate those bodies yet: a physical object stands where it was put.
/// </summary>
public sealed record PhysicsSettings(bool Collision = true, bool Trigger = false, bool Physical = false, float Mass = 1f, float Friction = .5f, float Bounce = 0f, string CollisionShape = "box", string Material = "", bool Temporary = false);

/// <summary>Reading, checking and writing the <c>PhysicsJson</c> of an object. The API keeps the same limits (a test compares both).</summary>
public static class PhysicsRules
{
    public const float MinMass = .01f, MaxMass = 10_000f;
    /// <summary>The surface materials an object can be given, with the friction and bounce they stand for (stone grips and barely bounces; rubber grips and bounces).</summary>
    public static readonly IReadOnlyDictionary<string, (string Label, float Friction, float Bounce)> Materials = new Dictionary<string, (string, float, float)>
    {
        ["stone"] = ("Pedra", .6f, .1f), ["metal"] = ("Metal", .3f, .3f), ["glass"] = ("Vidro", .2f, .6f), ["wood"] = ("Madeira", .6f, .4f),
        ["flesh"] = ("Carne", .5f, .3f), ["plastic"] = ("Plástico", .4f, .5f), ["rubber"] = ("Borracha", .9f, .9f)
    };


    /// <summary>Null when the physics settings are acceptable, otherwise why not.</summary>
    public static string? Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonNode? node;
        try { node = JsonNode.Parse(json); } catch (JsonException) { return "Physics settings must be valid JSON."; }
        if (node is not JsonObject o) return "Physics settings must be an object.";
        foreach (var key in new[] { "collision", "trigger", "temporary" }) if (o[key] is { } flag && !(flag is JsonValue v && v.TryGetValue<bool>(out _))) return $"Physics '{key}' must be true or false.";
        string? mode = null;
        if (o["mode"] is { } m) { if (m is not JsonValue mv || !mv.TryGetValue<string>(out mode) || mode is not ("static" or "physical")) return "Physics 'mode' must be static or physical."; }
        var shape = "box"; if (o["shape"] is { } s) { if (s is not JsonValue sv || !sv.TryGetValue<string>(out shape) || shape is not ("none" or "box" or "convex" or "triangle")) return "Physics 'shape' must be none, box, convex or triangle."; }
        if (o["material"] is { } material && !(material is JsonValue mv2 && mv2.TryGetValue<string>(out var materialName) && Materials.ContainsKey(materialName))) return "Physics 'material' is not a known material.";
        if (Number(o, "mass", MinMass, MaxMass) is { } massError) return massError;
        if (Number(o, "friction", 0f, 1f) is { } frictionError) return frictionError;
        if (Number(o, "bounce", 0f, 1f) is { } bounceError) return bounceError;
        if (mode == "physical" && (Flag(o, "collision", true) == false || Flag(o, "trigger", false))) return "A physical object needs solid collision (not a ghost, not a trigger).";
        if (mode == "physical" && shape == "triangle") return "Triangle mesh collision is static only; use box or convex for a physical object.";
        return null;
    }

    private static bool Flag(JsonObject o, string key, bool fallback) => o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static string? Number(JsonObject o, string key, float min, float max)
    {
        if (o[key] is not { } node) return null;
        if (node is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < min || number > max) return $"Physics '{key}' must be between {min} and {max}.";
        return null;
    }

    public static PhysicsSettings Read(string? json)
    {
        try
        {
            if (JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) is not JsonObject o) return new PhysicsSettings();
            float F(string key, float fallback, float min, float max) => o[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? Math.Clamp((float)n, min, max) : fallback;
            var shape = o["shape"] is JsonValue sh && sh.TryGetValue<string>(out var value) && value is "none" or "box" or "convex" or "triangle" ? value : "box";
            return new PhysicsSettings(Flag(o, "collision", true), Flag(o, "trigger", false), o["mode"] is JsonValue m && m.TryGetValue<string>(out var s) && s == "physical", F("mass", 1f, MinMass, MaxMass), F("friction", .5f, 0f, 1f), F("bounce", 0f, 0f, 1f), shape, o["material"] is JsonValue mat && mat.TryGetValue<string>(out var materialName) && Materials.ContainsKey(materialName) ? materialName : "", Flag(o, "temporary", false));
        }
        catch (JsonException) { return new PhysicsSettings(); }
    }

    /// <summary>The settings written into the existing JSON (other keys are kept). A static object carries no mode or numbers, so old objects stay exactly as they were.</summary>
    public static string Write(string? existing, PhysicsSettings settings)
    {
        JsonObject o;
        try { o = JsonNode.Parse(string.IsNullOrWhiteSpace(existing) ? "{}" : existing) as JsonObject ?? []; } catch (JsonException) { o = []; }
        o["collision"] = settings.Collision;
        if (!settings.Collision || settings.CollisionShape == "none") o["shape"] = "none"; else o["shape"] = settings.CollisionShape is "box" or "convex" or "triangle" ? settings.CollisionShape : "box";
        if (settings.Trigger) o["trigger"] = true; else o.Remove("trigger");
        if (Materials.ContainsKey(settings.Material)) o["material"] = settings.Material; else o.Remove("material");
        if (settings.Temporary) o["temporary"] = true; else o.Remove("temporary");
        if (settings.Physical && settings.Collision && !settings.Trigger)
        {
            o["mode"] = "physical"; o["mass"] = Math.Round(Math.Clamp(settings.Mass, MinMass, MaxMass), 3);
            o["friction"] = Math.Round(Math.Clamp(settings.Friction, 0f, 1f), 3); o["bounce"] = Math.Round(Math.Clamp(settings.Bounce, 0f, 1f), 3);
        }
        else { o.Remove("mode"); o.Remove("mass"); o.Remove("friction"); o.Remove("bounce"); }
        return o.ToJsonString();
    }
}
