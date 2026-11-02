using Godot;
using System.Text.Json;

namespace LifeVerse.Client.Scripts;

/// Procedural, deterministic fallback terrain. Server snapshots remain the source of truth;
/// this component provides a playable mesh/collision while licensed terrain packs are absent.
public partial class IslandTerrain : Node3D
{
    [Export] public int Size { get; set; } = 64;
    [Export] public float CellSize { get; set; } = 2f;
    [Export] public int Seed { get; set; } = 1407;
    [Export] public string Template { get; set; } = "plaza";

    public override void _Ready() => Generate();

    public void ApplySnapshot(string terrainJson, string environmentJson)
    {
        try { using var doc = JsonDocument.Parse(environmentJson); if (doc.RootElement.TryGetProperty("template", out var value)) Template = value.GetString() ?? Template; } catch { }
        foreach (var child in GetChildren().Where(x => x.Name.ToString() is "GeneratedTerrain" or "TerrainCollision" or "GeographicBoundary")) { RemoveChild(child); child.QueueFree(); }
        Generate();
    }

    public void Generate()
    {
        var count = Mathf.Clamp(Size, 16, 256); var noise = new FastNoiseLite { Seed = Seed, Frequency = .035f, FractalOctaves = 4 };
        var vertices = new Vector3[(count + 1) * (count + 1)]; var colors = new Color[vertices.Length];
        for (var z = 0; z <= count; z++) for (var x = 0; x <= count; x++)
        {
            var nx = x - count / 2f; var nz = z - count / 2f; var edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(count - x, count - z)); var h = Mathf.Max(0, noise.GetNoise2D(x, z) * 4f + 1.2f - Mathf.Max(0, 2 - edge * .15f));
            if (Template == "plaza") h = Mathf.Max(0, noise.GetNoise2D(x, z) * 1.2f); if (Template == "sandbox") h = noise.GetNoise2D(x, z) * 3f;
            vertices[z * (count + 1) + x] = new Vector3(nx * CellSize, h, nz * CellSize); colors[z * (count + 1) + x] = h < .2f && Template == "tropical" ? new Color("#d5bd75") : new Color("#4d8f55");
        }
        var indices = new int[count * count * 6]; var i = 0; for (var z = 0; z < count; z++) for (var x = 0; x < count; x++) { var a = z * (count + 1) + x; indices[i++] = a; indices[i++] = a + count + 1; indices[i++] = a + 1; indices[i++] = a + 1; indices[i++] = a + count + 1; indices[i++] = a + count + 2; }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = vertices; arrays[(int)Mesh.ArrayType.Index] = indices; arrays[(int)Mesh.ArrayType.Color] = colors;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .92f });
        var visual = new MeshInstance3D { Name = "GeneratedTerrain", Mesh = mesh }; AddChild(visual);
        var body = new StaticBody3D { Name = "TerrainCollision" }; var shape = new ConcavePolygonShape3D { Data = mesh.GetFaces() }; body.AddChild(new CollisionShape3D { Shape = shape }); AddChild(body);
        AddBoundaries(count * CellSize);
    }

    private void AddBoundaries(float extent)
    {
        foreach (var data in new[] { (new Vector3(0, 8, extent), new Vector3(extent * 2, 16, 1)), (new Vector3(0, 8, -extent), new Vector3(extent * 2, 16, 1)), (new Vector3(extent, 8, 0), new Vector3(1, 16, extent * 2)), (new Vector3(-extent, 8, 0), new Vector3(1, 16, extent * 2)) })
        { var wall = new StaticBody3D { Name = "GeographicBoundary", Position = data.Item1 }; wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = data.Item2 } }); AddChild(wall); }
    }
}
