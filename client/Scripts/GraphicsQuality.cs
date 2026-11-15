using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The graphics quality presets (Baixo, Médio, Alto, Ultra) and "Automático" (picked from the graphics card).
/// A preset sets the anti-aliasing, the screen-space effects (ambient occlusion, indirect light, glow), the tone mapping and the sun's shadows.
/// It is applied to the world's <c>WorldEnvironment</c>, its <c>Sun</c> and the viewport, and kept in <c>user://settings.cfg</c>.
/// </summary>
public static class GraphicsQuality
{
    public enum Level { Auto, Low, Medium, High, Ultra }
    public static readonly string[] Labels = ["Automático", "Baixo", "Médio", "Alto", "Ultra"];
    public static readonly string[] Keys = ["auto", "low", "medium", "high", "ultra"];

    /// <summary>Everything a preset decides. Plain data, so the rules can be tested without a window.</summary>
    public sealed record Preset(
        string Name, int Msaa, bool Fxaa, bool Taa,
        bool Ssao, RenderingServer.EnvironmentSsaoQuality SsaoQuality, bool SsaoHalfSize, bool Ssil,
        bool Glow, float GlowIntensity,
        bool Adjustment, float Contrast, float Saturation,
        bool Shadows, DirectionalLight3D.ShadowMode ShadowMode, float ShadowDistance, RenderingServer.ShadowQuality SoftShadows, int ShadowAtlas, float ShadowBlur);

    public static readonly Preset LowPreset = new("Baixo", 0, false, false, false, RenderingServer.EnvironmentSsaoQuality.VeryLow, true, false, false, 0f, false, 1f, 1f,
        true, DirectionalLight3D.ShadowMode.Parallel2Splits, 55f, RenderingServer.ShadowQuality.Hard, 2048, 1f);
    public static readonly Preset MediumPreset = new("Médio", 0, true, false, true, RenderingServer.EnvironmentSsaoQuality.Low, true, false, true, .35f, true, 1.04f, 1.06f,
        true, DirectionalLight3D.ShadowMode.Parallel2Splits, 90f, RenderingServer.ShadowQuality.SoftLow, 2048, 1f);
    public static readonly Preset HighPreset = new("Alto", 2, false, true, true, RenderingServer.EnvironmentSsaoQuality.Medium, false, false, true, .5f, true, 1.06f, 1.1f,
        true, DirectionalLight3D.ShadowMode.Parallel4Splits, 140f, RenderingServer.ShadowQuality.SoftMedium, 4096, 1.2f);
    public static readonly Preset UltraPreset = new("Ultra", 4, false, true, true, RenderingServer.EnvironmentSsaoQuality.High, false, true, true, .6f, true, 1.08f, 1.12f,
        true, DirectionalLight3D.ShadowMode.Parallel4Splits, 240f, RenderingServer.ShadowQuality.SoftHigh, 4096, 1.5f);

    public static Preset PresetOf(Level level) => level switch { Level.Low => LowPreset, Level.Medium => MediumPreset, Level.High => HighPreset, Level.Ultra => UltraPreset, _ => HighPreset };

    /// <summary>"Automático": a discrete graphics card gets Alto, an integrated one Baixo, anything else Médio.</summary>
    public static Level Resolve(Level chosen, RenderingDevice.DeviceType? adapter = null)
    {
        if (chosen != Level.Auto) return chosen;
        var type = adapter ?? RenderingServer.GetVideoAdapterType();
        return type switch { RenderingDevice.DeviceType.DiscreteGpu => Level.High, RenderingDevice.DeviceType.IntegratedGpu => Level.Low, _ => Level.Medium };
    }

    public static Level Parse(string? key) { var at = Array.IndexOf(Keys, (key ?? "").ToLowerInvariant()); return at < 0 ? Level.Auto : (Level)at; }

    /// <summary>The level kept in the settings file (Automático when none was chosen).</summary>
    public static Level Chosen { get; set; } = Level.Auto;
    /// <summary>Raised when the quality changes, so the world applies it.</summary>
    public static event Action? Changed;
    public static void Choose(Level level) { Chosen = level; Changed?.Invoke(); }

    public static Level Current => Resolve(Chosen);

    /// <summary>Applies the current quality to the viewport, the environment and the sun (either may be missing).</summary>
    public static void Apply(Viewport viewport, Godot.Environment? environment, DirectionalLight3D? sun) => Apply(PresetOf(Current), viewport, environment, sun);

    public static void Apply(Preset p, Viewport viewport, Godot.Environment? environment, DirectionalLight3D? sun)
    {
        // anti-aliasing: MSAA for the geometry edges, FXAA when MSAA is off, TAA for the shimmer on thin things
        viewport.Msaa3D = p.Msaa switch { 2 => Viewport.Msaa.Msaa2X, 4 => Viewport.Msaa.Msaa4X, 8 => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Disabled };
        viewport.ScreenSpaceAA = p.Fxaa ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        viewport.UseTaa = p.Taa;
        viewport.UseDebanding = p != LowPreset;               // removes the bands in the sky gradient
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
        }
        if (sun is not null)
        {
            sun.ShadowEnabled = p.Shadows; sun.DirectionalShadowMode = p.ShadowMode; sun.DirectionalShadowMaxDistance = p.ShadowDistance;
            sun.DirectionalShadowBlendSplits = true; sun.ShadowBlur = p.ShadowBlur; sun.DirectionalShadowFadeStart = .85f; sun.ShadowNormalBias = 1.2f;
        }
    }
}
