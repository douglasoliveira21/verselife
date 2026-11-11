using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Build;

/// <summary>
/// The "Recursos" of the selected objects: the light they give, the reflection they cast, the shape and numbers of their physics and, for a rigged mesh, its animation. They live in the
/// material and physics JSON stored with the object, so a change is an ordinary update the server checks (ranges, budget) and every player then sees. The limits of the graphics
/// card are respected by the world: a few lights, shadows and probes at a time, nearest first.
/// </summary>
public partial class BuildResourceManager : Node
{
    public sealed record LightModel(bool On, string Color, float Energy, float Range, float Falloff, bool Shadow);
    public sealed record ReflectionModel(bool On, float Intensity, float Range);
    public sealed record AnimationModel(bool On, string Clip, bool Playing, bool Loop, double Speed, double Offset);
    public sealed record PhysicsModel(string Shape, bool Physical, float Mass, float Friction, float Bounce, string Material);

    public static readonly (string Id, string Label)[] ShapeKinds = [("none", "Nenhuma"), ("box", "Prim (a forma do objeto)"), ("convex", "Casco convexo"), ("triangle", "Malha exata (só objeto parado)")];

    private BuildNetworkSynchronizer _sync = null!;
    private BuildSelectionManager _selection = null!;

    public void Initialize(BuildNetworkSynchronizer sync, BuildSelectionManager selection) { _sync = sync; _selection = selection; }

    private static JsonObject Material(BuildObjectState s) { try { return JsonNode.Parse(string.IsNullOrWhiteSpace(s.MaterialJson) ? "{}" : s.MaterialJson) as JsonObject ?? []; } catch (JsonException) { return []; } }
    private static double Num(JsonObject? o, string key, double fallback) => o?[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? n : fallback;
    private static bool Flag(JsonObject? o, string key) => o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    // ---- reading -----------------------------------------------------------------------------------------------------------

    public static LightModel ReadLight(BuildObjectState s)
    {
        var light = Material(s)["light"] as JsonObject;
        return new LightModel(light is not null, light?["color"] is JsonValue c && c.TryGetValue<string>(out var text) ? text : "#FFE0A8", (float)Num(light, "energy", 1.5), (float)Num(light, "range", 8), (float)Num(light, "falloff", 1), Flag(light, "shadow"));
    }

    public static ReflectionModel ReadReflection(BuildObjectState s) { var r = Material(s)["reflection"] as JsonObject; return new ReflectionModel(r is not null, (float)Num(r, "intensity", .6), (float)Num(r, "range", 8)); }

    public static AnimationModel ReadAnimation(BuildObjectState s)
    {
        var a = Material(s)["animation"] as JsonObject;
        return new AnimationModel(a is not null, a?["clip"] is JsonValue c && c.TryGetValue<string>(out var name) ? name : string.Empty, Flag(a, "playing"), Flag(a, "loop"), Num(a, "speed", 1), Num(a, "offset", 0));
    }

    public static PhysicsModel ReadPhysics(BuildObjectState s)
    {
        var p = PhysicsRules.Read(s.PhysicsJson); var json = JsonNode.Parse(string.IsNullOrWhiteSpace(s.PhysicsJson) ? "{}" : s.PhysicsJson) as JsonObject;
        var shape = json?["shape"] is null && s.PrimitiveType == "mesh" ? "triangle" : p.CollisionShape is "sphere" or "cylinder" ? "box" : p.CollisionShape;
        return new PhysicsModel(shape, p.Physical, p.Mass, p.Friction, p.Bounce, p.Material);
    }

    // ---- changing ------------------------------------------------------------------------------------------------------------

    private int Edit(Action<BuildObjectState, JsonObject> change)
    {
        var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            var after = BuildPersistenceManager.Clone(view.State); var material = Material(after); change(after, material);
            after.MaterialJson = material.ToJsonString(); if (after.MaterialJson == view.State.MaterialJson) continue;
            if (_sync.Update(after)) sent++;
        }
        return sent;
    }

    /// <summary>Turns the light on with these values, or off (which also takes the projector, that needs it, away).</summary>
    public int SetLight(LightModel model) => Edit((_, material) =>
    {
        if (!model.On) { material.Remove("light"); material.Remove("projector"); material.Remove("projectorTexture"); material.Remove("projectorBuiltin"); return; }
        var light = new JsonObject { ["color"] = model.Color, ["energy"] = Math.Round(Math.Clamp(model.Energy, 0, 8), 2), ["range"] = Math.Round(Math.Clamp(model.Range, .5, 30), 2), ["falloff"] = Math.Round(Math.Clamp(model.Falloff, .1, 4), 2) };
        if (model.Shadow) light["shadow"] = true; material["light"] = light;
    });

    // ---- projector and Flexi ---------------------------------------------------------------------------------------------

    /// <summary>The picture a projecting light throws: a system pattern (Builtin) or an image of the library (Asset), with the cone and the glow around it.</summary>
    public sealed record ProjectorModel(bool On, string? Builtin, Guid? Asset, float Fov, float Focus, float Ambiance);

