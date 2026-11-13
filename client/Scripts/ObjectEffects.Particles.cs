using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>Particles and lights that scripts put on objects ("effect" from the island server): smoke, fire, sparks, rain, snow, explosions, trails, teleport bursts and dust, and point lights that can flicker. Counts are capped so a script cannot bury the screen.</summary>
public partial class ObjectEffects
{
    public const int MaxEmitters = 48, MaxLights = 16, MaxParticlesPerEmitter = 240;
    private readonly Dictionary<string, Node3D> _effects = [];

    public int EmittersForTesting => _effects.Values.Count(e => e is CpuParticles3D);
    public int LightsForTesting => _effects.Values.Count(e => e is OmniLight3D);
    public Node3D? EffectForTesting(string key) => _effects.GetValueOrDefault(key);

    public void ClearEffects() { foreach (var node in _effects.Values) if (IsInstanceValid(node)) node.QueueFree(); _effects.Clear(); }

    private void OnEffect(JsonElement root)
    {
        var key = root.GetProperty("key").GetString() ?? ""; if (key.Length == 0 || key.Length > 80) return;
        if (!root.GetProperty("on").GetBoolean()) { if (_effects.Remove(key, out var gone) && IsInstanceValid(gone)) gone.QueueFree(); return; }
        double Number(string name, double fallback, double min, double max) => root.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        var position = new Vector3((float)Number("x", 0, -10000, 10000), (float)Number("y", 0, -10000, 10000), (float)Number("z", 0, -10000, 10000));
        Color Colour(string fallback) { try { return Color.FromHtml(Text("color") is { Length: > 0 } c ? c : fallback); } catch { return Color.FromHtml(fallback); } }
        var kind = Text("kind");
        if (_effects.TryGetValue(key, out var existing) && IsInstanceValid(existing) && (kind == "light" ? existing is OmniLight3D : existing is CpuParticles3D)) { existing.GlobalPosition = position; if (kind == "light") ConfigureLight((OmniLight3D)existing, Colour("#FFFFFF"), (float)Number("radius", 8, .5, 30), (float)Number("energy", 1.5, 0, 8), root.TryGetProperty("flicker", out var f) && f.ValueKind == JsonValueKind.True); else ConfigureParticles((CpuParticles3D)existing, Text("preset"), Number("rate", 20, 1, 60), Number("lifetime", 2, .2, 6), Number("size", .4, .05, 2), Number("speed", 1.5, 0, 10), Colour("#FFFFFF")); return; }
        if (existing is not null && IsInstanceValid(existing)) existing.QueueFree();
        _effects.Remove(key);
        if (kind == "light")
        {
            if (_effects.Values.Count(e => e is OmniLight3D) >= MaxLights) return;
            var light = new OmniLight3D { Name = "Light_" + key.Replace(':', '_'), ShadowEnabled = false }; AddChild(light); light.GlobalPosition = position;
            ConfigureLight(light, Colour("#FFFFFF"), (float)Number("radius", 8, .5, 30), (float)Number("energy", 1.5, 0, 8), root.TryGetProperty("flicker", out var flicker) && flicker.ValueKind == JsonValueKind.True); _effects[key] = light;
        }
        else if (kind == "particles")
        {
            if (_effects.Values.Count(e => e is CpuParticles3D) >= MaxEmitters) return;
            var emitter = new CpuParticles3D { Name = "Particles_" + key.Replace(':', '_'), LocalCoords = false }; AddChild(emitter); emitter.GlobalPosition = position;
            ConfigureParticles(emitter, Text("preset"), Number("rate", 20, 1, 60), Number("lifetime", 2, .2, 6), Number("size", .4, .05, 2), Number("speed", 1.5, 0, 10), Colour("#FFFFFF")); _effects[key] = emitter;
        }
    }

