using Godot;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// Open ocean around the island: a large animated water plane that follows the camera so it reaches the
/// horizon, with depth-based colour, shoreline foam, sun glints and an underwater tint. Swimming physics
/// live in PlayerController and AuthoritativeMovement; this node is visual only.
public partial class OceanSurface : Node3D
{
    private const float PlaneSize = 2600f;
    private const float GridStep = PlaneSize / 260f;
    private const string ShaderSource = @"
shader_type spatial;
render_mode cull_disabled, depth_draw_always, specular_schlick_ggx;
uniform sampler2D normal_a : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_b : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D foam_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec3 shallow_color : source_color = vec3(0.05, 0.50, 0.58);
uniform vec3 deep_color : source_color = vec3(0.005, 0.07, 0.22);
uniform vec3 horizon_color : source_color = vec3(0.74, 0.86, 0.97);
uniform float day = 1.0;
uniform float wave_height = 0.22;
uniform float clarity = 4.0;
varying vec3 wpos;
float wave(vec2 p, float t) {
    return sin(p.x * 0.11 + t * 0.9) * 0.5 + sin(p.y * 0.17 - t * 1.1) * 0.35 + sin((p.x + p.y) * 0.07 + t * 0.6) * 0.55 + sin((p.x - p.y) * 0.23 + t * 1.4) * 0.15;
}
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    VERTEX.y += wave(wpos.xz, TIME) * wave_height;
}
void fragment() {
    vec2 uv1 = wpos.xz * 0.035 + vec2(TIME * 0.018, TIME * 0.011);
    vec2 uv2 = wpos.xz * 0.12 + vec2(-TIME * 0.014, TIME * 0.02);
    vec2 uv3 = wpos.xz * 0.5 + vec2(TIME * 0.05, -TIME * 0.03);
    NORMAL_MAP = normalize(texture(normal_a, uv1).rgb + texture(normal_b, uv2).rgb * 0.8 + texture(normal_b, uv3).rgb * 0.35 - vec3(1.0, 1.0, 0.0));
    NORMAL_MAP = NORMAL_MAP * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 1.2;

    float raw = texture(depth_texture, SCREEN_UV).r;
    vec4 view = INV_PROJECTION_MATRIX * vec4(vec3(SCREEN_UV * 2.0 - 1.0, raw), 1.0);
    view.xyz /= view.w;
    float depth_diff = max(-view.z + VERTEX.z, 0.0);

    float depth01 = 1.0 - exp(-depth_diff / clarity);
    vec3 col = mix(shallow_color, deep_color, sqrt(depth01));
    float fresnel = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 4.0);
    col = mix(col, horizon_color * 0.85, fresnel * 0.28);

    float foam_noise_value = texture(foam_noise, wpos.xz * 0.09 + vec2(TIME * 0.02, 0.0)).r;
    float foam = (1.0 - smoothstep(0.0, 1.4 + foam_noise_value * 1.2, depth_diff)) * smoothstep(0.25, 0.75, foam_noise_value + 0.35);
    foam += (1.0 - smoothstep(0.0, 0.25, depth_diff)) * 0.6;
    col = mix(col, vec3(0.95, 0.98, 1.0), clamp(foam, 0.0, 1.0));

    float dist = length(wpos.xz - CAMERA_POSITION_WORLD.xz);
    col = mix(col, horizon_color, smoothstep(500.0, 1600.0, dist) * 0.85);
    col *= mix(0.07, 1.0, day);

    ALBEDO = col;
    ALPHA = clamp(mix(0.55, 0.98, depth01) + foam, 0.0, 1.0) * (1.0 - smoothstep(1100.0, 1290.0, dist));
    ROUGHNESS = mix(0.09, 0.4, clamp(foam, 0.0, 1.0));
    SPECULAR = 0.55;
    METALLIC = 0.0;
}";

    private MeshInstance3D _plane = null!;
    private ShaderMaterial _material = null!;
    private ColorRect _underwater = null!;
    private IslandEnvironmentController? _environment;
    private IslandTerrain? _terrain;
    private float Sea => _terrain?.Map.SeaLevel ?? TerrainMap.DefaultSeaLevel;
    public bool IsUnderwater => _underwater.Visible;

    public override void _Ready()
    {
        var normal = new FastNoiseLite { Seed = 3, Frequency = .02f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4 };
        var normalDetail = new FastNoiseLite { Seed = 9, Frequency = .045f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3 };
        var foam = new FastNoiseLite { Seed = 17, Frequency = .03f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 3 };
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderSource } };
        _material.SetShaderParameter("normal_a", new NoiseTexture2D { Noise = normal, Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = 7f });
        _material.SetShaderParameter("normal_b", new NoiseTexture2D { Noise = normalDetail, Width = 512, Height = 512, Seamless = true, AsNormalMap = true, BumpStrength = 5f });
        _material.SetShaderParameter("foam_noise", new NoiseTexture2D { Noise = foam, Width = 256, Height = 256, Seamless = true });
        _plane = new MeshInstance3D
        {
            Name = "OceanPlane",
            Mesh = new PlaneMesh { Size = new Vector2(PlaneSize, PlaneSize), SubdivideWidth = 259, SubdivideDepth = 259 },
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16f
        };
        AddChild(_plane);


        var layer = new CanvasLayer { Layer = -1 };
        _underwater = new ColorRect { Color = new Color(.02f, .26f, .4f, .5f), Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _underwater.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_underwater);
        AddChild(layer);
        _environment = GetParent().GetNodeOrNull<IslandEnvironmentController>("EnvironmentController");
        _terrain = GetParent().GetNodeOrNull<IslandTerrain>("IslandTerrain");
    }

    public override void _Process(double delta)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;
        // Waves are computed from world position, so snapping the plane to the grid is invisible.
        var position = camera.GlobalPosition;
        _plane.GlobalPosition = new Vector3(Mathf.Snapped(position.X, GridStep), Sea, Mathf.Snapped(position.Z, GridStep));
        _material.SetShaderParameter("day", _environment?.DayFactor ?? 1f);
        var under = position.Y < Sea;
        _underwater.Visible = under;
        if (under) _underwater.Color = new Color(.02f, .26f, .4f, .5f).Lerp(new Color(0, .02f, .06f, .5f), 1f - (_environment?.DayFactor ?? 1f));
    }
}
