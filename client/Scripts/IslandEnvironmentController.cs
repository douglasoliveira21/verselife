using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public partial class IslandEnvironmentController : Node
{
    private const string SkyShader = @"
shader_type sky;
uniform vec3 zenith_day : source_color = vec3(0.17, 0.42, 0.84);
uniform vec3 horizon_day : source_color = vec3(0.74, 0.86, 0.97);
uniform vec3 zenith_night : source_color = vec3(0.004, 0.01, 0.045);
uniform vec3 horizon_night : source_color = vec3(0.03, 0.045, 0.1);
uniform vec3 sunset_color : source_color = vec3(1.0, 0.52, 0.22);
uniform sampler2D cloud_noise : filter_linear, repeat_enable;
uniform float day = 1.0;
uniform float cloud_cover = 0.52;
uniform float cloud_speed = 0.004;
void sky() {
    vec3 dir = normalize(EYEDIR);
    vec3 sun_dir = normalize(LIGHT0_DIRECTION);
    float h = clamp(dir.y, 0.0, 1.0);
    vec3 zenith = mix(zenith_night, zenith_day, day);
    vec3 horizon = mix(horizon_night, horizon_day, day);
    vec3 col = mix(horizon, zenith, pow(h, 0.5));
    float toward_sun = max(dot(dir, sun_dir), 0.0);
    float low_sun = 1.0 - clamp(abs(sun_dir.y) * 3.0, 0.0, 1.0);
    col += sunset_color * pow(toward_sun, 5.0) * low_sun * 0.65 * (1.0 - h);
    float above = smoothstep(-0.04, 0.02, sun_dir.y);
    col += vec3(1.0, 0.96, 0.82) * (smoothstep(0.9994, 0.9998, toward_sun) * 4.0 + pow(toward_sun, 80.0) * 0.5) * above;
    if (dir.y < 0.0) col = mix(horizon, horizon * 0.8, clamp(-dir.y * 5.0, 0.0, 1.0));
    if (dir.y > 0.005) {
        vec2 uv = dir.xz / (dir.y + 0.14) * 0.32 + vec2(TIME * cloud_speed, TIME * cloud_speed * 0.35);
        float n = texture(cloud_noise, uv).r * 0.7 + texture(cloud_noise, uv * 2.7).r * 0.3;
        float c = smoothstep(1.0 - cloud_cover, 1.0 - cloud_cover + 0.24, n);
        float fade = smoothstep(0.005, 0.2, dir.y);
        vec3 lit = mix(vec3(0.1, 0.11, 0.16), vec3(1.0), day) * mix(0.82, 1.0, 1.0 - n);
        col = mix(col, lit, c * fade * 0.93);
    }
    COLOR = col;
}";

    [Export] public bool DayNightCycle { get; set; } = true;
    [Export] public float TimeScale { get; set; } = .02f;
    [Export] public float Hour { get; set; } = 12f;
    [Export] public string Weather { get; set; } = "clear";
    [Export] public float WeatherBlend { get; set; } = 1f;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _environment = null!;
    private ShaderMaterial? _sky;

    public override void _Ready()
    {
        _sun = GetParent().GetNodeOrNull<DirectionalLight3D>("Sun")!; _environment = GetParent().GetNodeOrNull<WorldEnvironment>("WorldEnvironment")!;
        if (float.TryParse(OS.GetEnvironment("LIFEVERSE_ISLAND_HOUR"), out var hour)) Hour = Mathf.PosMod(hour, 24); var configuredWeather = OS.GetEnvironment("LIFEVERSE_WEATHER"); if (!string.IsNullOrWhiteSpace(configuredWeather)) Weather = configuredWeather.ToLowerInvariant();
        BuildSky(); UpdateSun(); ApplyWeather();
    }
    public override void _Process(double delta)
    {
        if (DayNightCycle) Hour = Mathf.PosMod(Hour + (float)delta * TimeScale, 24);
        UpdateSun(); ApplyWeather();
    }

    private void BuildSky()
    {
        if (_environment?.Environment is not { } env) return;
        var noise = new FastNoiseLite { Seed = 5, Frequency = .012f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 5 };
        _sky = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
        _sky.SetShaderParameter("cloud_noise", new NoiseTexture2D { Noise = noise, Width = 512, Height = 512, Seamless = true });
        env.Sky = new Sky { SkyMaterial = _sky };
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
    }

    /// Hour 6 is sunrise, 12 is noon, 18 is sunset; the light is switched off while the sun is below the horizon.
    private void UpdateSun()
    {
        var elevation = Mathf.Sin((Hour - 6f) / 12f * Mathf.Pi);
        var day = Mathf.SmoothStep(-.08f, .3f, elevation);
        if (_sun is not null)
        {
            _sun.RotationDegrees = new Vector3(-(Hour - 6f) / 12f * 180f, -28f, 0);
            _sun.LightEnergy = day * 1.1f;
            _sun.Visible = elevation > -.05f;
        }
        _sky?.SetShaderParameter("day", day);
        if (_environment?.Environment is { } env) env.AmbientLightEnergy = Mathf.Lerp(.18f, .75f, day);
    }

    private void ApplyWeather() { if (_environment?.Environment is not { } env) return; var targetDensity = Weather switch { "fog" => .045f, "rain" => .012f, "storm" => .025f, "cloudy" => .006f, _ => 0f }; env.FogEnabled = targetDensity > 0; env.FogDensity = Mathf.Lerp(env.FogDensity, targetDensity, Mathf.Clamp((float)GetProcessDeltaTime() * 2f, 0f, 1f)); env.FogLightColor = Weather is "storm" ? new Color("#46506a") : new Color("#9fb5c9"); _sky?.SetShaderParameter("cloud_cover", Weather switch { "cloudy" => .72f, "rain" => .85f, "storm" => .95f, "fog" => .8f, _ => .5f }); }
}
