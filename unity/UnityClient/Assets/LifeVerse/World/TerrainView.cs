using System;
using LifeVerse.Shared.Terrain;
using UnityEngine;

namespace LifeVerse.Unity.World
{
    /// <summary>
    /// The island ground. The island server owns the authoritative <see cref="TerrainMap"/>; this component draws it: a mesh with the heights of the map and one
    /// albedo texture in which the eight ground layers (grass, dirt, sand, rock, gravel, snow, mud, dry grass) are blended by the splat weights of the map.
    /// The Godot client blends the layers in a shader per pixel; HDRP has no such shader without a Shader Graph asset, so the blend is baked on the CPU and a
    /// tiled grain detail map brings back the fine texture up close (decision D5, recorded in MIGRATION_PROGRESS.md).
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        /// <summary>The eight ground materials: name, the two colours of its procedural texture, and its colour on the minimap (the same as the Godot client).</summary>
        public static readonly (string Name, Color Dark, Color Light)[] Layers =
        {
            ("Grama", Hex("#3f7a35"), Hex("#78ad4e")), ("Terra", Hex("#5b4030"), Hex("#8a6a49")), ("Areia", Hex("#c9b57f"), Hex("#e6d7a5")), ("Rocha", Hex("#55565b"), Hex("#9a9a9c")),
            ("Cascalho", Hex("#6b6a64"), Hex("#aaa69c")), ("Neve", Hex("#d9e4ec"), Hex("#ffffff")), ("Lama", Hex("#35261c"), Hex("#5a4130")), ("Grama seca", Hex("#85823f"), Hex("#c4b768")),
        };

        public const float TilesPerMeter = .2f;
        /// <summary>How bright the ground is drawn: the HDRP sun and sky light it brighter than the Godot renderer does, so the baked colours are toned down to look the same.</summary>
        public const float GroundTone = .68f;
        private static Color32[][] _layerPixels;
        private static float[] _macro;
        private const int LayerSize = 256;

        private static Color Hex(string value) { ColorUtility.TryParseHtmlString(value, out var color); return color; }

        private TerrainMap _map = new TerrainMap();
        private Mesh _mesh;
        private Texture2D _albedo;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private MeshCollider _collider;
        private Material _material;
        /// <summary>Texels per side of the baked ground texture.</summary>
        public int AlbedoSize = 1024;

        public TerrainMap Map => _map;
        public float SeaLevel => _map.SeaLevel;

        public static float HeightOf(TerrainMap map, float worldX, float worldZ) => map.SampleHeight(worldX, worldZ);

        private bool _created;
        private void Awake() => Create();

        /// <summary>Done lazily as well as in Awake, because edit-mode tests add the component without Awake running.</summary>
        private void Create()
        {
            if (_created) return; _created = true;
            _filter = gameObject.AddComponent<MeshFilter>(); _renderer = gameObject.AddComponent<MeshRenderer>();
            var prototype = Resources.Load<Material>("LifeVerse/TerrainMaterial");
            _material = prototype != null ? new Material(prototype) : new Material(Shader.Find("HDRP/Lit"));
            _renderer.sharedMaterial = _material;
            // like the Godot terrain (CastShadow Off): the ground receives the shadows of objects but does not cast its own, which in HDRP shadowed the whole ground around the camera
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _collider = gameObject.AddComponent<MeshCollider>(); // the ground the avatar will stand on; rebuilt with the mesh (the server simulates the same heights)
        }

        public void Apply(TerrainMap map) { Create(); _map = map; Rebuild(); }

        public void Rebuild() { RebuildMesh(); BakeAlbedo(0, AlbedoSize - 1, 0, AlbedoSize - 1); ApplyMaterial(); _dirty = false; }

        private void ApplyMaterial()
        {
            _material.SetTexture("_BaseColorMap", _albedo); _material.SetColor("_BaseColor", new Color(GroundTone, GroundTone, GroundTone, 1f));
            // the detail map repeats every metre over the whole island
            _material.SetTextureScale("_DetailMap", new Vector2(_map.Cells * _map.CellSize, _map.Cells * _map.CellSize)); _material.SetFloat("_DetailAlbedoScale", 0f);   // no grain: the Godot ground has none
        }

