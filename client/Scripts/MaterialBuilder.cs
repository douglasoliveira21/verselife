using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Turns an object's material JSON into a Godot material. A plain colour (with roughness, metallic, opacity and glow) is a
/// StandardMaterial3D as before; as soon as a texture, a built-in pattern or a normal map is involved a small shader is used,
/// because StandardMaterial3D cannot rotate or flip UVs. A face entry's settings are laid over the object's settings.
public static class MaterialBuilder
{
    private const string OpaqueShader = """
        shader_type spatial;
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
        uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
        uniform bool has_albedo = false;
        uniform bool has_normal = false;
        uniform float roughness_value = 0.7;
        uniform float metallic_value = 0.0;
        uniform vec3 emission_color : source_color = vec3(0.0);
        uniform float emission_strength = 0.0;
        uniform vec2 uv_scale = vec2(1.0);
        uniform vec2 uv_offset = vec2(0.0);
        uniform float uv_rotation = 0.0;

        vec2 transform_uv(vec2 uv) {
            vec2 c = uv - vec2(0.5);
            float s = sin(uv_rotation); float k = cos(uv_rotation);
            c = vec2(k * c.x - s * c.y, s * c.x + k * c.y);
            return c * uv_scale + vec2(0.5) + uv_offset;
        }

        void fragment() {
            vec2 uv = transform_uv(UV);
            vec4 t = has_albedo ? texture(albedo_tex, uv) : vec4(1.0);
            ALBEDO = albedo.rgb * t.rgb;
            //ALPHA_LINE
            ROUGHNESS = roughness_value;
            METALLIC = metallic_value;
            if (has_normal) NORMAL_MAP = texture(normal_tex, uv).rgb;
            EMISSION = emission_color * emission_strength;
        }
        """;

    private static Shader? _opaque, _alpha;

    private static Shader ShaderFor(bool translucent) => translucent
        ? _alpha ??= new Shader { Code = OpaqueShader.Replace("shader_type spatial;", "shader_type spatial;\nrender_mode blend_mix, depth_draw_always;").Replace("//ALPHA_LINE", "ALPHA = albedo.a * t.a;") }
        : _opaque ??= new Shader { Code = OpaqueShader };

    private static string? Text(JsonObject layer, JsonObject? over, string key) => (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static float Number(JsonObject layer, JsonObject? over, string key, float fallback) =>
        (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? (float)number : fallback;

    private static bool Flag(JsonObject layer, JsonObject? over, string key) => (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static Color ColorOf(string? text, Color fallback)
    {
        try { return text is null ? fallback : Color.FromHtml(text); } catch { return fallback; }
    }

    public static bool IsTextured(JsonObject layer, JsonObject? over) => Text(layer, over, "texture") is not null || Text(layer, over, "builtin") is not null || Text(layer, over, "normalMap") is not null;

    /// `layer` is the object's material; `over` the entry of one face (or null). Textures arrive later and are put on the material then.
    public static Material Build(JsonObject layer, JsonObject? over, TextureCache? cache)
    {
        var color = ColorOf(Text(layer, over, "color"), new Color("#A8C7D8"));
        var roughness = Mathf.Clamp(Number(layer, over, "roughness", .7f), 0, 1);
        var metallic = Mathf.Clamp(Number(layer, over, "metallic", 0), 0, 1);
        var opacity = Number(layer, over, "transparency", 1f);
        var translucent = (over?["transparency"] ?? layer["transparency"]) is not null && opacity < .999f;
        var emission = Text(layer, over, "emission") is { } glow ? ColorOf(glow, Colors.Black) : (Color?)null;
        var strength = Mathf.Clamp(Number(layer, over, "emissionStrength", 1f), 0, 16);

        if (!IsTextured(layer, over))
        {
            var plain = new StandardMaterial3D { AlbedoColor = new Color(color, translucent ? Mathf.Clamp(opacity, 0, 1) : 1f), Roughness = roughness, Metallic = metallic };
            if (translucent) plain.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            if (emission is { } e) { plain.EmissionEnabled = true; plain.Emission = e; plain.EmissionEnergyMultiplier = strength; }
            return plain;
        }

        var rotation = Mathf.DegToRad(Number(layer, over, "uvRotation", 0));
        var scale = new Vector2(Mathf.Max(.01f, Number(layer, over, "uvRepeatX", 1)) * (Flag(layer, over, "flipU") ? -1 : 1), Mathf.Max(.01f, Number(layer, over, "uvRepeatY", 1)) * (Flag(layer, over, "flipV") ? -1 : 1));
        var material = new ShaderMaterial { Shader = ShaderFor(translucent) };
        material.SetShaderParameter("albedo", new Color(color, translucent ? Mathf.Clamp(opacity, 0, 1) : 1f));
        material.SetShaderParameter("roughness_value", roughness); material.SetShaderParameter("metallic_value", metallic);
        material.SetShaderParameter("uv_scale", scale); material.SetShaderParameter("uv_offset", new Vector2(Number(layer, over, "uvOffsetX", 0), Number(layer, over, "uvOffsetY", 0)));
        material.SetShaderParameter("uv_rotation", rotation);
        material.SetShaderParameter("emission_color", emission ?? Colors.Black); material.SetShaderParameter("emission_strength", emission is null ? 0f : strength);

        if (Text(layer, over, "builtin") is { } builtin && BuiltInTextures.Get(builtin) is { } pattern) { material.SetShaderParameter("albedo_tex", pattern); material.SetShaderParameter("has_albedo", true); }
        else if (Guid.TryParse(Text(layer, over, "texture"), out var textureId) && cache is not null)
            cache.Request(textureId, texture => { if (texture is null || !GodotObject.IsInstanceValid(material)) return; material.SetShaderParameter("albedo_tex", texture); material.SetShaderParameter("has_albedo", true); });
        if (Guid.TryParse(Text(layer, over, "normalMap"), out var normalId) && cache is not null)
            cache.Request(normalId, texture => { if (texture is null || !GodotObject.IsInstanceValid(material)) return; material.SetShaderParameter("normal_tex", texture); material.SetShaderParameter("has_normal", true); });
        return material;
    }
}
