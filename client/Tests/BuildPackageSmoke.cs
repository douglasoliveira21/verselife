using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 10: constructions (save, take out, preview and place, ready-made ones), the editing camera and the terrain button.</summary>
public partial class BuildPackageSmoke : Node3D
{
    private static string StateJson(Guid id, int revision, float x = 0, string operation = "update", Guid? group = null, Guid? parent = null) =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Peça", description = "", primitiveType = "cube", positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", groupId = group, parentObjectId = parent, revision, cost = 2 } });

    private static string Op(string command) => JsonDocument.Parse(command).RootElement.GetProperty("operation").GetString()!;

    public override async void _Ready()
    {
        try
        {
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (Op(json) is not ("lock" or "unlock" or "live")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var window = editor.Window; var packages = editor.Packages;

            Require(window.Creation.PresetsForTesting.Count == BuildPresets.All.Count && BuildPresets.All.Count > 0, "every ready-made construction has a button");

            // --- saving ----------------------------------------------------------------------------------------------------
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var group = Guid.NewGuid();
            editor.Synchronizer.OnOperation(StateJson(a, 1, 1, "create", group)); editor.Synchronizer.OnOperation(StateJson(b, 1, 3, "create", group, a)); await Frames(2);
            var saved = new List<string>(); packages.PackageSink = json => { saved.Add(json); return true; };
            editor.Selection.Set([a, b]); await Frames(2); window.SetPackageNameForTesting("Mesa"); window.PressSaveForTesting(false);
            Require(saved.Count == 1 && BuildPackages.Parse(saved[0], out _) is { Name: "Mesa", Objects.Length: 2 }, "Guardar sends the selection as a construction with its name");
            Require(commands.Count == 0, "saving does not touch the world");
            window.PressSaveForTesting(true);
            Require(saved.Count == 2 && commands.Count(c => Op(c) == "delete") == 2, "Retirar saves and then deletes the objects from the world");
            editor.Synchronizer.OnOperation(JsonSerializer.Serialize(new { operation = "delete", objectId = a })); editor.Synchronizer.OnOperation(JsonSerializer.Serialize(new { operation = "delete", objectId = b })); await Frames(2);

            // --- placing with a preview ----------------------------------------------------------------------------------
            var preset = BuildPresets.All[0]; commands.Clear();
            window.Creation.PickPresetForTesting(preset.Id); await Frames(2);
            Require(packages.PreviewActive && packages.PreviewParts == preset.Parts, "choosing a ready-made construction shows its preview");
            Require(commands.Count == 0, "the preview creates nothing");
            Require(packages.CancelPreview("cancelada") && !packages.PreviewActive, "Esc (cancel) removes the preview");
            window.Creation.PickPresetForTesting(preset.Id); await Frames(1);
            packages.ConfirmAt(new Vector3(10, 0, 10)); await Frames(2);
            Require(!packages.PreviewActive, "the click places it and the preview goes away");
            var creates = commands.Count(c => Op(c) == "create");
            Require(creates > 0 && (creates == preset.Parts || packages.Pending == preset.Parts - creates), $"the objects are created one by one under the rate limit ({creates} now, {packages.Pending} waiting)");
            var first = JsonDocument.Parse(commands.First(c => Op(c) == "create")).RootElement.GetProperty("object");
            Require(first.GetProperty("positionX").GetSingle() >= 5f, "the construction stands where it was clicked");
            // the server answers every creation; the construction is selected as a whole and is one undo step
            var undoBefore = editor.History.UndoCount;
            for (var guard = 0; guard < 400 && (packages.Pending > 0 || commands.Count(c => Op(c) == "create") < preset.Parts); guard++) await Frames(2);
            var made = new List<Guid>();
            foreach (var _ in commands.Where(c => Op(c) == "create")) { var id = Guid.NewGuid(); made.Add(id); editor.Synchronizer.OnOperation(StateJson(id, 1, 10, "create")); }
            await Frames(3);
            Require(editor.Selection.Count == preset.Parts && editor.History.UndoCount == undoBefore + 1, "the construction ends selected and is a single undo step");

            // --- editing camera --------------------------------------------------------------------------------------------
            editor.Selection.Set([made[0]]); await Frames(2);
            editor.ToggleEditCamera(); await Frames(2);
            Require(editor.EditCamera.Active && editor.OwnsTheMouse, "Câmera switches to the camera that circles the selection");
            editor.FocusEditCamera(); await Frames(1);
            Require(editor.EditCamera.Target.DistanceTo(editor.Synchronizer.Views[made[0]].GlobalPosition) < 2f, "Home focuses the selection");
            editor.ToggleEditCamera(); await Frames(2);
            Require(!editor.EditCamera.Active, "and it gives the normal camera back");
            editor.ToggleEditCamera(); editor.SetActive(false); await Frames(2);
            Require(!editor.EditCamera.Active, "closing the editor also leaves the editing camera");

            GD.Print("event=build_package_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_package_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
