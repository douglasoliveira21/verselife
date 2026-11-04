using Godot;
using LifeVerse.Shared.Terrain;

namespace LifeVerse.Client.Scripts;

/// Flat, textured island ground that can be reshaped and repainted. The island server owns the
/// authoritative TerrainMap; this node renders it (mesh, splat-blended textures) and keeps a matching
/// HeightMapShape3D so the local avatar walks on exactly what the server simulates.
public partial class IslandTerrain : Node3D
{
    /// The eight ground materials: name, the two colours of its procedural texture, and its colour on the minimap.
    public static readonly (string Name, Color Dark, Color Light, Color Map)[] Layers =
    [
        ("Grama", new Color("#3f7a35"), new Color("#78ad4e"), new Color("#5a9a47")),
        ("Terra", new Color("#5b4030"), new Color("#8a6a49"), new Color("#8a6a48")),
        ("Areia", new Color("#c9b57f"), new Color("#e6d7a5"), new Color("#d8c995")),
        ("Rocha", new Color("#55565b"), new Color("#9a9a9c"), new Color("#8d8d90")),
        ("Cascalho", new Color("#6b6a64"), new Color("#aaa69c"), new Color("#9c9890")),
        ("Neve", new Color("#d9e4ec"), new Color("#ffffff"), new Color("#f2f6f9")),
        ("Lama", new Color("#35261c"), new Color("#5a4130"), new Color("#4a3628")),
        ("Grama seca", new Color("#85823f"), new Color("#c4b768"), new Color("#a39b52"))
    ];
    public static readonly string[] LayerNames = [.. Layers.Select(l => l.Name)];
    private const string ShaderSource = @"
shader_type spatial;
uniform sampler2D l0 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l1 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l2 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l3 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l4 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l5 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l6 : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D l7 : source_color, filter_linear_mipmap, repeat_enable;
uniform float tiles_per_meter = 0.2;
varying vec4 splat_a;
varying vec4 splat_b;
varying vec3 world_pos;
void vertex() {
    splat_a = COLOR;
    splat_b = vec4(UV, UV2);
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
vec3 layer(sampler2D tex, vec2 uv) {
    // A second, larger and offset sample breaks up visible tiling.
    return texture(tex, uv).rgb * (0.78 + 0.44 * texture(tex, uv * 0.137 + vec2(0.31, 0.77)).r);
}
void fragment() {
    vec2 uv = world_pos.xz * tiles_per_meter;
    float total = max(dot(splat_a, vec4(1.0)) + dot(splat_b, vec4(1.0)), 0.001);
    vec3 c = vec3(0.0);
    if (splat_a.r > 0.004) c += layer(l0, uv) * splat_a.r;
    if (splat_a.g > 0.004) c += layer(l1, uv) * splat_a.g;
    if (splat_a.b > 0.004) c += layer(l2, uv) * splat_a.b;
    if (splat_a.a > 0.004) c += layer(l3, uv) * splat_a.a;
    if (splat_b.r > 0.004) c += layer(l4, uv) * splat_b.r;
    if (splat_b.g > 0.004) c += layer(l5, uv) * splat_b.g;
    if (splat_b.b > 0.004) c += layer(l6, uv) * splat_b.b;
    if (splat_b.a > 0.004) c += layer(l7, uv) * splat_b.a;
    ALBEDO = c / total;
    ROUGHNESS = 0.95;
    SPECULAR = 0.15;
}";

    [Export] public string Template { get; set; } = "plaza";

    private TerrainMap _map = new();
    private MeshInstance3D? _visual;
    private StaticBody3D? _body;
    private CollisionShape3D? _collision;
    private HeightMapShape3D? _shape;
    private ShaderMaterial? _material;
    private bool _dirty;
    private bool _hasLiveData;
    private float _boundaryExtent = -1f;

    public TerrainMap Map => _map;
    public StaticBody3D? Body => _body;
    public event Action? Changed;

    public override void _Ready()
    {
        BuildNodes();
        Rebuild();
    }

    public override void _Process(double delta)
    {
        if (!_dirty) return;
        _dirty = false;
        Rebuild();
        Changed?.Invoke();
    }

    /// REST snapshot loaded when joining; ignored once the live server copy has arrived.
    public void ApplySnapshot(string terrainJson, string environmentJson)
    {
        try { using var doc = System.Text.Json.JsonDocument.Parse(environmentJson); if (doc.RootElement.TryGetProperty("template", out var value)) Template = value.GetString() ?? Template; } catch { }
        if (_hasLiveData) return;
        _map = TerrainMap.FromJson(terrainJson);
        Rebuild();
        Changed?.Invoke();
    }

    public bool ApplyFull(byte[] data)
    {
        if (!TerrainMap.TryDeserialize(data, out var map)) return false;
        _map = map; _hasLiveData = true;
        Rebuild();
        Changed?.Invoke();
        return true;
    }

    /// Replays an edit accepted by the server. Returns false when the local revision diverged and a full resync is needed.
    public bool ApplyEdit(TerrainTool tool, float x, float z, float radius, float strength, int layer, long revision)
    {
        _map.Apply(tool, x, z, radius, strength, layer);
        _dirty = true;
        return _map.Revision == revision;
    }

    /// Replays an undo/redo patch accepted by the server; false means a full resync is needed.
    public bool ApplyPatch(byte[] data, long revision)
    {
        if (!TerrainPatch.TryDeserialize(data, _map.VertexCount * _map.VertexCount, out var patch)) return false;
        _map.Restore(patch);
        _dirty = true;
        return _map.Revision == revision;
    }

    public bool ApplySeaLevel(float level, long revision)
    {
        _map.SetSeaLevel(level);
        _dirty = true;
        return _map.Revision == revision;
    }

    /// How often the ground textures repeat (tiles per metre). A display setting of this computer, not part of the island.
    public void SetTextureScale(float tilesPerMeter) => _material?.SetShaderParameter("tiles_per_meter", Mathf.Clamp(tilesPerMeter, .03f, 1f));

    /// Ground point under a screen position, or null when the ray misses the terrain.
    public Vector3? Raycast(Camera3D camera, Vector2 screenPosition, float distance = 300f)
    {
        if (_body is null) return null;
        var from = camera.ProjectRayOrigin(screenPosition);
        var query = PhysicsRayQueryParameters3D.Create(from, from + camera.ProjectRayNormal(screenPosition) * distance);
        query.CollideWithAreas = false;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count > 0 && hit["collider"].AsGodotObject() == _body ? hit["position"].AsVector3() : null;
    }

    private void BuildNodes()
    {
        _material = new ShaderMaterial { Shader = new Shader { Code = ShaderSource } };
        for (var i = 0; i < Layers.Length; i++) _material.SetShaderParameter($"l{i}", MakeTexture(Layers[i].Dark, Layers[i].Light, 11 + i * 13));
        _visual = new MeshInstance3D { Name = "GeneratedTerrain", MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_visual);
        _body = new StaticBody3D { Name = "TerrainCollision" };
        _shape = new HeightMapShape3D();
        _collision = new CollisionShape3D { Shape = _shape };
        _body.AddChild(_collision);
        AddChild(_body);
    }

    private void Rebuild()
    {
        if (_visual is null || _shape is null || _collision is null) return;
        var count = _map.VertexCount;
        var cells = _map.Cells;
        var vertices = new Vector3[count * count];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var splatB = new Vector2[vertices.Length];
        var splatC = new Vector2[vertices.Length];
        var mapData = new float[vertices.Length];
        for (var z = 0; z < count; z++)
        for (var x = 0; x < count; x++)
        {
            var i = z * count + x;
            var height = _map.HeightAt(x, z);
            vertices[i] = new Vector3((x - cells / 2f) * _map.CellSize, height, (z - cells / 2f) * _map.CellSize);
            mapData[i] = height;
            normals[i] = new Vector3(_map.HeightAt(x - 1, z) - _map.HeightAt(x + 1, z), 2f * _map.CellSize, _map.HeightAt(x, z - 1) - _map.HeightAt(x, z + 1)).Normalized();
            colors[i] = new Color(_map.SplatAt(x, z, 0) / 255f, _map.SplatAt(x, z, 1) / 255f, _map.SplatAt(x, z, 2) / 255f, _map.SplatAt(x, z, 3) / 255f);
            splatB[i] = new Vector2(_map.SplatAt(x, z, 4) / 255f, _map.SplatAt(x, z, 5) / 255f); splatC[i] = new Vector2(_map.SplatAt(x, z, 6) / 255f, _map.SplatAt(x, z, 7) / 255f);
        }
        var indices = new int[cells * cells * 6];
        var n = 0;
        for (var z = 0; z < cells; z++)
        for (var x = 0; x < cells; x++)
        {
            var a = z * count + x;
            indices[n++] = a; indices[n++] = a + 1; indices[n++] = a + count;
            indices[n++] = a + 1; indices[n++] = a + count + 1; indices[n++] = a + count;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = splatB;
        arrays[(int)Mesh.ArrayType.TexUV2] = splatC;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _visual.Mesh = mesh;
        _shape.MapWidth = count;
        _shape.MapDepth = count;
        _shape.MapData = mapData;
        _collision.Scale = new Vector3(_map.CellSize, 1, _map.CellSize);
        if (Mathf.IsEqualApprox(_boundaryExtent, _map.HalfExtent)) return;
        foreach (var child in GetChildren()) if (child.Name.ToString().StartsWith("GeographicBoundary")) { RemoveChild(child); child.QueueFree(); }
        AddBoundaries(_map.HalfExtent);
        _boundaryExtent = _map.HalfExtent;
    }

    /// Beyond the island lies open ocean down to a flat sea floor at the map's edge height, out to an invisible world limit.
    private void AddBoundaries(float islandExtent)
    {
        var extent = islandExtent + TerrainMap.OceanMargin;
        const float wallHeight = 64f;
        var walls = new[] { (new Vector3(0, 0, extent), new Vector3(extent * 2, wallHeight, 1)), (new Vector3(0, 0, -extent), new Vector3(extent * 2, wallHeight, 1)), (new Vector3(extent, 0, 0), new Vector3(1, wallHeight, extent * 2)), (new Vector3(-extent, 0, 0), new Vector3(1, wallHeight, extent * 2)) };
        for (var i = 0; i < walls.Length; i++)
        {
            var wall = new StaticBody3D { Name = $"GeographicBoundary{i}", Position = walls[i].Item1 };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = walls[i].Item2 } });
            AddChild(wall);
        }
        var floor = new StaticBody3D { Name = "GeographicBoundarySeaFloor", Position = new Vector3(0, TerrainMap.MinHeight - .5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(extent * 2 + 4, 1, extent * 2 + 4) } });
        AddChild(floor);
    }

    /// Seamless procedural ground texture so the project needs no licensed art to look like ground.
    private static ImageTexture MakeTexture(Color dark, Color light, int seed)
    {
        const int size = 256;
        var coarse = new FastNoiseLite { Seed = seed, Frequency = .018f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4 }.GetSeamlessImage(size, size);
        var fine = new FastNoiseLite { Seed = seed + 7, Frequency = .16f, FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 2 }.GetSeamlessImage(size, size);
        var image = Image.CreateEmpty(size, size, true, Image.Format.Rgb8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
            image.SetPixel(x, y, dark.Lerp(light, Mathf.Clamp(coarse.GetPixel(x, y).R * .65f + fine.GetPixel(x, y).R * .35f, 0, 1)));
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}
