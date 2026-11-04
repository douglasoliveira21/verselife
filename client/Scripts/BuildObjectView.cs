using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Shared.Building;

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
    /// <summary>What the owner may do (LifeVerse.Shared.Items.ItemRights) and what the next owner will be able to do; the server decides, the editor only shows.</summary>
    public int OwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    public int NextOwnerRights { get; set; } = LifeVerse.Shared.Items.ItemRights.All;
    /// <summary>Protected against accidental editing (moving, changing, linking, deleting) until it is unprotected.</summary>
    public bool EditLocked { get; set; }
    /// <summary>The pivot of the object itself: an offset from its centre, in its own axes (metres).</summary>
    public float PivotX { get; set; }
    public float PivotY { get; set; }
    public float PivotZ { get; set; }
}

public partial class BuildObjectView : Node3D
{
    public BuildObjectState State { get; private set; } = new();
    private int _applyId;
    private Guid? _modelAsset;
    /// Objects that look the same share one mesh, one collision shape and one material per look, instead of each building its own.
    private sealed record Shared(ArrayMesh Mesh, int[] FaceIds, int[] TriangleEnds) { public ConcavePolygonShape3D? Shape { get; set; } }
    private static readonly Dictionary<string, Shared> MeshCache = [];
    private static readonly Dictionary<string, Material> MaterialCache = [];
    private const int MeshCacheLimit = 256, MaterialCacheLimit = 1024;
    public static int CachedMeshesForTesting => MeshCache.Count;
    public static int CachedMaterialsForTesting => MaterialCache.Count;
    public static void ClearCachesForTesting() { MeshCache.Clear(); MaterialCache.Clear(); }

    /// Objects farther than this many metres per metre of their own size are not drawn: a 1 m box disappears beyond 120 m, a 10 m wall beyond 600 m.
    public const float RangePerMetre = 120f, MinRange = 40f, MaxRange = 600f;
    private string _shapeKey = string.Empty;
    private readonly List<int> _faceIds = [];
    private readonly List<int> _faceTriangleEnd = [];

    /// Face ids of the procedural mesh, one per surface in order (empty for the other shapes).
    public IReadOnlyList<int> FaceIds => _faceIds;
    public bool IsProcedural => _faceIds.Count > 0;
    private string? _lockedBy;
    public string? LockedBy => _lockedBy;

