using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>
/// Drives the build editor against a tiny in-memory "server" that enforces revisions like the real one: handles,
/// selection, groups, copy/paste/duplicate, delete and undo/redo. Set LIFEVERSE_CAPTURE_PATH (windowed) for screenshots.
/// </summary>
public partial class BuildSmoke : Node
{
    private static class PrimMeshIds { public static int BoxInner => LifeVerse.Shared.Building.PrimMesh.InnerFace("cube"); }
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, BuildObjectState> _server = [];
    private BuildEditor _editor = null!;

    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>();
            AddChild(world);
            _editor = world.GetNode<BuildEditor>("BuildEditor");
            _editor.OperationSink = Sink;
            _editor.SetActive(true);
            if (world.FindChild("Entrar_em_uma_ilha", true, false) is Control joinWindow) joinWindow.Visible = false;
            await Frames(20);

            var a = Add(new Vector3(0, .5f, -6), "cube"); var b = Add(new Vector3(3, .5f, -6), "sphere");
            _editor.SelectIds([a]);
            Require(_editor.Selection.Count == 1, "selection failed");
            await Frames(3);
            await Capture("build-move");

            // Move along X by 3 m with the arrow handle (rays through the pivot and through a point 3 m to its right).
            var cameraOrigin = new Vector3(0, 4, 4);
            var pivot = new Vector3(0, .5f, -6);
            Require(_editor.BeginDrag(0, cameraOrigin, (pivot - cameraOrigin).Normalized()), "could not grab the move handle");
            _editor.UpdateDrag(cameraOrigin, (pivot + new Vector3(3, 0, 0) - cameraOrigin).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(_server[a].PositionX - 3f) < .01f && Mathf.Abs(_server[a].PositionZ + 6f) < .01f, $"move did not reach the server: {_server[a].PositionX},{_server[a].PositionZ}");
            Require(_server[a].Revision == 1, $"revision after move should be 1: {_server[a].Revision}");

            // Rotate 90 degrees about Y (from +X toward -Z).
            _editor.SetModeForTesting(GizmoMode.Rotate);
            await Frames(3);
            await Capture("build-rotate");
            var top = new Vector3(3, 6, -6); pivot = new Vector3(3, .5f, -6);
            Require(_editor.BeginDrag(1, top, (pivot + new Vector3(1, 0, 0) - top).Normalized()), "could not grab the rotate ring");
            _editor.UpdateDrag(top, (pivot + new Vector3(0, 0, -1) - top).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(Mathf.Abs(_server[a].RotationY) - 90f) < 1f, $"rotation was not 90 degrees: {_server[a].RotationY}");
            Require(_server[a].Revision == 2, "rotation must use and bump the revision");

            // Scale X (local axis, now pointing along world -Z) to double.
            _editor.SetModeForTesting(GizmoMode.Scale);
            await Frames(3);
            await Capture("build-scale");
            var viewer = new Vector3(8, 5, -6);
            var local = _editor.ViewForTesting(a)!.GlobalTransform.Basis.X.Normalized();
            Require(_editor.BeginDrag(0, viewer, (pivot + local - viewer).Normalized()), "could not grab the scale handle");
            _editor.UpdateDrag(viewer, (pivot + local * 2f - viewer).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(_server[a].ScaleX - 2f) < .06f, $"scale X should double: {_server[a].ScaleX}");
            Require(Mathf.Abs(_server[a].ScaleY - 1f) < .001f, "scaling one axis must leave the others alone");

            // Undo/redo of the three edits.
            _editor.Undo(); _editor.Undo(); _editor.Undo();
            Require(Mathf.Abs(_server[a].PositionX) < .01f && Mathf.Abs(_server[a].RotationY) < 1f && Mathf.Abs(_server[a].ScaleX - 1f) < .001f, $"undo did not restore the object: x={_server[a].PositionX} ry={_server[a].RotationY} sx={_server[a].ScaleX}");
            _editor.Redo(); _editor.Redo(); _editor.Redo();
            Require(Mathf.Abs(_server[a].PositionX - 3f) < .01f && Mathf.Abs(_server[a].ScaleX - 2f) < .06f, "redo did not reapply the edits");

