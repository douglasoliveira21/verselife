using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeVerse.Shared.Building;

/// <summary>The flexible-path settings of an object (a flag, a plant, a curtain): how soft it is and what pushes it. Stored under "flex" in the material JSON.</summary>
public readonly record struct FlexSettings(float Softness, float Gravity, float Drag, float Wind, float Tension, float ForceX, float ForceY, float ForceZ)
{
    public const float MaxSoftness = 3f, MaxLevel = 10f, MaxForce = 10f;

    public FlexSettings Clamped() => new(Math.Clamp(Softness, 0f, MaxSoftness), Math.Clamp(Gravity, -MaxLevel, MaxLevel), Math.Clamp(Drag, 0f, MaxLevel), Math.Clamp(Wind, 0f, MaxLevel), Math.Clamp(Tension, 0f, MaxLevel),
        Math.Clamp(ForceX, -MaxForce, MaxForce), Math.Clamp(ForceY, -MaxForce, MaxForce), Math.Clamp(ForceZ, -MaxForce, MaxForce));

    /// <summary>The flex settings in a material JSON, or null when the object is rigid.</summary>
    public static FlexSettings? FromMaterial(string? materialJson)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(materialJson) || JsonNode.Parse(materialJson) is not JsonObject root || root["flex"] is not JsonObject flex) return null;
            float F(string key, float fallback) => flex[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : fallback;
            return new FlexSettings(F("softness", 1), F("gravity", 0), F("drag", 2), F("wind", 0), F("tension", 1), F("forceX", 0), F("forceY", 0), F("forceZ", 0)).Clamped();
        }
        catch (JsonException) { return null; }
    }

    public JsonObject ToJson() => new()
    {
        ["softness"] = Math.Round(Softness, 2), ["gravity"] = Math.Round(Gravity, 2), ["drag"] = Math.Round(Drag, 2), ["wind"] = Math.Round(Wind, 2), ["tension"] = Math.Round(Tension, 2),
        ["forceX"] = Math.Round(ForceX, 2), ["forceY"] = Math.Round(ForceY, 2), ["forceZ"] = Math.Round(ForceZ, 2)
    };
}

/// <summary>The sway of a flexible object: a spring that the force and the wind push and the tension and drag hold back. The result is how far the top leans (X, Z) and how much it sags (Y), in fractions of its height.</summary>
public sealed class FlexSimulation
{
    private Vector2 _lean, _speed;
    public Vector2 Lean => _lean;
    public float Sag { get; private set; }

    public void Step(FlexSettings flex, float dt, float time, float phase)
    {
        flex = flex.Clamped(); dt = Math.Clamp(dt, 0f, .05f);
        var gust = new Vector2(MathF.Sin(time * 1.3f + phase) + .5f * MathF.Sin(time * 3.1f + phase * 2f), MathF.Cos(time * .9f + phase) + .5f * MathF.Sin(time * 2.3f + phase)) * flex.Wind;
        var push = (new Vector2(flex.ForceX, flex.ForceZ) + gust) * .05f * flex.Softness / (1f + .3f * flex.Tension); // a tight object gives way less
        var stiffness = 4f * (1f + flex.Tension); var damping = 2f * (.5f + flex.Drag);
        var acceleration = push * stiffness - _lean * stiffness - _speed * damping;
        _speed += acceleration * dt; _lean += _speed * dt;
        var length = _lean.Length(); if (length > 1f) _lean *= 1f / length;
        Sag = Math.Clamp(-(flex.Gravity + flex.ForceY) * .02f * flex.Softness, -.3f, .3f);
    }

    public void Reset() { _lean = Vector2.Zero; _speed = Vector2.Zero; Sag = 0; }
}
