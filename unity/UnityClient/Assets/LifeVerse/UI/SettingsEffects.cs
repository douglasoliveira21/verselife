using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LifeVerse.Shared.Camera;
using LifeVerse.Unity.Avatar;
using UnityEngine;

namespace LifeVerse.Unity.UI
{
    /// <summary>
    /// What the settings of the catalog do in the Unity client: the camera (lens, zoom, height, shoulder, follow speed, collision, first person comfort), the graphics beyond the preset (shadows, anisotropy, texture
    /// detail, particles, view distance), the toasts, the build grid, the footsteps, the inventory order, the performance numbers, the screen (mode, resolution, monitor), the background behaviour, and the values
    /// shown in Geral, Rede and Avançado. Kept apart from the window so it is applied the same way at start, after a change, and after a profile is loaded.
    /// </summary>
    public static class SettingsEffects
    {
        public static readonly (string Id, string Label)[] SortOrder = { ("", ""), ("name", "asc"), ("name", "desc"), ("date", "desc"), ("date", "asc"), ("size", "desc"), ("size", "asc"), ("type", "asc"), ("recent", "desc") };

        /// <summary>The inventory order of a "Ordem padrão" choice (0 to 8): the sort and the direction the API takes.</summary>
        public static (string Sort, string Dir) InventorySort(int choice) { var c = SortOrder[Mathf.Clamp(choice, 0, SortOrder.Length - 1)]; return (c.Id, c.Label); }

        /// <summary>The follow speed of the third person camera (Unity's exponential rate) for the Godot "suavização" (0.5 to 10; 3.5 is the usual).</summary>
        public static float FollowRate(float smoothing) => Mathf.Clamp(smoothing, .5f, 10f) * 5f;

        public static float ParticlesOf(string choice) => choice switch { "off" => 0f, "low" => .3f, "medium" => .6f, _ => 1f };

        /// <summary>Puts the camera settings on a camera.</summary>
        public static void Camera(SettingsStore store, FollowCamera follow, bool reduceMotion)
        {
            if (follow == null) return;
            follow.FieldOfView = store.Int("camera.fov"); follow.FirstPersonFieldOfView = store.Int("camera.fp_fov"); follow.ZoomStep = store.Float("camera.zoom_step");
            follow.HeightOffset = store.Float("camera.height"); follow.Shoulder = store.Float("camera.shoulder"); follow.Smoothing = FollowRate(store.Float("camera.smoothing"));
            follow.Collide = store.Bool("camera.collision"); follow.WheelToFirstPerson = store.Bool("camera.wheel_first_person");
            follow.Options = (follow.Options with
            {
                HeadBob = store.Bool("camera.fp_bob") && !reduceMotion, BobStrength = store.Float("camera.fp_bob_strength"), Sway = store.Bool("camera.fp_sway") && !reduceMotion, SwayStrength = store.Float("camera.fp_sway_strength"),
                Stabilization = store.Float("camera.fp_stabilization"),
            }).Clamped();
        }

        /// <summary>The graphics the preset does not decide by itself (or all of them once the preset is "Personalizada").</summary>
        public static void Graphics(SettingsStore store, Camera camera, Action<bool, float> setShadows, Action<float> particles)
        {
            var custom = store.Get("graphics.preset") == "custom"; var preset = GraphicsQuality.Current;
            if (camera != null) camera.farClipPlane = Mathf.Clamp(store.Int("perf.render_distance"), 100, 6000);
            var shadows = custom ? store.Bool("graphics.shadows_enabled") : preset.Shadows; var distance = custom ? store.Int("graphics.shadow_distance") : preset.ShadowDistance;
            setShadows?.Invoke(shadows, distance);
            var aniso = (int)SettingsCatalog.F(store.Get("graphics.anisotropic"), 2);
            if (custom || store.Settings.Values.ContainsKey("graphics.anisotropic")) { QualitySettings.anisotropicFiltering = aniso <= 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable; if (aniso > 0) Texture.SetGlobalAnisotropicFilteringLimits(aniso, aniso); }
            if (custom || store.Settings.Values.ContainsKey("graphics.texture_quality")) QualitySettings.globalTextureMipmapLimit = store.Get("graphics.texture_quality") switch { "low" => 2, "medium" => 1, _ => 0 };
            particles?.Invoke(ParticlesOf(store.Get("graphics.particles")));
        }