            // Groups: selecting one member selects all; a move keeps the group and moves both.
            _editor.SelectIds([a, b]);
            _editor.CommandForTesting("group");
            var group = _server[a].GroupId;
            Require(group is not null && _server[b].GroupId == group, "grouping did not assign one group id");
            _editor.SelectIds([]);
            _editor.SelectForTesting(a);
            Require(_editor.Selection.Count == 2, $"selecting a member should select the whole group: {_editor.Selection.Count}");
            _editor.SetModeForTesting(GizmoMode.Move);
            var zBefore = _server[a].PositionZ; var gapBefore = _server[b].PositionX - _server[a].PositionX;
            var pivotGroup = (_editor.ViewForTesting(a)!.Position + _editor.ViewForTesting(b)!.Position) / 2f;
            Require(_editor.BeginDrag(2, cameraOrigin, (pivotGroup - cameraOrigin).Normalized()), "could not grab the handle for a group");
            _editor.UpdateDrag(cameraOrigin, (pivotGroup + new Vector3(0, 0, -2) - cameraOrigin).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(_server[a].PositionZ - (zBefore - 2f)) < .05f && Mathf.Abs(_server[b].PositionZ - (zBefore - 2f)) < .05f, $"group members must move together: {_server[a].PositionZ} {_server[b].PositionZ}");
            Require(_server[a].GroupId == group && _server[b].GroupId == group, "a move must not ungroup the objects");
            Require(Mathf.Abs((_server[b].PositionX - _server[a].PositionX) - gapBefore) < .01f, "the layout of the group changed");

            // Duplicate a group: new objects, a new group, same layout; then undo and redo it.
            var before = _server.Count;
            _editor.CommandForTesting("duplicate");
            Require(_server.Count == before + 2, $"duplicate should add 2 objects: {before} -> {_server.Count}");
            var copies = _server.Values.Where(s => s.Id != a && s.Id != b).ToList();
            Require(copies.Count == 2 && copies[0].GroupId is not null && copies[0].GroupId == copies[1].GroupId && copies[0].GroupId != group, "a duplicated group must become a new group");
            Require(Mathf.Abs(Mathf.Abs(copies[0].PositionX - copies[1].PositionX) - Mathf.Abs(_server[a].PositionX - _server[b].PositionX)) < .01f, "duplicate must keep the layout");
            _editor.Undo();
            Require(_server.Count == before, "undo of duplicate must delete the copies");
            _editor.Redo();
            Require(_server.Count == before + 2, "redo of duplicate must create the copies again");

            // Ungroup, copy/paste, delete and its undo.
            _editor.SelectIds([a, b]);
            _editor.CommandForTesting("ungroup");
            Require(_server[a].GroupId is null && _server[b].GroupId is null, "ungroup failed");
            _editor.SelectIds([a]);
            _editor.CommandForTesting("copy"); _editor.CommandForTesting("paste");
            Require(_server.Count == before + 3, $"paste should add one object: {_server.Count}");
            var count = _server.Count;
            _editor.SelectIds([a]);
            _editor.CommandForTesting("delete");
            Require(_server.Count == count - 1 && !_server.ContainsKey(a), "delete failed");
            _editor.CommandForTesting("undo");
            Require(_server.Count == count, "undo of delete must bring the object back");
            Require(_server.Values.Any(s => Mathf.Abs(s.ScaleX - 2f) < .06f && s.Name == "cube"), "the restored object lost its transform");

