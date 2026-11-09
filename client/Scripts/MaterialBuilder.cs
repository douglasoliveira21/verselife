using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Turns an object's material JSON into a Godot material. A plain colour (with roughness, metallic, opacity, glow, full bright, alpha mode and double sided) is a
/// StandardMaterial3D; as soon as a texture, a built-in pattern or a normal map is involved a small shader is used, because StandardMaterial3D cannot rotate or flip
/// UVs or map a texture planar. A face entry's settings are laid over the object's settings.
/// Keys: color, roughness, metallic, transparency, emission, emissionStrength, texture, builtin, normalMap, uvRepeatX/Y, uvOffsetX/Y, uvRotation, flipU/V,
/// fullBright (unlit), mapping ("default" or "planar": texture sized in metres, projected along the face's main axis), alphaMode ("none", "blend" or "mask"),
/// alphaCutoff (0 to 1, for mask), doubleSided.
public static class MaterialBuilder
{
    private const string ShaderTemplate = """
        shader_type spatial;
        //RENDER_MODE
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
        uniform float alpha_cutoff = 0.5;
        varying vec3 local_pos;
        varying vec3 local_normal;
        varying vec3 model_scale;

        void vertex() {
            local_pos = VERTEX;
            local_normal = NORMAL;
            model_scale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
        }

        vec2 transform_uv(vec2 uv) {
            vec2 c = uv - vec2(0.5);
            float s = sin(uv_rotation); float k = cos(uv_rotation);
            c = vec2(k * c.x - s * c.y, s * c.x + k * c.y);
            return c * uv_scale + vec2(0.5) + uv_offset;
        }

        void fragment() {
            vec2 base_uv = UV;
            //PLANAR_LINE
            vec2 uv = transform_uv(base_uv);
            vec4 t = has_albedo ? texture(albedo_tex, uv) : vec4(1.0);
            ALBEDO = albedo.rgb * t.rgb;
            //ALPHA_LINE
            ROUGHNESS = roughness_value;
            METALLIC = metallic_value;
            if (has_normal) NORMAL_MAP = texture(normal_tex, uv).rgb;
            EMISSION = emission_color * emission_strength;
        }
        """;

    private const string PlanarLine = "vec3 pp = local_pos * model_scale; vec3 an = abs(local_normal); base_uv = (an.y >= an.x && an.y >= an.z) ? pp.xz : ((an.x >= an.z) ? pp.zy : pp.xy);";

    private static readonly Dictionary<string, Shader> Shaders = [];

    private static Shader ShaderFor(string alphaMode, bool doubleSided, bool unlit, bool planar)
    {
        var key = $"{alphaMode}|{doubleSided}|{unlit}|{planar}";
        if (Shaders.TryGetValue(key, out var cached)) return cached;
        var modes = new List<string>();
        if (alphaMode == "blend") { modes.Add("blend_mix"); modes.Add("depth_draw_always"); }
        if (doubleSided) modes.Add("cull_disabled");
        if (unlit) modes.Add("unshaded");
        var code = ShaderTemplate
            .Replace("//RENDER_MODE", modes.Count == 0 ? "" : "render_mode " + string.Join(", ", modes) + ";")
            .Replace("//PLANAR_LINE", planar ? PlanarLine : "")
            .Replace("//ALPHA_LINE", alphaMode switch { "blend" => "ALPHA = albedo.a * t.a;", "mask" => "ALPHA = albedo.a * t.a; ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;", _ => "" });
        return Shaders[key] = new Shader { Code = code };
    }

