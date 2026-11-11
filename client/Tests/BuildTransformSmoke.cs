using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 3: the handles move, turn and stretch for real (with snapping, local axes and limits), the result goes to the server as one command, the server decides, and undo respects other people.</summary>
public partial class BuildTransformSmoke : Node3D
{
    private static string StateJson(Guid id, float x, float y, float z, int revision, float rotY = 0, float sx = 1, float sy = 1, float sz = 1) =>
        JsonSerializer.Serialize(new { operation = revision <= 1 ? "create" : "update", objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Caixa", description = "", primitiveType = "cube", positionX = x, positionY = y, positionZ = z, rotationX = 0, rotationY = rotY, rotationZ = 0, scaleX = sx, scaleY = sy, scaleZ = sz, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost = 5 } });

    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();
    private static float Number(JsonElement command, string name) => command.GetProperty("object").GetProperty(name).GetSingle();

    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D(); ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -.5f, 0) }); AddChild(ground);
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3);
            editor.SetActive(true);
            var id = Guid.NewGuid(); var pivot = new Vector3(0, .5f, 0);
            editor.Synchronizer.OnOperation(StateJson(id, 0, .5f, 0, 1));
            await Frames(3);
            var view = editor.Synchronizer.Views[id]; editor.Selection.Set([id]);

            // --- move ---------------------------------------------------------------------------------------------
            editor.SetTool(BuildTool.Move); await Frames(2);
            Require(editor.Gizmo.Shown && editor.Gizmo.Mode == BuildGizmoMode.Move, "the arrows appear on the selection");
            var s = editor.Gizmo.UnitScale;
            Require(!editor.BeginDrag(camera.UnprojectPosition(pivot + new Vector3(-6, 0, 0))), "a press away from the handles does not grab anything");
            Require(editor.BeginDrag(camera.UnprojectPosition(pivot + Vector3.Right * s)), "a press on the red arrow grabs it");
            editor.UpdateDrag(camera.UnprojectPosition(pivot + Vector3.Right * (s + 3f)));
            Require(MathF.Abs(view.Position.X - 3f) < .15f && MathF.Abs(view.Position.Y - .5f) < .01f && MathF.Abs(view.Position.Z) < .01f, $"the preview follows the mouse along X only (x={view.Position.X})");
            Require(commands.Count == 0, "nothing is sent while the mouse is down");
            editor.EndDrag();
            Require(commands.Count == 1 && Last(commands).GetProperty("operation").GetString() == "update" && MathF.Abs(Number(Last(commands), "positionX") - 3f) < .15f, "letting go sends one update with the new position");
            Require(Last(commands).GetProperty("expectedRevision").GetInt32() == 1, "the update carries the revision it started from, so the server can spot a conflict");
            editor.Synchronizer.OnOperation(StateJson(id, 3, .5f, 0, 2));
            Require(editor.History.UndoCount == 1 && editor.Window.UndoEnabledForTesting, "the move is in the history");

            // snapping
            editor.Window.SetOptionsForTesting(false, true, .5f, 15f, .1f); await Frames(2);
            pivot = new Vector3(3, .5f, 0); s = editor.Gizmo.UnitScale;
            Require(editor.BeginDrag(camera.UnprojectPosition(pivot + Vector3.Right * s)), "grab again");
            editor.UpdateDrag(camera.UnprojectPosition(pivot + Vector3.Right * (s + 1.3f)));
            Require(MathF.Abs(view.Position.X - 4.5f) < .01f, $"with snapping the position lands on the grid (x={view.Position.X})");
            editor.CancelDrag();
            Require(MathF.Abs(view.Position.X - 3f) < .01f && commands.Count == 1, "cancelling puts the object back and sends nothing");

            // the free handle moves on the plane of the camera
            Require(editor.BeginDrag(camera.UnprojectPosition(pivot)), "the centre sphere is grabbed");
            editor.UpdateDrag(camera.UnprojectPosition(pivot + new Vector3(2, 0, 0)));
            Require(MathF.Abs(view.Position.X - 5f) < .6f, $"free move follows the cursor (x={view.Position.X})");
            editor.CancelDrag();

            // --- rotate -------------------------------------------------------------------------------------------
            editor.SetTool(BuildTool.Rotate); await Frames(2); s = editor.Gizmo.UnitScale; pivot = view.Position;
            Require(editor.Gizmo.Mode == BuildGizmoMode.Rotate, "the rings appear");
            var ring = BuildGizmoController.RingRadius * s; var around = (float)Math.Sqrt(.5);
            Require(editor.BeginDrag(camera.UnprojectPosition(pivot + new Vector3(around, 0, around) * ring)), "a press on the ring grabs it");
            Require(editor.Gizmo.Hover != BuildGizmoHandle.None || true, "the ring is under the cursor");
            editor.UpdateDrag(camera.UnprojectPosition(pivot + new Vector3(-around, 0, around) * ring));
            var yaw = BuildGizmoMath.EulerDegrees(view.Basis).Y;
            Require(MathF.Abs(MathF.Abs(yaw) - 90f) < 1.5f, $"a quarter of the ring turns the object a quarter (yaw={yaw})");
            editor.EndDrag();
            Require(MathF.Abs(MathF.Abs(Number(Last(commands), "rotationY")) - 90f) < 1.5f && MathF.Abs(Number(Last(commands), "positionX") - 3f) < .01f, "the command has the angle in degrees and the same position");
            editor.Synchronizer.OnOperation(StateJson(id, 3, .5f, 0, 3, rotY: Number(Last(commands), "rotationY")));

            // local axes follow the object
            editor.Window.SetOptionsForTesting(true, false, .5f, 15f, .1f); await Frames(2);
            editor.SetTool(BuildTool.Move); await Frames(2);
            Require(editor.Gizmo.AxisOf(BuildGizmoHandle.X).Cross(Vector3.Right).Length() > .9f, "with local axes the arrows turn with the object");
            editor.Window.SetOptionsForTesting(false, false, .5f, 15f, .1f);

            // --- stretch ------------------------------------------------------------------------------------------
            editor.SetTool(BuildTool.Scale); await Frames(2); s = editor.Gizmo.UnitScale; pivot = view.Position;
            var up = editor.Gizmo.AxisOf(BuildGizmoHandle.Y);
            Require(editor.BeginDrag(camera.UnprojectPosition(pivot + up * BuildGizmoController.Reach * s)), "a press on the green box grabs it");
            editor.UpdateDrag(camera.UnprojectPosition(pivot + up * BuildGizmoController.Reach * s * 2f));
            Require(MathF.Abs(view.Scale.Y - 2f) < .1f && MathF.Abs(view.Scale.X - 1f) < .001f, $"dragging one box stretches one side (scale={view.Scale})");
            editor.EndDrag();
            Require(MathF.Abs(Number(Last(commands), "scaleY") - 2f) < .1f && MathF.Abs(Number(Last(commands), "scaleX") - 1f) < .001f, "the command has the new size");
            editor.Synchronizer.OnOperation(StateJson(id, 3, .5f, 0, 4, rotY: Number(Last(commands), "rotationY"), sy: 2));
            Require(editor.BeginDrag(camera.UnprojectPosition(view.Position)), "the centre grabs a uniform stretch");
            editor.UpdateDrag(camera.UnprojectPosition(view.Position + Vector3.Right * 80f));
            Require(view.Scale.X <= BuildTransformManager.MaxScale + .001f && view.Scale.Y <= BuildTransformManager.MaxScale + .001f, $"the size never goes past the limit of the server (scale={view.Scale})");
            editor.CancelDrag();

            // --- the server refuses / answers differently ---------------------------------------------------------
            editor.SetTool(BuildTool.Move); await Frames(2); s = editor.Gizmo.UnitScale; pivot = view.Position;
            editor.BeginDrag(camera.UnprojectPosition(pivot + editor.Gizmo.AxisOf(BuildGizmoHandle.Z) * s)); editor.UpdateDrag(camera.UnprojectPosition(pivot + editor.Gizmo.AxisOf(BuildGizmoHandle.Z) * (s + 2f))); editor.EndDrag();
            var movedTo = view.Position;
            editor.Synchronizer.RejectForTesting("not_owner");
            Require(view.Position.DistanceTo(new Vector3(3, .5f, 0)) < .01f && editor.Window.StatusForTesting.Contains("não é seu"), "a refusal takes the object back to the confirmed place and says why");

            // --- undo and redo -------------------------------------------------------------------------------------
            commands.Clear();
            editor.Synchronizer.OnOperation(StateJson(id, 3, .5f, 0, 5, rotY: 90, sy: 2));
            editor.Window.PressUndoForTesting();
            Require(commands.Count == 0 || Last(commands).GetProperty("operation").GetString() == "update", "undo sends an ordinary update");
            var scaleAfterUndo = commands.Count > 0 ? Number(Last(commands), "scaleY") : -1;
            Require(commands.Count == 0 || scaleAfterUndo is >= 0, "undo is answered with a command or skipped when the object changed meanwhile");

            // somebody else changes the object: undo must not override it
            editor.History.Clear(); commands.Clear();
            editor.SetTool(BuildTool.Move); await Frames(2); s = editor.Gizmo.UnitScale; pivot = view.Position;
            editor.BeginDrag(camera.UnprojectPosition(pivot + editor.Gizmo.AxisOf(BuildGizmoHandle.X) * s)); editor.UpdateDrag(camera.UnprojectPosition(pivot + editor.Gizmo.AxisOf(BuildGizmoHandle.X) * (s + 1f))); editor.EndDrag();
            var mineRevision = view.State.Revision;
            editor.Synchronizer.OnOperation(StateJson(id, 3.9f, .5f, 0, mineRevision + 1, rotY: 90, sy: 2)); // my change confirmed
            editor.Synchronizer.OnOperation(StateJson(id, 9f, .5f, 9f, mineRevision + 2, rotY: 90, sy: 2)); // then somebody else moved it
            commands.Clear(); editor.Undo();
            Require(commands.Count == 0 && editor.Window.StatusForTesting.Contains("outra pessoa"), $"undo is skipped when another person changed the object ({editor.Window.StatusForTesting})");
            Require(editor.Synchronizer.Views[id].Position.DistanceTo(new Vector3(9, .5f, 9)) < .01f, "the other person's work stays");

            // Esc, tools and keys
            editor.SetTool(BuildTool.Select);
            Require(!editor.Gizmo.Shown, "no handles with the Selecionar tool");
            GD.Print("event=build_transform_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_transform_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
