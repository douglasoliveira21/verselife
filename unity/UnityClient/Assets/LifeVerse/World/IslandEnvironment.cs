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
        public const float NoonLux = 100000f, MoonLux = 4000f;

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
        private GradientSky _sky;
        private ParticleSystem _rain;
        /// <summary>0 to 1: how much rain falls now (the player can turn weather effects down in the graphics settings).</summary>
        public float RainQuality = 1f;
        public float RainAmount { get; private set; }
        public const float SkyNits = 14000f;
        private static readonly Color ZenithDay = new Color(.17f, .42f, .84f), HorizonDay = new Color(.74f, .86f, .97f), ZenithNight = new Color(.004f, .01f, .045f), HorizonNight = new Color(.03f, .045f, .1f), Sunset = new Color(1f, .52f, .22f);
        private Fog _fog;
        private CloudLayer _clouds;
        private Texture2D _cloudMap;
        private float _fogDensity;
        private Camera _camera;
        private float _seaLevel = -.8f;
        private float _underwaterWeight;

        // what the parcel under the player changes, and how far that has taken over (0 to 1)
        private float? _parcelHour; private string _parcelWeather; private float _parcelLight = 1f; private bool _parcelOn; private float _blend;

        public float HourShown => _parcelHour is { } target && _blend > 0 ? Mathf.Repeat(Hour + ShortestHourDelta(Hour, target) * _blend, 24f) : Hour;
        public string WeatherShown => _parcelWeather != null && _blend >= .5f ? _parcelWeather : Weather;
        public float LightShown => Mathf.Lerp(1f, _parcelLight, _blend);
        public Light Sun { get { Create(); return _sun; } }
        public Volume Volume => _volume;

        private static float ShortestHourDelta(float from, float to) { var d = Mathf.Repeat(to - from, 24f); return d > 12f ? d - 24f : d; }

        public void SetParcelLook(float? hour, string weather, float light) { _parcelHour = hour; _parcelWeather = weather; _parcelLight = light; _parcelOn = hour != null || weather != null || !Mathf.Approximately(light, 1f); }

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
            _sky = _profile.Add<GradientSky>(); _sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier); _sky.multiplier.Override(SkyNits); _sky.gradientDiffusion.Override(1f); _sky.top.Override(Color.blue); _sky.middle.Override(Color.cyan); _sky.bottom.Override(Color.gray); // overridden here so the values set every frame count
            _exposure = _profile.Add<Exposure>(); _exposure.mode.Override(ExposureMode.Fixed); _exposure.fixedExposure.Override(13.5f);
            BuildRain();
            _fog = _profile.Add<Fog>(); _fog.enabled.Override(true); _fog.meanFreePath.Override(6000f); _fog.baseHeight.Override(-50f); _fog.maximumHeight.Override(60f);
            _fog.enableVolumetricFog.Override(false); _fog.colorMode.Override(FogColorMode.SkyColor);
            _profile.Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            _clouds = _profile.Add<CloudLayer>(); _clouds.opacity.Override(1f);
            _cloudMap = ProceduralTextures.Clouds(512, 5); _clouds.layerA.cloudMap.Override(_cloudMap); _clouds.layerA.opacityR.Override(1f); _clouds.layerA.opacityG.Override(0f); _clouds.layerA.opacityB.Override(0f); _clouds.layerA.opacityA.Override(0f);
            _volume = new GameObject("World Volume").AddComponent<Volume>(); _volume.transform.SetParent(transform, false); _volume.isGlobal = true; _volume.sharedProfile = _profile; _volume.priority = 0;

            // under the water: murky blue fog and a colour filter, blended in as the camera goes below the sea
            _underwaterProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var waterFog = _underwaterProfile.Add<Fog>(); waterFog.enabled.Override(true); waterFog.meanFreePath.Override(40f); waterFog.colorMode.Override(FogColorMode.ConstantColor); waterFog.color.Override(new Color(.04f, .30f, .44f)); waterFog.baseHeight.Override(-200f); waterFog.maximumHeight.Override(200f);
            var filter = _underwaterProfile.Add<ColorAdjustments>(); filter.colorFilter.Override(new Color(.55f, .85f, 1f)); filter.postExposure.Override(.8f);
            _underwater = new GameObject("Underwater Volume").AddComponent<Volume>(); _underwater.transform.SetParent(transform, false); _underwater.isGlobal = true; _underwater.sharedProfile = _underwaterProfile; _underwater.priority = 10; _underwater.weight = 0;
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
            _blend = Mathf.MoveTowards(_blend, _parcelOn ? 1f : 0f, Time.deltaTime / BlendSeconds);
            Apply(Time.deltaTime);
        }

        /// <summary>Puts the sun, exposure and fog where the hour and the weather say. Called every frame; also by tests and the preview to settle in one go.</summary>
        public void Apply(float delta)
        {
            Create();
            var hour = HourShown; var elevation = Mathf.Sin((hour - 6f) / 12f * Mathf.PI);
            var day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.08f, .3f, elevation)); DayFactor = day;
            // x = 90 is noon (straight down), 0 sunrise, 180 sunset; built from the direction with the world up, so the light never has a rolled (upside-down) frame after noon
            var sunDirection = Quaternion.Euler((hour - 6f) / 12f * 180f, 152f, 0f) * Vector3.forward;
            _sun.transform.rotation = Mathf.Abs(sunDirection.y) > .999f ? Quaternion.Euler(Mathf.Sign(-sunDirection.y) * 90f, 152f, 0f) : Quaternion.LookRotation(sunDirection, Vector3.up);
            // sky colours: day and night blend by the day factor; near the horizon, with the sun low, the horizon turns orange
            var lowSun = 1f - Mathf.Clamp01(Mathf.Abs(Mathf.Sin((hour - 6f) / 12f * Mathf.PI)) * 3f); var above = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.04f, .02f, elevation));
            var horizon = Color.Lerp(HorizonNight, HorizonDay, day); var glow = lowSun * above; horizon = Color.Lerp(horizon, Sunset, glow * .9f); _sky.multiplier.value = SkyNits * (1f + 2.5f * glow);
            var overcast = WeatherShown switch { "cloudy" => .3f, "rain" => .6f, "storm" => .8f, "fog" => .7f, _ => 0f };
            Color Dull(Color c) { var grey = c.grayscale * .9f; return Color.Lerp(c, new Color(grey, grey, grey * 1.05f), overcast); }
            _sky.top.value = Dull(Color.Lerp(ZenithNight, ZenithDay, day)).linear; _sky.middle.value = Dull(horizon).linear; _sky.bottom.value = Dull(horizon * .8f).linear; _sky.multiplier.value = _sky.multiplier.value * (1f - .4f * overcast);
            var weather = WeatherShown; var cover = weather switch { "cloudy" => .72f, "rain" => .85f, "storm" => .95f, "fog" => .8f, _ => .5f };
            var dim = weather switch { "cloudy" => .75f, "rain" => .55f, "storm" => .4f, "fog" => .6f, _ => 1f };
            // the sun stays on (the sky is lit by it) and fades with the day; at night a faint bluish moon lights the ground from the opposite side
            // never exactly zero: with a dead sun the HDRP sky falls back to a default daylight sun and the whole night turns white
            var sunLux = Mathf.Max(NoonLux * day * LightShown * dim, .05f); _sunData.SetIntensity(sunLux, LightUnit.Lux);
            _moon.transform.rotation = Quaternion.LookRotation(-_sun.transform.forward); _moonData.SetIntensity(MoonLux * (1f - day) * LightShown, LightUnit.Lux);
            _exposure.fixedExposure.value = 13.5f; // a daylight camera at all hours: the night is dark because its light and its sky are dark, not because the camera opens up
            var targetFog = weather switch { "fog" => 1f, "rain" => .45f, "storm" => .7f, "cloudy" => .2f, _ => 0f };
            _fogDensity = Mathf.MoveTowards(_fogDensity, targetFog, Mathf.Clamp01(delta * 2f) * 2f);
            _fog.meanFreePath.value = Mathf.Lerp(8000f, weather == "fog" ? 90f : weather == "storm" ? 350f : 700f, _fogDensity);
            _clouds.opacity.value = Mathf.Lerp(.35f, 1f, cover); _clouds.layerA.opacityR.value = Mathf.Lerp(.5f, 1f, cover);
            UpdateRain(weather);
            if (_camera != null)
            {
                var target = _camera.transform.position.y < _seaLevel ? 1f : 0f;
                _underwaterWeight = Mathf.MoveTowards(_underwaterWeight, target, delta * 6f); _underwater.weight = _underwaterWeight;
            }
        }

        private void OnDestroy()
        {
            if (_profile != null) Destroy(_profile); if (_underwaterProfile != null) Destroy(_underwaterProfile); if (_cloudMap != null) Destroy(_cloudMap);
        }
    }
}