        private void RebuildMesh()
        {
            var count = _map.VertexCount; var cells = _map.Cells; var size = _map.CellSize; var half = cells * size / 2f;
            var vertices = new Vector3[count * count]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
            for (var z = 0; z < count; z++)
                for (var x = 0; x < count; x++)
                {
                    var i = z * count + x;
                    vertices[i] = GameSpace.ToUnity(x * size - half, _map.HeightAt(x, z), z * size - half); // the game's z axis is flipped on screen (see GameSpace)
                    normals[i] = GameSpace.ToUnity(new Vector3(_map.HeightAt(x - 1, z) - _map.HeightAt(x + 1, z), 2f * size, _map.HeightAt(x, z - 1) - _map.HeightAt(x, z + 1)).normalized);
                    uv[i] = new Vector2((float)x / cells, (float)z / cells);
                }
            var indices = new int[cells * cells * 6]; var n = 0;
            for (var z = 0; z < cells; z++)
                for (var x = 0; x < cells; x++)
                {
                    var a = z * count + x; // the same order as the Godot client: with z flipped it is clockwise seen from above, the front face in Unity
                    indices[n++] = a; indices[n++] = a + 1; indices[n++] = a + count;
                    indices[n++] = a + 1; indices[n++] = a + count + 1; indices[n++] = a + count;
                }
            if (_mesh == null) _mesh = new Mesh { name = "IslandTerrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _mesh.Clear(); _mesh.vertices = vertices; _mesh.normals = normals; _mesh.uv = uv; _mesh.triangles = indices; _mesh.RecalculateBounds(); _mesh.RecalculateTangents();
            _filter.sharedMesh = _mesh;
            _collider.sharedMesh = null; _collider.sharedMesh = _mesh;
        }

        /// <summary>The first point of the ground on a ray, or null. Uses the collider, so it follows every edit the server sent.</summary>
        public Vector3? Raycast(Ray ray, float distance = 1000f) // a ray of the Unity scene; the answer is a Unity point (GameSpace.ToGame turns it back)
        {
            Physics.SyncTransforms();
            return _collider != null && _collider.Raycast(ray, out var hit, distance) ? hit.point : (Vector3?)null;
        }

        /// <summary>The ground height under a place, or null off the island. Same answer as the server's TerrainMap.SampleHeight, through the collider.</summary>
        public float? GroundHeight(float worldX, float worldZ) => Raycast(new Ray(GameSpace.ToUnity(worldX, TerrainMap.MaxHeight + 10f, worldZ), Vector3.down), TerrainMap.MaxHeight - TerrainMap.MinHeight + 20f) is { } hit ? hit.y : (float?)null;

        /// <summary>Large, slow variation over the island (periodic over 256 m): bends the tiles of the ground layers and lights and darkens big patches, so the 5 m repeat of the layer texture does not show.</summary>
        private static float[] Macro() => _macro ??= ProceduralTextures.Fbm(128, 4, 4, 77);

        private static float MacroAt(float worldX, float worldZ, int shift)
        {
            var m = Macro(); var u = Mathf.Repeat(worldX / 256f + shift * .37f, 1f) * 128f; var v = Mathf.Repeat(worldZ / 256f + shift * .61f, 1f) * 128f;
            var x0 = (int)u & 127; var y0 = (int)v & 127; var tx = u - Mathf.Floor(u); var ty = v - Mathf.Floor(v);
            return Mathf.Lerp(Mathf.Lerp(m[y0 * 128 + x0], m[y0 * 128 + ((x0 + 1) & 127)], tx), Mathf.Lerp(m[((y0 + 1) & 127) * 128 + x0], m[((y0 + 1) & 127) * 128 + ((x0 + 1) & 127)], tx), ty);
        }

        private static Color32[][] LayerPixels()
        {
            if (_layerPixels != null) return _layerPixels;
            _layerPixels = new Color32[Layers.Length][];
            for (var i = 0; i < Layers.Length; i++) _layerPixels[i] = ProceduralTextures.Ground(Layers[i].Dark, Layers[i].Light, 11 + i * 13, LayerSize);
            return _layerPixels;
        }

        /// <summary>Blends the eight layers by the splat weights of the map into one texture covering the island (the same blend as the Godot shader, with a second, larger sample that breaks up the tiling).</summary>
        private void BakeAlbedo(int i0, int i1, int j0, int j1)
        {
            var layers = LayerPixels(); var side = AlbedoSize; var cells = _map.Cells; var size = _map.CellSize; var half = cells * size / 2f;
            i0 = Mathf.Clamp(i0, 0, side - 1); i1 = Mathf.Clamp(i1, i0, side - 1); j0 = Mathf.Clamp(j0, 0, side - 1); j1 = Mathf.Clamp(j1, j0, side - 1);
            var width = i1 - i0 + 1; var height = j1 - j0 + 1; var pixels = new Color32[width * height];
            var weights = new float[8];
            for (var j = j0; j <= j1; j++)
            {
                var worldZ = (j + .5f) / side * cells * size - half; var gz = Mathf.Clamp(worldZ / size + cells / 2f, 0, cells - .001f); var z0 = (int)gz; var tz = gz - z0;
                for (var i = i0; i <= i1; i++)
                {
                    var worldX = (i + .5f) / side * cells * size - half; var gx = Mathf.Clamp(worldX / size + cells / 2f, 0, cells - .001f); var x0 = (int)gx; var tx = gx - x0;
                    var total = 0f;
                    for (var layer = 0; layer < 8; layer++)
                    {
                        var top = Mathf.Lerp(_map.SplatAt(x0, z0, layer), _map.SplatAt(x0 + 1, z0, layer), tx); var bottom = Mathf.Lerp(_map.SplatAt(x0, z0 + 1, layer), _map.SplatAt(x0 + 1, z0 + 1, layer), tx);
                        weights[layer] = Mathf.Lerp(top, bottom, tz) / 255f; total += weights[layer];
                    }
                    if (total < .001f) total = 1f;
                    float r = 0, g = 0, b = 0;
                    var warpU = (MacroAt(worldX, worldZ, 1) - .5f) * 14f; var warpV = (MacroAt(worldX, worldZ, 2) - .5f) * 14f; // metres of bend
                    var u = (worldX + warpU) * TilesPerMeter; var v = (worldZ + warpV) * TilesPerMeter; var u2 = u * .137f + .31f; var v2 = v * .137f + .77f; var patch = .8f + .4f * MacroAt(worldX, worldZ, 3);
                    for (var layer = 0; layer < 8; layer++)
                    {
                        if (weights[layer] <= .004f) continue;
                        var sample = Smooth(layer, u, v); var large = Sample(layers[layer], u2, v2).r / 255f; var factor = (.78f + .44f * large) * patch * weights[layer];
                        r += sample.r * factor; g += sample.g * factor; b += sample.b * factor;
                    }
                    // the shore: where the ground is within a metre of the sea level, a band of foam (the Godot ocean draws it from the depth under the water)
                    var heightHere = _map.SampleHeight(worldX, worldZ); var offset = heightHere - _map.SeaLevel;
                    var foam = (1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(offset + .08f) / .22f)) * Mathf.SmoothStep(.25f, .75f, MacroAt(worldX * 3f, worldZ * 3f, 5) + .2f);
                    if (foam > 0f) { r = Mathf.Lerp(r / total, 240f, foam * .7f) * total; g = Mathf.Lerp(g / total, 245f, foam * .7f) * total; b = Mathf.Lerp(b / total, 250f, foam * .7f) * total; }
                    pixels[(j - j0) * width + (i - i0)] = new Color32((byte)Mathf.Clamp(r / total, 0, 255), (byte)Mathf.Clamp(g / total, 0, 255), (byte)Mathf.Clamp(b / total, 0, 255), 255);
                }
            }
            if (_albedo == null || _albedo.width != side) { if (_albedo != null) Destroy(_albedo); _albedo = new Texture2D(side, side, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 }; }
            _albedo.SetPixels32(i0, j0, width, height, pixels); _albedo.Apply(true);
        }