    private static string? Text(JsonObject layer, JsonObject? over, string key) => (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static float Number(JsonObject layer, JsonObject? over, string key, float fallback) =>
        (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? (float)number : fallback;

    private static bool Flag(JsonObject layer, JsonObject? over, string key) => (over?[key] ?? layer[key]) is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static Color ColorOf(string? text, Color fallback)
    {
        try { return text is null ? fallback : Color.FromHtml(text); } catch { return fallback; }
    }

    public static bool IsTextured(JsonObject layer, JsonObject? over) => Text(layer, over, "texture") is not null || Text(layer, over, "builtin") is not null || Text(layer, over, "normalMap") is not null;

    /// How the alpha channel is used: "none" (opaque), "blend" (see-through) or "mask" (cut out with a cutoff). Without a choice, an object with transparency below 1 blends.
    public static string AlphaMode(JsonObject layer, JsonObject? over)
    {
        var chosen = Text(layer, over, "alphaMode");
        if (chosen is "none" or "blend" or "mask") return chosen;
        var opacity = Number(layer, over, "transparency", 1f);
        return (over?["transparency"] ?? layer["transparency"]) is not null && opacity < .999f ? "blend" : "none";
    }

    /// `layer` is the object's material; `over` the entry of one face (or null). Textures arrive later and are put on the material then.
    public static Material Build(JsonObject layer, JsonObject? over, TextureCache? cache)
    {
        var color = ColorOf(Text(layer, over, "color"), new Color("#A8C7D8"));
        var roughness = Mathf.Clamp(Number(layer, over, "roughness", .7f), 0, 1);
        var metallic = Mathf.Clamp(Number(layer, over, "metallic", 0), 0, 1);
        var opacity = Mathf.Clamp(Number(layer, over, "transparency", 1f), 0, 1);
        var alphaMode = AlphaMode(layer, over);
        var cutoff = Mathf.Clamp(Number(layer, over, "alphaCutoff", .5f), 0, 1);
        var doubleSided = Flag(layer, over, "doubleSided"); var unlit = Flag(layer, over, "fullBright");
        var planar = Text(layer, over, "mapping") == "planar";
        var alpha = alphaMode == "none" ? 1f : opacity;
        var emission = Text(layer, over, "emission") is { } glow ? ColorOf(glow, Colors.Black) : (Color?)null;
        var strength = Mathf.Clamp(Number(layer, over, "emissionStrength", 1f), 0, 16);

        if (!IsTextured(layer, over))
        {
            var plain = new StandardMaterial3D { AlbedoColor = new Color(color, alpha), Roughness = roughness, Metallic = metallic };
            if (alphaMode == "blend") plain.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            else if (alphaMode == "mask") { plain.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor; plain.AlphaScissorThreshold = cutoff; }
            if (doubleSided) plain.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            if (unlit) plain.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            if (emission is { } e) { plain.EmissionEnabled = true; plain.Emission = e; plain.EmissionEnergyMultiplier = strength; }
            return plain;
        }

        var rotation = Mathf.DegToRad(Number(layer, over, "uvRotation", 0));
        var scale = new Vector2(Mathf.Max(.01f, Number(layer, over, "uvRepeatX", 1)) * (Flag(layer, over, "flipU") ? -1 : 1), Mathf.Max(.01f, Number(layer, over, "uvRepeatY", 1)) * (Flag(layer, over, "flipV") ? -1 : 1));
        var material = new ShaderMaterial { Shader = ShaderFor(alphaMode, doubleSided, unlit, planar) };
        material.SetShaderParameter("albedo", new Color(color, alpha));
        material.SetShaderParameter("roughness_value", roughness); material.SetShaderParameter("metallic_value", metallic);
        material.SetShaderParameter("uv_scale", scale); material.SetShaderParameter("uv_offset", new Vector2(Number(layer, over, "uvOffsetX", 0), Number(layer, over, "uvOffsetY", 0)));
        material.SetShaderParameter("uv_rotation", rotation); material.SetShaderParameter("alpha_cutoff", cutoff);
        material.SetShaderParameter("emission_color", emission ?? Colors.Black); material.SetShaderParameter("emission_strength", emission is null ? 0f : strength);

        if (Text(layer, over, "builtin") is { } builtin && BuiltInTextures.Get(builtin) is { } pattern) { material.SetShaderParameter("albedo_tex", pattern); material.SetShaderParameter("has_albedo", true); }
        else if (Guid.TryParse(Text(layer, over, "texture"), out var textureId) && cache is not null)
            cache.Request(textureId, texture => { if (texture is null || !GodotObject.IsInstanceValid(material)) return; material.SetShaderParameter("albedo_tex", texture); material.SetShaderParameter("has_albedo", true); });
        if (Guid.TryParse(Text(layer, over, "normalMap"), out var normalId) && cache is not null)
            cache.Request(normalId, texture => { if (texture is null || !GodotObject.IsInstanceValid(material)) return; material.SetShaderParameter("normal_tex", texture); material.SetShaderParameter("has_normal", true); });
        return material;
    }
}