    /// Marks the object as being edited by someone else (null clears it): a floating tag, so nobody is surprised by a refused edit.
    public void SetLockedBy(string? name)
    {
        _lockedBy = name;
        GetNodeOrNull<Label3D>("LockTag")?.QueueFree();
        if (name is null) return;
        AddChild(new Label3D { Name = "LockTag", Text = $"Em edição: {name}", Position = new Vector3(0, 1.5f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 40, OutlineSize = 8, Modulate = new Color(1, .8f, .3f) });
    }
    private readonly List<Material?> _faceBase = [];

    /// Lights up one face of a procedural mesh (-1 clears it) so the person sees which face the texture tools will change.
    public void HighlightFace(int faceId)
    {
        if (GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is not { } visual || _faceIds.Count == 0) return;
        for (var i = 0; i < _faceIds.Count; i++)
            visual.SetSurfaceOverrideMaterial(i, _faceIds[i] == faceId ? new StandardMaterial3D { AlbedoColor = new Color(1, .85f, .2f), EmissionEnabled = true, Emission = new Color(1, .75f, .1f), EmissionEnergyMultiplier = .6f } : _faceBase[i]);
    }

    /// The face id that owns a triangle of the collision mesh (what a ray reports as face_index), or -1.
    public int FaceAtTriangle(int triangle)
    {
        for (var i = 0; i < _faceTriangleEnd.Count; i++) if (triangle < _faceTriangleEnd[i]) return _faceIds[i];
        return -1;
    }

    /// Box, prism, cylinder, tube, cone and pyramid are built from real geometry: every setting of the shape changes the mesh,
    /// each face is its own surface (so it can have its own colour) and the collision follows the exact shape, holes included.
    private void BuildProcedural(BuildObjectState state)
    {
        var parameters = PrimParams.Parse(state.GeometryJson);
        var meshKey = $"{state.PrimitiveType.ToLowerInvariant()}|{parameters.Hollow}|{parameters.TaperX}|{parameters.TaperY}|{parameters.Twist}|{parameters.CutStart}|{parameters.CutEnd}";
        if (!MeshCache.TryGetValue(meshKey, out var shared))
        {
            var data = PrimMesh.Build(state.PrimitiveType, parameters);
            var built = new ArrayMesh(); var ids = new List<int>(); var ends = new List<int>(); var triangles = 0;
            foreach (var face in data.Faces)
            {
                var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = face.Positions.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = face.Normals.Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = face.Uvs.Select(p => new Vector2(p.X, p.Y)).ToArray();
                arrays[(int)Mesh.ArrayType.Index] = face.Indices;
                built.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                ids.Add(face.Id); triangles += face.Indices.Length / 3; ends.Add(triangles);
            }
            if (MeshCache.Count >= MeshCacheLimit) MeshCache.Clear();
            MeshCache[meshKey] = shared = new Shared(built, [.. ids], [.. ends]);
        }
        var mesh = shared.Mesh;
        _faceIds.AddRange(shared.FaceIds); _faceTriangleEnd.AddRange(shared.TriangleEnds);
        var visual = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = mesh };
        AddChild(visual); ApplyRange(visual);
        var material = ParseMaterial(state.MaterialJson);
        var faces = FaceEntries(material);
        for (var i = 0; i < _faceIds.Count; i++)
        {
            var entry = faces.GetValueOrDefault(_faceIds[i]);
            var materialKey = state.MaterialJson + "#" + (entry?.ToJsonString() ?? "");
            if (!MaterialCache.TryGetValue(materialKey, out var cached))
            {
                cached = MaterialBuilder.Build(material, entry, Textures);
                // a textured material built before the texture cache exists would stay without its image for good: do not share it
                if (Textures is not null || !MaterialBuilder.IsTextured(material, entry))
                {
                    if (MaterialCache.Count >= MaterialCacheLimit) MaterialCache.Clear();
                    MaterialCache[materialKey] = cached;
                }
            }
            visual.SetSurfaceOverrideMaterial(i, cached);
        }
        AddObjectLight(material);
        for (var i = 0; i < _faceIds.Count; i++) _faceBase.Add(visual.GetSurfaceOverrideMaterial(i));
        if (!string.IsNullOrWhiteSpace(state.Name)) AddChild(new Label3D { Name = "ObjectName", Text = state.Name, Position = new Vector3(0, 1.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false });
        if (!TryGetBool(state.PhysicsJson, "collision", true) || mesh.GetSurfaceCount() == 0) return;
        var trigger = TryGetBool(state.PhysicsJson, "trigger", false);
        var body = new StaticBody3D { Name = trigger ? "Trigger" : "CollisionBody", CollisionLayer = trigger ? 0u : 1u, CollisionMask = trigger ? 0u : 1u };
        body.AddChild(new CollisionShape3D { Shape = shared.Shape ??= mesh.CreateTrimeshShape() });
        AddChild(body);
    }

    public override void _EnterTree() { if (_modelAsset is not null && GetNodeOrNull("Model") is null) RequestModel(); }

    /// A mesh object shows an imported 3D model. Until the model arrives a translucent box stands in for it; the
    /// invisible "PrimitiveMesh" box afterwards only exists so selection highlighting works like on primitives.
    private void BuildMeshObject(BuildObjectState state)
    {
        var proxy = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = new BoxMesh { Size = Vector3.One }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.6f, .7f, .8f, .35f) } };
        AddChild(proxy);
        if (!string.IsNullOrWhiteSpace(state.Name)) AddChild(new Label3D { Name = "ObjectName", Text = state.Name, Position = new Vector3(0, .8f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false });
        if (TryGetBool(state.PhysicsJson, "collision", true))
        {
            var body = new StaticBody3D { Name = "CollisionBody", CollisionLayer = 1u, CollisionMask = 1u };
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
            AddChild(body);
        }
        _modelAsset = AssetIdOf(state.GeometryJson);
        RequestModel();
    }

    private void RequestModel()
    {
        if (_modelAsset is not { } asset || GetParent()?.GetNodeOrNull<ModelCache>("ModelCache") is not { } cache) return;
        var id = _applyId;
        cache.Request(asset, instance => OnModelReady(id, instance));
    }

    private void OnModelReady(int id, ModelInstance? instance)
    {
        if (id != _applyId || !IsInstanceValid(this) || !IsInsideTree()) { instance?.Node.QueueFree(); return; }
        var proxy = GetNodeOrNull<MeshInstance3D>("PrimitiveMesh");
        if (instance is not { } model)
        {
            if (proxy?.MaterialOverride is StandardMaterial3D missing) missing.AlbedoColor = new Color(.9f, .3f, .3f, .4f);
            return;
        }
        AddChild(model.Node);
        if (proxy is not null)
        {
            proxy.Mesh = new BoxMesh { Size = model.Size };
            proxy.MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, 1, 1, 0) };
        }
        if (GetNodeOrNull<StaticBody3D>("CollisionBody")?.GetChildOrNull<CollisionShape3D>(0) is { } shape) shape.Shape = new BoxShape3D { Size = model.Size };
    }

    private static Guid? AssetIdOf(string geometryJson)
    {
        try { using var doc = JsonDocument.Parse(geometryJson); return doc.RootElement.TryGetProperty("assetId", out var value) && Guid.TryParse(value.GetString(), out var id) ? id : null; }
        catch (JsonException) { return null; }
    }

    public void Apply(BuildObjectState state)
    {
        State = state; Position = new Vector3(state.PositionX, state.PositionY, state.PositionZ); Rotation = new Vector3(Mathf.DegToRad(state.RotationX), Mathf.DegToRad(state.RotationY), Mathf.DegToRad(state.RotationZ)); Scale = new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ);
        // Moving, turning or resizing an object changes only its transform: the mesh, materials and collision stay as they are.
        var shapeKey = string.Join('\u001f', state.PrimitiveType, state.GeometryJson, state.MaterialJson, state.PhysicsJson, state.Name);
        if (shapeKey == _shapeKey && GetChildCount() > 0) { RefreshRange(); return; }
        _shapeKey = shapeKey;
        foreach (var child in GetChildren().ToArray()) { RemoveChild(child); child.QueueFree(); }
        var keepTag = _lockedBy; _lockedBy = null; // rebuilding the children removes the tag; put it back right after
        if (keepTag is not null) Callable.From(() => { if (IsInstanceValid(this)) SetLockedBy(keepTag); }).CallDeferred();
        _applyId++; _modelAsset = null; _faceIds.Clear(); _faceTriangleEnd.Clear(); _faceBase.Clear();
        if (state.PrimitiveType.Equals("mesh", StringComparison.OrdinalIgnoreCase)) { BuildMeshObject(state); return; }
        if (PrimMesh.Supports(state.PrimitiveType)) { BuildProcedural(state); return; }
        var mesh = CreateMesh(state.PrimitiveType, state.GeometryJson); var materialJson = ParseMaterial(state.MaterialJson); var visual = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = mesh, MaterialOverride = MaterialBuilder.Build(materialJson, null, Textures) }; AddChild(visual); ApplyRange(visual); AddObjectLight(materialJson);
        if (!string.IsNullOrWhiteSpace(state.Name)) AddChild(new Label3D { Name = "ObjectName", Text = state.Name, Position = new Vector3(0, 1.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Visible = false });
        if (!TryGetBool(state.PhysicsJson, "collision", true)) return;
        var body = new StaticBody3D { Name = TryGetBool(state.PhysicsJson, "trigger", false) ? "Trigger" : "CollisionBody" }; body.CollisionLayer = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.CollisionMask = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.AddChild(new CollisionShape3D { Shape = CreateShape(state.PrimitiveType) }); AddChild(body);
    }
    private void ApplyRange(MeshInstance3D visual)
    {
        var size = Mathf.Max(State.ScaleX, Mathf.Max(State.ScaleY, State.ScaleZ));
        var end = Mathf.Clamp(size * RangePerMetre, MinRange, MaxRange);
        visual.VisibilityRangeEnd = end; visual.VisibilityRangeEndMargin = end * .1f; visual.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
    }

    private void RefreshRange() { if (GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is { } visual) ApplyRange(visual); }

    public float VisibilityRangeForTesting => GetNodeOrNull<MeshInstance3D>("PrimitiveMesh")?.VisibilityRangeEnd ?? 0f;
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
            "torus" => new TorusMesh { InnerRadius = Mathf.Clamp(.2f - hollow * .15f, .03f, .45f), OuterRadius = .5f },
            "hemisphere" => new SphereMesh { Radius = .5f, Height = .5f, IsHemisphere = true },
            "prism" => new BoxMesh { Size = new Vector3(1, .7f, 1) }, "pyramid" => new CylinderMesh { TopRadius = 0, BottomRadius = .6f, Height = 1 }, _ => new BoxMesh { Size = Vector3.One }
        };
    }
    private static float JsonNumber(string json, string key, float fallback) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(key, out var value) && value.TryGetSingle(out var number) ? number : fallback; } catch { return fallback; } }
    private static Shape3D CreateShape(string type) => type.ToLowerInvariant() switch { "sphere" => new SphereShape3D { Radius = .5f }, "cylinder" or "cone" or "tube" or "pyramid" => new CylinderShape3D { Radius = .5f, Height = 1 }, "hemisphere" => new SphereShape3D { Radius = .5f }, "torus" or "ring" => new CylinderShape3D { Radius = .5f, Height = .3f }, "plane" => new BoxShape3D { Size = new Vector3(1, .05f, 1) }, _ => new BoxShape3D { Size = Vector3.One } };
    private TextureCache? Textures => GetParent()?.GetNodeOrNull<TextureCache>("TextureCache");

    private static JsonObject ParseMaterial(string json)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? []; }
        catch (JsonException) { return []; }
    }

    /// The per-face entries of a material, by face id (procedural shapes only; other shapes have a single material).
    private Dictionary<int, JsonObject> FaceEntries(JsonObject material)
    {
        var result = new Dictionary<int, JsonObject>();
        if (material["faces"] is not JsonArray faces) return result;
        foreach (var node in faces) if (node is JsonObject entry && entry["index"] is JsonValue index && index.TryGetValue<int>(out var id)) result[id] = entry;
        return result;
    }

    /// A glowing object lights its surroundings: a light with the colour, strength and reach saved with the material.
    private void AddObjectLight(JsonObject material)
    {
        if (material["light"] is not JsonObject light) return;
        float Read(string key, float fallback) => light[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : fallback;
        Color color; try { color = light["color"] is JsonValue c && c.TryGetValue<string>(out var text) ? Color.FromHtml(text) : Colors.White; } catch { color = Colors.White; }
        AddChild(new ObjectLight { Name = "ObjectLight", LightColor = color, LightEnergy = Mathf.Clamp(Read("energy", 1f), 0, 8), OmniRange = Mathf.Clamp(Read("range", 6f), .5f, 30f), ShadowEnabled = false });
    }

    /// Dynamic lights are expensive, so only the first few in the world are switched on; the rest stay dark until some go away.
    public partial class ObjectLight : OmniLight3D
    {
        public const int MaxLights = 16;
        private static int _active;
        public override void _EnterTree() { _active++; Visible = _active <= MaxLights; }
        public override void _ExitTree() { _active--; }
        public static int ActiveForTesting => _active;
    }

    private static bool TryGetBool(string json, string property, bool fallback) { try { using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback; } catch { return fallback; } }
}
