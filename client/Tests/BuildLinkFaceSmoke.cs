using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 3: linking and unlinking (with the same plan the server checks, one all-or-nothing command, undo) and picking faces.</summary>
public partial class BuildLinkFaceSmoke : Node3D
{
    private static object StateOf(Guid id, string name, float x, int revision, Guid? group = null, Guid? parent = null, string type = "cube") =>
        new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, groupId = group, parentObjectId = parent, name, description = "", primitiveType = type, positionX = x, positionY = .5f, positionZ = 0, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost = 4 };

    private static string Created(object state) => JsonSerializer.Serialize(new { operation = "create", objectState = state });
    private static string Structure(params object[] states) => JsonSerializer.Serialize(new { operation = "structure", objects = states });

    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D(); ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -.5f, 0) }); AddChild(ground);
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
            editor.Synchronizer.OnOperation(Created(StateOf(a, "Base", -3, 1))); editor.Synchronizer.OnOperation(Created(StateOf(b, "Teto", 0, 1))); editor.Synchronizer.OnOperation(Created(StateOf(c, "Porta", 3, 1)));
            await Frames(3);

            // --- linking ------------------------------------------------------------------------------------------
            editor.Selection.Set([a]);
            Require(!editor.Window.LinkEnabledForTesting && !editor.Window.UnlinkEnabledForTesting, "with one loose object there is nothing to link or unlink");
            editor.Selection.Pick(a, false, false); editor.Selection.Pick(b, true, false);
            Require(editor.Window.LinkEnabledForTesting, "two objects can be linked");
            editor.Window.PressLinkForTesting();
            var link = JsonDocument.Parse(commands[^1]).RootElement;
            var links = link.GetProperty("links").EnumerateArray().ToList();
            Require(link.GetProperty("operation").GetString() == "structure" && links.Count == 2, "linking is one structure command for all the pieces");
            var root = links.First(l => l.GetProperty("objectId").GetGuid() == a); var child = links.First(l => l.GetProperty("objectId").GetGuid() == b);
            Require(root.GetProperty("parentId").ValueKind == JsonValueKind.Null && child.GetProperty("parentId").GetGuid() == a, "the first one picked is the root and the other hangs under it");
            Require(root.GetProperty("groupId").GetGuid() == child.GetProperty("groupId").GetGuid() && root.GetProperty("expectedRevision").GetInt32() == 1, "they share a group and each carries the version it was seen at");
            Require(editor.Synchronizer.Views[a].State.GroupId is null, "nothing is linked until the server answers");
            var group = root.GetProperty("groupId").GetGuid();
            editor.Synchronizer.OnOperation(Structure(StateOf(a, "Base", -3, 2, group, null), StateOf(b, "Teto", 0, 2, group, a)));
            Require(editor.Synchronizer.Views[b].State.ParentObjectId == a && editor.Synchronizer.Views[a].State.GroupId == group, "the server's answer links them");
            Require(editor.Window.UnlinkEnabledForTesting && editor.Window.HierarchyRowsForTesting == 2, "the window shows the parts of the set as a tree");
            Require(editor.History.UndoCount == 1, "linking is in the history");

            // picking one object selects the whole set; Ctrl picks one part; the tree picks one part
            editor.Selection.Pick(b, false, false);
            Require(editor.Selection.Count == 2, "picking a part of a linked set selects the whole set");
            editor.Selection.Pick(b, false, true);
            Require(editor.Selection.Ids.SequenceEqual([b]), "Ctrl picks only that part");
            editor.Window.PickHierarchyForTesting(0);
            Require(editor.Selection.Count == 1, "clicking a row of the tree selects that part");

            // moving the set moves every piece by the same amount and keeps what is between them
            editor.Selection.Pick(a, false, false); editor.SetTool(BuildTool.Move); await Frames(2);
            var s = editor.Gizmo.UnitScale; var centre = editor.Gizmo.GlobalPosition;
            Require(centre.DistanceTo(new Vector3(-1.5f, .5f, 0)) < .01f, "the handles stand at the centre of the set");
            commands.Clear(); editor.BeginDrag(camera.UnprojectPosition(centre + Vector3.Right * s)); editor.UpdateDrag(camera.UnprojectPosition(centre + Vector3.Right * (s + 2f))); editor.EndDrag();
            Require(commands.Count == 2 && commands.All(m => JsonDocument.Parse(m).RootElement.GetProperty("operation").GetString() == "update"), "one update per piece");
            var moved = commands.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("object").GetProperty("positionX").GetSingle()).OrderBy(x => x).ToArray();
            Require(MathF.Abs(moved[1] - moved[0] - 3f) < .02f, $"the distance between the pieces is kept ({moved[0]} / {moved[1]})");
            editor.Undo(); commands.Clear(); // the move was not confirmed by the server: undo must not guess
            editor.SetTool(BuildTool.Select);

            // --- unlinking ----------------------------------------------------------------------------------------
            editor.Selection.Pick(a, false, false); commands.Clear();
            editor.Window.PressUnlinkForTesting();
            var unlink = JsonDocument.Parse(commands[^1]).RootElement.GetProperty("links").EnumerateArray().ToList();
            Require(unlink.All(l => l.GetProperty("groupId").ValueKind == JsonValueKind.Null && l.GetProperty("parentId").ValueKind == JsonValueKind.Null), "unlinking clears group and parent of every piece");
            editor.Synchronizer.OnOperation(Structure(StateOf(a, "Base", -3, 3), StateOf(b, "Teto", 0, 3)));
            Require(editor.Synchronizer.Views[a].State.GroupId is null && editor.Synchronizer.Views[a].Position.X == -3f && editor.Window.HierarchyRowsForTesting == 0, "after the answer they are loose again, in the same places");

            // undo brings the link back, with the versions of the moment
            commands.Clear(); editor.History.Clear();
            editor.Selection.Pick(a, false, false); editor.Selection.Pick(c, true, false); editor.Window.PressLinkForTesting();
            var group2 = JsonDocument.Parse(commands[^1]).RootElement.GetProperty("links")[0].GetProperty("groupId").GetGuid();
            editor.Synchronizer.OnOperation(Structure(StateOf(a, "Base", -3, 4, group2, null), StateOf(c, "Porta", 3, 4, group2, a)));
            commands.Clear(); editor.Window.PressUndoForTesting();
            var undo = JsonDocument.Parse(commands[^1]).RootElement;
            Require(undo.GetProperty("operation").GetString() == "structure" && undo.GetProperty("links").EnumerateArray().All(l => l.GetProperty("groupId").ValueKind == JsonValueKind.Null && l.GetProperty("expectedRevision").GetInt32() == 4), "undoing a link asks the server to unlink, at the current version");

            // a protected object cannot be linked, and the editor says so before asking
            var guarded = (Dictionary<string, object?>)JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(StateOf(b, "Teto", 0, 5)))!; guarded["editLocked"] = true;
            editor.Synchronizer.OnOperation(JsonSerializer.Serialize(new { operation = "update", objectState = guarded }));
            editor.Selection.Set([]); editor.Selection.Pick(b, false, false); editor.Selection.Pick(c, true, false); commands.Clear(); editor.LinkSelection();
            Require(commands.Count == 0 && editor.Window.StatusForTesting.Contains("protegidos"), "a protected object is refused before anything is sent");

            // --- faces --------------------------------------------------------------------------------------------
            editor.Selection.Set([a]); editor.SetTool(BuildTool.Face); await Frames(2);
            Require(!editor.Gizmo.Shown, "no handles while choosing faces");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(-3, 1f, 0)), false, false);
            Require(editor.Faces.Count == 1 && editor.Faces.Faces.Contains(0) && editor.Window.StatusForTesting.Contains("Topo"), $"a click on the top picks the top face ({editor.Window.StatusForTesting})");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(-3, .5f, .5f)), true, false);
            Require(editor.Faces.Count == 2, "Shift adds the front face");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(-3, .5f, .5f)), true, false);
            Require(editor.Faces.Count == 1, "Shift on a chosen face takes it off");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(40, 0, 0)), false, false);
            Require(editor.Faces.Count == 0, "a click on empty ground clears the faces without touching the object");
            Require(editor.Selection.Contains(a), "the object stays selected");
            editor.HandleClick(camera.UnprojectPosition(new Vector3(-3, 1f, 0)), false, false);
            editor.Selection.Set([c]);
            Require(editor.Faces.Count == 0, "faces never outlive the selection of their object");
            editor.Synchronizer.OnOperation(Created(StateOf(Guid.NewGuid(), "Bola", 6, 1, type: "sphere")));
            Require(BuildFaceSelectionManager.NameOf("cube", 2) == "Frente" && BuildFaceSelectionManager.NameOf("sphere", 0) == "Cima" && BuildFaceSelectionManager.NameOf("pyramid", 5) == "Lado 4", "faces have names");
            editor.SetTool(BuildTool.Select);
            GD.Print("event=build_link_face_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_link_face_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
