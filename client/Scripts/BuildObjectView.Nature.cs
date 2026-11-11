using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

/// <summary>
/// The two plants of the creation grid. A tree is a trunk and two cones of leaves; grass is a tuft of blades. Both are generated here (nothing to import), fit in a
/// one-metre box like every primitive and take their colour from the object's material (brown trunk always). Only the trunk of a tree stops people; grass never does.
/// </summary>
public partial class BuildObjectView
{
    public static bool IsNature(string type) => type.Equals("tree", StringComparison.OrdinalIgnoreCase) || type.Equals("grass", StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, ArrayMesh> NatureMeshes = [];

    private static Godot.Collections.Array Moved(PrimitiveMesh source, Vector3 offset)
    {
        var arrays = source.GetMeshArrays(); var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        for (var i = 0; i < vertices.Length; i++) vertices[i] += offset;
        arrays[(int)Mesh.ArrayType.Vertex] = vertices; return arrays;
    }

    private static ArrayMesh TreeMesh()
    {
        if (NatureMeshes.TryGetValue("tree", out var known)) return known;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Moved(new CylinderMesh { TopRadius = .05f, BottomRadius = .08f, Height = .6f, RadialSegments = 8, Rings = 1 }, new Vector3(0, -.2f, 0)));
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Moved(new CylinderMesh { TopRadius = 0, BottomRadius = .38f, Height = .5f, RadialSegments = 10, Rings = 1 }, new Vector3(0, .05f, 0)));
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, Moved(new CylinderMesh { TopRadius = 0, BottomRadius = .27f, Height = .45f, RadialSegments = 10, Rings = 1 }, new Vector3(0, .275f, 0)));
        return NatureMeshes["tree"] = mesh;
    }

    private static ArrayMesh GrassMesh()
    {
        if (NatureMeshes.TryGetValue("grass", out var known)) return known;
        var tool = new SurfaceTool(); tool.Begin(Mesh.PrimitiveType.Triangles);
        uint seed = 12345; float Next() { seed = seed * 1664525u + 1013904223u; return (seed >> 8) / (float)(1 << 24); }
        for (var i = 0; i < 18; i++)
        {
            var angle = Next() * Mathf.Tau; var radius = .38f * Mathf.Sqrt(Next()); var centre = new Vector3(Mathf.Cos(angle) * radius, -.5f, Mathf.Sin(angle) * radius);
            var yaw = Next() * Mathf.Pi; var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * .045f; var height = .55f + Next() * .4f; var lean = new Vector3(Next() - .5f, 0, Next() - .5f) * .18f;
            var tip = centre + new Vector3(0, height, 0) + lean;
            // both faces, so a blade is seen from either side
            foreach (var corner in new[] { centre - side, centre + side, tip }) { tool.SetNormal(Vector3.Up); tool.AddVertex(corner); }
            foreach (var corner in new[] { centre + side, centre - side, tip }) { tool.SetNormal(Vector3.Up); tool.AddVertex(corner); }
        }
        return NatureMeshes["grass"] = tool.Commit();
    }

    private static Color ColorOfMaterial(string materialJson, Color fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(materialJson);
            if (document.RootElement.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() is { Length: 7 } hex && hex[0] == '#') return Color.FromHtml(hex);
        }
        catch (Exception exception) when (exception is JsonException or FormatException) { }
        return fallback;
    }

    private void BuildNature(BuildObjectState state)
    {
        var tree = state.PrimitiveType.Equals("tree", StringComparison.OrdinalIgnoreCase);
        var visual = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = tree ? TreeMesh() : GrassMesh() }; AddChild(visual); ApplyRange(visual);
        // the default plywood colour of a new object means "no colour chosen": plants keep their own greens until the person picks one
        var chosen = ColorOfMaterial(state.MaterialJson, Colors.White);
        var leaves = chosen.IsEqualApprox(Colors.White) ? (tree ? new Color("#3F8F3A") : new Color("#6DBE45")) : chosen;
        var leafMaterial = new StandardMaterial3D { AlbedoColor = leaves, Roughness = .9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (tree) { visual.SetSurfaceOverrideMaterial(0, new StandardMaterial3D { AlbedoColor = new Color("#7A5230"), Roughness = .95f }); visual.SetSurfaceOverrideMaterial(1, leafMaterial); visual.SetSurfaceOverrideMaterial(2, leafMaterial); }
        else visual.SetSurfaceOverrideMaterial(0, leafMaterial);
        var solid = TryGetBool(state.PhysicsJson, "collision", true) && !TryGetBool(state.PhysicsJson, "trigger", false);
        if (tree && solid)
        {
            var body = new StaticBody3D { Name = "CollisionBody", CollisionLayer = 1u, CollisionMask = 1u };
            body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .1f, Height = .8f }, Position = new Vector3(0, -.1f, 0) }); AddChild(body);
        }
        AddSelectionBody(new BoxShape3D { Size = tree ? new Vector3(.8f, 1, .8f) : new Vector3(.8f, .9f, .8f) });
    }
}
