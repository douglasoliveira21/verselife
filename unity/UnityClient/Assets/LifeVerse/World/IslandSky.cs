using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The sky of an island: the sky shader of the Godot client as an HDRP sky. Zenith and horizon for day and night, orange only towards a low sun, the sun's disc and glow, the
    /// same drifting clouds, the colour a parcel or the island gives it, and the weather's fog covering it. All colours are linear, in Godot's units (1 is white);
    /// <see cref="IslandEnvironment"/> fills them in.
    /// </summary>
    [VolumeComponentMenu("Sky/LifeVerse Island Sky")]
    [SkyUniqueID(UniqueId)]
    public sealed class IslandSky : SkySettings
    {
        public const int UniqueId = 7531;

        [Tooltip("Towards the sun.")] public Vector3Parameter sunDirection = new Vector3Parameter(Vector3.up);
        public FloatParameter day = new FloatParameter(1f);
        public FloatParameter cloudCover = new FloatParameter(IslandEnvironment.CloudCover);
        public Vector4Parameter fog = new Vector4Parameter(Vector4.zero);       // colour, and how much of the sky it takes (0 to 1)
        public Vector4Parameter tint = new Vector4Parameter(new Vector4(1f, 1f, 1f, 0f));   // colour, and how much (0 to 0.55)
        public Texture2DParameter cloudNoise = new Texture2DParameter(null);

        public override Type GetSkyRendererType() => typeof(IslandSkyRenderer);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = base.GetHashCode();
                foreach (var parameter in new VolumeParameter[] { sunDirection, day, cloudCover, fog, tint, cloudNoise }) hash = hash * 23 + parameter.GetHashCode();
                return hash;
            }
        }

        public override int GetHashCode(Camera camera) => GetHashCode();
    }

    internal sealed class IslandSkyRenderer : SkyRenderer
    {
        private static readonly int SunId = Shader.PropertyToID("_IslandSun"), WeatherId = Shader.PropertyToID("_IslandWeather"), FogId = Shader.PropertyToID("_IslandFog"),
            TintId = Shader.PropertyToID("_IslandTint"), NoiseId = Shader.PropertyToID("_IslandClouds"), IntensityId = Shader.PropertyToID("_IslandIntensity"), ViewId = Shader.PropertyToID("_PixelCoordToViewDirWS");
        private Material _material; private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        public override void Build()
        {
            var shader = Resources.Load<Shader>("LifeVerse/IslandSky"); if (shader != null) _material = CoreUtils.CreateEngineMaterial(shader);
        }

        public override void Cleanup() { CoreUtils.Destroy(_material); _material = null; }

        protected override bool Update(BuiltinSkyParameters builtinParams) => false;

        public override void RenderSky(BuiltinSkyParameters builtinParams, bool renderForCubemap, bool renderSunDisk)
        {
            if (_material == null || !(builtinParams.skySettings is IslandSky sky)) return;
            var sun = sky.sunDirection.value;
            _block.SetVector(SunId, new Vector4(sun.x, sun.y, sun.z, sky.day.value));
            _block.SetVector(WeatherId, new Vector4(sky.cloudCover.value, 0f, 0f, 0f));
            _block.SetVector(FogId, sky.fog.value);
            _block.SetVector(TintId, sky.tint.value);
            _block.SetFloat(IntensityId, GetSkyIntensity(sky, builtinParams.debugSettings));
            _block.SetTexture(NoiseId, sky.cloudNoise.value != null ? sky.cloudNoise.value : (Texture)Texture2D.blackTexture);
            _block.SetMatrix(ViewId, builtinParams.pixelCoordToViewDirMatrix);
            CoreUtils.DrawFullScreen(builtinParams.commandBuffer, _material, _block, renderForCubemap ? 0 : 1);
        }
    }
}