    public static ProjectorModel ReadProjector(BuildObjectState s)
    {
        var material = Material(s); var projector = material["projector"] as JsonObject;
        var builtin = material["projectorBuiltin"] is JsonValue b && b.TryGetValue<string>(out var id) ? id : null;
        Guid? asset = material["projectorTexture"] is JsonValue a && a.TryGetValue<string>(out var text) && Guid.TryParse(text, out var parsed) ? parsed : null;
        return new ProjectorModel(projector is not null, builtin, asset, (float)Num(projector, "fov", 45), (float)Num(projector, "focus", 10), (float)Num(projector, "ambiance", .1));
    }

    /// <summary>Turns the projector on with a picture, or off. It needs the light to be on (the server also refuses it otherwise).</summary>
    public int SetProjector(ProjectorModel model) => Edit((_, material) =>
    {
        material.Remove("projector"); material.Remove("projectorTexture"); material.Remove("projectorBuiltin");
        if (!model.On || material["light"] is null) return;
        if (model.Asset is { } asset) material["projectorTexture"] = asset.ToString(); else if (model.Builtin is { Length: > 0 } builtin) material["projectorBuiltin"] = builtin; else return;
        material["projector"] = new JsonObject { ["fov"] = Math.Round(Math.Clamp(model.Fov, 10, 120), 1), ["focus"] = Math.Round(Math.Clamp(model.Focus, 0, 30), 1), ["ambiance"] = Math.Round(Math.Clamp(model.Ambiance, 0, 1), 2) };
    });

    public static FlexSettings? ReadFlex(BuildObjectState s) => FlexSettings.FromMaterial(s.MaterialJson);

    /// <summary>Flexi: the object sways like a flag or a plant (base still, top moving). Null turns it off.</summary>
    public int SetFlex(FlexSettings? settings) => Edit((_, material) => { if (settings is { } flex) material["flex"] = flex.Clamped().ToJson(); else material.Remove("flex"); });

    public int SetReflection(ReflectionModel model) => Edit((_, material) =>
    {
        if (!model.On) { material.Remove("reflection"); return; }
        material["reflection"] = new JsonObject { ["intensity"] = Math.Round(Math.Clamp(model.Intensity, 0, 1), 2), ["range"] = Math.Round(Math.Clamp(model.Range, 2, 30), 1) };
    });

    /// <summary>Sets the animation of the selected meshes. The server writes the moment it started (<c>since</c>); <c>offset</c> is where in the clip it was when the person changed it.</summary>
    public int SetAnimation(AnimationModel model) => Edit((state, material) =>
    {
        if (state.PrimitiveType != "mesh") return;
        if (!model.On || model.Clip.Length == 0) { material.Remove("animation"); return; }
        var animation = new JsonObject { ["clip"] = model.Clip, ["playing"] = model.Playing, ["loop"] = model.Loop, ["speed"] = Math.Round(Math.Clamp(model.Speed, .1, 4), 2), ["offset"] = Math.Round(Math.Max(0, model.Offset), 2) };
        if (material["animation"] is JsonObject old && old["since"] is { } since) animation["since"] = since.DeepClone(); // the server restamps it when something changed
        material["animation"] = animation;
    });

    /// <summary>The shape of the physics: none, the form of the object, a convex hull or (meshes, static only) the exact triangles. Returns how many were sent, or why not.</summary>
    public (int Sent, string? Problem) SetPhysicsShape(string shape)
    {
        var sent = 0; var plan = new List<BuildObjectState>();
        foreach (var view in _selection.Views().ToList())
        {
            var settings = PhysicsRules.Read(view.State.PhysicsJson);
            if (shape == "triangle" && view.State.PrimitiveType != "mesh") return (0, "A malha exata só existe para modelos importados.");
            if (settings.Physical && shape is "none" or "triangle") return (0, "Um objeto físico precisa da forma Prim ou Casco convexo: desligue Física antes.");
            var after = BuildPersistenceManager.Clone(view.State); after.PhysicsJson = PhysicsRules.Write(view.State.PhysicsJson, settings with { CollisionShape = shape });
            if (after.PhysicsJson != view.State.PhysicsJson) plan.Add(after);
        }
        foreach (var after in plan) if (_sync.Update(after)) sent++;
        return (sent, null);
    }

    /// <summary>Mass (kg), friction and bounce (0 to 1) or a surface material that sets the last two; they matter for objects the island simulates.</summary>
    public int SetPhysicsNumbers(float mass, float friction, float bounce, string material)
    {
        var sent = 0;
        foreach (var view in _selection.Views().ToList())
        {
            var settings = PhysicsRules.Read(view.State.PhysicsJson);
            var after = BuildPersistenceManager.Clone(view.State); after.PhysicsJson = PhysicsRules.Write(view.State.PhysicsJson, settings with { Mass = Math.Clamp(mass, PhysicsRules.MinMass, PhysicsRules.MaxMass), Friction = Math.Clamp(friction, 0, 1), Bounce = Math.Clamp(bounce, 0, 1), Material = material });
            if (after.PhysicsJson != view.State.PhysicsJson && _sync.Update(after)) sent++;
        }
        return sent;
    }
}