        private static Color[][] _layerSmooth; private const int SmoothSize = 64;
        /// <summary>A layer averaged down to what one texel of the baked ground covers (the Godot shader reads the texture with mipmaps; reading single texels here showed as grain).</summary>
        private static Color Smooth(int layer, float u, float v)
        {
            if (_layerSmooth == null)
            {
                var layers = LayerPixels(); _layerSmooth = new Color[layers.Length][]; var step = LayerSize / SmoothSize;
                for (var l = 0; l < layers.Length; l++)
                {
                    var small = new Color[SmoothSize * SmoothSize];
                    for (var y = 0; y < SmoothSize; y++) for (var x = 0; x < SmoothSize; x++) { var sum = Color.clear; for (var dy = 0; dy < step; dy++) for (var dx = 0; dx < step; dx++) sum += (Color)layers[l][(y * step + dy) * LayerSize + x * step + dx]; small[y * SmoothSize + x] = sum / (step * step); }
                    _layerSmooth[l] = small;
                }
            }
            var t = _layerSmooth[layer]; var fu = (u - Mathf.Floor(u)) * SmoothSize - .5f; var fv = (v - Mathf.Floor(v)) * SmoothSize - .5f; var x0 = Mathf.FloorToInt(fu); var y0 = Mathf.FloorToInt(fv); var tx = fu - x0; var ty = fv - y0;
            int W(int i) => (i % SmoothSize + SmoothSize) % SmoothSize;
            var c = Color.Lerp(Color.Lerp(t[W(y0) * SmoothSize + W(x0)], t[W(y0) * SmoothSize + W(x0 + 1)], tx), Color.Lerp(t[W(y0 + 1) * SmoothSize + W(x0)], t[W(y0 + 1) * SmoothSize + W(x0 + 1)], tx), ty);
            return c * 255f;
        }