            // The Portuguese build window: eleven primitives, numeric fields applied through revisioned updates.
            Require(_editor.PrimitiveIdsForTesting.Contains("torus") && _editor.PrimitiveIdsForTesting.Contains("hemisphere"), "torus and hemisphere must be creatable");
            var hemisphere = Add(new Vector3(-4, .5f, -6), "hemisphere"); var torus = Add(new Vector3(-6, .5f, -6), "torus");
            Require(_editor.ViewForTesting(hemisphere)!.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh is SphereMesh { IsHemisphere: true }, "a hemisphere must render as half a sphere");
            Require(_editor.ViewForTesting(torus)!.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh is TorusMesh, "a torus must render as a torus");
            _editor.SelectIds([torus]);
            _editor.ApplyFieldsForTesting(new Vector3(5, 1, -2), new Vector3(2, 3, 4), "Parede", .3f);
            var edited = _server[torus];
            Require(Mathf.Abs(edited.PositionX - 5f) < .01f && Mathf.Abs(edited.PositionY - 1f) < .01f && Mathf.Abs(edited.PositionZ + 2f) < .01f, $"typed position not applied: {edited.PositionX},{edited.PositionY},{edited.PositionZ}");
            Require(Mathf.Abs(edited.ScaleX - 2f) < .01f && Mathf.Abs(edited.ScaleY - 3f) < .01f && Mathf.Abs(edited.ScaleZ - 4f) < .01f, "typed size not applied");
            Require(edited.Name == "Parede" && edited.GeometryJson.Contains("0.3") && edited.Revision == 1, $"name/geometry/revision wrong: {edited.Name} {edited.GeometryJson} r{edited.Revision}");
            _editor.ApplyFieldsForTesting(new Vector3(5, 1, -2), new Vector3(100, 0, 1), "Parede", 5f);
            Require(_server[torus].ScaleX <= 64f && _server[torus].ScaleY >= .01f && _server[torus].GeometryJson.Contains("0.95"), $"typed values must be clamped: {_server[torus].ScaleX} {_server[torus].ScaleY} {_server[torus].GeometryJson}");
            _editor.SelectToolForTesting();
            Require(!_editor.BeginDrag(0, new Vector3(0, 4, 4), Vector3.Forward), "the select tool must not show manipulation handles");
            Require(_editor.Selection.Count == 1, "the select tool keeps the selection");

            // Procedural geometry: six surfaces for a box, a bored hole adds triangles, faces can be painted one at a time.
            var solid = Add(new Vector3(20, .5f, -6), "cube");
            var solidView = _editor.ViewForTesting(solid)!;
            Require(solidView.IsProcedural && solidView.FaceIds.Count == 6 && solidView.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh.GetSurfaceCount() == 6, "a box must be six faces");
            Require(solidView.GetNode<StaticBody3D>("CollisionBody").GetChild<CollisionShape3D>(0).Shape is ConcavePolygonShape3D, "collision must follow the exact mesh");
            var solidTriangles = ((ArrayMesh)solidView.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh).GetFaces().Length / 3;
            _editor.SelectIds([solid]);
            _editor.ApplyFieldsForTesting(new Vector3(20, .5f, -6), Vector3.One, "Caixa oca", .5f);
            await Frames(2);
            var hollowTriangles = ((ArrayMesh)_editor.ViewForTesting(solid)!.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh).GetFaces().Length / 3;
            Require(hollowTriangles > solidTriangles && _editor.ViewForTesting(solid)!.FaceIds.Contains(PrimMeshIds.BoxInner), $"hollow must change the mesh: {solidTriangles} -> {hollowTriangles}");
            _editor.SelectFaceForTesting(solid, 0);
            _editor.SetPaintColorForTesting(new Color("#FF0000"));
            _editor.PaintFaceForTesting(false);
            Require(_server[solid].MaterialJson.Contains("\"faces\"") && _server[solid].MaterialJson.Contains("#ff0000", StringComparison.OrdinalIgnoreCase), $"face colour not saved: {_server[solid].MaterialJson}");
            _editor.PaintFaceForTesting(true);
            Require(!_server[solid].MaterialJson.Contains("\"faces\""), "clearing the face must remove its colour");

