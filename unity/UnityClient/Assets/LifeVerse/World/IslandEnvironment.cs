using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Sky, sun, fog and weather of an island, on the HDRP volumes: a physically based sky whose sun is this component's directional light, a cloud layer, fixed exposure
    /// that follows the day, and fog by weather. Hour 6 is sunrise, 12 is noon, 18 is sunset; the light is off while the sun is below the horizon (the rules of the Godot
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
        private GradientSky _sky; private CloudLayer _clouds;
        private static Texture2D _cloudMap;
        /// <summary>How much of the sky the clouds cover (the Godot sky's cloud_cover).</summary>
        public const float CloudCover = .52f;

        /// <summary>The cloud map of the upper half of the sky (lat-long: across is the direction around, up is the height above the horizon), with the Godot shader's projection and noise.</summary>
        public static Texture2D CloudMap()
        {
            if (_cloudMap != null) return _cloudMap;
            const int width = 1024, height = 256, noiseSize = 256; var noise = ProceduralTextures.Fbm(noiseSize, 6, 5, 5); var pixels = new Color32[width * height];
            float N(float u, float v)
            {
                u = (u - Mathf.Floor(u)) * noiseSize; v = (v - Mathf.Floor(v)) * noiseSize; var x0 = (int)u % noiseSize; var y0 = (int)v % noiseSize; var x1 = (x0 + 1) % noiseSize; var y1 = (y0 + 1) % noiseSize; var tx = u - Mathf.Floor(u); var ty = v - Mathf.Floor(v);
                return Mathf.Lerp(Mathf.Lerp(noise[y0 * noiseSize + x0], noise[y0 * noiseSize + x1], tx), Mathf.Lerp(noise[y1 * noiseSize + x0], noise[y1 * noiseSize + x1], tx), ty);
            }
            for (var j = 0; j < height; j++)
            {
                var elevation = (j + .5f) / height * Mathf.PI / 2f; var dy = Mathf.Sin(elevation); var flat = Mathf.Cos(elevation);
                for (var i = 0; i < width; i++)
                {
                    var azimuth = (i + .5f) / width * Mathf.PI * 2f; var dx = flat * Mathf.Sin(azimuth); var dz = flat * Mathf.Cos(azimuth);
                    var u = dx / (dy + .14f) * .32f; var v = dz / (dy + .14f) * .32f; var n = N(u, v) * .7f + N(u * 2.7f, v * 2.7f) * .3f;
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
        public const float SkyNits = 14000f;
        private static readonly Color ZenithDay = new Color(.17f, .42f, .84f), HorizonDay = new Color(.74f, .86f, .97f), ZenithNight = new Color(.004f, .01f, .045f), HorizonNight = new Color(.03f, .045f, .1f), Sunset = new Color(1f, .52f, .22f);
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
            var environment = _profile.Add<VisualEnvironment>(); // The physically based sky ignored the sun light in this setup (it stayed in full daylight at night and at sunset), so the sky is a gradient driven by the hour,
            // as the Godot client does with its own sky shader: zenith and horizon colours for day and night, an orange horizon when the sun is low.
            environment.skyType.Override((int)SkyType.Gradient); environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            _sky = _profile.Add<GradientSky>(); _sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier); _sky.multiplier.Override(SkyNits); _sky.gradientDiffusion.Override(1.6f); _sky.top.Override(Color.blue); _sky.middle.Override(Color.cyan); _sky.bottom.Override(Color.gray); // overridden here so the values set every frame count
            // the clouds of the Godot sky shader: the same noise, projected on the dome the same way (uv = dir.xz / (dir.y + 0.14) * 0.32), drawn by the cloud layer of HDRP
            environment.cloudType.Override((int)CloudType.CloudLayer); _clouds = _profile.Add<CloudLayer>(); _clouds.opacity.Override(.93f); _clouds.upperHemisphereOnly.Override(true);
            _clouds.layerA.cloudMap.Override(CloudMap()); _clouds.layerA.opacityR.Override(1f); _clouds.layerA.lighting.Override(false); _clouds.layerA.tint.Override(Color.white); _clouds.layerA.exposure.Override(0f);
            _exposure = _profile.Add<Exposure>(); _exposure.mode.Override(ExposureMode.Fixed); _exposure.fixedExposure.Override(13.5f);
            BuildRain();
            _fog = _profile.Add<Fog>(); _fog.enabled.Override(true); _fog.meanFreePath.Override(6000f); _fog.baseHeight.Override(-50f); _fog.maximumHeight.Override(60f);
            _fog.enableVolumetricFog.Override(false); _fog.colorMode.Override(FogColorMode.SkyColor);
            _tonemapping = _profile.Add<Tonemapping>(); _tonemapping.mode.Override(TonemappingMode.External); _tonemapping.lutContribution.Override(1f); ApplyTonemap();   // the filmic curve of the Godot client
            _indirect = _profile.Add<IndirectLightingController>(); _indirect.indirectDiffuseLightingMultiplier.Override(AmbientDay);
            _ao = _profile.Add<ScreenSpaceAmbientOcclusion>(); _ao.intensity.Override(.6f); _ao.radius.Override(1.2f); _ao.active = false;
            _ssr = _profile.Add<ScreenSpaceReflection>(); _ssr.enabled.Override(false);
            var bloom = _bloom = _profile.Add<Bloom>(); bloom.intensity.Override(.2f); bloom.threshold.Override(1f); bloom.scatter.Override(.65f); // the sun disc and bright lamps glow
            BuildSunDisc();
            _shadowSettings = _profile.Add<HDShadowSettings>(); _shadowSettings.maxShadowDistance.Override(120f);
            _volume = new GameObject("World Volume").AddComponent<Volume>(); _volume.transform.SetParent(transform, false); _volume.isGlobal = true; _volume.sharedProfile = _profile; _volume.priority = 0;

            // under the water: the blue the Godot client lays over the screen, as part of the last step of the picture, switched on as the camera goes below the sea
            _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            _underTonemap = _underwaterProfile.Add<Tonemapping>(); ApplyTonemap();
            _underwater = new GameObject("Underwater Volume").AddComponent<Volume>(); _underwater.transform.SetParent(transform, false); _underwater.isGlobal = true; _underwater.sharedProfile = _underwaterProfile; _underwater.priority = 10; _underwater.weight = 0;
        }

        // ---- the sun in the sky: a bright disc on the sun's direction (the gradient sky has none), about 1.8 degrees wide, glowing through the bloom ----
        private GameObject _sunDisc; private Material _sunDiscMaterial;
        public const float SunDiscDistance = 1800f, SunDiscNits = 80000f;

        private void BuildSunDisc()
        {
            _sunDisc = GameObject.CreatePrimitive(PrimitiveType.Sphere); _sunDisc.name = "Sun Disc"; _sunDisc.transform.SetParent(transform, false);
            var collider = _sunDisc.GetComponent<Collider>(); if (collider != null) { if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            var renderer = _sunDisc.GetComponent<MeshRenderer>(); renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var prototype = Resources.Load<Material>("LifeVerse/SunMaterial");
            if (prototype != null) { _sunDiscMaterial = new Material(prototype); renderer.sharedMaterial = _sunDiscMaterial; }
            _sunDisc.transform.localScale = Vector3.one * (SunDiscDistance * 2f * Mathf.Tan(.9f * Mathf.Deg2Rad));
        }

        /// <summary>How visible the sun disc is, 0 to 1: gone below the horizon and behind a heavy sky.</summary>
        public float SunDiscVisibility { get; private set; }

        private void UpdateSunDisc(float elevation, float above, float overcast)
        {
            SunDiscVisibility = above * Mathf.Clamp01(1f - overcast * 1.25f);
            var origin = _camera != null ? _camera.transform.position : Vector3.zero;
            _sunDisc.transform.position = origin - _sun.transform.forward * SunDiscDistance;
            _sunDisc.SetActive(SunDiscVisibility > .01f);
            if (_sunDiscMaterial != null) _sunDiscMaterial.SetColor("_EmissiveColor", new Color(1f, .96f, .82f) * (SunDiscNits * SunDiscVisibility));
        }

        private void BuildRain()
        {
            var rainObject = new GameObject("Rain"); rainObject.transform.SetParent(transform, false); rainObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // a box emits along its forward: down
            _rain = rainObject.AddComponent<ParticleSystem>(); _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _rain.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 9000;
            main.startLifetime = 1.1f; main.startSpeed = new ParticleSystem.MinMaxCurve(28f, 34f); main.startSize = .045f; main.gravityModifier = .15f;
            var shape = _rain.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(60f, 60f, .5f);
            var emission = _rain.emission; emission.rateOverTime = 0f;
            var renderer = _rain.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.lengthScale = 3f; renderer.velocityScale = .04f; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var material = Resources.Load<Material>("LifeVerse/RainMaterial"); if (material != null) renderer.sharedMaterial = material;
        }

        private void UpdateRain(string weather)
        {
            var amount = (weather == "storm" ? 1f : weather == "rain" ? .6f : 0f) * RainQuality; RainAmount = amount;
            var emission = _rain.emission; emission.rateOverTime = 7000f * amount;
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
            // sky colours: day and night blend by the day factor; near the horizon, with the sun low, the horizon turns orange
            var lowSun = 1f - Mathf.Clamp01(Mathf.Abs(Mathf.Sin((hour - 6f) / 12f * Mathf.PI)) * 3f); var above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.04f, .02f, elevation));
            // the colours are mixed in linear light, as the Godot sky shader does (mixing the written values would make dawn and dusk several times too dark)
            var horizon = Color.Lerp(HorizonNight.linear, HorizonDay.linear, day); var glow = lowSun * above; horizon = Color.Lerp(horizon, Sunset.linear, glow * .05f); _sky.multiplier.value = SkyNits;   // the Godot sky turns orange only towards the sun; this sky has no direction, so it takes a hint of it
            var overcast = WeatherShown switch { "cloudy" => .3f, "rain" => .6f, "storm" => .8f, "fog" => .7f, _ => 0f };
            Color Dull(Color c) { var grey = c.grayscale * .9f; return Color.Lerp(c, new Color(grey, grey, grey * 1.05f), overcast); }
            // a tint chosen for the sky (a parcel or the island) colours it by up to 55 % while the look is in force: colour * tint * 1.6, as the Godot sky shader does
            var tint = _lastLook.Sky != null && LifeVerse.Shared.Land.ParcelEnvironment.IsColour(_lastLook.Sky) && ColorUtility.TryParseHtmlString(_lastLook.Sky, out var tintColor) ? tintColor.linear : Color.white; var tintAmount = _lastLook.Sky == null ? 0f : .55f * _blend;
            Color Tinted(Color c) => tintAmount > 0f ? Color.Lerp(c, new Color(c.r * tint.r * 1.6f, c.g * tint.g * 1.6f, c.b * tint.b * 1.6f), tintAmount) : c;
            _sky.top.value = Tinted(Dull(Color.Lerp(ZenithNight.linear, ZenithDay.linear, day))); _sky.middle.value = Tinted(Dull(horizon)); _sky.bottom.value = Tinted(Dull(horizon * .8f)); _sky.multiplier.value = _sky.multiplier.value * (1f - .4f * overcast);
            var weather = WeatherShown; var cover = weather switch { "cloudy" => .72f, "rain" => .85f, "storm" => .95f, "fog" => .8f, _ => .5f };
            var dim = weather switch { "cloudy" => .75f, "rain" => .55f, "storm" => .4f, "fog" => .6f, _ => 1f };
            // the sun stays on (the sky is lit by it) and fades with the day; at night a faint bluish moon lights the ground from the opposite side
            // never exactly zero: with a dead sun the HDRP sky falls back to a default daylight sun and the whole night turns white
            var sunLux = Mathf.Max(NoonLux * day * LightShown * dim, .05f); _sunData.SetIntensity(sunLux, LightUnit.Lux);
            _moon.transform.rotation = Quaternion.LookRotation(-_sun.transform.forward); _moonData.SetIntensity(MoonLux * (1f - day) * LightShown, LightUnit.Lux);
            _exposure.fixedExposure.value = 13.5f; // a daylight camera at all hours: the night is dark because its light and its sky are dark, not because the camera opens up
            var targetFog = Mathf.Clamp01((weather switch { "fog" => 1f, "rain" => .45f, "storm" => .7f, "cloudy" => .2f, _ => 0f }) + (_lastLook.Fog ?? 0f) * _blend);
            UpdateSunDisc(elevation, above, overcast);
            _fogDensity = Mathf.MoveTowards(_fogDensity, targetFog, Mathf.Clamp01(delta * 2f) * 2f);
            _fog.meanFreePath.value = Mathf.Lerp(8000f, weather == "fog" ? 90f : weather == "storm" ? 350f : 700f, _fogDensity);
            _clouds.opacity.value = Mathf.Lerp(.35f, 1f, cover); _clouds.layerA.opacityR.value = Mathf.Lerp(.5f, 1f, cover);
            var ambient = Mathf.Lerp(AmbientNight, AmbientDay, day) * LightShown; _indirect.indirectDiffuseLightingMultiplier.value = ambient;
            Publish(day, sunLux, ambient, _sky.multiplier.value / SkyNits);
            UpdateRain(weather);
            if (_camera != null)
            {
                var target = _camera.transform.position.y < _seaLevel ? 1f : 0f;
                if (target > 0f && Mathf.Abs(day - _underDay) > .05f) { _underDay = day; ApplyTonemap(); }
                _underwaterWeight = Mathf.MoveTowards(_underwaterWeight, target, delta * 6f); _underwater.weight = _underwaterWeight;
            }
        }

        /// <summary>Tells the shaders that light themselves (the sea) where the sun is and what the sky looks like, in Godot's units (1 is white).</summary>
        private void Publish(float day, float sunLux, float ambient, float skyScale)
        {
            var top = _sky.top.value * skyScale; var horizon = _sky.middle.value * skyScale; var sun = sunLux / (Mathf.PI * ExposureNits);
            Shader.SetGlobalVector("_LVSunDir", -_sun.transform.forward);
            Shader.SetGlobalVector("_LVSunLight", new Vector4(sun, sun, sun, 1f));
            Shader.SetGlobalVector("_LVSkyTop", top); Shader.SetGlobalVector("_LVSkyHorizon", horizon);
            Shader.SetGlobalVector("_LVAmbient", Color.Lerp(horizon, top, .55f) * ambient);
            Shader.SetGlobalVector("_LVFog", new Vector4(horizon.r, horizon.g, horizon.b, _fog.active && _fogDensity > .01f ? _fog.meanFreePath.value : 0f));
            Shader.SetGlobalFloat("_LVDay", day); Shader.SetGlobalFloat("_LVClouds", _clouds.layerA.opacityR.value * _clouds.opacity.value);
            Shader.SetGlobalTexture("_LVCloudMap", CloudMap());
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile); if (_underwaterProfile != null) Destroy(_underwaterProfile); if (_cloudMap != null) Destroy(_cloudMap);
        }
    }
}