        private static Color32 Sample(Color32[] texture, float u, float v)
        {
            var x = Mathf.FloorToInt((u - Mathf.Floor(u)) * LayerSize) & (LayerSize - 1); var y = Mathf.FloorToInt((v - Mathf.Floor(v)) * LayerSize) & (LayerSize - 1);
            return texture[y * LayerSize + x];
        }

        public Texture2D Albedo => _albedo;

        // ---- what the island server tells (the same rules as the Godot IslandTerrain): edits, patches, a full map, the sea level ----
        private bool _dirty; private int _bi0 = int.MaxValue, _bi1 = -1, _bj0 = int.MaxValue, _bj1 = -1; private bool _all;
        public bool HasLiveData { get; private set; }
        /// <summary>Called when the terrain changed in a way the viewer sees (for example to rebuild the water or the minimap).</summary>
        public event Action Changed;

        private void MarkRegion(float worldX, float worldZ, float radius)
        {
            var half = _map.Cells * _map.CellSize / 2f; var total = _map.Cells * _map.CellSize; var margin = _map.CellSize * 2f;
            _bi0 = Mathf.Min(_bi0, Mathf.FloorToInt((worldX - radius - margin + half) / total * AlbedoSize)); _bi1 = Mathf.Max(_bi1, Mathf.CeilToInt((worldX + radius + margin + half) / total * AlbedoSize));
            _bj0 = Mathf.Min(_bj0, Mathf.FloorToInt((worldZ - radius - margin + half) / total * AlbedoSize)); _bj1 = Mathf.Max(_bj1, Mathf.CeilToInt((worldZ + radius + margin + half) / total * AlbedoSize));
            _dirty = true;
        }

        /// <summary>A map that arrived whole (joining the island, a resync, undoing an import).</summary>
        public bool ApplyFull(byte[] data)
        {
            if (!TerrainMap.TryDeserialize(data, out var map)) return false;
            Create(); _map = map; HasLiveData = true; Rebuild(); Changed?.Invoke(); return true;
        }

        /// <summary>Replays an edit the server accepted. False when the local revision diverged and a full resync is needed.</summary>
        public bool ApplyEdit(TerrainTool tool, float x, float z, float radius, float strength, int layer, long revision)
        {
            if (BrushCodec.TryUnpack(layer, out var plainLayer, out var brush)) _map.Apply(tool, x, z, radius, strength, plainLayer, brush, null);
            else _map.Apply(tool, x, z, radius, strength, layer);
            MarkRegion(x, z, radius); return _map.Revision == revision;
        }

        /// <summary>Replays an undo/redo patch or the vertices a masked edit changed. False means a full resync is needed.</summary>
        public bool ApplyPatch(byte[] data, long revision)
        {
            if (!TerrainPatch.TryDeserialize(data, _map.VertexCount * _map.VertexCount, out var patch)) return false;
            _map.Restore(patch); _all = true; _dirty = true; return _map.Revision == revision;
        }

        public bool ApplySeaLevel(float level, long revision) { _map.SetSeaLevel(level); _dirty = true; return _map.Revision == revision; }

        /// <summary>Redraws what changed: the mesh at once, the ground texture only over the touched area. Called every frame (and by tests).</summary>
        public void Flush()
        {
            if (!_dirty) return; _dirty = false;
            RebuildMesh();
            if (_all || _bi1 < 0) BakeAlbedo(0, AlbedoSize - 1, 0, AlbedoSize - 1); else BakeAlbedo(_bi0, _bi1, _bj0, _bj1);
            ApplyMaterial(); _bi0 = _bj0 = int.MaxValue; _bi1 = _bj1 = -1; _all = false; Changed?.Invoke();
        }

        private void LateUpdate() => Flush();
        private void OnDestroy() { if (_albedo != null) Destroy(_albedo); if (_mesh != null) Destroy(_mesh); if (_material != null) Destroy(_material); }
    }
}
