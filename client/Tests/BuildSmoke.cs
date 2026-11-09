using System.Text.Json;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

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
    private readonly List<string> _sentLocks = [], _sentReturns = [], _sentRez = [];

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
            Require(_editor.ViewForTesting(hemisphere)!.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh is ArrayMesh hemi && hemi.GetAabb().Size.Y is > .45f and < .55f, "a hemisphere must render as half a sphere");
            Require(_editor.ViewForTesting(torus)!.GetNode<MeshInstance3D>("PrimitiveMesh").Mesh is ArrayMesh ring && ring.GetAabb().Size.Y is > .25f and < .35f && ring.GetAabb().Size.X is > .95f, "a torus must render as a torus");
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

            // Saving a construction in the inventory and placing it again.
            var wallA = Add(new Vector3(100, 1, -6), "cube"); var wallB = Add(new Vector3(104, 1, -6), "cube"); var roof = Add(new Vector3(102, 3, -2), "prism");
            _editor.SelectIds([wallA, wallB]); _editor.CommandForTesting("group");
            _editor.SelectIds([wallA, wallB, roof]);
            string? savedJson = null;
            _editor.PackageSink = json => { savedJson = json; return true; };
            _editor.SetPackageNameForTesting("Casinha");
            _editor.SaveSelectionForTesting(false);
            var package = BuildPackages.Parse(savedJson ?? "", out var packageError) ?? throw new InvalidOperationException($"saved package invalid: {packageError}");
            Require(package.Name == "Casinha" && package.Objects.Length == 3, "the package must hold the three objects");
            Require(package.Objects.Count(o => o.Group == 0) == 2 && package.Objects.Count(o => o.Group is null) == 1, "linked objects must stay linked in the package");
            Require(Mathf.Abs(package.Objects[0].X + 2f) < .01f && Mathf.Abs(package.Objects[1].X - 2f) < .01f, $"positions must be relative to the middle: {package.Objects[0].X},{package.Objects[1].X}");
            Require(_server.ContainsKey(wallA) && _server.ContainsKey(roof), "saving a copy must leave the objects in the world");

            var originalGroup = _server[wallA].GroupId;
            var beforeTake = _server.Count;
            _editor.SaveSelectionForTesting(true);
            Require(_server.Count == beforeTake - 3 && !_server.ContainsKey(wallA), "taking must remove the objects from the world");
            _editor.CommandForTesting("undo");
            Require(_server.Count == beforeTake, "undoing a take must bring the objects back");

            var beforeRez = _server.Count;
            var rezzed = _editor.RezPackageJson(savedJson!, new Vector3(200, 0, -6), instant: true);
            Require(rezzed == 3 && _server.Count == beforeRez + 3, $"placing must create the three objects: {_server.Count - beforeRez}");
            var fresh = _server.Values.Where(s => s.PositionX > 190).ToList();
            Require(fresh.Count == 3 && fresh.Count(s => s.GroupId is not null) == 2 && fresh.Select(s => s.GroupId).Where(g => g is not null).Distinct().Count() == 1, "the placed walls must be one new group");
            Require(!fresh.Any(s => s.GroupId == originalGroup), "a placed copy must not share the original's group");
            Require(_editor.Selection.Count == 3, "the placed construction must be selected");
            _editor.CommandForTesting("undo");
            Require(_server.Count == beforeRez, "one undo must remove the whole placed construction");

            var smallBefore = _server.Count;
            _editor.RezPackageJson(savedJson!, new Vector3(300, 0, -6));
            Require(_server.Count == smallBefore + 3 && _editor.RezPendingForTesting == 0, "a small construction goes out at once");
            var bigJson = BuildPackages.Serialize(BuildPackages.Create("Grande", Enumerable.Range(0, 12).Select(i => new SourceObject("cube", "B", "", i, 1, 0, 0, 0, 0, 1, 1, 1, "{}", "{}", "{\"collision\":true}", "[]", null)).ToList()));
            var pacedBefore = _server.Count;
            _editor.RezPackageJson(bigJson, new Vector3(400, 0, -6));
            Require(_editor.RezPendingForTesting == 12 && _server.Count == pacedBefore, "a big construction is paced: nothing is created in the first instant");
            await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
            Require(_server.Count == pacedBefore + 12 && _editor.RezPendingForTesting == 0, $"the paced placing must finish: {_server.Count - pacedBefore}");
            Require(_editor.RezPackageJson("{\"not\":\"a package\"}", Vector3.Zero) == 0, "a damaged package must not place anything");

            // The Conteúdo tab.
            var box = Add(new Vector3(110, .5f, -6), "cube");
            _editor.SelectIds([box]);
            var soundAsset = Guid.NewGuid();
            _editor.AddContentForTesting(soundAsset, "Campainha", "sounds");
            Require(_server[box].ContentJson.Contains(soundAsset.ToString()) && _editor.ContentCountForTesting == 1, $"content not saved: {_server[box].ContentJson}");
            _editor.AddContentForTesting(soundAsset, "Campainha", "sounds");
            Require(_editor.ContentCountForTesting == 1, "the same item cannot be listed twice");
            _editor.RenameContentForTesting(0, "Toque");
            Require(_server[box].ContentJson.Contains("Toque") && _editor.ContentTextForTesting(0).StartsWith("Toque"), "renaming must reach the server and the list");
            _editor.RemoveContentForTesting(0);
            Require(_editor.ContentCountForTesting == 0 && _server[box].ContentJson == "[]", "removing must empty the object");
            _editor.FillPicker($"{{\"items\":[{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Som\",\"category\":\"sounds\"}},{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Lixo\",\"category\":\"sounds\",\"deleted\":true}},{{\"assetId\":\"{Guid.NewGuid()}\",\"name\":\"Estranho\",\"category\":\"malware\"}}]}}");
            Require(_editor.PickerCountForTesting == 1, "the picker lists only kept items of kinds an object can hold");

            // Building together: leases, refusals, limits, returning and history.
            var shared = Add(new Vector3(120, .5f, -6), "cube");
            _sentLocks.Clear();
            _editor.SelectIds([shared]);
            Require(_sentLocks.Any(j => j.Contains("\"lock\"") && j.Contains(shared.ToString())) && _editor.MyLockCountForTesting == 1, "selecting an object must take a lease on it");
            _sentLocks.Clear(); _editor.RenewLocksForTesting();
            Require(_sentLocks.Count == 1 && _sentLocks[0].Contains("\"lock\""), "the lease must be renewed while the selection stays");
            _sentLocks.Clear(); _editor.SelectIds([]);
            Require(_sentLocks.Any(j => j.Contains("\"unlock\"") && j.Contains(shared.ToString())) && _editor.MyLockCountForTesting == 0, "letting go of the selection must give the lease back");

            var maria = Guid.NewGuid();
            string LockMessage(string operation) => $"{{\"operation\":\"{operation}\",\"objectIds\":[\"{shared}\"],\"userId\":\"{maria}\",\"userName\":\"Maria\",\"expiresAtMs\":{DateTimeOffset.UtcNow.AddSeconds(20).ToUnixTimeMilliseconds()}}}";
            _editor.ApplyOperationForTesting(LockMessage("lock"));
            Require(_editor.LockedByForTesting(shared) == "Maria", "someone else's lease must be known");
            await Frames(2);
            Require(_editor.ViewForTesting(shared)!.GetNodeOrNull<Label3D>("LockTag") is { } tag && tag.Text.Contains("Maria"), "a locked object must show who is editing it");
            _sentLocks.Clear(); _editor.SelectIds([shared]);
            Require(!_sentLocks.Any(j => j.Contains("\"lock\"")), "a lease held by someone else must not be requested");
            Require(!_editor.BeginDrag(0, new Vector3(0, 4, 4), Vector3.Forward) && _editor.StatusForTesting.Contains("Maria"), "no handles on an object someone else is editing, and the person is told why");
            _editor.ApplyOperationForTesting(LockMessage("unlock"));
            Require(_editor.LockedByForTesting(shared) is null, "an unlock must free the object");
            _editor.ApplyOperationForTesting($"{{\"operation\":\"lock\",\"objectIds\":[\"{shared}\"],\"userId\":\"{maria}\",\"userName\":\"Maria\",\"expiresAtMs\":{DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds()}}}");
            Require(_editor.LockedByForTesting(shared) is null, "an expired lease must be ignored even if the unlock never arrived");

            foreach (var (code, expected) in new[] { ("locked:Maria", "Maria"), ("parcel_forbidden", "terreno"), ("forbidden", "permissão"), ("not_owner", "não é seu"), ("cost_limit", "custo"), ("parcel_object_limit", "terreno"), ("not_allowed_to_return", "devolver"), ("invalid_object", "recusou"), ("object_quota", "limite de objetos") })
            {
                _editor.RejectForTesting(code);
                Require(_editor.StatusForTesting.Contains(expected, StringComparison.OrdinalIgnoreCase), $"refusal '{code}' must be explained in Portuguese: '{_editor.StatusForTesting}'");
            }

            _sentReturns.Clear(); _editor.SelectIds([shared]);
            _editor.ReturnSelectionForTesting();
            Require(_sentReturns.Count == 1 && _sentReturns[0].Contains("\"return\"") && _sentReturns[0].Contains(shared.ToString()), "returning must ask the island to return the selection");
            var objectsBefore = _editor.ObjectCountForTesting;
            _editor.ApplyOperationForTesting($"{{\"operation\":\"delete\",\"objectId\":\"{shared}\",\"returned\":true}}");
            Require(_editor.ObjectCountForTesting == objectsBefore - 1, "a returned object must leave the world for everyone");

            _editor.ShowLimits("{\"objects\":1234,\"maxObjects\":5000,\"cost\":52000,\"maxCost\":200000,\"canBuildHere\":true,\"isAdmin\":false,\"parcel\":{\"id\":\"" + Guid.NewGuid() + "\",\"name\":\"Praça\",\"mine\":true,\"publicBuild\":false,\"objects\":45,\"maxObjects\":100,\"cost\":300,\"maxCost\":2000}}");
            Require(_editor.LimitsTextForTesting.Contains("1.234") && _editor.LimitsTextForTesting.Contains("52 mil") && _editor.LimitsTextForTesting.Contains("Praça") && _editor.LimitsTextForTesting.Contains("(seu)") && _editor.LimitsTextForTesting.Contains("pode construir aqui"), $"limits text: {_editor.LimitsTextForTesting}");
            _editor.ShowLimits("{\"objects\":1,\"maxObjects\":5000,\"cost\":1,\"maxCost\":200000,\"canBuildHere\":false,\"parcel\":null}");
            Require(_editor.LimitsTextForTesting.Contains("não pode construir"), "a spot where building is not allowed must say so");
            _editor.ShowLimits("garbage");
            Require(_editor.LimitsTextForTesting.Contains("Não foi possível"), "a broken answer must not leave stale numbers");
            _editor.ShowHistoryText($"[{{\"operation\":\"update\",\"actorUserId\":\"{Guid.NewGuid()}\",\"revision\":3,\"at\":\"2026-11-03T12:00:00Z\"}},{{\"operation\":\"create\",\"actorUserId\":\"{Guid.NewGuid()}\",\"revision\":1,\"at\":\"2026-11-03T11:00:00Z\"}}]");
            Require(_editor.HistoryTextForTesting.Contains("Alterado") && _editor.HistoryTextForTesting.Contains("Criado") && _editor.HistoryTextForTesting.Contains("revisão 3"), $"history text: {_editor.HistoryTextForTesting}");

            // Polish: unchanged objects are left alone, look-alikes share their mesh, far objects fade out of range, big loads are paced.
            var spare = Add(new Vector3(130, .5f, -6), "cube");
            _editor.SelectIds([spare]);
            _editor.ApplyFieldsForTesting(new Vector3(130, .5f, -6), Vector3.One, "Quieta", 0f); // settles the stored geometry text
            await Frames(2);
            var spareView = _editor.ViewForTesting(spare)!; var meshNode = spareView.GetNode("PrimitiveMesh");
            _editor.ApplyFieldsForTesting(new Vector3(133, .5f, -6), Vector3.One, "Quieta", 0f);
            await Frames(2);
            Require(Mathf.Abs(spareView.Position.X - 133f) < .01f && ReferenceEquals(meshNode, spareView.GetNode("PrimitiveMesh")), "moving an object must not rebuild its mesh");
            _editor.ApplyFieldsForTesting(new Vector3(133, .5f, -6), Vector3.One, "Quieta", .4f);
            await Frames(2);
            Require(!ReferenceEquals(meshNode, spareView.GetNode("PrimitiveMesh")), "changing the shape must rebuild it");

            BuildObjectView.ClearCachesForTesting();
            var twins = Enumerable.Range(0, 30).Select(i => Add(new Vector3(140 + i, .5f, -20), "cube")).ToList();
            Require(BuildObjectView.CachedMeshesForTesting == 1, $"thirty identical boxes need one mesh, not {BuildObjectView.CachedMeshesForTesting}");
            var firstMesh = _editor.ViewForTesting(twins[0])!.GetNode<MeshInstance3D>("PrimitiveMesh"); var lastMesh = _editor.ViewForTesting(twins[29])!.GetNode<MeshInstance3D>("PrimitiveMesh");
            Require(ReferenceEquals(firstMesh.Mesh, lastMesh.Mesh) && ReferenceEquals(firstMesh.GetSurfaceOverrideMaterial(0), lastMesh.GetSurfaceOverrideMaterial(0)), "look-alikes must share mesh and material");
            Require(ReferenceEquals(_editor.ViewForTesting(twins[0])!.GetNode<StaticBody3D>("CollisionBody").GetChild<CollisionShape3D>(0).Shape, _editor.ViewForTesting(twins[29])!.GetNode<StaticBody3D>("CollisionBody").GetChild<CollisionShape3D>(0).Shape), "look-alikes must share their collision shape");
            Require(Mathf.Abs(_editor.ViewForTesting(twins[0])!.VisibilityRangeForTesting - BuildObjectView.RangePerMetre) < .01f, "a one-metre object is drawn up to 120 m");
            var giant = Add(new Vector3(0, 5, -60), "cube"); _server[giant].ScaleX = 20; _editor.LoadStatesForTesting([_server[giant]]);
            Require(Mathf.Abs(_editor.ViewForTesting(giant)!.VisibilityRangeForTesting - BuildObjectView.MaxRange) < .01f, "the range is capped for huge objects");

            var backlog = Enumerable.Range(0, 100).Select(i => new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "cube", Name = $"Fila{i}", PositionX = i, PositionZ = -80, Revision = 1 }).ToList();
            var countBefore = _editor.ObjectCountForTesting;
            _editor.QueueStatesForTesting(backlog);
            Require(_editor.LoadBacklogForTesting == 100 && _editor.ObjectCountForTesting == countBefore, "queued objects are not built yet");
            _editor.PumpLoadForTesting();
            Require(_editor.ObjectCountForTesting == countBefore + 40 && _editor.LoadBacklogForTesting == 60, "a frame builds a bounded number of objects");
            _editor.PumpLoadForTesting(); _editor.PumpLoadForTesting();
            Require(_editor.ObjectCountForTesting == countBefore + 100 && _editor.LoadBacklogForTesting == 0, "the whole backlog is eventually built");

            // A big paste is paced so the island limit on operations is never hit.
            var many = Enumerable.Range(0, 12).Select(i => Add(new Vector3(200 + i, .5f, -30), "cube")).ToList();
            _editor.SelectIds(many); _editor.CommandForTesting("copy");
            var serverBefore = _server.Count;
            _editor.CommandForTesting("paste");
            Require(_editor.RezPendingForTesting == 12 && _server.Count == serverBefore, "a paste of many objects is queued, not sent in one burst");
            await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
            Require(_server.Count == serverBefore + 12 && _editor.RezPendingForTesting == 0, $"the queued paste must finish: {_server.Count - serverBefore}");

            // The saving indicator follows the answers of the server.
            Require(_editor.SaveTextForTesting.Contains("Salvo") && _editor.PendingForTesting == 0, "nothing pending: saved");
            var realSink = _editor.OperationSink; _editor.OperationSink = _ => true; // a server that never answers
            _editor.SelectIds([spare]);
            _editor.ApplyFieldsForTesting(new Vector3(135, .5f, -6), Vector3.One, "Quieta", .4f);
            Require(_editor.PendingForTesting == 1 && _editor.SaveTextForTesting.Contains("Salvando"), $"an unanswered change shows as saving: {_editor.SaveTextForTesting}");
            _editor.ExpireSavingForTesting();
            Require(_editor.SaveTextForTesting.Contains("Sem resposta"), "a change the server never answers must be flagged");
            _editor.RejectForTesting("rate_limit");
            Require(_editor.PendingForTesting == 0 && _editor.SaveTextForTesting.Contains("Salvo"), "a refusal ends the waiting");
            _editor.OperationSink = realSink;
            _editor.ApplyFieldsForTesting(new Vector3(135, .5f, -6), Vector3.One, "Quieta", .4f);
            Require(_editor.PendingForTesting == 0 && _server[spare].PositionX == 135, "an answered change settles at once");

            // The editing camera takes over, circles the selection, focuses and gives the view back.
            var lookCamera = GetViewport().GetCamera3D();
            _editor.SelectIds([spare]);
            _editor.ToggleEditCameraForTesting();
            var rig = _editor.EditCameraForTesting;
            Require(_editor.EditCameraActive && GetViewport().GetCamera3D() == rig, "the editing camera must become the current camera");
            Require(rig.Target.DistanceTo(new Vector3(135, .5f, -6)) < 1.5f, $"it must look at the selection: {rig.Target}");
            var near = rig.Distance; rig.ZoomSteps(3);
            Require(rig.Distance < near, "zooming in must bring it closer");
            _editor.SelectIds([twins[5]]); _editor.FocusSelectionForTesting();
            Require(rig.Target.DistanceTo(new Vector3(145, .5f, -20)) < 1.5f, $"focus must move to the new selection: {rig.Target}");
            _editor.ToggleEditCameraForTesting();
            Require(!_editor.EditCameraActive && GetViewport().GetCamera3D() == lookCamera, "leaving must give the camera of the player back");

            // The tutorial moves on only when the person really does each thing, in order.
            _editor.ForgetTutorialDoneForTesting(); _editor.RestartTutorialForTesting();
            Require(_editor.TutorialCardVisibleForTesting && _editor.TutorialTextForTesting.StartsWith("1/8"), "the tutorial starts with step 1");
            var lone = Add(new Vector3(300, .5f, -6), "cube");
            _editor.SelectIds([lone]);
            _editor.ApplyFieldsForTesting(new Vector3(300, .5f, -6), new Vector3(2, 2, 2), "Treino", 0f);
            Require(_editor.TutorialStepForTesting == TutorialStep.Create, "doing step 3 before step 1 must not count");
            _editor.CommandForTesting("create");
            Require(_editor.IsPlacingForTesting && _editor.TutorialStepForTesting == TutorialStep.Create, "the cube button only starts choosing a spot: nothing is created before the click");
            _editor.CommandForTesting("create");
            Require(!_editor.IsPlacingForTesting, "pressing the button again cancels the placement");
            _editor.CommandForTesting("create");
            _editor.PlaceAtForTesting(new Vector3(310, .5f, -6));
            Require(!_editor.IsPlacingForTesting, "one click places one shape and ends the placement");
            Require(_editor.TutorialStepForTesting == TutorialStep.Move && _editor.TutorialTextForTesting.StartsWith("2/8"), $"creating a box finishes step 1: {_editor.TutorialStepForTesting}");
            var trained = _editor.Selection[0].State.Id;
            var at = _editor.Selection[0].Position;
            _editor.ApplyFieldsForTesting(at + new Vector3(2, 0, 0), _editor.Selection[0].Scale, "Treino", 0f);
            Require(_editor.TutorialStepForTesting == TutorialStep.Resize, "moving finishes step 2");
            _editor.ApplyFieldsForTesting(_editor.Selection[0].Position, new Vector3(3, 3, 3), "Treino", 0f);
            Require(_editor.TutorialStepForTesting == TutorialStep.Rotate, "resizing finishes step 3");
            _editor.ApplyRotationForTesting(new Vector3(0, 45, 0));
            Require(_editor.TutorialStepForTesting == TutorialStep.Texture, "turning finishes step 4");
            _editor.ApplyLightForTesting(true, 1f, 4f);
            Require(_editor.TutorialStepForTesting == TutorialStep.Duplicate, "changing the look finishes step 5");
            _editor.CommandForTesting("duplicate");
            Require(_editor.TutorialStepForTesting == TutorialStep.Link, "duplicating finishes step 6");
            _editor.SelectIds([trained, lone]); _editor.CommandForTesting("group");
            Require(_editor.TutorialStepForTesting == TutorialStep.Save, "linking finishes step 7");
            _editor.PackageSink = _ => true; _editor.SetPackageNameForTesting("Treino"); _editor.SaveSelectionForTesting(false);
            Require(_editor.TutorialStepForTesting == TutorialStep.Done && _editor.TutorialTextForTesting.Contains("concluído"), "saving finishes the tutorial");
            _editor.RestartTutorialForTesting(); _editor.SkipTutorialForTesting();
            Require(!_editor.TutorialCardVisibleForTesting, "skipping hides the card");

            // Smooth handles: without Ctrl the drag follows the mouse exactly, instead of jumping from step to step.
            var fluid = Add(new Vector3(150, .5f, -40), "cube");
            _editor.SelectIds([fluid]); _editor.SetModeForTesting(GizmoMode.Move);
            await Frames(2);
            var fluidPivot = new Vector3(150, .5f, -40); var fluidEye = new Vector3(150, 4, -30);
            Require(_editor.BeginDrag(0, fluidEye, (fluidPivot - fluidEye).Normalized()), "could not grab the handle for the smooth move");
            _editor.UpdateDrag(fluidEye, (fluidPivot + new Vector3(.37f, 0, 0) - fluidEye).Normalized());
            Require(Mathf.Abs(_editor.ViewForTesting(fluid)!.Position.X - 150.37f) < .005f, $"a move of 0.37 m must be followed exactly, not rounded to a step: {_editor.ViewForTesting(fluid)!.Position.X}");
            _editor.UpdateDrag(fluidEye, (fluidPivot + new Vector3(.52f, 0, 0) - fluidEye).Normalized());
            Require(Mathf.Abs(_editor.ViewForTesting(fluid)!.Position.X - 150.52f) < .005f, "and every in-between position as well");
            _editor.EndDrag();
            Require(Mathf.Abs(_server[fluid].PositionX - 150.52f) < .002f, $"the saved position is the dragged one, tidied to a millimetre: {_server[fluid].PositionX}");

            _editor.SetModeForTesting(GizmoMode.Rotate); await Frames(2);
            var fluidTop = new Vector3(150.52f, 6, -40); var fluidSpin = new Vector3(150.52f, .5f, -40);
            Require(_editor.BeginDrag(1, fluidTop, (fluidSpin + new Vector3(1, 0, 0) - fluidTop).Normalized()), "could not grab the ring for the smooth turn");
            var radians = Mathf.DegToRad(33f);
            _editor.UpdateDrag(fluidTop, (fluidSpin + new Vector3(Mathf.Cos(radians), 0, -Mathf.Sin(radians)) - fluidTop).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(Mathf.Abs(_server[fluid].RotationY) - 33f) < .3f, $"a turn of 33 degrees must stay 33, not become a multiple of 5: {_server[fluid].RotationY}");

            _editor.SetModeForTesting(GizmoMode.Scale); await Frames(2);
            var fluidLocal = _editor.ViewForTesting(fluid)!.GlobalTransform.Basis.X.Normalized();
            var fluidViewer = new Vector3(158, 5, -40);
            Require(_editor.BeginDrag(0, fluidViewer, (fluidSpin + fluidLocal - fluidViewer).Normalized()), "could not grab the handle for the smooth stretch");
            _editor.UpdateDrag(fluidViewer, (fluidSpin + fluidLocal * 1.37f - fluidViewer).Normalized());
            _editor.EndDrag();
            Require(Mathf.Abs(_server[fluid].ScaleX - 1.37f) < .02f, $"a stretch to 1.37 must not jump to a 5 % step: {_server[fluid].ScaleX}");
            _editor.SetModeForTesting(GizmoMode.Move);

            // linksets: a root with a tree under it, edited as a whole or one part at a time, protected against accidents, undoable
            var lsRoot = Add(new Vector3(400, .5f, -6), "cube"); var lsA = Add(new Vector3(403, .5f, -6), "cube"); var lsB = Add(new Vector3(406, .5f, -6), "cube"); var lsAlone = Add(new Vector3(420, .5f, -6), "cube");
            _editor.SelectIds([lsRoot, lsA, lsB]); _editor.LinkForTesting();
            var lsGroup = _server[lsRoot].GroupId; Require(lsGroup is not null && _server[lsA].GroupId == lsGroup && _server[lsB].GroupId == lsGroup, "linking must put the three in one linkset");
            Require(_server[lsRoot].ParentObjectId is null && _server[lsA].ParentObjectId == lsRoot && _server[lsB].ParentObjectId == lsRoot, "the first selected is the root and the others hang under it");
            _editor.SelectForTesting(lsA); Require(_editor.Selection.Count == 3, "clicking a part of a linkset selects the whole linkset");
            _editor.SetPartModeForTesting(true, children: false); _editor.SelectForTesting(lsA); Require(_editor.Selection.Count == 1 && _editor.LinkNoteForTesting.Contains("peça de “"), $"part mode selects only that part: {_editor.LinkNoteForTesting}");
            _editor.SelectForTesting(lsRoot); Require(_editor.Selection.Count == 1 && _editor.LinkNoteForTesting.Contains("raiz do conjunto"), "the root alone, without its children");
            _editor.SetPartModeForTesting(true, children: true); _editor.SelectForTesting(lsRoot); Require(_editor.Selection.Count == 3, "part mode with children takes what hangs under the part"); _editor.SetPartModeForTesting(false);
            var tree = _editor.HierarchyLinesForTesting; var setAt = tree.ToList().IndexOf("Conjunto de 3 peças"); Require(setAt >= 0 && tree[setAt + 1].EndsWith("(raiz)") && tree[setAt + 2].StartsWith("    ") && tree[setAt + 3].StartsWith("    "), $"the tree shows the root and its two parts: {string.Join(" | ", tree)}");
            // another root: the chain turns around and nobody leaves the tree
            _editor.PickInHierarchyForTesting(lsB); _editor.MakeRootForTesting(); Require(_server[lsB].ParentObjectId is null && _server[lsRoot].ParentObjectId == lsB && _server[lsA].ParentObjectId == lsRoot, "making a part the root turns the chain around");
            _editor.CommandForTesting("undo"); Require(_server[lsRoot].ParentObjectId is null && _server[lsB].ParentObjectId == lsRoot, "undo of a root change restores the tree in one step");
            _editor.CommandForTesting("redo"); Require(_server[lsB].ParentObjectId is null, "redo asks for it again"); _editor.CommandForTesting("undo");
            // protection: not even a move or a delete, until it is lifted
            _editor.SelectIds([lsAlone]); _editor.ProtectForTesting(true); Require(_server[lsAlone].EditLocked && _editor.LinkNoteForTesting.Contains("🔒"), "protecting marks the object");
            var revBefore = _server[lsAlone].Revision; _editor.CommandForTesting("delete"); Require(_server.ContainsKey(lsAlone) && _server[lsAlone].Revision == revBefore && _editor.StatusForTesting.Contains("protegido"), $"a protected object is not deleted: {_editor.StatusForTesting}");
            Require(!_editor.BeginDrag(0, new Vector3(420, 5, 4), (new Vector3(420, .5f, -6) - new Vector3(420, 5, 4)).Normalized()), "a protected object shows no handles to drag");
            _editor.SelectIds([lsAlone, lsA]); _editor.UnlinkForTesting(); Require(_server[lsA].GroupId is not null, "a selection with a protected object is not linked or unlinked"); _editor.SelectIds([lsAlone]); _editor.ProtectForTesting(false); Require(!_server[lsAlone].EditLocked, "unprotecting frees it");
            _editor.CommandForTesting("undo"); Require(_server[lsAlone].EditLocked, "undo of unprotect protects again"); _editor.CommandForTesting("undo"); Require(!_server[lsAlone].EditLocked, "and undo of protect frees it");
            // duplicating a linkset keeps the shape of its tree
            _editor.SelectIds([lsRoot, lsA, lsB]); var countBeforeCopy = _server.Count; _editor.CommandForTesting("duplicate"); await Frames(3);
            var copiesOfSet = _server.Values.Where(s => s.GroupId is not null && s.GroupId != lsGroup && s.PositionX >= 400).ToList(); Require(_server.Count == countBeforeCopy + 3 && copiesOfSet.Count == 3, $"the copies are a linkset of their own: {_server.Count - countBeforeCopy}");
            Require(copiesOfSet.Count(s => s.ParentObjectId is null) == 1 && copiesOfSet.Count(s => s.ParentObjectId is not null && copiesOfSet.Any(c => c.Id == s.ParentObjectId)) == 2, "the copy has one root and two parts hanging under it");
            // deleting the root re-hangs what was under it (the server says what changed)
            _editor.SelectIds([lsRoot]); _editor.SetPartModeForTesting(false); _editor.CommandForTesting("delete"); Require(!_server.ContainsKey(lsRoot) && _server[lsA].GroupId is not null && (_server[lsA].ParentObjectId is null ^ _server[lsB].ParentObjectId is null), "deleting the root promotes one of its parts and re-hangs the other");

            // precise transforms: pivot, axes, typed moves/turns/stretches, mirror, multiple duplication
            var pa = Add(new Vector3(500, .5f, -6), "cube"); var pb = Add(new Vector3(504, .5f, -6), "cube"); _editor.SelectIds([pa, pb]);
            Require((_editor.PivotForTesting - new Vector3(502, .5f, -6)).Length() < .001f, $"the default pivot is the centre of the selection: {_editor.PivotForTesting}");
            _editor.SetPivotModeForTesting(1); Require((_editor.PivotForTesting - new Vector3(502, 0, -6)).Length() < .001f, "the base pivot is the lowest point"); _editor.SetPivotModeForTesting(3); Require((_editor.PivotForTesting - new Vector3(500, .5f, -6)).Length() < .001f, "the first selected");
            _editor.SetPivotModeForTesting(6); Require(_editor.PivotForTesting.Length() < .001f, "the world origin"); _editor.SetPivotPointForTesting(500, 0, -10); _editor.SetPivotModeForTesting(5); Require((_editor.PivotForTesting - new Vector3(500, 0, -10)).Length() < .001f, "a typed point");
            _editor.SetPivotModeForTesting(0); _editor.MoveByForTesting(1, 2, 3); Require(Mathf.Abs(_server[pa].PositionX - 501) < .001f && Mathf.Abs(_server[pa].PositionY - 2.5f) < .001f && Mathf.Abs(_server[pb].PositionZ + 3) < .001f, $"a typed move: {_server[pa].PositionX},{_server[pa].PositionY}");
            _editor.CommandForTesting("undo"); Require(Mathf.Abs(_server[pa].PositionX - 500) < .001f && Mathf.Abs(_server[pb].PositionZ + 6) < .001f, "a typed move is one undo step");
            _editor.SelectIds([pa, pb]); _editor.TurnForTesting(1, 90); Require(Mathf.Abs(_server[pa].PositionX - 502) < .01f && Mathf.Abs(Mathf.Abs(_server[pa].RotationY) - 90) < .1f && Mathf.Abs(_server[pb].PositionX - 502) < .01f, $"a quarter turn around the centre: a={_server[pa].PositionX},{_server[pa].PositionZ} rot {_server[pa].RotationY}; status {_editor.StatusForTesting}");
            _editor.CommandForTesting("undo"); _editor.SelectIds([pa, pb]); _editor.StretchForTesting(2, 2, 2); Require(Mathf.Abs(_server[pa].PositionX - 498) < .01f && Mathf.Abs(_server[pb].PositionX - 506) < .01f && Mathf.Abs(_server[pa].ScaleX - 2) < .01f, $"a stretch around the centre doubles size and distances: {_server[pa].PositionX} {_server[pa].ScaleX}");
            _editor.CommandForTesting("undo");
            // a pivot of the object itself, saved with it
            _editor.SelectIds([pa]); _editor.SetObjectPivotForTesting(0, -.5f, 0); Require(Mathf.Abs(_server[pa].PivotY + .5f) < .001f, "the pivot of the object is saved"); _editor.SetPivotModeForTesting(4); Require((_editor.PivotForTesting - new Vector3(500, 0, -6)).Length() < .001f, $"the pivot of the object is its centre plus the offset: {_editor.PivotForTesting}");
            _editor.TurnForTesting(2, 90); Require(Mathf.Abs(_server[pa].PositionY - .5f) > .1f || Mathf.Abs(_server[pa].PositionX - 500) > .1f, "turning around the object's own pivot moves its centre"); _editor.CommandForTesting("undo"); _editor.SetPivotModeForTesting(0);
            // mirror: copies by default, through the plane of the pivot; shapes that cannot be mirrored are left out
            var beforeMirror = _server.Count; _editor.SelectIds([pa, pb]); _editor.SetPivotModeForTesting(6); _editor.MirrorForTesting(0, copy: true);
            var mirroredCopies = _server.Values.Where(s => s.Id != pa && s.Id != pb && s.PositionX < -499 && s.PositionX > -505).ToList(); Require(_server.Count == beforeMirror + 2 && mirroredCopies.Count == 2, $"the mirror makes two copies: {_server.Count - beforeMirror}");
            Require(mirroredCopies.Any(s => Mathf.Abs(s.PositionX + 500) < .01f) && mirroredCopies.Any(s => Mathf.Abs(s.PositionX + 504) < .01f), "through the world origin on X"); _editor.CommandForTesting("undo"); Require(_server.Count == beforeMirror, "undoing a mirror removes its copies");
            _editor.SelectIds([pa]); _editor.MirrorForTesting(0, copy: false); Require(Mathf.Abs(_server[pa].PositionX + 500) < .01f, "mirrored in place"); _editor.CommandForTesting("undo"); _editor.SetPivotModeForTesting(0);
            // multiple duplication
            var beforeLine = _server.Count; _editor.SelectIds([pa]); _editor.ArrayForTesting(0, 3, 1, 1, new Vector3(2, 0, 0), Vector3.Zero, Vector3.One); Require(_server.Count == beforeLine + 3, $"a line of three copies: {_server.Count - beforeLine}");
            var line = _server.Values.Where(s => s.Id != pa && Mathf.Abs(s.PositionZ + 6) < .01f && s.PositionX > 500 && s.PositionX < 507 && s.PositionY < 1).Select(s => Mathf.Round(s.PositionX)).OrderBy(x => x).ToList(); Require(line.Contains(502) && line.Contains(504) && line.Contains(506), $"a step of two metres each: {string.Join(",", line)}");
            _editor.CommandForTesting("undo"); Require(_server.Count == beforeLine, "a multiple duplication is one undo step");
            var beforeGrid = _server.Count; _editor.SelectIds([pa]); _editor.ArrayForTesting(1, 2, 2, 1, new Vector3(3, 0, 3), Vector3.Zero, Vector3.One); Require(_server.Count == beforeGrid + 3, "a grid of 2 x 2 has three copies"); _editor.CommandForTesting("undo");
            var beforeCircle = _server.Count; _editor.SelectIds([pa]); _editor.SetPivotModeForTesting(5); _editor.SetPivotPointForTesting(500, .5f, -10); _editor.ArrayForTesting(2, 4, 1, 1, Vector3.Zero, Vector3.Zero, Vector3.One); Require(_server.Count == beforeCircle + 3, "a circle of four has three copies"); _editor.CommandForTesting("undo"); _editor.SetPivotModeForTesting(0);
            var beforeMany = _server.Count; _editor.SelectIds([pa, pb]); _editor.ArrayForTesting(0, 200, 1, 1, new Vector3(1, 0, 0), Vector3.Zero, Vector3.One); Require(_server.Count == beforeMany && _editor.StatusForTesting.Contains("limite"), $"too many objects at once are refused: {_editor.StatusForTesting}");
            // a copy of a linkset keeps its tree, once for each copy
            var ra = Add(new Vector3(520, .5f, -6), "cube"); var rb = Add(new Vector3(523, .5f, -6), "cube"); var rc = Add(new Vector3(526, .5f, -6), "cube"); _editor.SelectIds([ra, rb, rc]); _editor.LinkForTesting();
            var beforeSets = _server.Count; _editor.SelectIds([ra, rb, rc]); _editor.ArrayForTesting(0, 2, 1, 1, new Vector3(0, 0, 4), Vector3.Zero, Vector3.One);
            var madeSets = _server.Values.Where(s => s.GroupId is not null && s.GroupId != _server[ra].GroupId && s.PositionZ > -5 && s.PositionX >= 520).GroupBy(s => s.GroupId).ToList(); Require(_server.Count == beforeSets + 6 && madeSets.Count == 2 && madeSets.All(g => g.Count() == 3 && g.Count(s => s.ParentObjectId is null) == 1), $"each copy of the linkset is a linkset with one root: {madeSets.Count}");

            // phase 3: magnetic snap, advanced alignment, choosing by area
            var ma = Add(new Vector3(600, .5f, -20), "cube"); var mt = Add(new Vector3(604, .5f, -20), "cube"); _editor.SelectIds([ma]);
            _editor.SetMagnetForTesting(false); Require(_editor.MagnetForTesting(Vector3.Right, 2.9f) is null, "with the magnet off nothing snaps");
            _editor.SetMagnetForTesting(true, .3f); var pulled = _editor.MagnetForTesting(Vector3.Right, 2.9f); Require(pulled is { } p0 && Mathf.Abs(p0.Amount - 3f) < .01f, $"the magnet pulls the face against the neighbour: {pulled?.Amount}");
            Require(_editor.MagnetForTesting(Vector3.Right, 1.5f) is null, "too far from anything: no pull"); _editor.SetMagnetForTesting(false);
            var q1 = Add(new Vector3(610, .5f, -24), "cube"); var q2 = Add(new Vector3(613, .5f, -24), "cube"); var q3 = Add(new Vector3(617, .5f, -24), "cube");
            _editor.SelectIds([q1, q2, q3]); _editor.SetAlignOptionsForTesting(1, 0, 0); _editor.AlignForTesting(0, 0);
            Require(Mathf.Abs(_server[q2].PositionX - 610f) < .02f && Mathf.Abs(_server[q3].PositionX - 610f) < .02f && Mathf.Abs(_server[q1].PositionX - 610f) < .001f, $"aligned to the first: {_server[q2].PositionX},{_server[q3].PositionX}");
            _editor.SetAlignOptionsForTesting(0, 0, 0);
            var g1 = Add(new Vector3(620, .5f, -28), "cube"); var g2 = Add(new Vector3(621, .5f, -28), "cube"); var g3 = Add(new Vector3(630, .5f, -28), "cube");
            _editor.SelectIds([g1, g2, g3]); _editor.SetAlignOptionsForTesting(0, 2, 2); _editor.DistributeForTesting(0);
            Require(Mathf.Abs(_server[g1].PositionX - 620f) < .001f && Mathf.Abs(_server[g2].PositionX - 623f) < .02f && Mathf.Abs(_server[g3].PositionX - 626f) < .02f, $"a fixed gap of two metres: {_server[g2].PositionX},{_server[g3].PositionX}"); _editor.SetAlignOptionsForTesting(0, 0, 0);
            _editor.SelectIds([]); Require(_editor.SelectInVolumeForTesting(new Vector3(618, 0, -29), new Vector3(628, 3, -27), touching: false) == 3, $"an area of the world chooses what is inside: {_editor.StatusForTesting}");
            Require(_editor.SelectInVolumeForTesting(new Vector3(626.4f, 0, -29), new Vector3(640, 3, -27), touching: false) == 0 && _editor.SelectInVolumeForTesting(new Vector3(626.4f, 0, -29), new Vector3(640, 3, -27), touching: true) == 1, "touching also chooses what only reaches into the area");
            _editor.SelectIds([]);

            // phase 4: constructions keep their tree, presets with a preview, physics settings
            var h1 = Add(new Vector3(700, .5f, -40), "cube"); var h2 = Add(new Vector3(702, .5f, -40), "cube"); var h3 = Add(new Vector3(704, .5f, -40), "cube");
            _editor.SelectIds([h1, h2, h3]); _editor.LinkForTesting(); string? treeJson = null; _editor.PackageSink = json => { treeJson = json; return true; };
            _editor.SelectIds([h1, h2, h3]); _editor.SetPackageNameForTesting("Árvore"); _editor.SaveSelectionForTesting(false);
            var treePackage = BuildPackages.Parse(treeJson ?? "", out var treeError) ?? throw new InvalidOperationException($"tree package invalid: {treeError}");
            Require(treePackage.Objects.Count(o => o.Parent is null) == 1 && treePackage.Objects.Count(o => o.Parent == 0) == 2, $"a saved linkset keeps its tree: {string.Join(",", treePackage.Objects.Select(o => o.Parent?.ToString() ?? "-"))}");
            var beforeTree = _server.Count; _editor.RezPackageJson(treeJson!, new Vector3(800, 0, -40), instant: true);
            var treeCopies = _server.Values.Where(s => s.PositionX > 790 && s.PositionX < 810).ToList();
            Require(_server.Count == beforeTree + 3 && treeCopies.Select(s => s.GroupId).Distinct().Count() == 1 && treeCopies.Count(s => s.ParentObjectId is null) == 1, "placing a construction brings back one linkset with one root");
            var treeRoot = treeCopies.Single(s => s.ParentObjectId is null); Require(treeCopies.Count(s => s.ParentObjectId == treeRoot.Id) == 2, "and its parts hang from the root");
            _editor.CommandForTesting("undo"); Require(_server.Count == beforeTree, "the whole placing is one undo step");
            _editor.PackageSink = null;

            var beforePreset = _server.Count; _editor.PlacePresetForTesting("table");
            Require(_editor.PreviewActiveForTesting && _editor.PreviewPartsForTesting == 5 && _editor.IsPlacingForTesting, "choosing a preset shows its preview");
            Require(_server.Count == beforePreset, "the preview creates nothing");
            _editor.ConfirmPreviewForTesting(new Vector3(900, 0, -40));
            Require(!_editor.PreviewActiveForTesting && _server.Count == beforePreset + 5, $"the click places the preset: {_server.Count - beforePreset}");
            var tableParts = _server.Values.Where(s => s.PositionX > 890 && s.PositionX < 910).ToList(); var tableTop = tableParts.Single(s => s.ParentObjectId is null);
            Require(tableTop.Name == "Tampo" && Mathf.Abs(tableTop.PositionY - .75f) < .02f && tableParts.Count(s => s.ParentObjectId == tableTop.Id) == 4, $"the table is a linkset standing on the ground: tableTop at {tableTop.PositionY}");
            _editor.CommandForTesting("undo"); Require(_server.Count == beforePreset, "the preset is placed in one undo step");
            _editor.PlacePresetForTesting("wall"); _editor.CancelPreviewForTesting(); Require(!_editor.PreviewActiveForTesting && _server.Count == beforePreset, "cancelling the preview places nothing");

            var phy = Add(new Vector3(950, .5f, -40), "cube"); _editor.SelectIds([phy]);
            _editor.SetPhysicsForTesting(true, 20f, .8f, .1f);
            Require(_server[phy].PhysicsJson.Contains("\"physical\"") && _server[phy].PhysicsJson.Contains("\"mass\":20"), $"physics settings are saved: {_server[phy].PhysicsJson}");
            _editor.SelectIds([]); _editor.SelectIds([phy]); _editor.SetPhysicsForTesting(false);
            Require(!_server[phy].PhysicsJson.Contains("physical") && !_server[phy].PhysicsJson.Contains("mass"), "back to static removes the settings");
            _editor.SelectIds([]);

            // phase 5: undo that respects other people and what the server really did
            var co = Add(new Vector3(1000, .5f, -40), "cube"); _editor.SelectIds([co]); _editor.MoveByForTesting(1, 0, 0);
            Require(Mathf.Abs(_server[co].PositionX - 1001) < .01f && !_editor.ChangedByOthersForTesting(co), "my own change is not a foreign edit");
            var theirs = _server[co]; theirs.PositionX = 1010; theirs.Revision++; _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation = "update", objectState = theirs }, Web));
            Require(_editor.ChangedByOthersForTesting(co), "an edit by somebody else after mine is noticed");
            var depth = _editor.UndoDepthForTesting; _editor.SelectIds([co]); _editor.Undo();
            Require(Mathf.Abs(_server[co].PositionX - 1010) < .01f && _editor.StatusForTesting.Contains("outra pessoa"), $"undo must not write over their change: x={_server[co].PositionX}, {_editor.StatusForTesting}");
            Require(_editor.UndoDepthForTesting == depth - 1 && _editor.RedoDepthForTesting == 0, "a step that could not be undone leaves the history");
            _editor.SelectIds([co]); _editor.MoveByForTesting(1, 0, 0); Require(!_editor.ChangedByOthersForTesting(co), "building on top of their change makes it mine again");
            _editor.SelectIds([co]); _editor.Undo(); Require(Mathf.Abs(_server[co].PositionX - 1010) < .01f, $"and then undo works again: {_server[co].PositionX}");
            var heldSink = _editor.OperationSink; _editor.OperationSink = _ => true; // a server that never answers, then refuses
            var depthBefore = _editor.UndoDepthForTesting; _editor.SelectIds([co]); _editor.MoveByForTesting(0, 0, 3); Require(_editor.UndoDepthForTesting == depthBefore + 1, "the step enters the history when it is made");
            _editor.RejectForTesting("revision_conflict"); Require(_editor.UndoDepthForTesting == depthBefore, "a step the server refused leaves the history"); Require(_editor.StatusForTesting.Contains("Outra pessoa"), "and the conflict is explained");
            _editor.OperationSink = heldSink; _editor.SelectIds([co]); _editor.MoveByForTesting(0, 0, 2); var settledDepth = _editor.UndoDepthForTesting;
            _editor.OperationSink = _ => true; _editor.SelectIds([co]); _editor.Undo(); Require(_editor.RedoDepthForTesting == 1, "the undo is waiting for the server");
            _editor.RejectForTesting("rate_limit"); Require(_editor.UndoDepthForTesting == settledDepth && _editor.RedoDepthForTesting == 0, "an undo the server refused goes back where it was");
            _editor.OperationSink = heldSink; _editor.SelectIds([]);
            _editor.ShowLinksetHistoryText("{\"members\":[{},{}],\"log\":[{\"objectName\":\"Tampo\",\"operation\":\"structure\",\"actorUserId\":\"" + Guid.NewGuid() + "\",\"revision\":3,\"at\":\"2026-11-10T12:00:00Z\"}]}");
            Require(_editor.HistoryTextForTesting.Contains("Conjunto de 2 peça(s)") && _editor.HistoryTextForTesting.Contains("Vínculo alterado · Tampo"), $"the linkset history is listed: {_editor.HistoryTextForTesting}");

            // rights: shown for the selection (the strictest of it), with a warning when it may not be modified
            var closedOne = Add(new Vector3(300, .5f, -6), "cube"); _server[closedOne].OwnerRights = 5; _server[closedOne].NextOwnerRights = 4; _editor.LoadStatesForTesting([_server[closedOne]]);
            _editor.SelectIds([closedOne]); Require(_editor.RightsNoteForTesting.Contains("direitos C-T-") && _editor.RightsNoteForTesting.Contains("sem direito de modificar"), $"the rights of the selection must be shown: {_editor.RightsNoteForTesting}");
            var openOne = Add(new Vector3(303, .5f, -6), "cube"); _editor.SelectIds([closedOne, openOne]); Require(_editor.RightsNoteForTesting.Contains("direitos C-T-"), "a selection has only what every object allows");
            _editor.SelectIds([openOne]); Require(_editor.RightsNoteForTesting.Contains("direitos CMTR") && !_editor.RightsNoteForTesting.Contains("sem direito"), "a free object shows every right");
            // the refusals the new rules can give are explained in words
            _editor.RejectForTesting("no_modify"); Require(_editor.StatusForTesting.Contains("não pode ser modificado"), $"no_modify must be explained: {_editor.StatusForTesting}");
            _editor.RejectForTesting("no_copy_in_use"); Require(_editor.StatusForTesting.Contains("não pode ser copiada"), "no_copy_in_use must be explained");
            // a construction that cannot be copied is placed by the server: one command, and every object it made arrives at once
            _sentRez.Clear(); var rezItem = Guid.NewGuid(); _editor.RezByServer(rezItem, new Vector3(5, 0, 7));
            Require(_sentRez.Count == 1 && _sentRez[0].Contains("\"operation\":\"rez\"") && _sentRez[0].Contains(rezItem.ToString()) && _sentRez[0].Contains("\"originX\":5"), $"the rez command: {string.Join("|", _sentRez)}");
            var objectsBeforeRez = _editor.ObjectCountForTesting; var madeA = new BuildObjectState { Id = Guid.NewGuid(), Name = "A", PrimitiveType = "cube", PositionX = 5, PositionY = 1, PositionZ = 7, Revision = 1, OwnerRights = 6, NextOwnerRights = 4 }; var madeB = new BuildObjectState { Id = Guid.NewGuid(), Name = "B", PrimitiveType = "cube", PositionX = 7, PositionY = 1, PositionZ = 7, Revision = 1, OwnerRights = 6, NextOwnerRights = 4 };
            _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation = "rez", itemId = rezItem, consumed = true, objects = new[] { madeA, madeB } }, Web));
            Require(_editor.ObjectCountForTesting == objectsBeforeRez + 2 && _editor.StatusForTesting.Contains("Construção colocada (2 objeto(s))") && _editor.StatusForTesting.Contains("não pode ser copiado"), $"the placed construction must arrive whole: {_editor.StatusForTesting}");

            // Build window modes: each shows its own panel, only Editar shows the tabs, the status counts the selection, and locked objects are read-only
            _editor.SelectIds([openOne]);
            _editor.SetBuildModeForTesting(BuildMode.Move); Require(_editor.CurrentBuildMode == BuildMode.Move && !_editor.EditAreaVisibleForTesting, "Mover hides the property tabs");
            Require(_editor.ModeStatusForTesting == "Selecionado: 1 objeto / Prims: 1", $"the mode status counts the selection: {_editor.ModeStatusForTesting}");
            _editor.SetBuildModeForTesting(BuildMode.Create); Require(!_editor.EditAreaVisibleForTesting, "Criar hides the property tabs");
            _editor.SetBuildModeForTesting(BuildMode.Focus); Require(_editor.CurrentBuildMode == BuildMode.Focus && !_editor.IsPlacingForTesting, "leaving Criar disarms placement");
            _editor.SetBuildModeForTesting(BuildMode.Edit); Require(_editor.EditAreaVisibleForTesting, "Editar shows the property tabs");
            _editor.SelectIds([closedOne, openOne]); Require(_editor.ModeStatusForTesting == "Selecionado: 2 objetos / Prims: 2", "the status counts several objects");
            _editor.SelectIds([]); Require(!_editor.ReadOnlyForTesting && _editor.ModeStatusForTesting.StartsWith("Selecionado: 0"), "an empty selection is not read-only");

            // Geral tab sharing: only the fields that were touched are sent, with the real operation name
            var sinkBeforeSharing = _editor.OperationSink; _editor.OperationSink = _ => true;
            _editor.SelectIds([openOne]); _editor.ApplySharingForTesting(); Require(_editor.SharingCommandForTesting.Length == 0 || !_editor.SharingCommandForTesting.Contains("anyoneMove"), "nothing touched, nothing sent");
            _editor.ToggleSharingForTesting("anyoneMove", true); _editor.ToggleSharingForTesting("ownerCopy", false); _editor.SetSaleForTesting(2, "250"); _editor.ApplySharingForTesting();
            Require(_editor.SharingCommandForTesting.Contains("\"operation\":\"properties\"") && _editor.SharingCommandForTesting.Contains("\"anyoneMove\":true") && _editor.SharingCommandForTesting.Contains("\"saleType\":\"copy\"") && _editor.SharingCommandForTesting.Contains("\"salePrice\":250") && _editor.SharingCommandForTesting.Contains("ownerRights"), $"the properties command: {_editor.SharingCommandForTesting}");
            Require(!_editor.SharingCommandForTesting.Contains("groupShare") && !_editor.SharingCommandForTesting.Contains("anyoneCopy"), "untouched fields stay out of the command");
            _editor.OperationSink = sinkBeforeSharing;
            _editor.SelectIds([]);

            // Objeto tab: advanced shape sliders are real, type-aware settings
            _editor.SelectIds([openOne]);
            Require(_editor.ShapeEnabledForTesting("pathBegin") && _editor.ShapeEnabledForTesting("shearX") && !_editor.ShapeEnabledForTesting("radiusOffset") && !_editor.ShapeEnabledForTesting("revolutions"), "a box uses path cut and shear, not the torus settings");
            var shapeCommands = new List<string>(); var sinkBeforeShape = _editor.OperationSink; _editor.OperationSink = command => { shapeCommands.Add(command); return true; };
            _editor.SetShapeForTesting("pathBegin", .25); _editor.SetShapeForTesting("shearX", .2); _editor.SetShapeForTesting("holeX", .5); _editor.ApplyForTesting();
            Require(shapeCommands.Count > 0 && shapeCommands[^1].Contains("pathBegin") && shapeCommands[^1].Contains("shearX") && shapeCommands[^1].Contains("holeX") && !shapeCommands[^1].Contains("revolutions"), $"only the settings the shape uses are written: {(shapeCommands.Count > 0 ? shapeCommands[^1] : "no command")}");
            _editor.OperationSink = sinkBeforeShape; _editor.SelectIds([]);

            // Recursos tab: flexible path, light and projector really change what the object is and does
            _editor.SelectIds([openOne]); _editor.SetResourcesForTesting(true, 6, 2, true, "bricks"); await Frames(3);
            var resourceView = _editor.ViewForTesting(openOne)!;
            Require(resourceView.GetNodeOrNull("FlexDriver") is not null, "a flexible object is animated"); Require(resourceView.GetNodeOrNull<SpotLight3D>("ObjectProjector") is { LightProjector: not null }, "the projector shines an image");
            Require(resourceView.GetNodeOrNull<OmniLight3D>("ObjectLight") is not null, "the round light stays as the ambiance");
            _editor.SelectIds([]); _editor.SelectIds([openOne]); Require(_editor.FlexOnForTesting, "the flexible settings come back when the object is selected again");
            _editor.SetResourcesForTesting(false, 0, 1, false, null); await Frames(3);
            Require(_editor.ViewForTesting(openOne)!.GetNodeOrNull("FlexDriver") is null && _editor.ViewForTesting(openOne)!.GetNodeOrNull("ObjectProjector") is null, "turning them off removes them");
            _editor.SelectIds([]);

            // Textura tab: several faces at once and the advanced material settings
            _editor.SelectIds([openOne]); _editor.SelectFaceForTesting(openOne, 2, false); _editor.SelectFaceForTesting(openOne, 3, true);
            Require(_editor.SelectedFacesForTesting.SequenceEqual([2, 3]), $"shift + click adds a face: {string.Join(",", _editor.SelectedFacesForTesting)}");
            _editor.SetAdvancedMaterialForTesting(true, 2, true, true, .3); await Frames(2);
            var advancedJson = System.Text.Json.Nodes.JsonNode.Parse(_editor.ViewForTesting(openOne)!.State.MaterialJson)!.AsObject();
            var faceEntries = advancedJson["faces"]!.AsArray().Select(n => n!.AsObject()).Where(n => n["fullBright"]?.GetValue<bool>() == true).ToList();
            Require(faceEntries.Count == 2 && faceEntries.All(n => n["mapping"]?.GetValue<string>() == "planar" && n["alphaMode"]?.GetValue<string>() == "mask" && n["doubleSided"]?.GetValue<bool>() == true), $"both faces got the settings: {advancedJson.ToJsonString()}");
            Require(advancedJson["fullBright"] is null, "the rest of the object keeps its material");
            var builtMaterial = MaterialBuilder.Build(advancedJson, faceEntries[0], null);
            Require(builtMaterial is StandardMaterial3D { ShadingMode: BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode: BaseMaterial3D.CullModeEnum.Disabled, Transparency: BaseMaterial3D.TransparencyEnum.AlphaScissor }, "full bright, double sided and mask change the real material");
            _editor.SelectAllFacesForTesting(); Require(_editor.SelectedFacesForTesting.Count == 6, "all the faces of a box");
            _editor.SelectIds([]);

            // Conteúdo tab actions and the advanced edit options
            _editor.SelectIds([openOne]); var contentAsset = Guid.NewGuid(); _editor.AddContentForTesting(contentAsset, "Som do portão", "sounds"); await Frames(2);
            Require(_editor.ContentCountForTesting >= 1, "the item is in the object");
            var lastContent = _editor.ContentCountForTesting - 1;
            foreach (var action in new[] { "open", "properties", "copy", "transfer" }) { _editor.RunContentActionForTesting(lastContent, action); Require(_editor.LastContentActionForTesting.Length > 0, $"content action {action} answers"); }
            var textured = Add(new Vector3(310, .5f, -6), "cube"); _server[textured].MaterialJson = "{\"builtin\":\"bricks\"}"; _editor.LoadStatesForTesting([_server[textured]]); _editor.SelectIds([textured]);
            _editor.SetStretchOptionsForTesting(true, false); _editor.ApplyFieldsForTesting(new Vector3(310, .5f, -6), new Vector3(3, 1, 1), "Parede", 0); await Frames(2);
            Require(_editor.ViewForTesting(textured)!.State.MaterialJson.Contains("\"mapping\":\"planar\""), $"textures keep their size when stretching is off: {_editor.ViewForTesting(textured)!.State.MaterialJson}");
            var plainBox = Add(new Vector3(313, .5f, -6), "cube"); _editor.LoadStatesForTesting([_server[plainBox]]); _editor.SelectIds([plainBox]); _editor.SetStretchOptionsForTesting(true, true);
            _editor.ApplyFieldsForTesting(new Vector3(313, .5f, -6), new Vector3(3, 1, 1), "Parede", 0); await Frames(2);
            Require(!_editor.ViewForTesting(plainBox)!.State.MaterialJson.Contains("planar"), "with stretching on nothing changes");
            var turned = Add(new Vector3(316, .5f, -6), "cube"); _server[turned].RotationY = 40; _editor.LoadStatesForTesting([_server[turned]]); _editor.SelectIds([turned]);
            _editor.SetAxesForTesting(1); Require(Mathf.Abs(Mathf.RadToDeg(_editor.AxesEulerForTesting.Y) - 40) < .5f, "local axes follow the object");
            _editor.SelectIds([turned]); _editor.UseSelectionAsReferenceForTesting(); _editor.SelectIds([plainBox]);
            Require(Mathf.Abs(Mathf.RadToDeg(_editor.AxesEulerForTesting.Y) - 40) < .5f, "the reference frame stays the chosen object while another is selected");
            _editor.SetAxesForTesting(2); Require(Mathf.Abs(Mathf.RadToDeg(_editor.GridRotationForTesting.Y) - 40) < .5f, "the grid follows the reference");
            _editor.SetAxesForTesting(0); Require(Mathf.IsZeroApprox(_editor.GridRotationForTesting.Y), "world axes keep the grid straight");
            _editor.SelectIds([]);

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
        if (operation is "lock" or "unlock") { _sentLocks.Add(json); return true; }
        if (operation == "return") { _sentReturns.Add(json); return true; }
        if (operation == "rez") { _sentRez.Add(json); return true; }
        if (operation == "structure")
        {
            // what the server does: every object must be at the revision the editor saw, the result must be a consistent set of trees, and one message carries all that changed
            var asked = root.GetProperty("links").EnumerateArray().Select(l => (Id: Guid.Parse(l.GetProperty("objectId").GetString()!), Group: l.GetProperty("groupId").ValueKind == JsonValueKind.Null ? (Guid?)null : Guid.Parse(l.GetProperty("groupId").GetString()!), Parent: l.GetProperty("parentId").ValueKind == JsonValueKind.Null ? (Guid?)null : Guid.Parse(l.GetProperty("parentId").GetString()!), Revision: l.GetProperty("expectedRevision").GetInt32())).ToList();
            foreach (var a in asked) if (!_server.TryGetValue(a.Id, out var s) || s.Revision != a.Revision || s.EditLocked) throw new InvalidOperationException($"structure used a stale revision or a protected object: {a.Id}");
            var after = _server.Values.Select(s => asked.Any(a => a.Id == s.Id) ? new LifeVerse.Shared.Building.LinkNode(s.Id, asked.First(a => a.Id == s.Id).Group, asked.First(a => a.Id == s.Id).Group is null ? null : asked.First(a => a.Id == s.Id).Parent) : new LifeVerse.Shared.Building.LinkNode(s.Id, s.GroupId, s.ParentObjectId)).ToList();
            if (LifeVerse.Shared.Building.LinksetRules.Validate(after) is { } broken) throw new InvalidOperationException("structure left a broken tree: " + broken);
            var changed = new List<BuildObjectState>();
            foreach (var n in after) { var s = _server[n.Id]; if (s.GroupId == n.GroupId && s.ParentObjectId == n.ParentId) continue; s.GroupId = n.GroupId; s.ParentObjectId = n.ParentId; s.Revision++; changed.Add(s); }
            _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, objects = changed }, Web)); return true;
        }
        if (operation == "protect")
        {
            var ids = root.GetProperty("objectIds").EnumerateArray().Select(i => Guid.Parse(i.GetString()!)).ToList(); var locked = root.GetProperty("locked").GetBoolean(); var changed = new List<BuildObjectState>();
            foreach (var id in ids) { var s = _server[id]; if (s.EditLocked == locked) continue; s.EditLocked = locked; s.Revision++; changed.Add(s); }
            _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, locked, objects = changed }, Web)); return true;
        }
        if (operation == "delete")
        {
            var id = Guid.Parse(root.GetProperty("objectId").GetString()!);
            var expected = root.GetProperty("expectedRevision").GetInt32();
            if (!_server.TryGetValue(id, out var current) || current.Revision != expected) throw new InvalidOperationException($"delete used a stale revision: {expected} vs {current?.Revision}");
            if (current.EditLocked) throw new InvalidOperationException("delete of a protected object");
            _server.Remove(id); var healedNow = LifeVerse.Shared.Building.LinksetRules.Heal([.. _server.Values.Append(current).Select(s => new LifeVerse.Shared.Building.LinkNode(s.Id, s.GroupId, s.ParentObjectId))], [id]).Select(c => { var s = _server[c.Id]; s.GroupId = c.GroupId; s.ParentObjectId = c.ParentId; s.Revision++; return s; }).ToList();
            _editor.ApplyOperationForTesting(JsonSerializer.Serialize(new { operation, objectId = id, healed = healedNow }, Web));
            return true;
        }
        var payload = JsonSerializer.Deserialize<BuildObjectState>(root.GetProperty("object").GetRawText(), Web)!;
        if (operation == "create") { payload.Id = Guid.NewGuid(); payload.Revision = 1; }
        else
        {
            payload.Id = Guid.Parse(root.GetProperty("objectId").GetString()!);
            var expected = root.GetProperty("expectedRevision").GetInt32();
            if (!_server.TryGetValue(payload.Id, out var current) || current.Revision != expected) throw new InvalidOperationException($"update used a stale revision: {expected} vs {current?.Revision}");
            payload.Revision = expected + 1; payload.GroupId = current.GroupId; payload.ParentObjectId = current.ParentObjectId; payload.EditLocked = current.EditLocked; // a plain update never changes the linkset or the protection
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
