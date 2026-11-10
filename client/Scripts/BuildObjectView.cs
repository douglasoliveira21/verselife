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
    /// A raycast-only layer: ghost/trigger objects stay editable without becoming solid to avatars.
    /// A raycast-only layer: selection bodies must never be treated as a placement surface.
    public const uint SelectionPhysicsLayer = 1u << 18;
    /// Import assets may render up to 300k triangles, but creating an exact runtime physics
    /// shape for that many faces freezes the Godot main thread. This is deliberately lower.
    private const int MaxRuntimeMeshCollisionTriangles = 300_000;
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
    public void HighlightFace(int faceId) => HighlightFaces(faceId < 0 ? [] : [faceId]);
    public void HighlightFaces(IReadOnlyCollection<int> faceIdsToMark)
    {
        if (GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is not { } visual || _faceIds.Count == 0) return;
        for (var i = 0; i < _faceIds.Count; i++)
            visual.SetSurfaceOverrideMaterial(i, faceIdsToMark.Contains(_faceIds[i]) ? new StandardMaterial3D { AlbedoColor = new Color(1, .85f, .2f), EmissionEnabled = true, Emission = new Color(1, .75f, .1f), EmissionEnergyMultiplier = .6f } : _faceBase[i]);
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
        var meshKey = $"{state.PrimitiveType.ToLowerInvariant()}|{parameters}";
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
        if (!TryGetBool(state.PhysicsJson, "collision", true) || mesh.GetSurfaceCount() == 0) { AddSelectionBody(new BoxShape3D { Size = mesh.GetAabb().Size }); return; }
        var trigger = TryGetBool(state.PhysicsJson, "trigger", false);
        var body = new StaticBody3D { Name = trigger ? "Trigger" : "CollisionBody", CollisionLayer = trigger ? 0u : 1u, CollisionMask = trigger ? 0u : 1u };
        body.AddChild(new CollisionShape3D { Shape = shared.Shape ??= mesh.CreateTrimeshShape() });
        AddChild(body);
        if (trigger) AddSelectionBody(new BoxShape3D { Size = mesh.GetAabb().Size });
    }

    public override void _EnterTree() { AddToGroup("lifeverse_build_objects"); if (_modelAsset is not null && GetNodeOrNull("Model") is null) RequestModel(); }

    /// A mesh object shows an imported 3D model. Until the model arrives a translucent box stands in for it; the
    /// invisible "PrimitiveMesh" box afterwards only exists so selection highlighting works like on primitives.
    private void BuildMeshObject(BuildObjectState state)
    {
        var proxy = new MeshInstance3D { Name = "PrimitiveMesh", Mesh = new BoxMesh { Size = Vector3.One }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(.6f, .7f, .8f, .35f) } };
        AddChild(proxy);
        AddMeshCollision(state, null, Vector3.One); // a safe box is kept until the imported model arrives
        if (!TryGetBool(state.PhysicsJson, "collision", true) || CollisionMode(state.PhysicsJson, true) == "none" || TryGetBool(state.PhysicsJson, "trigger", false)) AddSelectionBody(new BoxShape3D { Size = Vector3.One });
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
        AddChild(model.Node); ApplyModelMaterial(model.Node);
        if (proxy is not null)
        {
            proxy.Mesh = new BoxMesh { Size = model.Size };
            proxy.MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1, 1, 1, 0) };
        }
        GetNodeOrNull<StaticBody3D>("CollisionBody")?.QueueFree();
        AddMeshCollision(State, model.Node, model.Size);
    }

    /// An imported model arrives with the materials of its file. Once the person textures it in the Textura tab ("override"), those are replaced by the object's
    /// material, so a mesh uploaded without textures can get base colour, normal, metallic-roughness and emissive maps afterwards, as in Second Life.
    public void ApplyModelMaterialForTesting(Node root, string materialJson) { State = new BuildObjectState { Id = State.Id, PrimitiveType = "mesh", MaterialJson = materialJson }; ApplyModelMaterial(root); }

    private void ApplyModelMaterial(Node root)
    {
        var material = ParseMaterial(State.MaterialJson);
        if (material["override"] is not JsonValue flag || !flag.TryGetValue<bool>(out var on) || !on) return;
        var faces = FaceEntries(material); var surfaceIndex = 0;
        void Visit(Node node)
        {
            if (node is MeshInstance3D { Mesh: not null } instance)
                for (var s = 0; s < instance.Mesh.GetSurfaceCount(); s++, surfaceIndex++)
                    instance.SetSurfaceOverrideMaterial(s, MaterialBuilder.Build(material, faces.GetValueOrDefault(surfaceIndex), Textures));
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root);
    }

    /// An imported model with no shape chosen collides exactly (triangle mesh), so people can walk into houses; a box would be an invisible wall around them.
    private static ConcavePolygonShape3D TrimeshOf(Mesh mesh) { var shape = mesh.CreateTrimeshShape(); shape.BackfaceCollision = true; return shape; }

    private static string CollisionMode(string json, bool mesh = false)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var mode = document.RootElement.TryGetProperty("shape", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return mode is "none" or "box" or "convex" or "triangle" ? mode : mesh ? "triangle" : "box";
        }
        catch (JsonException) { return mesh ? "triangle" : "box"; }
    }

    /// <summary>Creates real Godot collision shapes for an imported model. Triangle mesh is intentionally static;
    /// the API rejects it for physical bodies and this client never turns it into a rigid body.</summary>
    private void AddMeshCollision(BuildObjectState state, Node3D? model, Vector3 fallbackSize)
    {
        var mode = CollisionMode(state.PhysicsJson, true);
        if (!TryGetBool(state.PhysicsJson, "collision", true) || mode == "none") return;
        var trigger = TryGetBool(state.PhysicsJson, "trigger", false);
        var body = new StaticBody3D { Name = trigger ? "Trigger" : "CollisionBody", CollisionLayer = trigger ? 0u : 1u, CollisionMask = trigger ? 0u : 1u };
        var debug = new Node3D { Name = "CollisionDebug", Visible = false };
        if (mode == "box" || model is null)
        {
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = fallbackSize } });
            debug.AddChild(DebugMesh(new BoxMesh { Size = fallbackSize }, Transform3D.Identity));
        }
        else
        {
            var sourceMeshes = model.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(n => n.Mesh is not null).ToArray();
            var totalTriangles = sourceMeshes.Sum(n => TriangleCount(n.Mesh!));
            if (totalTriangles > MaxRuntimeMeshCollisionTriangles)
            {
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = fallbackSize } });
                debug.AddChild(DebugMesh(new BoxMesh { Size = fallbackSize }, Transform3D.Identity));
                AddPhysicsWarning($"Colisão simplificada: {totalTriangles:N0} triângulos excedem o limite de {MaxRuntimeMeshCollisionTriangles:N0}.");
                AddChild(body); AddChild(debug); return;
            }
            var found = 0;
            foreach (var node in sourceMeshes)
            {
                if (TriangleCount(node.Mesh!) == 0) continue;
                var faces = node.Mesh!.GetFaces();
                Shape3D shape = mode == "triangle" ? TrimeshOf(node.Mesh) : new ConvexPolygonShape3D { Points = faces };
                var collision = new CollisionShape3D { Shape = shape };
                // The body is a direct child of this view. Preserve each imported node's
                // transform relative to the view rather than its world transform, so moving
                // the placed object does not apply its position twice.
                collision.Transform = GlobalTransform.AffineInverse() * node.GlobalTransform;
                body.AddChild(collision); found++;
                // Godot does not expose a render mesh for ConvexPolygonShape3D. The imported
                // source faces are still a useful translucent inspection layer; the actual
                // collider remains the convex/triangle shape created above.
                debug.AddChild(DebugMesh(node.Mesh, collision.Transform));
            }
            if (found == 0) { body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = fallbackSize } }); debug.AddChild(DebugMesh(new BoxMesh { Size = fallbackSize }, Transform3D.Identity)); }
        }
        AddChild(body); AddChild(debug);
    }

    private static int TriangleCount(Mesh mesh)
    {
        if (mesh is not ArrayMesh array) return mesh.GetFaces().Length / 3;
        var total = 0;
        for (var surface = 0; surface < array.GetSurfaceCount(); surface++)
        {
            if (array.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles) continue;
            var indices = array.SurfaceGetArrayIndexLen(surface);
            total += (indices > 0 ? indices : array.SurfaceGetArrayLen(surface)) / 3;
        }
        return total;
    }

    private void AddPhysicsWarning(string text)
    {
        GetNodeOrNull<Label3D>("PhysicsWarning")?.QueueFree();
        AddChild(new Label3D { Name = "PhysicsWarning", Text = text, Position = new Vector3(0, 1.8f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 26, OutlineSize = 6, Modulate = new Color(1f, .7f, .2f) });
    }

    private void AddSelectionBody(Shape3D shape)
    {
        if (GetNodeOrNull<StaticBody3D>("SelectionBody") is not null) return;
        var body = new StaticBody3D { Name = "SelectionBody", CollisionLayer = SelectionPhysicsLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape }); AddChild(body);
    }

    private static MeshInstance3D DebugMesh(Mesh mesh, Transform3D transform) => new()
    {
        Mesh = mesh, Transform = transform, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D { Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0f, .9f, 1f, .28f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = true }
    };

    public void SetCollisionPreview(bool visible)
    {
        if (GetNodeOrNull<Node3D>("CollisionDebug") is { } debug) debug.Visible = visible;
    }

    private static Guid? AssetIdOf(string geometryJson)
    {
        try { using var doc = JsonDocument.Parse(geometryJson); return doc.RootElement.TryGetProperty("assetId", out var value) && Guid.TryParse(value.GetString(), out var id) ? id : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>The island simulates physical objects and sends their poses; while one is driven the stored position (where the builder left it) is not put back by an update.</summary>
    private Vector3? _drivenFrom;
    public bool Selected { get; private set; }
    public bool IsDriven => _drivenFrom is not null;

    public void DriveTo(Vector3 position, Quaternion rotation)
    {
        _drivenFrom ??= new Vector3(State.PositionX, State.PositionY, State.PositionZ);
        Position = position; Quaternion = rotation;
    }

    public void Apply(BuildObjectState state)
    {
        State = state; var stored = new Vector3(state.PositionX, state.PositionY, state.PositionZ);
        if (_drivenFrom is { } driven && driven.DistanceSquaredTo(stored) < 1e-6f) { Scale = new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ); }
        else { _drivenFrom = null; Position = stored; Rotation = new Vector3(Mathf.DegToRad(state.RotationX), Mathf.DegToRad(state.RotationY), Mathf.DegToRad(state.RotationZ)); Scale = new Vector3(state.ScaleX, state.ScaleY, state.ScaleZ); }
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
        if (!TryGetBool(state.PhysicsJson, "collision", true)) { AddSelectionBody(CreateShape(state.PrimitiveType)); return; }
        var body = new StaticBody3D { Name = TryGetBool(state.PhysicsJson, "trigger", false) ? "Trigger" : "CollisionBody" }; body.CollisionLayer = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.CollisionMask = TryGetBool(state.PhysicsJson, "trigger", false) ? 0u : 1u; body.AddChild(new CollisionShape3D { Shape = CreateShape(state.PrimitiveType) }); AddChild(body);
        if (TryGetBool(state.PhysicsJson, "trigger", false)) AddSelectionBody(CreateShape(state.PrimitiveType));
    }
    private void ApplyRange(MeshInstance3D visual)
    {
        var size = Mathf.Max(State.ScaleX, Mathf.Max(State.ScaleY, State.ScaleZ));
        var end = Mathf.Clamp(size * RangePerMetre, MinRange, MaxRange);
        visual.VisibilityRangeEnd = end; visual.VisibilityRangeEndMargin = end * .1f; visual.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
    }

    private void RefreshRange() { if (GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is { } visual) ApplyRange(visual); }

    public float VisibilityRangeForTesting => GetNodeOrNull<MeshInstance3D>("PrimitiveMesh")?.VisibilityRangeEnd ?? 0f;
    public void SetSelected(bool selected) { Selected = selected; var mesh = GetNodeOrNull<MeshInstance3D>("PrimitiveMesh"); if (mesh is null) return; mesh.MaterialOverlay = selected ? new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1, .8f, .15f, .35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha } : null; }
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
        AddFlex(material);
        if (material["light"] is not JsonObject light) return;
        float Read(string key, float fallback) => light[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : fallback;
        Color color; try { color = light["color"] is JsonValue c && c.TryGetValue<string>(out var text) ? Color.FromHtml(text) : Colors.White; } catch { color = Colors.White; }
        var energy = Mathf.Clamp(Read("energy", 1f), 0, 8); var range = Mathf.Clamp(Read("range", 6f), .5f, 30f);
        var projector = material["projector"] as JsonObject;
        var hasImage = material["projectorTexture"] is JsonValue || material["projectorBuiltin"] is JsonValue;
        float Projector(string key, float fallback) => projector?[key] is JsonValue v && v.TryGetValue<double>(out var n) && double.IsFinite(n) ? (float)n : fallback;
        // with a projector the round light is only the "ambiance" (the share of the light that falls outside the image)
        var ambient = hasImage ? Mathf.Clamp(Projector("ambiance", .1f), 0f, 1f) : 1f;
        AddChild(new ObjectLight { Name = "ObjectLight", LightColor = color, LightEnergy = energy * ambient, OmniRange = range, OmniAttenuation = Mathf.Clamp(Read("falloff", 1f), .1f, 4f), ShadowEnabled = false });
        if (!hasImage) return;
        var spot = new SpotLight3D
        {
            Name = "ObjectProjector", LightColor = color, LightEnergy = energy, SpotAngle = Mathf.Clamp(Projector("fov", 45f) / 2f, 5f, 60f), SpotRange = Mathf.Max(range, Mathf.Clamp(Projector("focus", 0f), 0f, 30f)),
            SpotAttenuation = Mathf.Clamp(Read("falloff", 1f), .1f, 4f), ShadowEnabled = false
        };
        AddChild(spot);
        if (material["projectorBuiltin"] is JsonValue builtin && builtin.TryGetValue<string>(out var name)) spot.LightProjector = BuiltInTextures.Get(name);
        else if (material["projectorTexture"] is JsonValue asset && asset.TryGetValue<string>(out var text2) && Guid.TryParse(text2, out var assetId))
            Textures?.Request(assetId, texture => { if (texture is not null && IsInstanceValid(spot)) spot.LightProjector = texture; });
    }

    private void AddFlex(JsonObject material)
    {
        if (LifeVerse.Shared.Building.FlexSettings.FromMaterial(material.ToJsonString()) is not { } flex) return;
        if (GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is not { } visual) return;
        AddChild(new FlexDriver { Name = "FlexDriver", Settings = flex, Visual = visual, Phase = (State.Id.GetHashCode() & 0xFFFF) / 10000f });
    }

    /// Bends the visible mesh like a flag or a plant: the base stays where it is and the top leans, swaying with the wind and the force.
    public partial class FlexDriver : Node
    {
        public LifeVerse.Shared.Building.FlexSettings Settings;
        public MeshInstance3D? Visual;
        public float Phase;
        private readonly LifeVerse.Shared.Building.FlexSimulation _sim = new();
        public Vector2 LeanForTesting => new(_sim.Lean.X, _sim.Lean.Y);

        public override void _Process(double delta)
        {
            if (Visual is null || !IsInstanceValid(Visual)) return;
            _sim.Step(Settings, (float)delta, Time.GetTicksMsec() / 1000f, Phase);
            var lean = _sim.Lean; var sag = _sim.Sag;
            Visual.Transform = new Transform3D(new Basis(new Vector3(1, 0, 0), new Vector3(lean.X, 1f + sag, lean.Y), new Vector3(0, 0, 1)), new Vector3(.5f * lean.X, .5f * sag, .5f * lean.Y));
        }
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
