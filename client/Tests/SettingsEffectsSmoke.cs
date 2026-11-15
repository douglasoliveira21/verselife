using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>Stage 2 of the settings menu: every setting of the Qualidade, Mundo e avatares and Otimização tabs really changes the game (a store of its own, the real appliers, the real world).</summary>
public partial class SettingsEffectsSmoke : Node
{
    private const string Root = "user://smoke_settings_effects";
    private static void Wipe() { var absolute = ProjectSettings.GlobalizePath(Root); if (!DirAccess.DirExistsAbsolute(absolute)) return; foreach (var file in DirAccess.GetFilesAt(absolute)) DirAccess.RemoveAbsolute($"{absolute}/{file}"); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    public override async void _Ready()
    {
        try
        {
            Wipe();
            // ---- the presets give every option a value the option accepts
            foreach (var level in new[] { "verylow", "low", "medium", "high", "ultra" })
                foreach (var (id, value) in SettingsCatalog.Presets.Presets[level])
                    Require(SettingsCatalog.Find(id)!.Normalize(value).Equals(value), $"preset {level}: {id}={value} is a valid value");
            foreach (var d in SettingsCatalog.All.Where(d => d.PresetControlled))
                Require(SettingsCatalog.Presets.Presets["high"].ContainsKey(d.Id), $"{d.Id} is controlled by the presets and every preset gives it a value");
            Require(AutoQuality.Below(GraphicsQuality.Level.Ultra) == GraphicsQuality.Level.High && AutoQuality.Below(GraphicsQuality.Level.VeryLow) is null && AutoQuality.IsBad(30, 60) && !AutoQuality.IsBad(50, 60), "auto quality steps down one level at a time and only for a bad frame rate");

            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            await Frames(20);
            var env = world.GetNode<WorldEnvironment>("WorldEnvironment").Environment; var sun = world.GetNode<DirectionalLight3D>("Sun"); var viewport = GetViewport();
            var store = new SettingsStore(Root, SettingsCatalog.All, SettingsCatalog.Presets); SettingsAppliers.Register(store);

            // ---- Personalizada: each option is used as set
            store.Set("graphics.preset", "custom"); store.Set("graphics.msaa", "8"); store.Set("graphics.ssao", false); store.Set("graphics.ssil", true); store.Set("graphics.glow", false); store.Set("graphics.color_adjust", false);
            store.Set("graphics.shadows_enabled", false); store.Set("graphics.shadow_cascades", "2"); store.Set("graphics.shadow_distance", 75); store.Set("graphics.shadow_quality", "hard");
            store.Apply(); await Frames(3);
            Require(viewport.Msaa3D == Viewport.Msaa.Msaa8X && !env.SsaoEnabled && env.SsilEnabled && !env.GlowEnabled && !env.AdjustmentEnabled, "custom: antialiasing, ambient occlusion, indirect light, glow and color follow the options");
            Require(!sun.ShadowEnabled && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel2Splits && Mathf.IsEqualApprox(sun.DirectionalShadowMaxDistance, 75f), "custom: the sun's shadows follow the options");
            store.Set("graphics.preset", "low"); store.Apply(); await Frames(3);
            Require(viewport.Msaa3D == Viewport.Msaa.Disabled && !env.SsaoEnabled && !env.GlowEnabled && Mathf.IsEqualApprox(sun.DirectionalShadowMaxDistance, 55f) && sun.ShadowEnabled, "a preset decides by itself again (Baixa)");
            store.Set("graphics.preset", "ultra"); store.Apply(); await Frames(3);
            Require(viewport.Msaa3D == Viewport.Msaa.Msaa4X && env.SsaoEnabled && env.SsilEnabled && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel4Splits && Mathf.IsEqualApprox(sun.DirectionalShadowMaxDistance, 240f), "and Ultra gives its own values");
            Require((string)store.Value("graphics.msaa") == "4" && (bool)store.Value("graphics.ssil"), "the options show what the preset gave them");

            // ---- options no preset controls
            store.Set("graphics.texture_quality", "low"); store.Set("graphics.anisotropic", "16"); store.Set("graphics.ssr", true); store.Set("graphics.volumetric", true); store.Set("graphics.fog", false); store.Apply(); await Frames(3);
            Require(Mathf.IsEqualApprox(viewport.TextureMipmapBias, 2f) && viewport.AnisotropicFilteringLevel == Viewport.AnisotropicFiltering.Anisotropy16X, "texture detail and anisotropic filtering reach the viewport");
            Require(env.SsrEnabled && env.VolumetricFogEnabled && !GraphicsQuality.WeatherFog, "reflections, volumetric fog and the weather fog switch");
            world.GetNode<IslandEnvironmentController>("EnvironmentController").Weather = "fog"; await Frames(30);
            Require(!env.FogEnabled || env.FogDensity < .001f, "with the weather fog off, a foggy day draws no fog");
            store.Set("graphics.fog", true); store.Apply(); await Frames(60);
            Require(env.FogEnabled && env.FogDensity > .001f, "and with it on the fog is back");
            world.GetNode<IslandEnvironmentController>("EnvironmentController").Weather = "clear";
            store.Set("graphics.particles", "off"); store.Set("graphics.water_waves", false); store.Apply();
            Require(ParticleQuality.Factor == 0f && !OceanSurface.WavesEnabled, "particles and waves switch");
            store.Set("graphics.particles", "low"); store.Apply(); Require(Mathf.IsEqualApprox(ParticleQuality.Factor, .3f), "particles scale");

            // ---- world: lights and the distance of objects
            var lights = new List<BuildObjectView.ObjectLight>();
            for (var i = 0; i < 10; i++) { var light = new BuildObjectView.ObjectLight { WantsShadow = true }; world.AddChild(light); lights.Add(light); }
            await Frames(2);
            Require(BuildObjectView.ObjectLight.OnForTesting == 10 && BuildObjectView.ObjectLight.ShadowsForTesting == 4, "by default the first 16 lights are on and 4 cast shadows");
            store.Set("world.max_lights", 6); store.Set("world.max_light_shadows", 1); store.Apply(); await Frames(2);
            Require(BuildObjectView.ObjectLight.OnForTesting == 6 && BuildObjectView.ObjectLight.ShadowsForTesting == 1 && lights.Take(6).All(l => l.Visible) && lights.Skip(6).All(l => !l.Visible), "the light limits take effect at once on the lights that exist");
            store.Set("world.max_lights", 0); store.Apply(); Require(BuildObjectView.ObjectLight.OnForTesting == 0, "zero lights turns them all off");
            foreach (var light in lights) light.QueueFree(); await Frames(2);
            Require(BuildObjectView.ObjectLight.ActiveForTesting == 0, "lights that go away leave the list");

            var near = new Node3D { Position = new Vector3(10, 0, 0) }; var far = new Node3D { Position = new Vector3(400, 0, 0) }; world.AddChild(near); world.AddChild(far);
            store.Set("world.object_distance", 200); store.Apply();
            ObjectCuller.Update(Vector3.Zero, [near, far]);
            Require(near.Visible && !far.Visible && ObjectCuller.HiddenCount == 1, "objects beyond the distance are not drawn");
            ObjectCuller.Update(new Vector3(390, 0, 0), [near, far]);
            Require(!near.Visible && far.Visible, "and come back when the person gets near");
            store.Set("world.object_distance", 1000); store.Apply(); ObjectCuller.Update(Vector3.Zero, [near, far]);
            Require(near.Visible && far.Visible && ObjectCuller.HiddenCount == 0, "the maximum means no limit and everything hidden is shown");

            // ---- avatars
            var network = GetNode<NetworkManager>("/root/NetworkManager");
            store.Set("avatars.max_full", 3); store.Set("avatars.lod", "low"); store.Set("avatars.simplify_distant", false); store.Set("avatars.visibility_distance", 80); store.Set("avatars.triangle_budget", 100); store.Apply();
            var budget = network.AvatarBudget;
            Require(budget.MaxFull == 3 && budget.FullRange < new DetailBudget().FullRange && budget.MaxReduced == 0 && budget.BasicRange >= 80f && budget.Triangles == 100_000, "the avatar settings reach the detail budget of the island");
            store.Set("avatars.animation_quality", "high"); store.Apply(); Require(AvatarRig.AnimationDivisor == 1, "animation quality sets how often reduced avatars animate");
            store.Set("avatars.animation_quality", "low"); store.Apply(); Require(AvatarRig.AnimationDivisor == 3, "...and lower makes it rarer");

            // ---- automatic adjustment and the rest
            store.Set("perf.auto_quality", true); store.Apply(); Require(AutoQuality.Enabled, "auto quality is switched by its setting");
            store.Set("perf.auto_quality", false); store.Apply(); Require(!AutoQuality.Enabled, "...and off again");
            Require(SettingsCatalog.All.Where(d => !d.IsAvailable).All(d => d.Unavailable!.Length > 25), "everything unavailable says why");
            store.ResetAll(); store.Apply(); await Frames(5);

            Wipe();
            GD.Print("event=settings_effects_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=settings_effects_smoke result=failed error={exception.Message} {exception.StackTrace}"); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