            // Align, distribute, ground and grid.
            var p1 = Add(new Vector3(30, .5f, -6), "cube"); var p2 = Add(new Vector3(34, 2f, -6), "cube");
            _editor.SelectIds([p1, p2]);
            _editor.AlignForTesting(1, 0); // Y, lowest side
            Require(Mathf.Abs(_server[p2].PositionY - .5f) < .02f && Mathf.Abs(_server[p1].PositionY - .5f) < .02f, $"align by base failed: {_server[p1].PositionY},{_server[p2].PositionY}");
            var d1 = Add(new Vector3(40, .5f, -9), "cube"); var d2 = Add(new Vector3(41, .5f, -9), "cube"); var d3 = Add(new Vector3(50, .5f, -9), "cube");
            _editor.SelectIds([d1, d2, d3]);
            _editor.DistributeForTesting(0);
            Require(Mathf.Abs(_server[d2].PositionX - 45f) < .02f && Mathf.Abs(_server[d1].PositionX - 40f) < .001f && Mathf.Abs(_server[d3].PositionX - 50f) < .001f, $"distribute failed: {_server[d2].PositionX}");
            var air = Add(new Vector3(60, 8f, -6), "cube");
            _editor.SelectIds([air]);
            _editor.RestOnGroundForTesting();
            Require(_server[air].PositionY < 7f, $"the object must come down to the ground: {_server[air].PositionY}");
            _editor.SetGridForTesting(true, true);
            Require(_editor.GridVisibleForTesting, "the grid must show under the selection");
            _editor.SetGridForTesting(false, false);
            Require(!_editor.GridVisibleForTesting, "the grid must hide");

