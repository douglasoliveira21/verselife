using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The graphics quality presets (Baixo, Médio, Alto, Ultra) and "Automático" (picked from the graphics card).
/// A preset sets the anti-aliasing, the screen-space effects (ambient occlusion, indirect light, glow), the tone mapping and the sun's shadows.
/// It is applied to the world's <c>WorldEnvironment</c>, its <c>Sun</c> and the viewport, and kept in <c>user://settings.cfg</c>.
/// </summary>
public static class GraphicsQuality
{
    public enum Level { Auto, VeryLow, Low, Medium, High, Ultra, Custom }
    public static readonly string[] Labels = ["Automático", "Muito baixa", "Baixa", "Média", "Alta", "Ultra", "Personalizada"];
    public static readonly string[] Keys = ["auto", "verylow", "low", "medium", "high", "ultra", "custom"];

    /// <summary>Everything a preset decides. Plain data, so the rules can be tested without a window.</summary>
    public sealed record Preset(
        string Name, int Msaa, bool Fxaa, bool Taa,
        bool Ssao, RenderingServer.EnvironmentSsaoQuality SsaoQuality, bool SsaoHalfSize, bool Ssil,
        bool Glow, float GlowIntensity,
        bool Adjustment, float Contrast, float Saturation,
        bool Shadows, DirectionalLight3D.ShadowMode ShadowMode, float ShadowDistance, RenderingServer.ShadowQuality SoftShadows, int ShadowAtlas, float ShadowBlur);

    public static readonly Preset VeryLowPreset = new("Muito baixa", 0, false, false, false, RenderingServer.EnvironmentSsaoQuality.VeryLow, true, false, false, 0f, false, 1f, 1f,
        false, DirectionalLight3D.ShadowMode.Orthogonal, 30f, RenderingServer.ShadowQuality.Hard, 1024, 1f);
    public static readonly Preset LowPreset = new("Baixo", 0, false, false, false, RenderingServer.EnvironmentSsaoQuality.VeryLow, true, false, false, 0f, false, 1f, 1f,
        true, DirectionalLight3D.ShadowMode.Parallel2Splits, 55f, RenderingServer.ShadowQuality.Hard, 2048, 1f);
    public static readonly Preset MediumPreset = new("Médio", 2, false, false, true, RenderingServer.EnvironmentSsaoQuality.Low, true, false, true, .35f, true, 1.04f, 1.06f,
        true, DirectionalLight3D.ShadowMode.Parallel2Splits, 90f, RenderingServer.ShadowQuality.SoftLow, 2048, 1f);
    public static readonly Preset HighPreset = new("Alto", 4, false, false, true, RenderingServer.EnvironmentSsaoQuality.Medium, false, false, true, .5f, true, 1.06f, 1.1f,
        true, DirectionalLight3D.ShadowMode.Parallel4Splits, 140f, RenderingServer.ShadowQuality.SoftMedium, 4096, 1.2f);
    public static readonly Preset UltraPreset = new("Ultra", 4, false, false, true, RenderingServer.EnvironmentSsaoQuality.High, false, true, true, .6f, true, 1.08f, 1.12f,
        true, DirectionalLight3D.ShadowMode.Parallel4Splits, 240f, RenderingServer.ShadowQuality.SoftHigh, 4096, 1.5f);

    public static Preset PresetOf(Level level) => level switch { Level.VeryLow => VeryLowPreset, Level.Low => LowPreset, Level.Medium => MediumPreset, Level.High => HighPreset, Level.Ultra => UltraPreset, _ => HighPreset };

    /// <summary>"Automático": a discrete graphics card gets Alto, an integrated one Baixo, anything else Médio.</summary>
    public static Level Resolve(Level chosen, RenderingDevice.DeviceType? adapter = null)
    {
        if (chosen == Level.Custom) return LastConcrete;
        if (chosen != Level.Auto) return chosen;
        var type = adapter ?? RenderingServer.GetVideoAdapterType();
        return type switch { RenderingDevice.DeviceType.DiscreteGpu => Level.High, RenderingDevice.DeviceType.IntegratedGpu => Level.Low, _ => Level.Medium };
    }

    public static Level Parse(string? key) { var at = Array.IndexOf(Keys, (key ?? "").ToLowerInvariant()); return at < 0 ? Level.Auto : (Level)at; }

    /// <summary>The level kept in the settings file (Automático when none was chosen).</summary>
    public static Level Chosen { get; set; } = Level.Auto;
    /// <summary>Raised when the quality changes, so the world applies it.</summary>
    public static event Action? Changed;
    private static Level _lastConcrete = Level.High;
    /// <summary>The level that "Personalizada" is built on (the one in use before the person changed an option by hand).</summary>
    public static Level LastConcrete { get => _lastConcrete; private set => _lastConcrete = value; }
    /// <summary>The sun shadow distance the person chose, over the preset's (null: the preset decides).</summary>
    public static float? ShadowDistanceOverride { get; set; }
    /// <summary>Applies the current quality again (a value that feeds it changed).</summary>
    public static void Reapply() => Changed?.Invoke();
    public static void Choose(Level level) { if (level != Level.Custom) LastConcrete = Resolve(level); Chosen = level; Changed?.Invoke(); }

    public static Level Current => Resolve(Chosen);

