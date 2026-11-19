using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// The four graphics presets (Baixo, Médio, Alto, Ultra), applied to the engine's quality settings: how far models keep their detail (<c>lodBias</c>), how sharp ground and
    /// walls are seen at a slant (anisotropic filtering), how large the textures are (the top mip level is skipped on Baixo), and the shadow range of the sun. Plus the sync and the
    /// frame limit. The terrain, sea and weather read <see cref="Current"/> when they draw (rain particles, shadows). The preset is chosen in the settings window and saved with
    /// <see cref="UserSettings"/>; nothing here needs the window.
    /// </summary>
    public static class GraphicsQuality
    {
        public sealed class Preset
        {
            public string Name; public float LodBias; public AnisotropicFiltering Anisotropic; public int MipmapLimit; public float ShadowDistance; public float RainAmount; public bool Shadows;
        }

        public static readonly Preset[] Presets =
        {
            new Preset { Name = "Baixo", LodBias = .6f, Anisotropic = AnisotropicFiltering.Disable, MipmapLimit = 1, ShadowDistance = 0f, RainAmount = .4f, Shadows = false },
            new Preset { Name = "Médio", LodBias = .85f, Anisotropic = AnisotropicFiltering.Enable, MipmapLimit = 0, ShadowDistance = 60f, RainAmount = .7f, Shadows = true },
            new Preset { Name = "Alto", LodBias = 1.1f, Anisotropic = AnisotropicFiltering.Enable, MipmapLimit = 0, ShadowDistance = 120f, RainAmount = 1f, Shadows = true },
            new Preset { Name = "Ultra", LodBias = 1.6f, Anisotropic = AnisotropicFiltering.ForceEnable, MipmapLimit = 0, ShadowDistance = 250f, RainAmount = 1.2f, Shadows = true },
        };

        public static Preset Current { get; private set; } = Presets[2];

        public static Preset Of(int index) => Presets[Mathf.Clamp(index, 0, Presets.Length - 1)];

        /// <summary>Puts a preset, the sync and the frame limit on the engine now.</summary>
        public static void Apply(UserSettings settings)
        {
            var preset = Of(settings.GraphicsPreset); Current = preset;
            QualitySettings.lodBias = preset.LodBias; QualitySettings.anisotropicFiltering = preset.Anisotropic; QualitySettings.globalTextureMipmapLimit = preset.MipmapLimit;
            QualitySettings.vSyncCount = settings.VSync ? 1 : 0;
            Application.targetFrameRate = settings.FpsLimit > 0 && !settings.VSync ? settings.FpsLimit : -1;
        }
    }
}
