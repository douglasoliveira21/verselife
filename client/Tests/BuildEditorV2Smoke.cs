using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phases 1 and 2: the creation screen, placing with a preview, the word of the server deciding, selection, duplicate, delete, impact and limits, and switching editors.</summary>
public partial class BuildEditorV2Smoke : Node3D
{
    private static string StateJson(Guid id, string type, float x, float y, float z, int revision, int cost, string name = "Objeto") =>
        JsonSerializer.Serialize(new { operation = revision <= 1 ? "create" : "update", objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name, description = "", primitiveType = type, positionX = x, positionY = y, positionZ = z, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost } });

    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D(); ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -.5f, 0) }); AddChild(ground);
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3);

            // the creation screen
            editor.SetActive(true);
            Require(editor.Active && editor.Window.Visible, "the window opens");
            Require(editor.Window.Creation.TypesForTesting.Count == BuildPrimitiveFactory.Entries.Count && BuildPrimitiveFactory.Entries.All(e => PrimitiveCatalog.IsKnown(e.Type)), "every shape of the grid is one the server accepts");
            foreach (var type in new[] { "cube", "sphere", "cylinder", "prism", "torus", "tube", "ring", "cone", "pyramid", "hemisphere" }) Require(editor.Window.Creation.TypesForTesting.Contains(type), $"the grid has {type}");
            Require(editor.Window.Creation.TypesForTesting.Contains("tree") && editor.Window.Creation.TypesForTesting.Contains("grass"), "the grid has the tree and the grass");
            var plants = new Node3D(); AddChild(plants);
            var treeView = new BuildObjectView(); plants.AddChild(treeView); treeView.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "tree", MaterialJson = BuildPrimitiveFactory.DefaultMaterial, PhysicsJson = "{\"collision\":true}" });
            var grassView = new BuildObjectView(); plants.AddChild(grassView); grassView.Apply(new BuildObjectState { Id = Guid.NewGuid(), PrimitiveType = "grass", MaterialJson = "{\"color\":\"#FF8800\"}", PhysicsJson = "{\"collision\":true}" });
            Require(treeView.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is { Mesh: { } treeMesh } && treeMesh.GetSurfaceCount() == 3 && treeView.GetNodeOrNull("CollisionBody") is not null, "a tree is a trunk and two cones, and its trunk is solid");
            Require(grassView.GetNodeOrNull<MeshInstance3D>("PrimitiveMesh") is { Mesh: { } grassMesh } && grassMesh.GetSurfaceCount() == 1 && grassView.GetNodeOrNull("CollisionBody") is null && grassView.GetNodeOrNull("SelectionBody") is not null, "grass is a tuft with nothing solid, but it can still be selected");
            plants.QueueFree();
            var importAsked = false; editor.Window.ImportRequested += () => importAsked = true; editor.Window.Creation.PressImportForTesting();
            Require(importAsked, "the import button asks for a model to be brought in");
            editor.CreateMeshFromAsset(Guid.NewGuid(), "Casa", new Vector3(2, 0, 2));
            Require(commands.Count == 1 && commands[0].Contains("\"primitiveType\":\"mesh\"") && commands[0].Contains("assetId") && commands[0].Contains("\"name\":\"Casa\""), "placing a model from the inventory asks for a mesh object");
            commands.Clear(); editor.Synchronizer.RejectForTesting("forbidden"); // the server may refuse it

            // placing: a see-through preview, nothing on the server until the click
            editor.Window.PressToolForTesting(BuildTool.Create); Require(editor.Tool == BuildTool.Create, "the Criar tool");
            editor.Window.Creation.PickForTesting("sphere"); await Frames(2);
            Require(editor.PlacingType == "sphere" && editor.Window.Creation.HintForTesting.Contains("Esfera"), "choosing a shape arms the placement");
            var capture0 = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            if (!string.IsNullOrWhiteSpace(capture0) && DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng($"{capture0}-build-editor-v2-create.png"); }
            var spot = editor.SpotAt(GetViewport().GetVisibleRect().Size / 2);
            Require(spot is { } s && MathF.Abs(s.Y - .5f) < .05f, $"the ray finds the ground and lifts the shape half a metre (got {spot})");
            Require(editor.Synchronizer.Views.Count == 0 && commands.Count == 0, "the preview creates nothing on the server");
            editor.PlaceAt(spot!.Value, false);
            Require(commands.Count == 1 && commands[0].Contains("\"operation\":\"create\"") && commands[0].Contains("\"primitiveType\":\"sphere\""), "the click sends a create command");
            Require(editor.Synchronizer.Views.Count == 0 && editor.PlacingType is null, "nothing exists until the server confirms; placement ends without Shift");

            // the server accepts: the object appears, is selected, and its cost is the one the server counted
            var sphere = Guid.NewGuid();
            editor.Synchronizer.OnOperation(StateJson(sphere, "sphere", 0, .5f, 0, 1, 12, "Esfera"));
            Require(editor.Synchronizer.Views.ContainsKey(sphere) && editor.Selection.Ids.SequenceEqual([sphere]), "the confirmed object appears and is selected");
            Require(editor.Window.SelectedTextForTesting == "Objetos selecionados: 1" && editor.Window.ImpactTextForTesting == "Impacto na ilha: 12", $"the counters show the numbers of the server ({editor.Window.SelectedTextForTesting} / {editor.Window.ImpactTextForTesting})");

            await Frames(2);
            var capture = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            if (!string.IsNullOrWhiteSpace(capture) && DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng($"{capture}-build-editor-v2.png"); }

            // the server refuses
            editor.Window.Creation.PickForTesting("cube"); editor.PlaceAt(new Vector3(3, .5f, 0), false);
            editor.Synchronizer.RejectForTesting("object_quota");
            Require(editor.Synchronizer.Views.Count == 1 && editor.Window.StatusForTesting.Contains("limite de objetos"), "a refusal explains why and creates nothing");

            // selecting with the mouse
            await Frames(3);
            editor.Synchronizer.OnOperation(StateJson(sphere, "sphere", 0, .5f, 0, 2, 12, "Esfera"));
            editor.Selection.Clear(); Require(editor.Window.SelectedTextForTesting == "Objetos selecionados: 0", "clearing the selection updates the counter");
            await Frames(3);
            editor.SetTool(BuildTool.Select); editor.HandleClick(camera.UnprojectPosition(new Vector3(0, .5f, 0)), false, false);
            Require(editor.Selection.Ids.SequenceEqual([sphere]), "a click on the object selects it");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(40, 0, 0)), false, false);
            Require(editor.Selection.Count == 0, "a click on empty ground clears the selection");

            // out-of-order and remote changes
            editor.Synchronizer.OnOperation(StateJson(sphere, "sphere", 5, .5f, 0, 5, 12, "Esfera nova"));
            editor.Synchronizer.OnOperation(StateJson(sphere, "sphere", 9, .5f, 0, 3, 12, "Esfera velha"));
            Require(editor.Synchronizer.Views[sphere].State.Name == "Esfera nova" && editor.Synchronizer.Views[sphere].Position.X == 5, "an older answer arriving late does not undo a newer one");

            // duplicate and delete
            editor.Selection.Set([sphere]); commands.Clear();
            Require(editor.Window.DuplicateEnabledForTesting && editor.Window.DeleteEnabledForTesting, "duplicate and delete are enabled with a selection");
            editor.Window.PressDuplicateForTesting();
            Require(commands.Count == 1 && commands[0].Contains("\"operation\":\"create\"") && commands[0].Contains("Esfera nova"), "Ctrl+D asks the server for a copy");
            var copy = Guid.NewGuid(); editor.Synchronizer.OnOperation(StateJson(copy, "sphere", 6.75f, .5f, 0, 1, 12, "Esfera nova"));
            Require(editor.Selection.Ids.SequenceEqual([copy]), "the copy is selected when it arrives");
            commands.Clear(); editor.Selection.Set([sphere, copy]);
            Require(editor.Window.ImpactTextForTesting == "Impacto na ilha: 24", "the impact adds up the selected objects");
            editor.Window.PressDeleteForTesting();
            Require(commands.Count == 2 && commands.All(c => c.Contains("\"operation\":\"delete\"")), "delete asks the server to remove each object");
            Require(editor.Synchronizer.Views.Count == 2, "they stay until the server confirms");
            editor.Synchronizer.OnOperation(JsonSerializer.Serialize(new { operation = "delete", objectId = sphere }));
            Require(editor.Synchronizer.Views.Count == 1 && !editor.Selection.Contains(sphere), "a removal announced by the server takes the object away and out of the selection");

            // the numbers about the island come from the server
            var limits = BuildNetworkSynchronizer.ParseLimits("{\"objects\":10,\"maxObjects\":5000,\"cost\":120,\"maxCost\":200000,\"canBuildHere\":true,\"parcel\":{\"name\":\"Praça\",\"objects\":3,\"maxObjects\":100,\"cost\":30,\"maxCost\":500}}");
            Require(limits is { Cost: 120, ParcelMaxCost: 500 }, "the limits are read");
            editor.Window.SetLimits(limits!.Value);
            Require(editor.Window.ParcelTextForTesting.Contains("restam 199.880") && editor.Window.ParcelTextForTesting.Contains("restam 470"), $"the remaining impact is shown ({editor.Window.ParcelTextForTesting})");
            Require(BuildNetworkSynchronizer.ParseLimits("{broken") is null, "a broken answer is ignored");

            // tools
            editor.SetTool(BuildTool.Create); editor.Arm("cube"); editor.SetTool(BuildTool.Select);
            Require(editor.PlacingType is null, "choosing another tool stops the placement");
            editor.SetActive(false);
            Require(!editor.Active && !editor.Window.Visible && editor.Selection.Count == 0, "closing clears the selection");

            // the game finds the one editor through the host
            var world = new Node3D { Name = "World" }; AddChild(world);
            Require(BuildHost.Editor(world) is null, "no editor before it is needed");
            var hosted = BuildHost.Ensure(world);
            Require(BuildHost.Editor(world) == hosted && BuildHost.Ensure(world) == hosted, "the host creates the editor once");
            BuildHost.SetActive(world, true); Require(BuildHost.IsActive(world), "and opens and closes it");
            BuildHost.SetActive(world, false); Require(!BuildHost.IsActive(world), "closed again");
            GD.Print("event=build_editor_v2_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_editor_v2_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
