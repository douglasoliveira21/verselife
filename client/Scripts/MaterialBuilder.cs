using System.Text.Json.Nodes;
using Godot;

namespace LifeVerse.Client.Scripts;

/// Turns an object's material JSON into a Godot material. A plain colour in the PBR model is a StandardMaterial3D; a textured material, or the Blinn-Phong model, is a
/// small shader. A face entry's settings are laid over the object's settings.
///
/// Models (key "shading"): "pbr" (base colour, normal map, a metallic-roughness-occlusion map "ormMap", an emissive map, roughness and metallic numbers) and
/// "blinnphong" (diffuse colour or texture, normal map, a specular map "specularMap" with "specularColor", "glossiness" and "environment": the classic lit-by-the-lamps
/// look). Without the key the material behaves as a PBR one, so old objects look as before.
/// Maps: texture/builtin (base colour or diffuse), normalMap, specularMap, ormMap, emissiveMap. Each map has its own repeat, offset and rotation: the layer's uvRepeatX/Y,
/// uvOffsetX/Y and uvRotation are the default of every map and "mapUv": { "<map>": { repeatX, repeatY, offsetX, offsetY, rotation } } overrides one map.
/// Other keys: color, transparency, emission, emissionStrength, flipU/flipV, fullBright, mapping ("default"/"planar"), alphaMode, alphaCutoff, doubleSided, noTexture
/// (the layer has no texture at all, whatever the object has).
public static class MaterialBuilder
{
    public static readonly string[] MapKeys = ["texture", "normalMap", "specularMap", "ormMap", "emissiveMap"];

    private const string ShaderTemplate = """
        shader_type spatial;
        //RENDER_MODE
        uniform vec4 albedo : source_color = vec4(1.0);
        uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
        uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
        uniform sampler2D spec_tex : source_color, filter_linear_mipmap, repeat_enable;
        uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
        uniform sampler2D emissive_tex : source_color, filter_linear_mipmap, repeat_enable;
        uniform bool has_albedo = false;
        uniform bool has_normal = false;
        uniform bool has_spec = false;
        uniform bool has_orm = false;
        uniform bool has_emissive = false;
        uniform float roughness_value = 0.7;
        uniform float metallic_value = 0.0;
        uniform vec3 emission_color : source_color = vec3(0.0);
        uniform float emission_strength = 0.0;
        uniform vec4 st_albedo = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 st_normal = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 st_spec = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 st_orm = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 st_emissive = vec4(1.0, 1.0, 0.0, 0.0);
        uniform vec4 rot_a = vec4(0.0); // rotations (radians) of albedo, normal, spec, orm
        uniform float rot_e = 0.0;      // rotation of the emissive map
        uniform float alpha_cutoff = 0.5;
        uniform vec3 spec_color : source_color = vec3(1.0);
        uniform float glossiness = 0.5;
        uniform float environment = 0.0;
        varying vec3 local_pos;
        varying vec3 local_normal;
        varying vec3 model_scale;
        varying vec2 map_uv;

        void vertex() {
            local_pos = VERTEX;
            local_normal = NORMAL;
            model_scale = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
        }

        vec2 tuv(vec2 uv, vec4 st, float rot) {
            vec2 c = uv - vec2(0.5);
            float s = sin(rot); float k = cos(rot);
            c = vec2(k * c.x - s * c.y, s * c.x + k * c.y);
            return c * st.xy + vec2(0.5) + st.zw;
        }

        void fragment() {
            vec2 base_uv = UV;
            //PLANAR_LINE
            map_uv = base_uv;
            vec4 t = has_albedo ? texture(albedo_tex, tuv(base_uv, st_albedo, rot_a.x)) : vec4(1.0);
            ALBEDO = albedo.rgb * t.rgb;
            //ALPHA_LINE
            if (has_normal) NORMAL_MAP = texture(normal_tex, tuv(base_uv, st_normal, rot_a.y)).rgb;
            //SURFACE
            vec3 glow = emission_color * emission_strength;
            if (has_emissive) glow *= texture(emissive_tex, tuv(base_uv, st_emissive, rot_e)).rgb;
            EMISSION = glow;
            //ENVIRONMENT
        }
        //LIGHT_FUNCTION
        """;