        /// <summary>The effects of the picture on the island's volume and the camera: fog, volumetric light, glow, colour, ambient occlusion, reflections, shadow bands and sharpness, the smoothing of edges and the render scale.</summary>
        public static void Effects(SettingsStore store, LifeVerse.Unity.World.IslandEnvironment environment, Camera camera)
        {
            environment?.SetEffects(store.Bool("graphics.fog"), store.Bool("graphics.volumetric"), store.Bool("graphics.glow"), store.Bool("graphics.color_adjust"), store.Bool("graphics.ssao"), store.Get("graphics.ssao_quality"), store.Bool("graphics.ssr"), (int)store.Float("graphics.shadow_cascades"), store.Get("graphics.shadow_quality"));
            RenderScale.Target = Mathf.Clamp(store.Int("graphics.render_scale"), 50, 100); RenderScale.Dynamic = store.Bool("graphics.dynamic_resolution"); RenderScale.Install();
            if (camera != null && camera.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>() is { } data)
            {
                data.antialiasing = AntialiasingOf(store.Get("graphics.msaa")); data.allowDynamicResolution = true;
            }
        }

        /// <summary>The "MSAA" of the Godot client in this renderer: off, then edge smoothing done after drawing (FXAA for 2x, SMAA for 4x, temporal for 8x).</summary>
        public static UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode AntialiasingOf(string msaa) => msaa switch
        {
            "2" => UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing, "4" => UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            "8" => UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing, _ => UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None,
        };

        /// <summary>The resolution the 3D world is drawn at, as a percentage of the window; with the dynamic resolution on it goes down (to half) while the frames are slow and back up when they are quick.</summary>
        public static class RenderScale
        {
            public static float Target = 100f; public static bool Dynamic; public static float Current { get; private set; } = 100f; private static bool _installed; private static float _frame = 1f / 60f;
            public static void Install() { if (!_installed) { _installed = true; UnityEngine.Rendering.DynamicResolutionHandler.SetDynamicResScaler(() => Current, UnityEngine.Rendering.DynamicResScalePolicyType.ReturnsPercentage); } if (!Dynamic) Current = Target; }
            /// <summary>Called each frame with its time.</summary>
            public static void Tick(float dt)
            {
                if (!Dynamic) { Current = Target; return; }
                _frame = Mathf.Lerp(_frame, Mathf.Max(.001f, dt), .05f);
                if (_frame > 1f / 50f) Current = Mathf.Max(50f, Current - 1f); else if (_frame < 1f / 70f) Current = Mathf.Min(Target, Current + .5f);
                Current = Mathf.Min(Current, Target);
            }
        }

        /// <summary>The interface options of the catalog that are not about the picture: chat, names, mini-map, how opaque windows and the map are, the radial menu and the script editor.</summary>
        public static void Interface(SettingsStore store, Hud hud, UiRoot ui)
        {
            if (hud != null) { hud.Chat.ShowTime = store.Bool("chat.show_time"); hud.Chat.VisibleSeconds = store.Int("chat.visible_seconds"); hud.Map.ZoomMetres = store.Int("world.minimap_zoom"); hud.Map.ShowCoords = store.Bool("world.show_coords"); hud.Map.ShowAvatars = store.Bool("world.minimap_avatars"); hud.Map.style.opacity = Mathf.Clamp(store.Int("interface.map_opacity"), 30, 100) / 100f; }
            if (ui != null) ui.WindowLayer.style.opacity = Mathf.Clamp(store.Int("interface.window_opacity"), 30, 100) / 100f;
            LifeVerse.Unity.Avatar.AvatarView.ShowNames = store.Bool("avatar.show_names"); LifeVerse.Unity.Avatar.AvatarView.ShowOwnName = store.Bool("avatar.show_own_name");
            RadialMenu.Scale = Mathf.Clamp(store.Int("interface.radial_size"), 60, 150) / 100f; RadialMenu.Opacity = Mathf.Clamp(store.Int("interface.radial_opacity"), 50, 100) / 100f; RadialMenu.Glow = Mathf.Clamp(store.Int("interface.radial_glow"), 0, 100) / 100f;
            LuaEditorWindow.FontSize = store.Int("scripts.font_size"); LuaEditorWindow.Indent = store.Int("scripts.indent"); LuaEditorWindow.AutoComplete = store.Bool("scripts.autocomplete");
        }

