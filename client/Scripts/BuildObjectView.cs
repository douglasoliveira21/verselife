using System.Text.Json;
using Godot;

namespace LifeVerse.Client.Scripts;

public sealed class BuildObjectState
{
    public Guid Id { get; set; }
    public Guid IslandId { get; set; }
    public Guid CreatorUserId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ParentObjectId { get; set; }
    public string Name { get; set; } = "Object";
    public string Description { get; set; } = string.Empty;
    public string PrimitiveType { get; set; } = "cube";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationX { get; set; }
    public float RotationY { get; set; }
    public float RotationZ { get; set; }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float ScaleZ { get; set; } = 1;
    public string MaterialJson { get; set; } = "{}";
    public string GeometryJson { get; set; } = "{}";
    public string PhysicsJson { get; set; } = "{}";
    public string PermissionsJson { get; set; } = "{}";
    public string ContentJson { get; set; } = "[]";
    public int Revision { get; set; }
}

public partial class BuildObjectView : Node3D
{
    public BuildObjectState State { get; private set; } = new();
    public void Apply(BuildObjectState state)
    {
        State = state; Position = new Vector3(state.PositionX, state.PositionY, state.PositionZ); Rotation = new Vector3(Mathf.DegToRad(state.RotationX), Mathf.DegToRad(state.RotationY), Mathf.DegToRad(state.RotationZ)); Scale = new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ);
        foreach (var child in GetChildren().ToArray()) { RemoveChild(child); child.QueueFree(); }
        var mesh = CreateMesh(state.PrimitiveType, state.GeometryJson); var material = CreateMaterial(state.MaterialJson); var visual = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = mesh, MaterialOverride = material }; AddChild(visual); ApplyFaceMaterials(visual, state.MaterialJson);
        if (!string.IsNullOrWhiteSpace(state.Name)) AddChild(new Label3D { Name = "ObjectName", Text = state.Name, Position = new Vector3(0, 1.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false });
        if (!TryGetBool(state.PhysicsJson, "collision", true)) return;
        var body = new StaticBody3D { Name = TryGetBool(state.PhysicsJson, "trigger", false) ? "Trigger" : "CollisionBody" }; body.CollisionLayer = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.CollisionMask = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.AddChild(new CollisionShape3D { Shape = CreateShape(state.PrimitiveType) }); AddChild(body);
    }
    public void SetSelected(bool selected) { var mesh = GetNodeOrNull<MeshInstance3D>("PrimitiveMesh"); if (mesh is null) return; mesh.MaterialOverlay = selected ? new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .8f, .15f, .35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha } : null; }
    private static PrimitiveMesh CreateMesh(string type, string geometryJson)
    {
        var taperX = JsonNumber(geometryJson, "taperX", 0); var taperY = JsonNumber(geometryJson, "taperY", 0); var hollow = JsonNumber(geometryJson, "hollow", 0);
        return type.ToLowerInvariant() switch
        {
            "sphere" => new SphereMesh { Radius = .5f, Height = 1 },
            "cylinder" => new CylinderMesh { TopRadius = Mathf.Clamp(.5f * (1 - taperX), .03f, .5f), BottomRadius = Mathf.Clamp(.5f * (1 + taperY), .03f, .5f), Height = 1 },
            "cone" => new CylinderMesh { TopRadius = Mathf.Clamp(.05f * (1 - taperX), .01f, .5f), BottomRadius = .5f, Height = 1 },
            "plane" => new PlaneMesh { Size = new Vector2(1, 1) }, "tube" => new CylinderMesh { TopRadius = .5f, BottomRadius = .5f, Height = 1 },
            "ring" => new TorusMesh { InnerRadius = Mathf.Clamp(.28f - hollow * .2f, .05f, .45f), OuterRadius = .5f },
            "prism" => new BoxMesh { Size = new Vector3(1, .7f, 1) }, "pyramid" => new CylinderMesh { TopRadius = 0, BottomRadius = .6f, Height = 1 }, _ => new BoxMesh { Size = Vector3.One }
        };
    }
    private static float JsonNumber(string json, string key, float fallback) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(key, out var value) && value.TryGetSingle(out var number) ? number : fallback; } catch { return fallback; } }
    private static Shape3D CreateShape(string type) => type.ToLowerInvariant() switch { "sphere" => new SphereShape3D { Radius = .5f }, "cylinder" or "cone" or "tube" or "pyramid" => new CylinderShape3D { Radius = .5f, Height = 1 }, "plane" => new BoxShape3D { Size = new Vector3(1, .05f, 1) }, _ => new BoxShape3D { Size = Vector3.One } };
    private static StandardMaterial3D CreateMaterial(string json) { var material = new StandardMaterial3D { AlbedoColor = new Color("#A8C7D8"), Roughness = .7f }; try { using var doc = JsonDocument.Parse(json); var root = doc.RootElement; if (root.TryGetProperty("color", out var color)) material.AlbedoColor = Color.FromHtml(color.GetString() ?? "#A8C7D8"); if (root.TryGetProperty("roughness", out var rough)) material.Roughness = Math.Clamp(rough.GetSingle(), 0, 1); if (root.TryGetProperty("metallic", out var metallic)) material.Metallic = Math.Clamp(metallic.GetSingle(), 0, 1); if (root.TryGetProperty("transparency", out var transparency)) { material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; material.AlbedoColor = new Color(material.AlbedoColor, Math.Clamp(transparency.GetSingle(), 0, 1)); } if (root.TryGetProperty("emission", out var emission)) { material.EmissionEnabled = true; material.Emission = Color.FromHtml(emission.GetString() ?? "#000000"); } } catch { } return material; }
    private static void ApplyFaceMaterials(MeshInstance3D visual, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); if (!doc.RootElement.TryGetProperty("faces", out var faces) || faces.ValueKind != JsonValueKind.Array) return;
            foreach (var face in faces.EnumerateArray()) if (face.TryGetProperty("index", out var index) && face.TryGetProperty("color", out var color) && index.TryGetInt32(out var surface)) visual.SetSurfaceOverrideMaterial(surface, CreateMaterial($"{{\"color\":\"{color.GetString() ?? "#A8C7D8"}\"}}"));
        }
        catch (JsonException) { }
    }
    private static bool TryGetBool(string json, string property, bool fallback) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback; } catch { return fallback; } }
}