    // PBR: roughness and metallic come from the ORM map (green and blue; red is the occlusion) when there is one, else from the numbers.
    private const string PbrSurface = """
            vec4 orm = has_orm ? texture(orm_tex, tuv(base_uv, st_orm, rot_a.w)) : vec4(1.0);
            ROUGHNESS = has_orm ? orm.g : roughness_value;
            METALLIC = has_orm ? orm.b : metallic_value;
            AO = has_orm ? orm.r : 1.0; AO_LIGHT_AFFECT = 1.0;
        """;

    // Blinn-Phong: the lamps light the surface with a diffuse term and a specular highlight shaped by the glossiness; the built-in physically based light is replaced.
    private const string BlinnSurface = """
            ROUGHNESS = 1.0; METALLIC = 0.0; SPECULAR = 0.0;
        """;

    private const string BlinnEnvironment = """
            // a hint of the sky on grazing angles, as strong as "environment" says
            EMISSION += environment * pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0) * vec3(0.55, 0.72, 0.95);
        """;

    private const string BlinnLight = """
        void light() {
            vec3 n = normalize(NORMAL); vec3 l = normalize(LIGHT); vec3 v = normalize(VIEW);
            float ndl = clamp(dot(n, l), 0.0, 1.0);
            vec3 h = normalize(l + v);
            float shininess = mix(6.0, 220.0, glossiness);
            vec3 sc = spec_color * (has_spec ? texture(spec_tex, tuv(map_uv, st_spec, rot_a.z)).rgb : vec3(1.0));
            float highlight = pow(clamp(dot(n, h), 0.0, 1.0), shininess) * ndl * (0.15 + 0.85 * glossiness);
            DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndl / PI;
            SPECULAR_LIGHT += sc * LIGHT_COLOR * ATTENUATION * highlight;
        }
        """;

    private const string PlanarLine = "vec3 pp = local_pos * model_scale; vec3 an = abs(local_normal); base_uv = (an.y >= an.x && an.y >= an.z) ? pp.xz : ((an.x >= an.z) ? pp.zy : pp.xy);";

    private static readonly Dictionary<string, Shader> Shaders = [];