        /// <summary>Deletes the files of islands kept on disk (they come again when needed). How many were deleted.</summary>
        public static int ClearCache()
        {
            var folder = LifeVerse.Unity.World.WorldJoin.CacheFolder; var removed = 0;
            try { if (System.IO.Directory.Exists(folder)) foreach (var file in System.IO.Directory.GetFiles(folder)) { System.IO.File.Delete(file); removed++; } } catch (System.IO.IOException) { } catch (UnauthorizedAccessException) { }
            return removed;
        }

        /// <summary>The toasts: how many, how long, and "não perturbe".</summary>
        public static void Toasts(SettingsStore store, NotificationCenter notices)
        {
            if (notices == null) return; notices.MaxShown = store.Int("notifications.max_toasts"); notices.Seconds = store.Int("notifications.toast_seconds"); notices.DoNotDisturb = store.Bool("notifications.dnd");
        }

        public static void Build(SettingsStore store, BuildEditor builder)
        {
            if (builder == null) return; builder.SnapToGrid = store.Bool("build.snap"); builder.Grid = Mathf.Max(.05f, store.Float("build.move_step")); builder.RotateStep = Mathf.Max(1f, store.Float("build.rotate_step")); builder.ScaleStep = Mathf.Max(.01f, store.Float("build.scale_step"));
        }

        /// <summary>The performance numbers asked in the settings (FPS, frame time, memory): the overlay shows when any is on, with the memory line when asked.</summary>
        public static bool Metrics(SettingsStore store, Hud hud)
        {
            var want = store.Bool("metrics.fps") || store.Bool("metrics.frame_time") || store.Bool("metrics.memory");
            if (hud != null && hud.PerformanceVisible != want) hud.TogglePerformance();
            return want;
        }

        // ---- the screen ----

        public static readonly (string Id, string Label)[] Modes = { ("auto", "Automático"), ("window", "Janela"), ("borderless", "Tela cheia (sem bordas)"), ("exclusive", "Tela cheia exclusiva") };

        public static List<(string Id, string Label)> Choices(string id)
        {
            switch (id)
            {
                case "display.mode": return Modes.ToList();
                case "display.resolution": { var list = new List<(string, string)> { ("auto", "Automático") }; foreach (var r in Screen.resolutions.Select(r => (r.width, r.height)).Distinct().OrderByDescending(r => r.width * r.height)) list.Add((r.width + "x" + r.height, r.width + " × " + r.height)); return list; }
                case "display.monitor": { var list = new List<(string, string)> { ("auto", "Automático") }; for (var i = 0; i < Display.displays.Length; i++) list.Add((i.ToString(), "Monitor " + (i + 1))); return list; }
                case "voice.input_device": { var list = new List<(string, string)> { ("auto", "Automático") }; foreach (var d in Microphone.devices) list.Add((d, d)); return list; }
                case "audio.output_device": return new List<(string, string)> { ("auto", "Automático (o do sistema)") };
                default: return new List<(string, string)>();
            }
        }

