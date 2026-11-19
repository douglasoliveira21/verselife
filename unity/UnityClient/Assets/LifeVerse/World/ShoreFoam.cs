using LifeVerse.Shared.Terrain;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// Surf foam on the shore, drawn per pixel: a flat sheet at the sea level over the terrain whose shader reads the depth of the water at every pixel from a height
    /// texture (one texel per terrain vertex, filtered smoothly) and puts lace where it is shallow. The texture is baked again whenever the terrain, its map or the sea
    /// level change, so terraforming and tides move the foam. Visual only. Sits beside the <see cref="OceanView"/> and reads the sea level from it.
    /// </summary>
    public sealed class ShoreFoam : MonoBehaviour
    {
        public TerrainView Terrain;
        public OceanView Ocean;
        private Material _material; private Texture2D _heights, _noise; private Transform _sheet;
        private TerrainMap _bakedMap; private long _bakedRevision = -1; private float _bakedSea = float.NaN;
        public bool Baked { get; private set; }
        public Material Material => _material;

        private void Awake()
        {
            var prototype = Resources.Load<Material>("LifeVerse/FoamMaterial");
            if (prototype == null) { enabled = false; return; }
            _material = new Material(prototype);
            _noise = ProceduralTextures.GreyNoise(256, 5, 4, 17); _material.SetTexture("_Noise", _noise);
            var sheet = new GameObject("ShoreFoam"); sheet.transform.SetParent(transform, false); _sheet = sheet.transform;
            sheet.AddComponent<MeshFilter>().sharedMesh = SheetMesh();
            var renderer = sheet.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private static Mesh SheetMesh()
        {
            var mesh = new Mesh { name = "FoamSheet" }; const float half = .5f;
            mesh.vertices = new[] { new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 1f)); return mesh;
        }

        private void LateUpdate()
        {
            if (_material == null) return;
            if (Terrain == null) Terrain = transform.parent != null ? transform.parent.GetComponentInChildren<TerrainView>() : FindAnyObjectByType<TerrainView>();
            if (Terrain == null || Terrain.Map == null) return;
            var map = Terrain.Map;
            if (map != _bakedMap || map.Revision != _bakedRevision || !Mathf.Approximately(map.SeaLevel, _bakedSea)) Bake(map);
        }

        /// <summary>Bakes the height texture now (also called by tests).</summary>
        public void Bake(TerrainMap map)
        {
            var side = map.VertexCount;
            if (_heights == null || _heights.width != side) { if (_heights != null) Destroy(_heights); _heights = new Texture2D(side, side, TextureFormat.RFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear }; }
            var data = new float[side * side]; for (var z = 0; z < side; z++) for (var x = 0; x < side; x++) data[z * side + x] = map.HeightAt(x, z);
            _heights.SetPixelData(data, 0); _heights.Apply(false);
            var half = map.HalfExtent; var span = half * 2f; var margin = TerrainMap.OceanMargin;
            _material.SetTexture("_Height", _heights); _material.SetVector("_Area", new Vector4(-half, -half, span, span)); _material.SetFloat("_Sea", map.SeaLevel);
            // the sheet covers the terrain and a skirt around it; beyond the terrain edge the height texture is clamped, and the map border is deep water, so no foam there
            _sheet.localScale = new Vector3(span + margin * 2f, 1f, span + margin * 2f); _sheet.position = new Vector3(0f, map.SeaLevel + .03f, 0f);
            _bakedMap = map; _bakedRevision = map.Revision; _bakedSea = map.SeaLevel; Baked = true;
        }

        private void OnDestroy() { if (_material != null) Destroy(_material); if (_heights != null) Destroy(_heights); if (_noise != null) Destroy(_noise); }
    }
}