    /// <summary>What each preset looks like: direction, spread, gravity, how it fades, whether it adds light and whether it fires once.</summary>
    private static readonly Dictionary<string, (Vector3 Direction, float Spread, float Gravity /* downward acceleration: negative rises */, bool Additive, bool Once, float Grow)> Looks = new()
    {
        ["smoke"] = (Vector3.Up, 18, -.6f, false, false, 2.2f), ["fire"] = (Vector3.Up, 14, -1.5f, true, false, .4f), ["sparks"] = (Vector3.Up, 55, 6f, true, false, .2f), ["rain"] = (Vector3.Down, 4, 9f, false, false, 1f),
        ["snow"] = (Vector3.Down, 25, .8f, false, false, 1f), ["explosion"] = (Vector3.Up, 180, 3f, true, true, 1.6f), ["trail"] = (Vector3.Zero, 6, 0f, true, false, .3f), ["teleport"] = (Vector3.Up, 25, -3f, true, true, .3f), ["dust"] = (Vector3.Up, 70, .3f, false, false, 1.5f)
    };

    private static void ConfigureParticles(CpuParticles3D emitter, string preset, double rate, double lifetime, double size, double speed, Color color)
    {
        var look = Looks.GetValueOrDefault(preset, Looks["smoke"]);
        emitter.Amount = (int)Math.Clamp(Math.Ceiling(rate * lifetime), 1, MaxParticlesPerEmitter); emitter.Lifetime = lifetime; emitter.OneShot = look.Once; emitter.Explosiveness = look.Once ? .95f : 0f; emitter.Emitting = true;
        emitter.Direction = look.Direction; emitter.Spread = look.Spread; emitter.Gravity = new Vector3(0, -look.Gravity, 0);
        emitter.EmissionShape = preset is "rain" or "snow" ? CpuParticles3D.EmissionShapeEnum.Box : CpuParticles3D.EmissionShapeEnum.Point; if (preset is "rain" or "snow") { emitter.EmissionBoxExtents = new Vector3(6, .1f, 6); emitter.Position += Vector3.Zero; }
        emitter.InitialVelocityMin = (float)speed * .6f; emitter.InitialVelocityMax = (float)speed; emitter.ScaleAmountMin = (float)size * .6f; emitter.ScaleAmountMax = (float)size;
        var fade = new Gradient { Offsets = [0f, .7f, 1f], Colors = [new Color(color, 0f), new Color(color, .9f), new Color(color, 0f)] }; emitter.ColorRamp = fade;
        var scale = new Curve(); scale.AddPoint(new Vector2(0, .4f)); scale.AddPoint(new Vector2(1, look.Grow)); emitter.ScaleAmountCurve = scale;
        var quad = new QuadMesh { Size = Vector2.One, Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, BlendMode = look.Additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix, BillboardKeepScale = true, AlbedoTexture = Soft() } };
        emitter.Mesh = quad;
    }

    private static GradientTexture2D? _soft;
    /// <summary>A round soft dot, so particles are puffs and not squares.</summary>
    private static GradientTexture2D Soft() => _soft ??= new GradientTexture2D { Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1f, .5f), Width = 64, Height = 64, Gradient = new Gradient { Offsets = [0f, 1f], Colors = [Colors.White, new Color(1, 1, 1, 0)] } };

    private static void ConfigureLight(OmniLight3D light, Color color, float radius, float energy, bool flicker)
    {
        light.LightColor = color; light.OmniRange = radius; light.LightEnergy = energy; light.SetMeta("base_energy", energy); light.SetMeta("flicker", flicker);
    }

    public override void _Process(double delta)
    {
        if (_effects.Count == 0) return;
        var time = Time.GetTicksMsec() / 1000.0;
        foreach (var node in _effects.Values) if (node is OmniLight3D light && IsInstanceValid(light) && light.GetMeta("flicker", false).AsBool()) light.LightEnergy = light.GetMeta("base_energy", 1.5f).AsSingle() * (float)(.78 + .22 * Math.Sin(time * 23 + light.GetInstanceId() % 7) * Math.Sin(time * 7.3));
    }
}