    private static Shader ShaderFor(string alphaMode, bool doubleSided, bool unlit, bool planar, bool blinn)
    {
        var key = $"{alphaMode}|{doubleSided}|{unlit}|{planar}|{blinn}";
        if (Shaders.TryGetValue(key, out var cached)) return cached;
        var modes = new List<string>();
        if (alphaMode == "blend") { modes.Add("blend_mix"); modes.Add("depth_draw_always"); }
        if (doubleSided) modes.Add("cull_disabled");
        if (unlit) modes.Add("unshaded");
        var code = ShaderTemplate
            .Replace("//RENDER_MODE", modes.Count == 0 ? "" : "render_mode " + string.Join(", ", modes) + ";")
            .Replace("//PLANAR_LINE", planar ? PlanarLine : "")
            .Replace("//ALPHA_LINE", alphaMode switch { "blend" => "ALPHA = albedo.a * t.a;", "mask" => "ALPHA = albedo.a * t.a; ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;", _ => "" })
            .Replace("//SURFACE", blinn ? BlinnSurface : PbrSurface)
            .Replace("//ENVIRONMENT", blinn ? BlinnEnvironment : "")
            .Replace("//LIGHT_FUNCTION", blinn && !unlit ? BlinnLight : "");
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

    /// True when the layer (the face, or the object) has been cleared of textures: its own flag, or the object's when the face says nothing about textures.
    private static bool Blank(JsonObject layer, JsonObject? over)
    {
        if (over is not null)
        {
            if (over["noTexture"] is JsonValue own && own.TryGetValue<bool>(out var flag)) return flag;
            if (MapKeys.Any(key => over[key] is not null) || over["builtin"] is not null) return false;
        }
        return layer["noTexture"] is JsonValue shared && shared.TryGetValue<bool>(out var blank) && blank;
    }

    private static string? Map(JsonObject layer, JsonObject? over, string key) => Blank(layer, over) ? null : Text(layer, over, key);

    public static string Shading(JsonObject layer, JsonObject? over) => Text(layer, over, "shading") == "blinnphong" ? "blinnphong" : "pbr";

    public static bool IsTextured(JsonObject layer, JsonObject? over) =>
        Shading(layer, over) == "blinnphong" || Map(layer, over, "texture") is not null || Map(layer, over, "builtin") is not null || MapKeys.Skip(1).Any(key => Map(layer, over, key) is not null);

    /// How the alpha channel is used: "none" (opaque), "blend" (see-through) or "mask" (cut out with a cutoff). Without a choice, an object with transparency below 1 blends.
    public static string AlphaMode(JsonObject layer, JsonObject? over)
    {
        var chosen = Text(layer, over, "alphaMode");
        if (chosen is "none" or "blend" or "mask") return chosen;
        var opacity = Number(layer, over, "transparency", 1f);
        return (over?["transparency"] ?? layer["transparency"]) is not null && opacity < .999f ? "blend" : "none";
    }

    /// The repeat, offset and rotation of one map: its own "mapUv" entry where it has one, else the layer's general settings.
    public static (Vector2 Repeat, Vector2 Offset, float RotationDegrees) MapTransform(JsonObject layer, JsonObject? over, string map)
    {
        var entry = ((over?["mapUv"] ?? layer["mapUv"]) as JsonObject)?[map] as JsonObject;
        float Pick(string field, string general, float fallback) => entry?[field] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : Number(layer, over, general, fallback);
        var repeat = new Vector2(Mathf.Max(.01f, Pick("repeatX", "uvRepeatX", 1)) * (Flag(layer, over, "flipU") ? -1 : 1), Mathf.Max(.01f, Pick("repeatY", "uvRepeatY", 1)) * (Flag(layer, over, "flipV") ? -1 : 1));
        return (repeat, new Vector2(Pick("offsetX", "uvOffsetX", 0), Pick("offsetY", "uvOffsetY", 0)), Pick("rotation", "uvRotation", 0));
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
        var blinn = Shading(layer, over) == "blinnphong";
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

        var material = new ShaderMaterial { Shader = ShaderFor(alphaMode, doubleSided, unlit, planar, blinn) };
        material.SetShaderParameter("albedo", new Color(color, alpha));
        material.SetShaderParameter("roughness_value", roughness); material.SetShaderParameter("metallic_value", metallic);
        material.SetShaderParameter("alpha_cutoff", cutoff);
        material.SetShaderParameter("emission_color", emission ?? Colors.Black); material.SetShaderParameter("emission_strength", emission is null ? 0f : strength);
        material.SetShaderParameter("spec_color", ColorOf(Text(layer, over, "specularColor"), Colors.White));
        material.SetShaderParameter("glossiness", Mathf.Clamp(Number(layer, over, "glossiness", .5f), 0, 1)); material.SetShaderParameter("environment", Mathf.Clamp(Number(layer, over, "environment", 0), 0, 1));
        var rotations = new float[5];
        for (var i = 0; i < MapKeys.Length; i++)
        {
            var (repeat, offset, rotation) = MapTransform(layer, over, MapKeys[i]); rotations[i] = Mathf.DegToRad(rotation);
            material.SetShaderParameter(new[] { "st_albedo", "st_normal", "st_spec", "st_orm", "st_emissive" }[i], new Vector4(repeat.X, repeat.Y, offset.X, offset.Y));
        }
        material.SetShaderParameter("rot_a", new Vector4(rotations[0], rotations[1], rotations[2], rotations[3])); material.SetShaderParameter("rot_e", rotations[4]);

        if (Map(layer, over, "builtin") is { } builtin && BuiltInTextures.Get(builtin) is { } pattern) { material.SetShaderParameter("albedo_tex", pattern); material.SetShaderParameter("has_albedo", true); }
        else Request(Map(layer, over, "texture"), "albedo_tex", "has_albedo");
        Request(Map(layer, over, "normalMap"), "normal_tex", "has_normal"); Request(Map(layer, over, "specularMap"), "spec_tex", "has_spec");
        Request(Map(layer, over, "ormMap"), "orm_tex", "has_orm"); Request(Map(layer, over, "emissiveMap"), "emissive_tex", "has_emissive");
        return material;

        void Request(string? id, string texture, string flag)
        {
            if (!Guid.TryParse(id, out var assetId) || cache is null) return;
            cache.Request(assetId, loaded => { if (loaded is null || !GodotObject.IsInstanceValid(material)) return; material.SetShaderParameter(texture, loaded); material.SetShaderParameter(flag, true); });
        }
    }
}