            // Textures, UV and light.
            var textures = world.GetNode<TextureCache>("TextureCache");
            var png = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); png.Fill(new Color("#336699"));
            var assetId = Guid.NewGuid();
            Require(textures.Preload(assetId, png.SavePngToBuffer()), "a PNG must decode into a texture");
            Require(!textures.Preload(Guid.NewGuid(), new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0, 0, 0, 0, 0 }), "a GIF is not an accepted texture format");
            Require(!textures.Preload(Guid.NewGuid(), new byte[] { 1, 2, 3 }), "garbage must not become a texture");
            _editor.LoadLibraryForTesting($"{{\"items\":[{{\"assetId\":\"{assetId}\",\"name\":\"Azul\",\"mimeType\":\"image/png\",\"favorite\":true}},{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Grama\",\"mimeType\":\"image/jpeg\",\"favorite\":false}},{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Modelo\",\"mimeType\":\"model/gltf-binary\"}},{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Vetor\",\"mimeType\":\"image/svg+xml\"}}]}}");
            Require(_editor.LibraryCountForTesting == 2, $"only the two raster images belong in the texture library: {_editor.LibraryCountForTesting}");
            _editor.SelectLibraryForTesting(0, 1);
            Require(_editor.LibraryCountForTesting == 1, "favourites must show only the favourite image");
            _editor.SelectLibraryForTesting(0, 3);
            Require(_editor.LibraryCountForTesting == 6, "the system library has six patterns");

            var painted = Add(new Vector3(70, .5f, -6), "cube");
            _editor.SelectIds([painted]);
            _editor.SelectLibraryForTesting(0, 3); // checker
            _editor.ApplyTextureForTesting(false);
            Require(_server[painted].MaterialJson.Contains("\"builtin\":\"checker\""), $"built-in texture not saved: {_server[painted].MaterialJson}");
            await Frames(2);
            Require(_editor.ViewForTesting(painted)!.GetNode<MeshInstance3D>("PrimitiveMesh").GetSurfaceOverrideMaterial(0) is ShaderMaterial, "a textured object must use the texture shader");
            _editor.ApplyUvForTesting(2, 3, .5f, 90, true);
            Require(_server[painted].MaterialJson.Contains("\"uvRepeatX\":2") && _server[painted].MaterialJson.Contains("\"uvRotation\":90") && _server[painted].MaterialJson.Contains("\"flipU\":true"), $"uv not saved: {_server[painted].MaterialJson}");
            await Frames(2);
            var shader = (ShaderMaterial)_editor.ViewForTesting(painted)!.GetNode<MeshInstance3D>("PrimitiveMesh").GetSurfaceOverrideMaterial(0);
            Require(shader.GetShaderParameter("uv_scale").AsVector2().IsEqualApprox(new Vector2(-2, 3)), $"flip and repeat must reach the shader: {shader.GetShaderParameter("uv_scale")}");
            Require(Mathf.Abs(shader.GetShaderParameter("uv_rotation").AsSingle() - Mathf.DegToRad(90)) < .001f, "rotation must reach the shader");
            _editor.RemoveTextureForTesting();
            Require(!_server[painted].MaterialJson.Contains("builtin"), "removing the texture must clear it");

            var faced = Add(new Vector3(74, .5f, -6), "cube");
            _editor.SelectFaceForTesting(faced, 0);
            _editor.SelectLibraryForTesting(1, 3); // bricks
            _editor.ApplyTextureForTesting(false);
            Require(_server[faced].MaterialJson.Contains("\"faces\"") && _server[faced].MaterialJson.Contains("\"builtin\":\"bricks\""), $"face texture not saved: {_server[faced].MaterialJson}");
            await Frames(2);
            var facedMesh = _editor.ViewForTesting(faced)!.GetNode<MeshInstance3D>("PrimitiveMesh");
            Require(facedMesh.GetSurfaceOverrideMaterial(0) is ShaderMaterial && facedMesh.GetSurfaceOverrideMaterial(1) is StandardMaterial3D, "only the chosen face gets the texture");

            var imaged = Add(new Vector3(78, .5f, -6), "cube");
            _editor.SelectIds([imaged]);
            _editor.SelectLibraryForTesting(0, 1); // the favourite image
            _editor.ApplyTextureForTesting(false);
            Require(_server[imaged].MaterialJson.Contains(assetId.ToString()), "the chosen image id must be saved");
            await Frames(2);
            var imagedMaterial = (ShaderMaterial)_editor.ViewForTesting(imaged)!.GetNode<MeshInstance3D>("PrimitiveMesh").GetSurfaceOverrideMaterial(0);
            Require(imagedMaterial.GetShaderParameter("has_albedo").AsBool(), "the downloaded image must reach the material");

            var lit = Add(new Vector3(82, .5f, -6), "cube");
            _editor.SelectIds([lit]);
            _editor.ApplyLightForTesting(true, 2f, 8f);
            await Frames(2);
            var light = _editor.ViewForTesting(lit)!.GetNodeOrNull<OmniLight3D>("ObjectLight");
            Require(light is not null && Mathf.Abs(light.LightEnergy - 2f) < .01f && Mathf.Abs(light.OmniRange - 8f) < .01f && !light.ShadowEnabled, "the object must light its surroundings");
            _editor.ApplyLightForTesting(false, 2f, 8f);
            await Frames(2);
            Require(_editor.ViewForTesting(lit)!.GetNodeOrNull<OmniLight3D>("ObjectLight") is null, "removing the light must remove it");

            GD.Print("event=build_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=build_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private Guid Add(Vector3 position, string primitive)
    {
        var state = new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = primitive, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Name = primitive, Revision = 0 };
        _server[state.Id] = state;
        _editor.LoadStatesForTesting([state]);
        return state.Id;
    }

    /// Stands in for the island server: checks the revision, applies the change and replicates the result.
    private bool Sink(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var operation = root.GetProperty("operation").GetString();
        if (operation == "delete")
        {
            var id = Guid.Parse(root.GetProperty("objectId").GetString()!);
            var expected = root.GetProperty("expectedRevision").GetInt32();
            if (!_server.TryGetValue(id, out var current) || current.Revision != expected) throw new InvalidOperationException($"delete used a stale revision: {expected} vs {current?.Revision}");
            _server.Remove(id);
            _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, objectId = id }));
            return true;
        }
        var payload = JsonSerializer.Deserialize<BuildObjectState>(root.GetProperty("object").GetRawText(), Web)!;
        if (operation == "create") { payload.Id = Guid.NewGuid(); payload.Revision = 1; }
        else
        {
            payload.Id = Guid.Parse(root.GetProperty("objectId").GetString()!);
            var expected = root.GetProperty("expectedRevision").GetInt32();
            if (!_server.TryGetValue(payload.Id, out var current) || current.Revision != expected) throw new InvalidOperationException($"update used a stale revision: {expected} vs {current?.Revision}");
            payload.Revision = expected + 1;
        }
        _server[payload.Id] = payload;
        _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, objectState = payload }, Web));
        return true;
    }

    private async Task Capture(string name)
    {
        var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().GetTexture().GetImage().SavePng($"{path}-{name}.png");
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
