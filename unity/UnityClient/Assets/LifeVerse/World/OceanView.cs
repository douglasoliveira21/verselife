using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Open ocean around the island: one large water surface that follows the camera so it reaches the horizon. The mesh is a grid whose cells grow with the distance
    /// from its centre (fine under the camera, 50 m at the edge), and its vertices ride the same waves as the Godot ocean (a sum of four sines of the world position,
    /// 0.22 m high, fading out with distance), recomputed every frame. Its shader (LifeVerse/Ocean) is the water of the Godot client: colour by depth, sliding ripples, foam on the shallows,
    /// the sky mirrored and the sun's glint, and seen from below a translucent ceiling (double sided). Swimming physics are the server's
    /// (AuthoritativeMovement); this is visual only. The surface sits at the sea level of the terrain map.
    /// </summary>
    public sealed class OceanView : MonoBehaviour
    {
        public const float PlaneSize = 2600f, SnapStep = 2f, WaveHeight = .22f, WaveFadeStart = 140f, WaveFadeEnd = 300f;
        private const int Cells = 160; // per side; cells grow as |u|^3, so the centre is fine and the rim coarse

        public Camera Camera;
        public float SeaLevel = -.8f;
        public bool Waves = true; private bool _flat;
        private Transform _plane;
        private Material _material;
        /// <summary>The full sea (three layers of ripples, lacy foam, clouds in the mirror), or the cheaper one of the low presets.</summary>
        public bool Detailed { get; set; } = true;
        private Texture2D _swell, _ripples, _foam;
        public Material Material => _material;
        private Mesh _mesh;
        private Vector3[] _local, _world;
        private bool _animate = true;

        public bool Animate { get => _animate; set => _animate = value; }
        public bool IsUnderwater { get; private set; }

        /// <summary>The height the waves add at a place and a time (a sum of four sines, as the Godot ocean shader does), without the distance fade.</summary>
        public static float Wave(float x, float z, float t) =>
            Mathf.Sin(x * .11f + t * .9f) * .5f + Mathf.Sin(z * .17f - t * 1.1f) * .35f + Mathf.Sin((x + z) * .07f + t * .6f) * .55f + Mathf.Sin((x - z) * .23f + t * 1.4f) * .15f;

        private void Awake()
        {
            var prototype = Resources.Load<Material>("LifeVerse/OceanMaterial");
            _material = prototype != null ? new Material(prototype) : new Material(Shader.Find("LifeVerse/Ocean"));
            // the same noises as the Godot sea: about ten and twenty-three waves across the two normal maps, and the lace of the foam
            _swell = ProceduralTextures.NormalMap(512, 10, 4, 3, 7f); _ripples = ProceduralTextures.NormalMap(512, 23, 3, 9, 5f); _foam = ProceduralTextures.GreyNoise(256, 8, 3, 17);
            _material.SetTexture("_NormalA", _swell); _material.SetTexture("_NormalB", _ripples); _material.SetTexture("_FoamNoise", _foam);
            var plane = new GameObject("OceanPlane"); plane.transform.SetParent(transform, false); _plane = plane.transform;
            plane.AddComponent<MeshFilter>().sharedMesh = BuildMesh();
            var renderer = plane.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;   // a plane of water needs neither: they only cost every pixel of it
        }

        private static float Axis(int i) { var u = (float)i / Cells * 2f - 1f; return Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), 3f) * PlaneSize * .5f; }

        private Mesh BuildMesh()
        {
            var side = Cells + 1; _local = new Vector3[side * side]; _world = new Vector3[side * side]; var uv = new Vector2[side * side]; var normals = new Vector3[side * side]; var indices = new int[Cells * Cells * 6]; var n = 0;
            for (var z = 0; z < side; z++)
                for (var x = 0; x < side; x++)
                {
                    var i = z * side + x; _local[i] = new Vector3(Axis(x), 0, Axis(z)); _world[i] = _local[i];
                    uv[i] = new Vector2(_local[i].x, _local[i].z); // metres: the material tiling turns them into texture repeats
                    normals[i] = Vector3.up;
                }
            for (var z = 0; z < Cells; z++)
                for (var x = 0; x < Cells; x++) { var a = z * side + x; indices[n++] = a; indices[n++] = a + side; indices[n++] = a + 1; indices[n++] = a + 1; indices[n++] = a + side; indices[n++] = a + side + 1; }
            _mesh = new Mesh { name = "OceanGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };
            _mesh.MarkDynamic(); _mesh.vertices = _local; _mesh.normals = normals; _mesh.uv = uv; _mesh.triangles = indices; _mesh.bounds = new Bounds(Vector3.zero, new Vector3(PlaneSize, 40f, PlaneSize));
            return _mesh;
        }

        private void LateUpdate()
        {
            var camera = Camera != null ? Camera : UnityEngine.Camera.main; if (camera == null) return;
            var position = camera.transform.position;
            var planePosition = new Vector3(Mathf.Round(position.x / SnapStep) * SnapStep, SeaLevel, Mathf.Round(position.z / SnapStep) * SnapStep);
            _plane.position = planePosition; IsUnderwater = position.y < SeaLevel;
            var time = _animate ? Time.time : 0f;
            if (Waves) { DisplaceVertices(planePosition, position, time); _flat = false; }
            else if (!_flat) { _flat = true; _mesh.vertices = _local; }   // the waves were turned off: the sea lies flat
            _material.SetFloat("_WaveTime", time); _material.SetFloat("_Detail", Detailed ? 1f : 0f);
        }

        private void DisplaceVertices(Vector3 planePosition, Vector3 camera, float time)
        {
            for (var i = 0; i < _local.Length; i++)
            {
                var wx = _local[i].x + planePosition.x; var wz = _local[i].z + planePosition.z;
                var dx = wx - camera.x; var dz = wz - camera.z; var distance = Mathf.Sqrt(dx * dx + dz * dz);
                var fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WaveFadeStart, WaveFadeEnd, distance));
                _world[i] = new Vector3(_local[i].x, fade > 0f ? Wave(wx, wz, time) * WaveHeight * fade : 0f, _local[i].z);
            }
            _mesh.vertices = _world;
        }

        /// <summary>The height of the water surface at a place right now (the sea level plus the wave under the plane's current fade), for things that float.</summary>
        public float SurfaceAt(float worldX, float worldZ, Vector3 camera)
        {
            var dx = worldX - camera.x; var dz = worldZ - camera.z; var distance = Mathf.Sqrt(dx * dx + dz * dz);
            var fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WaveFadeStart, WaveFadeEnd, distance));
            return SeaLevel + (Waves ? Wave(worldX, worldZ, _animate ? Time.time : 0f) * WaveHeight * fade : 0f);
        }

        private void OnDestroy() { if (_material != null) Destroy(_material); if (_mesh != null) Destroy(_mesh); if (_swell != null) Destroy(_swell); if (_ripples != null) Destroy(_ripples); if (_foam != null) Destroy(_foam); }
    }
}
