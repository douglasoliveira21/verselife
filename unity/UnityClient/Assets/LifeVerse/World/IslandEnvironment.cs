using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Sky, sun, fog and weather of an island, on the HDRP volumes: the island sky of the Godot client (<see cref="IslandSky"/>), whose sun is this component's directional light,
    /// fixed exposure, and the weather's fog. Hour 6 is sunrise, 12 is noon, 18 is sunset; the light is off while the sun is below the horizon (the rules of the Godot
    /// IslandEnvironmentController). Parcel looks (a parcel changing hour, weather or tint) are applied by <see cref="SetParcelLook"/> and fade over three seconds.
    /// </summary>
    public sealed class IslandEnvironment : MonoBehaviour
    {
        public const float BlendSeconds = 3f;
        /// <summary>The light levels of the Godot client in HDRP units. At the fixed exposure of the world (EV 13.5) a radiance of <see cref="ExposureNits"/> is white (1), so Godot's sun of energy 1.1 is this many lux.</summary>
        public const float ExposureNits = 13904f, SunEnergy = 1.1f, NoonLux = SunEnergy * Mathf.PI * ExposureNits, MoonLux = 1900f;
        /// <summary>Godot's ambient light: the sky times 0.75 by day and 0.18 by night.</summary>
        public const float AmbientDay = .75f, AmbientNight = .18f;

        public bool DayNightCycle = true;
        public float TimeScale = .02f;
        [Range(0, 24)] public float Hour = 12f;
        public string Weather = "clear";

        public float DayFactor { get; private set; } = 1f;

        private Light _sun;
        private HDAdditionalLightData _sunData, _moonData;
        private Light _moon;
        private Volume _volume, _underwater;
        private VolumeProfile _profile, _underwaterProfile;
        private Exposure _exposure;
        private IslandSky _sky; private float _cloudAmount = .5f;
        private static Texture2D _cloudMap;
        /// <summary>How much of the sky the clouds cover (the Godot sky's cloud_cover).</summary>
        public const float CloudCover = .52f;

        private static Texture2D _noiseTexture; private static float[] _noise; private const int NoiseSize = 512;
        /// <summary>The cloud noise: a tiling fractal stretched over the whole range, as the Godot noise texture is.</summary>
        private static float[] Noise()
        {
            if (_noise != null) return _noise;
            var field = ProceduralTextures.Fbm(NoiseSize, 10, 5, 5);   /* cloud puffs of the size of the Godot noise */ var low = float.MaxValue; var high = float.MinValue;
            foreach (var v in field) { low = Mathf.Min(low, v); high = Mathf.Max(high, v); }
            for (var i = 0; i < field.Length; i++) field[i] = (field[i] - low) / Mathf.Max(1e-5f, high - low);
            return _noise = field;
        }

        /// <summary>The noise of the clouds as the sky shader reads it.</summary>
        public static Texture2D CloudNoise()
        {
            if (_noiseTexture != null) return _noiseTexture;
            var field = Noise(); var pixels = new Color32[field.Length];
            for (var i = 0; i < pixels.Length; i++) { var v = (byte)(Mathf.Clamp01(field[i]) * 255f); pixels[i] = new Color32(v, v, v, 255); }
            _noiseTexture = new Texture2D(NoiseSize, NoiseSize, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, name = "CloudNoise" };
            _noiseTexture.SetPixels32(pixels); _noiseTexture.Apply(true); return _noiseTexture;
        }

        /// <summary>The clouds of the upper half of the sky as the sea mirrors them (lat-long: across is the direction around, up is the height above the horizon).</summary>
        public static Texture2D CloudMap()
        {
            if (_cloudMap != null) return _cloudMap;
            const int width = 1024, height = 256; var noise = Noise(); var pixels = new Color32[width * height];
            float N(float u, float v)
            {
                u = (u - Mathf.Floor(u)) * NoiseSize; v = (v - Mathf.Floor(v)) * NoiseSize; var x0 = (int)u % NoiseSize; var y0 = (int)v % NoiseSize; var x1 = (x0 + 1) % NoiseSize; var y1 = (y0 + 1) % NoiseSize; var tx = u - Mathf.Floor(u); var ty = v - Mathf.Floor(v);
                return Mathf.Lerp(Mathf.Lerp(noise[y0 * NoiseSize + x0], noise[y0 * NoiseSize + x1], tx), Mathf.Lerp(noise[y1 * NoiseSize + x0], noise[y1 * NoiseSize + x1], tx), ty);
            }
            for (var j = 0; j < height; j++)
            {
                var elevation = (j + .5f) / height * Mathf.PI / 2f; var dy = Mathf.Sin(elevation); var flat = Mathf.Cos(elevation);
                for (var i = 0; i < width; i++)
                {
                    var azimuth = (i + .5f) / width * Mathf.PI * 2f; var dx = flat * Mathf.Sin(azimuth); var dz = flat * Mathf.Cos(azimuth);
                    var u = dx / (dy + .14f) * .32f; var v = -dz / (dy + .14f) * .32f; var n = N(u, v) * .7f + N(u * 2.7f, v * 2.7f) * .3f;
                    var c = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - CloudCover, 1f - CloudCover + .24f, n)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.005f, .2f, dy));
                    var value = (byte)Mathf.Clamp(c * 255f, 0f, 255f); pixels[j * width + i] = new Color32(value, value, value, 255);
                }
            }
            _cloudMap = new Texture2D(width, height, TextureFormat.RGBA32, true, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Clouds" };
            _cloudMap.SetPixels32(pixels); _cloudMap.Apply(true); return _cloudMap;
        }
        private ParticleSystem _rain;
        /// <summary>0 to 1: how much rain falls now (the player can turn weather effects down in the graphics settings).</summary>
        public float RainQuality = 1f;
        private HDShadowSettings _shadowSettings;

        /// <summary>The shadows of the sun and how far they reach (the graphics preset): none on Baixo, 60 to 250 m above it.</summary>
        public void SetShadows(bool on, float distance)
        {
            Create(); ShadowsOn = on; _sunData.EnableShadows(on); if (_shadowSettings != null && distance > 0f) _shadowSettings.maxShadowDistance.value = distance;
        }
        public bool ShadowsOn { get; private set; } = true;
        private Tonemapping _tonemapping, _underTonemap; private float _underDay = 1f; private Bloom _bloom; private IndirectLightingController _indirect; private float _contrast = 1.06f, _saturation = 1.1f; private ScreenSpaceAmbientOcclusion _ao; private ScreenSpaceReflection _ssr;

        /// <summary>The effects of the picture chosen in Configurações › Gráficos: fog (and its volumetric light), glow, the colour treatment, ambient occlusion, reflections, and the shadows' bands and sharpness.</summary>
        public void SetEffects(bool fog, bool volumetric, bool glow, bool colour, bool ambientOcclusion, string occlusionQuality, bool reflections, int cascades, string shadowQuality)
        {
            Create(); Effects = (fog, volumetric, glow, colour, ambientOcclusion, reflections, cascades, shadowQuality);
            _fog.active = fog; _fog.enableVolumetricFog.Override(fog && volumetric); _bloom.active = glow; ApplyTonemap();
            _ao.active = ambientOcclusion; var (intensity, radius) = occlusionQuality switch { "low" => (.45f, .8f), "high" => (.8f, 1.6f), "ultra" => (.9f, 2f), _ => (.6f, 1.2f) }; _ao.intensity.value = intensity; _ao.radius.value = radius;
            _ssr.enabled.value = reflections; _shadowSettings.cascadeShadowSplitCount.Override(Mathf.Clamp(cascades, 1, 4));
            _sunData.SetShadowResolution(shadowQuality switch { "hard" or "low" => 512, "medium" => 1024, "ultra" => 4096, _ => 2048 });
        }
        /// <summary>The contrast and the saturation "Ajuste de cor" adds (they come with the quality preset, as in Godot: 1 and 1 on the low ones).</summary>
        public void SetAdjustment(float contrast, float saturation) { Create(); _contrast = contrast; _saturation = saturation; ApplyTonemap(); }
        /// <summary>The contrast and saturation of each graphics preset (0 low to 3 ultra), as the Godot client's presets.</summary>
        public static (float Contrast, float Saturation) AdjustmentOf(int preset) => preset <= 0 ? (1f, 1f) : preset == 1 ? (1.04f, 1.06f) : preset == 2 ? (1.06f, 1.1f) : (1.08f, 1.12f);
        private void ApplyTonemap()
        {
            var asset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
            var size = asset != null ? asset.currentPlatformRenderPipelineSettings.postProcessSettings.lutSize : 32;
            var adjust = Effects.Colour;
            _tonemapping.lutTexture.Override(GodotTonemap.Table(size, adjust ? _contrast : 1f, adjust ? _saturation : 1f));
            if (_underTonemap != null) _underTonemap.lutTexture.Override(GodotTonemap.Table(size, adjust ? _contrast : 1f, adjust ? _saturation : 1f, _underDay));
        }
        /// <summary>The effects applied last (for the checks).</summary>
        public (bool Fog, bool Volumetric, bool Glow, bool Colour, bool AmbientOcclusion, bool Reflections, int Cascades, string ShadowQuality) Effects { get; private set; } = (true, false, true, true, false, false, 4, "high");
        public float RainAmount { get; private set; }
        public const float SkyNits = ExposureNits;
        private static readonly Color ZenithDay = new Color(.17f, .42f, .84f), HorizonDay = new Color(.74f, .86f, .97f), ZenithNight = new Color(.004f, .01f, .045f), HorizonNight = new Color(.03f, .045f, .1f);
        private static readonly Color FogGrey = new Color(0x9F / 255f, 0xB5 / 255f, 0xC9 / 255f), FogStorm = new Color(0x46 / 255f, 0x50 / 255f, 0x6A / 255f);
        /// <summary>The density of the fog of each weather (per metre), as in Godot; a parcel's fog adds up to 0.06.</summary>
        public static float FogDensityOf(string weather) => weather switch { "fog" => .045f, "rain" => .012f, "storm" => .025f, "cloudy" => .006f, _ => 0f };
        /// <summary>The density in force now, and how much of the sky the fog covers (for the checks).</summary>
        public float FogDensity => _fogDensity;
        public float SkyCover => Mathf.Clamp01(_fogDensity / .001f);
        private Fog _fog;
        private float _fogDensity;
        private Camera _camera;
        private float _seaLevel = -.8f;
        private float _underwaterWeight;

        // The look the island owner set (hour, weather, fog, light, sky tint) or the parcel under the player sets: it takes over gradually (0 to 1), and the island's own
        // settings stay underneath and come back when it goes away. Same rules as the Godot IslandEnvironmentController.
        private LifeVerse.Shared.Land.ParcelEnvironment _look; private LifeVerse.Shared.Land.ParcelEnvironment _lastLook = new LifeVerse.Shared.Land.ParcelEnvironment(); private float _blend;

        /// <summary>How far the look has taken over, 0 to 1.</summary>
        public float LookBlend => _blend;
        public bool LookActive => _look != null;
        public float HourShown => _lastLook.Hour is { } target && _blend > 0 ? Mathf.Repeat(Hour + ShortestHourDelta(Hour, target) * _blend, 24f) : Hour;
        public string WeatherShown => _lastLook.Weather != null && _blend >= .5f ? _lastLook.Weather : Weather;
        public float LightShown => Mathf.Lerp(1f, _lastLook.Light ?? 1f, _blend);
        public Light Sun { get { Create(); return _sun; } }
        public Volume Volume => _volume;

        private static float ShortestHourDelta(float from, float to) { var d = Mathf.Repeat(to - from, 24f); return d > 12f ? d - 24f : d; }

        /// <summary>Sets (or, with null or an empty look, removes) the look that takes over: the island's, or a parcel's.</summary>
        public void SetLook(LifeVerse.Shared.Land.ParcelEnvironment look) { _look = look != null && !look.IsEmpty ? look : null; if (_look != null) _lastLook = _look; }
        public void SetParcelLook(float? hour, string weather, float light) => SetLook(new LifeVerse.Shared.Land.ParcelEnvironment(hour, weather, null, light, null));
        /// <summary>For tests: jump straight to the end of the fade.</summary>
        public void FinishBlend() => _blend = _look == null ? 0f : 1f;

        public void Initialize(Camera camera, float seaLevel) { Create(); _camera = camera; _seaLevel = seaLevel; }

        private bool _created;
        private void Awake() => Create();

        /// <summary>Done lazily as well as in Awake, because edit-mode tests add the component without Awake running.</summary>
        private void Create()
        {
            if (_created) return; _created = true;
            var sunObject = new GameObject("Sun"); sunObject.transform.SetParent(transform, false);
            _sun = sunObject.AddComponent<Light>(); _sun.type = LightType.Directional; _sun.shadows = LightShadows.Soft;
            _sunData = sunObject.AddComponent<HDAdditionalLightData>(); _sunData.SetIntensity(NoonLux, LightUnit.Lux);
            _sunData.EnableShadows(true);
            var moonObject = new GameObject("Moon"); moonObject.transform.SetParent(transform, false);
            _moon = moonObject.AddComponent<Light>(); _moon.type = LightType.Directional; _moon.color = new Color(.62f, .72f, 1f); _moon.shadows = LightShadows.None;
            _moonData = moonObject.AddComponent<HDAdditionalLightData>(); _moonData.SetIntensity(0f, LightUnit.Lux);

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var environment = _profile.Add<VisualEnvironment>(); // the sky of the Godot client, drawn by our own sky renderer, lighting the world as it changes
            environment.skyType.Override(IslandSky.UniqueId); environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            _sky = _profile.Add<IslandSky>(); _sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier); _sky.multiplier.Override(SkyNits); _sky.updateMode.Override(EnvironmentUpdateMode.OnChanged);
            _sky.sunDirection.Override(Vector3.up); _sky.day.Override(1f); _sky.cloudCover.Override(CloudCover); _sky.fog.Override(Vector4.zero); _sky.tint.Override(new Vector4(1f, 1f, 1f, 0f)); _sky.cloudNoise.Override(CloudNoise()); // overridden here so the values set every frame count
            _exposure = _profile.Add<Exposure>(); _exposure.mode.Override(ExposureMode.Fixed); _exposure.fixedExposure.Override(13.5f);
            BuildRain();
            // HDRP's fog only carries the light shafts of "Névoa volumétrica"; the weather's fog is a pass of our own (it must not dim the sun, as Godot's does not)
            _fog = _profile.Add<Fog>(); _fog.enabled.Override(false); _fog.meanFreePath.Override(250f); _fog.baseHeight.Override(-50f); _fog.maximumHeight.Override(60f);
            _fog.enableVolumetricFog.Override(false); _fog.colorMode.Override(FogColorMode.SkyColor);
            BuildWeatherFog();
            _tonemapping = _profile.Add<Tonemapping>(); _tonemapping.mode.Override(TonemappingMode.External); _tonemapping.lutContribution.Override(1f); ApplyTonemap();   // the filmic curve of the Godot client
            _indirect = _profile.Add<IndirectLightingController>(); _indirect.indirectDiffuseLightingMultiplier.Override(AmbientDay);
            _ao = _profile.Add<ScreenSpaceAmbientOcclusion>(); _ao.intensity.Override(.6f); _ao.radius.Override(1.2f); _ao.active = false;
            _ssr = _profile.Add<ScreenSpaceReflection>(); _ssr.enabled.Override(false);
            var bloom = _bloom = _profile.Add<Bloom>(); bloom.intensity.Override(.2f); bloom.threshold.Override(1f); bloom.scatter.Override(.65f); // the sun disc and bright lamps glow
            _shadowSettings = _profile.Add<HDShadowSettings>(); _shadowSettings.maxShadowDistance.Override(120f);
            _volume = new GameObject("World Volume").AddComponent<Volume>(); _volume.transform.SetParent(transform, false); _volume.isGlobal = true; _volume.sharedProfile = _profile; _volume.priority = 0;

            // under the water: the blue the Godot client lays over the screen, as part of the last step of the picture, switched on as the camera goes below the sea
            _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _underTonemap = _underwaterProfile.Add<Tonemapping>(); ApplyTonemap();
            _underwater = new GameObject("Underwater Volume").AddComponent<Volume>(); _underwater.transform.SetParent(transform, false); _underwater.isGlobal = true; _underwater.sharedProfile = _underwaterProfile; _underwater.priority = 10; _underwater.weight = 0;
        }

        /// <summary>How visible the sun's disc is in the sky, 0 to 1: gone below the horizon and behind the fog of the weather.</summary>
        public float SunDiscVisibility { get; private set; }

        private CustomPassVolume _fogPass; private Material _fogMaterial;
        private static readonly int FogColourId = Shader.PropertyToID("_IslandFogColour"), FogShapeId = Shader.PropertyToID("_IslandFogShape");
        /// <summary>Whether the weather's fog is drawn now (for the checks).</summary>
        public bool WeatherFogShown => _fogPass != null && _fogPass.enabled;

        private void BuildWeatherFog()
        {
            var shader = Resources.Load<Shader>("LifeVerse/IslandFog"); if (shader == null) return;
            _fogMaterial = CoreUtils.CreateEngineMaterial(shader);
            var host = new GameObject("Weather Fog"); host.transform.SetParent(transform, false);
            _fogPass = host.AddComponent<CustomPassVolume>(); _fogPass.injectionPoint = CustomPassInjectionPoint.BeforeTransparent; _fogPass.isGlobal = true; _fogPass.enabled = false;
            _fogPass.customPasses.Add(new FullScreenCustomPass { name = "Weather fog", fullscreenPassMaterial = _fogMaterial, targetColorBuffer = CustomPass.TargetBuffer.Camera, targetDepthBuffer = CustomPass.TargetBuffer.None });
        }

        private void BuildRain()
        {
            var rainObject = new GameObject("Rain"); rainObject.transform.SetParent(transform, false); rainObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // a box emits along its forward: down
            _rain = rainObject.AddComponent<ParticleSystem>(); _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 9000;
            main.startLifetime = 1.1f; main.startSpeed = new ParticleSystem.MinMaxCurve(28f, 34f); main.startSize = .018f; main.gravityModifier = .15f;   // thin streaks of about 0.6 m, as Godot's
            var shape = _rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(60f, 60f, .5f);
            var emission = _rain.emission; emission.rateOverTime = 0f;
            var renderer = _rain.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 3f; renderer.velocityScale = .0176f; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var material = Resources.Load<Material>("LifeVerse/RainMaterial"); if (material != null) renderer.sharedMaterial = material;
        }

        private void UpdateRain(string weather)
        {
            var amount = (weather == "storm" ? 1f : weather == "rain" ? .6f : 0f) * RainQuality; RainAmount = amount;
            var emission = _rain.emission; emission.rateOverTime = 5450f * amount;
            if (amount > 0f && !_rain.isPlaying) _rain.Play(); else if (amount <= 0f && _rain.isPlaying) _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_camera != null) _rain.transform.position = _camera.transform.position + Vector3.up * 14f;
        }

        private void Update()
        {
            if (DayNightCycle) Hour = Mathf.Repeat(Hour + Time.deltaTime * TimeScale, 24f);
            _blend = Mathf.MoveTowards(_blend, _look != null ? 1f : 0f, Time.deltaTime / BlendSeconds);
            Apply(Time.deltaTime);
        }

        /// <summary>The turn of the Godot sun around the vertical, in degrees.</summary>
        public const float SunYaw = 28f;
        /// <summary>Where the sunlight travels at an hour, in the scene: the Godot sun (turned <see cref="SunYaw"/> degrees, up at 6, overhead at 12, down at 18) with the game's z flipped.</summary>
        public static Vector3 SunDirection(float hour)
        {
            var angle = (hour - 6f) / 12f * Mathf.PI; var yaw = SunYaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * Mathf.Sin(yaw), -Mathf.Sin(angle), Mathf.Cos(angle) * Mathf.Cos(yaw));
        }

        /// <summary>Puts the sun, exposure and fog where the hour and the weather say. Called every frame; also by tests and the preview to settle in one go.</summary>
        public void Apply(float delta)
        {
            Create();
            var hour = HourShown; var elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.PI);
            var day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.08f, .3f, elevation)); DayFactor = day;
            // built from the direction with the world up, so the light never has a rolled (upside-down) frame after noon
            var sunDirection = SunDirection(hour);
            _sun.transform.rotation = Mathf.Abs(sunDirection.y) > .999f ? Quaternion.Euler(Mathf.Sign(-sunDirection.y) * 90f, 180f - SunYaw, 0f) : Quaternion.LookRotation(sunDirection, Vector3.up);
            var above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.04f, .02f, elevation));
            var weather = WeatherShown;
            // the sun stays on (the sky is lit by it) and fades with the day; at night a faint bluish moon lights the ground from the opposite side. As in Godot, the weather does not dim it: its fog does the greying
            // never exactly zero: with a dead sun the HDRP sky falls back to a default daylight sun and the whole night turns white
            var sunLux = Mathf.Max(NoonLux * day * LightShown, .05f); _sunData.SetIntensity(sunLux, LightUnit.Lux);
            _moon.transform.rotation = Quaternion.LookRotation(-_sun.transform.forward); _moonData.SetIntensity(MoonLux * (1f - day) * LightShown, LightUnit.Lux);
            _exposure.fixedExposure.value = 13.5f; // a daylight camera at all hours: the night is dark because its light and its sky are dark, not because the camera opens up
            // the fog of the weather (and what a parcel adds), reached gradually; it covers the sky as it comes
            var targetFog = FogDensityOf(weather) + (_lastLook.Fog ?? 0f) * .06f * _blend;
            _fogDensity = Mathf.Lerp(_fogDensity, targetFog, Mathf.Clamp01(delta * 2f)); if (_fogDensity < 1e-5f) _fogDensity = 0f;
            var fogColour = (weather == "storm" ? FogStorm : FogGrey).linear;
            var cover = Effects.Fog ? SkyCover : 0f;
            _fog.enabled.value = Effects.Volumetric;
            if (_fogPass != null)
            {
                _fogPass.enabled = Effects.Fog && _fogDensity > 0f;
                _fogMaterial.SetVector(FogColourId, fogColour); _fogMaterial.SetVector(FogShapeId, new Vector4(_fogDensity, ExposureNits, 0f, 0f));
            }
            // a tint chosen for the sky (a parcel or the island) colours it by up to 55 % while the look is in force
            var tint = _lastLook.Sky != null && LifeVerse.Shared.Land.ParcelEnvironment.IsColour(_lastLook.Sky) && ColorUtility.TryParseHtmlString(_lastLook.Sky, out var tintColor) ? tintColor.linear : Color.white; var tintAmount = _lastLook.Sky == null ? 0f : .55f * _blend;
            _cloudAmount = weather switch { "cloudy" => .72f, "rain" => .85f, "storm" => .95f, "fog" => .8f, _ => .5f };
            _sky.sunDirection.value = -_sun.transform.forward; _sky.day.value = day; _sky.cloudCover.value = _cloudAmount;
            _sky.fog.value = new Vector4(fogColour.r, fogColour.g, fogColour.b, cover); _sky.tint.value = new Vector4(tint.r, tint.g, tint.b, tintAmount);
            SunDiscVisibility = above * (1f - cover);
            var ambient = Mathf.Lerp(AmbientNight, AmbientDay, day) * LightShown; _indirect.indirectDiffuseLightingMultiplier.value = ambient;
            // what the sea mirrors: the same sky, without its sun
            Color Tinted(Color c) => Color.Lerp(c, new Color(c.r * tint.r * 1.6f, c.g * tint.g * 1.6f, c.b * tint.b * 1.6f), tintAmount);
            var top = Color.Lerp(Tinted(Color.Lerp(ZenithNight.linear, ZenithDay.linear, day)), fogColour, cover); var horizon = Color.Lerp(Tinted(Color.Lerp(HorizonNight.linear, HorizonDay.linear, day)), fogColour, cover);
            Publish(day, sunLux, ambient, top, horizon, fogColour, cover);
            UpdateRain(weather);
            if (_camera != null)
            {
                var target = _camera.transform.position.y < _seaLevel ? 1f : 0f;
                if (target > 0f && Mathf.Abs(day - _underDay) > .05f) { _underDay = day; ApplyTonemap(); }
                _underwaterWeight = Mathf.MoveTowards(_underwaterWeight, target, delta * 6f); _underwater.weight = _underwaterWeight;
            }
        }

        /// <summary>Tells the shaders that light themselves (the sea) where the sun is and what the sky looks like, in Godot's units (1 is white).</summary>
        private void Publish(float day, float sunLux, float ambient, Color top, Color horizon, Color fog, float cover)
        {
            var sun = sunLux / (Mathf.PI * ExposureNits);
            Shader.SetGlobalVector("_LVSunDir", -_sun.transform.forward);
            Shader.SetGlobalVector("_LVSunLight", new Vector4(sun, sun, sun, 1f));
            Shader.SetGlobalVector("_LVSkyTop", top); Shader.SetGlobalVector("_LVSkyHorizon", horizon);
            Shader.SetGlobalVector("_LVAmbient", Color.Lerp(horizon, top, .55f) * ambient);
            Shader.SetGlobalVector("_LVFog", new Vector4(fog.r, fog.g, fog.b, Effects.Fog && _fogDensity > 0f ? 1f / _fogDensity : 0f));
            Shader.SetGlobalFloat("_LVDay", day); Shader.SetGlobalFloat("_LVClouds", Mathf.Lerp(.5f, 1f, _cloudAmount) * (1f - cover));
            Shader.SetGlobalTexture("_LVCloudMap", CloudMap());
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile); if (_underwaterProfile != null) Destroy(_underwaterProfile); if (_cloudMap != null) Destroy(_cloudMap); if (_noiseTexture != null) Destroy(_noiseTexture); if (_fogMaterial != null) Destroy(_fogMaterial);
        }
    }
}