        /// <summary>A resolution as "1920x1080", or null.</summary>
        public static (int W, int H)? ParseResolution(string value)
        {
            var parts = (value ?? "").Split('x'); return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) && w >= 640 && h >= 360 && w <= 16384 && h <= 16384 ? (w, h) : ((int, int)?)null;
        }

        public static FullScreenMode ModeOf(string mode) => mode switch { "window" => FullScreenMode.Windowed, "borderless" => FullScreenMode.FullScreenWindow, "exclusive" => FullScreenMode.ExclusiveFullScreen, _ => Screen.fullScreenMode };

        /// <summary>Puts the screen settings on the window (the caller asks the person to confirm and can put the old ones back).</summary>
        public static void Screen_(SettingsStore store)
        {
            var mode = ModeOf(store.Get("display.mode")); var size = ParseResolution(store.Get("display.resolution"));
            if (int.TryParse(store.Get("display.monitor"), out var monitor) && monitor >= 0 && monitor < Display.displays.Length)
            {
                var layout = new List<DisplayInfo>(); Screen.GetDisplayLayout(layout);
                if (monitor < layout.Count && !layout[monitor].Equals(Screen.mainWindowDisplayInfo)) Screen.MoveMainWindowTo(layout[monitor], Vector2Int.zero);
            }
            if (size is { } s) Screen.SetResolution(s.W, s.H, mode); else Screen.fullScreenMode = mode;
        }

        // ---- the values shown (Info) ----

        public static string Info(string id, GameApp app)
        {
            switch (id)
            {
                case "advanced.version": return Application.version;
                case "advanced.godot": return Application.unityVersion;
                case "advanced.system": return SystemInfo.operatingSystem;
                case "advanced.gpu": return SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsMemorySize + " MB)";
                case "advanced.renderer": return SystemInfo.graphicsDeviceType + " · HDRP";
                case "advanced.profile": return app?.Settings?.ActiveProfile is { Length: > 0 } p ? p : "(nenhum)";
                case "advanced.data_folder": return Application.persistentDataPath;
                case "network.protocol": return LifeVerse.Shared.Networking.NetworkProtocol.Version.ToString(CultureInfo.InvariantCulture);
                case "network.state": case "account.connection": return app?.Client != null ? (app.Client.Connected ? "Conectado à ilha" : "Desconectado") : "Fora de uma ilha";
                case "network.avatars": return app?.PeopleViews != null ? app.PeopleViews.Count.ToString(CultureInfo.InvariantCulture) : "0";
                case "account.user": return app?.Settings?.LastLogin is { Length: > 0 } user ? user : "—";
                case "account.session": return app?.Context?.Api?.UserId is { } uid && uid != Guid.Empty ? "Entrou (sessão ativa)" : "Não entrou";
                case "account.server": return app?.ApiBase ?? "—";
                case "general.updates": return app?.UpdateInfo != null ? app.UpdateInfo.Describe() : "Sem informação";
                default: return "";
            }
        }

        /// <summary>A text with the version, the system and the settings that differ from the default, for "Exportar diagnóstico".</summary>
        public static string Diagnostics(SettingsStore store, GameApp app)
        {
            var lines = new List<string> { "LifeVerse (Unity) " + Application.version + " · Unity " + Application.unityVersion, SystemInfo.operatingSystem, SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsMemorySize + " MB · " + SystemInfo.graphicsDeviceType, SystemInfo.processorType + " · " + SystemInfo.systemMemorySize + " MB", "Tela: " + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode, "", "Configurações diferentes do padrão:" };
            foreach (var d in SettingsCatalog.All.Where(d => d.IsValue && !d.Device)) { var v = store.Get(d.Id); if (v != SettingsCatalog.Normalize(d, d.Default)) lines.Add("  " + d.Id + " = " + v); }
            return string.Join(Environment.NewLine, lines);
        }

        public static string WriteDiagnostics(SettingsStore store, GameApp app)
        {
            var path = Path.Combine(Application.persistentDataPath, "diagnostico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt"); File.WriteAllText(path, Diagnostics(store, app)); return path;
        }
    }
}