    /// <summary>Applies the current quality to the viewport, the environment and the sun (either may be missing).</summary>
    public static void Apply(Viewport viewport, Godot.Environment? environment, DirectionalLight3D? sun) => Apply(PresetOf(Current), viewport, environment, sun);

    /// <summary>Values the person set by hand (a Personalizada preset): each one, when not null, replaces what the preset gives. Settings that no preset controls (textures, reflections, fog...) are always applied.</summary>
    public sealed record Tweaks
    {
        public int? Msaa { get; init; }
        public bool? Ssao { get; init; }
        public RenderingServer.EnvironmentSsaoQuality? SsaoQuality { get; init; }
        public bool? Ssil { get; init; }
        public bool? Glow { get; init; }
        public bool? Adjustment { get; init; }
        public bool? Shadows { get; init; }
        public RenderingServer.ShadowQuality? SoftShadows { get; init; }
        public DirectionalLight3D.ShadowMode? ShadowMode { get; init; }
    }

    public static Tweaks Tweak { get; set; } = new();
    /// <summary>Always-on extras no preset decides: texture detail (mipmap bias), anisotropic filtering, screen-space reflections, volumetric fog, and whether weather fog is drawn.</summary>
    public static float TextureMipmapBias { get; set; }
    public static Viewport.AnisotropicFiltering Anisotropic { get; set; } = Viewport.AnisotropicFiltering.Anisotropy2X;
    public static bool Reflections { get; set; }
    public static bool VolumetricFog { get; set; }
    public static bool WeatherFog { get; set; } = true;

    public static Preset WithTweaks(Preset p) => p with
    {
        Msaa = Tweak.Msaa ?? p.Msaa, Ssao = Tweak.Ssao ?? p.Ssao, SsaoQuality = Tweak.SsaoQuality ?? p.SsaoQuality, Ssil = Tweak.Ssil ?? p.Ssil, Glow = Tweak.Glow ?? p.Glow,
        Adjustment = Tweak.Adjustment ?? p.Adjustment, Shadows = Tweak.Shadows ?? p.Shadows, SoftShadows = Tweak.SoftShadows ?? p.SoftShadows, ShadowMode = Tweak.ShadowMode ?? p.ShadowMode
    };

    public static void Apply(Preset p, Viewport viewport, Godot.Environment? environment, DirectionalLight3D? sun)
    {
        var named = p; p = WithTweaks(p);
        viewport.AnisotropicFilteringLevel = Anisotropic; viewport.TextureMipmapBias = TextureMipmapBias;
        // anti-aliasing: MSAA only. FXAA and TAA smear anything drawn as a moving flat sprite, such as the name over an avatar, so no preset uses them
        viewport.Msaa3D = p.Msaa switch { 2 => Viewport.Msaa.Msaa2X, 4 => Viewport.Msaa.Msaa4X, 8 => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Disabled };
        viewport.ScreenSpaceAA = p.Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = p.Taa;
        viewport.UseDebanding = named != LowPreset && named != VeryLowPreset;               // removes the bands in the sky gradient
        RenderingServer.EnvironmentSetSsaoQuality(p.SsaoQuality, p.SsaoHalfSize, .5f, 2, .7f, 50f);
        RenderingServer.DirectionalShadowAtlasSetSize(p.ShadowAtlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(p.SoftShadows);
        if (environment is not null)
        {
            environment.SsaoEnabled = p.Ssao; environment.SsaoRadius = 1.6f; environment.SsaoIntensity = 1.6f; environment.SsaoPower = 1.5f; environment.SsaoDetail = .5f;
            environment.SsilEnabled = p.Ssil; environment.SsilRadius = 4f; environment.SsilIntensity = .8f;
            environment.GlowEnabled = p.Glow; environment.GlowIntensity = p.GlowIntensity; environment.GlowBloom = .02f; environment.GlowHdrThreshold = 1.1f; environment.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight;
            environment.TonemapMode = Godot.Environment.ToneMapper.Filmic; environment.TonemapExposure = 1f; environment.TonemapWhite = 6f;
            environment.AdjustmentEnabled = p.Adjustment; environment.AdjustmentContrast = p.Contrast; environment.AdjustmentSaturation = p.Saturation;
            environment.SsrEnabled = Reflections; environment.SsrMaxSteps = 64; environment.SsrFadeIn = .15f; environment.SsrFadeOut = 2f;
            environment.VolumetricFogEnabled = VolumetricFog; environment.VolumetricFogDensity = .004f; environment.VolumetricFogAlbedo = new Color(.9f, .93f, 1f); environment.VolumetricFogLength = 120f;
        }
        if (sun is not null)
        {
            sun.ShadowEnabled = p.Shadows; sun.DirectionalShadowMode = p.ShadowMode; sun.DirectionalShadowMaxDistance = ShadowDistanceOverride ?? p.ShadowDistance;
            sun.DirectionalShadowBlendSplits = true; sun.ShadowBlur = p.ShadowBlur; sun.DirectionalShadowFadeStart = .85f; sun.ShadowNormalBias = 1.2f;
        }
    }
}

/// <summary>How many particles the world spends (Configurações › Gráficos › Qualidade): the rain and the emitters of objects. 1 is everything, 0 none.</summary>
public static class ParticleQuality
{
    public static float Factor { get; set; } = 1f;
}
