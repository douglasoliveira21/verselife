using System.Text.Json;
using Godot;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Building;

namespace LifeVerse.Client.Tests;

/// <summary>The new editor, phase 5: the OBJETO tab changes the real object: switches, kind of shape, numbers, radius, parametric shape, alignment and copy and paste.</summary>
public partial class BuildObjectTabSmoke : Node3D
{
    private static string StateJson(Guid id, string type, int revision, float x = 0, float y = .5f, float z = 0, string geometry = "{}", string physics = "{\"collision\":true}", string material = "{}", bool locked = false, DateTimeOffset? expires = null, float sx = 1, float sy = 1, float sz = 1, string operation = "update") =>
        JsonSerializer.Serialize(new { operation, objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name = "Peça " + type, description = "", primitiveType = type, positionX = x, positionY = y, positionZ = z, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = sx, scaleY = sy, scaleZ = sz, materialJson = material, geometryJson = geometry, physicsJson = physics, permissionsJson = "{}", contentJson = "[]", revision, cost = 2, editLocked = locked, expiresAt = expires } });

    private static JsonElement Last(List<string> commands) => JsonDocument.Parse(commands[^1]).RootElement.Clone();
    private static JsonElement Obj(List<string> commands) => Last(commands).GetProperty("object");

    public override async void _Ready()
    {
        try
        {
            var ground = new StaticBody3D(); ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(200, 1, 200) }, Position = new Vector3(0, -.5f, 0) }); AddChild(ground);
            var camera = new Camera3D { Current = true, Position = new Vector3(0, 8, 8) }; AddChild(camera); camera.LookAt(Vector3.Zero);
            var editor = new BuildEditorController { Name = "BuildEditorController" }; AddChild(editor);
            var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.StartsWith("{\"operation\":\"lock\"") && !json.StartsWith("{\"operation\":\"unlock\"") && !json.StartsWith("{\"operation\":\"live\"")) commands.Add(json); return true; };
            await Frames(3); editor.SetActive(true);
            var tab = editor.Window.Object; var id = Guid.NewGuid();
            Require(editor.Window.TabNamesForTesting.SequenceEqual(["GERAL", "OBJETO", "RECURSOS", "TEXTURA", "CONTEÚDO"]), "the tabs that work: GERAL and OBJETO");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 1, operation: "create")); editor.Selection.Set([id]); editor.Window.SelectTabForTesting(1); await Frames(2);

            var capture = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); if (!string.IsNullOrWhiteSpace(capture) && DisplayServer.GetName() != "headless") { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng($"{capture}-object-tab.png"); }
            // --- the switches -----------------------------------------------------------------------------------------
            commands.Clear(); tab.ToggleForTesting("ghost", true);
            Require(!PhysicsRules.Read(Obj(commands).GetProperty("physicsJson").GetString()).Collision, "Fantasma turns the solid collision off");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 2, physics: Obj(commands).GetProperty("physicsJson").GetString()!)); Require(tab.GhostForTesting, "the answer of the server is shown");
            commands.Clear(); tab.ToggleForTesting("physical", true);
            Require(commands.Count == 0 && tab.NoteForTesting.Length == 0 && editor.Window.StatusForTesting.Contains("Fantasma") && !tab.PhysicalForTesting, "a ghost cannot be physical, and the editor says why before asking");
            tab.ToggleForTesting("ghost", false); var solid = Obj(commands).GetProperty("physicsJson").GetString()!; editor.Synchronizer.OnOperation(StateJson(id, "cube", 3, physics: solid));
            commands.Clear(); tab.ToggleForTesting("physical", true);
            var physical = PhysicsRules.Read(Obj(commands).GetProperty("physicsJson").GetString());
            Require(physical.Physical && physical.Collision, "Física asks the island to simulate it, keeping the solid collision");
            commands.Clear(); tab.ToggleForTesting("temporary", true);
            Require(PhysicsRules.Read(Obj(commands).GetProperty("physicsJson").GetString()).Temporary, "Temporário marks it for the server to remove");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 4, physics: Obj(commands).GetProperty("physicsJson").GetString()!, expires: DateTimeOffset.UtcNow.AddMinutes(9.5)));
            Require(tab.LifetimeForTesting.Contains("10 min") && tab.TemporaryForTesting, $"the time left is shown ({tab.LifetimeForTesting})");
            commands.Clear(); tab.ToggleForTesting("locked", true);
            var protect = Last(commands); Require(protect.GetProperty("operation").GetString() == "protect" && protect.GetProperty("locked").GetBoolean() && protect.GetProperty("objectIds").GetArrayLength() == 1, "Travar asks the server to protect it");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 5, locked: true)); Require(tab.LockedForTesting, "it shows as locked");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 6)); // unlocked again, plain

            // --- the kind of primitive ----------------------------------------------------------------------------------
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 7, geometry: "{\"holeX\":0.3,\"twist\":40,\"hollow\":0.5}", sx: 2)); await Frames(2);
            Require(tab.TypeForTesting == "cube" && tab.TypeEditableForTesting, "the kind of primitive is shown");
            commands.Clear(); tab.ChooseTypeForTesting("sphere");
            Require(tab.ConfirmOpenForTesting && tab.ConfirmTextForTesting.Contains("Furo X") && commands.Count == 0, "a change that would drop adjustments asks first and lists them");
            tab.ConfirmTypeForTesting();
            var changed = Obj(commands); var kept = JsonDocument.Parse(changed.GetProperty("geometryJson").GetString()!).RootElement;
            Require(changed.GetProperty("primitiveType").GetString() == "sphere" && kept.TryGetProperty("twist", out _) && kept.TryGetProperty("hollow", out _) && !kept.TryGetProperty("holeX", out _) && changed.GetProperty("scaleX").GetSingle() == 2f, "the new shape keeps the size and the adjustments that still fit");
            editor.Synchronizer.OnOperation(StateJson(id, "sphere", 8, geometry: changed.GetProperty("geometryJson").GetString()!, sx: 2)); commands.Clear();
            tab.ChooseTypeForTesting("cylinder"); Require(commands.Count == 1 && Obj(commands).GetProperty("primitiveType").GetString() == "cylinder", "with nothing to lose it changes at once");
            editor.Synchronizer.OnOperation(StateJson(id, "cylinder", 9, geometry: Obj(commands).GetProperty("geometryJson").GetString()!, sx: 2));

            // --- numbers ----------------------------------------------------------------------------------------------------
            Require(tab.TransformEditableForTesting, "numbers can be typed for one object");
            commands.Clear(); tab.SetPositionForTesting(3.25, 1.5, -2); tab.SetSizeForTesting(2, 3, 100); tab.SetRotationForTesting(0, 45, 0);
            Require(commands.Count == 0, "a burst of changes is not sent one by one");
            tab.FlushForTesting();
            var numbers = Obj(commands);
            Require(commands.Count == 1 && numbers.GetProperty("positionX").GetSingle() == 3.25f && numbers.GetProperty("positionZ").GetSingle() == -2f && numbers.GetProperty("scaleY").GetSingle() == 3f && numbers.GetProperty("scaleZ").GetSingle() == 64f && numbers.GetProperty("rotationY").GetSingle() == 45f, "the numbers go as one update, the size inside the limit of the server");
            Require(Last(commands).GetProperty("expectedRevision").GetInt32() == 9, "carrying the version it saw");
            Require(editor.History.UndoCount >= 1, "typed numbers are in the history");

            // --- radius ----------------------------------------------------------------------------------------------------------
            editor.Synchronizer.OnOperation(StateJson(id, "sphere", 10)); await Frames(2);
            Require(tab.RadiusVisibleForTesting && Math.Abs(tab.RadiusForTesting - .5) < .001, $"a sphere has a radius (visible={tab.RadiusVisibleForTesting}, r={tab.RadiusForTesting}, type={tab.TypeForTesting})");
            commands.Clear(); tab.RadiusForTesting = 2; tab.FlushForTesting();
            Require(Obj(commands).GetProperty("scaleX").GetSingle() == 4f && Obj(commands).GetProperty("scaleY").GetSingle() == 4f && Obj(commands).GetProperty("scaleZ").GetSingle() == 4f, "the radius sets the width (the height follows on a sphere)");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 11)); await Frames(2);
            Require(!tab.RadiusVisibleForTesting, "a box has no radius, so the control is not there");

            // --- the shape controls of each primitive ------------------------------------------------------------------
            var cube = tab.ShapeKeysForTesting.ToHashSet();
            Require(cube.IsSupersetOf(["pathBegin", "pathEnd", "hollow", "holeX", "holeY", "twistBegin", "twist", "taperX", "taperY", "shearX", "shearZ", "cutStart", "cutEnd"]) && !cube.Contains("radiusOffset") && !cube.Contains("revolutions"), "a box has the cut, hollow, twist, taper, shear and profile cut, and no ring controls");
            editor.Synchronizer.OnOperation(StateJson(id, "torus", 12)); await Frames(2);
            var torus = tab.ShapeKeysForTesting.ToHashSet();
            Require(torus.IsSupersetOf(["radiusOffset", "revolutions", "skew", "holeX", "holeY"]) && !torus.Contains("shearX") && tab.ShapeLabelsForTesting.Contains("Perfil do anel: Espessura do perfil"), "a torus has its own controls and not the ones that would do nothing");
            editor.Synchronizer.OnOperation(StateJson(id, "sphere", 13)); await Frames(2);
            Require(!tab.ShapeKeysForTesting.Contains("holeX") && tab.ShapeKeysForTesting.Contains("shearX"), "a sphere has no hole size but can be sheared");

            editor.Synchronizer.OnOperation(StateJson(id, "cube", 14)); await Frames(2);
            commands.Clear(); tab.SetShapeForTesting("hollow", 40); tab.SetShapeForTesting("twist", 90); tab.SetShapeForTesting("twistBegin", -30); tab.FlushForTesting();
            var geometry = JsonDocument.Parse(Obj(commands).GetProperty("geometryJson").GetString()!).RootElement;
            Require(Math.Abs(geometry.GetProperty("hollow").GetDouble() - .4) < .001 && geometry.GetProperty("twist").GetDouble() == 90 && geometry.GetProperty("twistBegin").GetDouble() == -30, "the hollow is shown as a percentage and stored as a fraction");
            editor.Synchronizer.OnOperation(StateJson(id, "cube", 15, geometry: Obj(commands).GetProperty("geometryJson").GetString()!)); await Frames(2);
            Require(Math.Abs(tab.ShapeValueForTesting("hollow") - 40) < .01, "the stored value comes back to the control");
            commands.Clear(); tab.SetShapeForTesting("pathBegin", .93); tab.SetShapeForTesting("pathEnd", .94); tab.FlushForTesting();
            var cut = JsonDocument.Parse(Obj(commands).GetProperty("geometryJson").GetString()!).RootElement;
            Require(cut.GetProperty("pathEnd").GetDouble() - cut.GetProperty("pathBegin").GetDouble() >= .0499, "a cut always leaves part of the shape");
            // the stored value builds a real mesh
            var built = PrimMesh.Build("cube", PrimParams.Parse(Obj(commands).GetProperty("geometryJson").GetString()));
            Require(built.TriangleCount > 0 && built.Faces.All(f => f.Normals.Length == f.Positions.Length && f.Uvs.Length == f.Positions.Length), "the stored shape builds a mesh with normals and UVs");
            commands.Clear(); tab.PressResetShapeForTesting();
            Require(Obj(commands).GetProperty("geometryJson").GetString() == "{}", "Restaurar forma goes back to the plain shape");

            // --- several objects at once -------------------------------------------------------------------------------------
            var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
            editor.Synchronizer.OnOperation(StateJson(a, "cube", 1, x: 0, y: 3, material: "{\"color\":\"#FF0000\"}", operation: "create")); editor.Synchronizer.OnOperation(StateJson(b, "cube", 1, x: 4, y: 5, operation: "create")); editor.Synchronizer.OnOperation(StateJson(c, "sphere", 1, x: 9, y: 1, operation: "create"));
            editor.Selection.Set([a, b]); await Frames(2);
            Require(!tab.TransformEditableForTesting && tab.AlignVisibleForTesting && tab.NoteForTesting.Contains("2 objetos"), "with several objects the numbers are off and the alignment is on");
            commands.Clear(); tab.SetShapeForTesting("hollow", 30); tab.FlushForTesting();
            Require(commands.Count == 2 && commands.All(m => JsonDocument.Parse(m).RootElement.GetProperty("object").GetProperty("geometryJson").GetString()!.Contains("hollow")), "a shape change goes to every selected object");
            commands.Clear(); tab.PressAlignForTesting(AlignAxis.Y, AlignEdge.Min);
            var lifted = commands.Select(m => JsonDocument.Parse(m).RootElement).ToList();
            Require(lifted.Count == 1 && lifted[0].GetProperty("objectId").GetGuid() == b && Math.Abs(lifted[0].GetProperty("object").GetProperty("positionY").GetSingle() - 3f) < .001f, "aligning by the low side moves only what has to move");
            editor.Synchronizer.OnOperation(StateJson(b, "cube", 2, x: 4, y: 3, geometry: "{\"hollow\":0.3}")); editor.Synchronizer.OnOperation(StateJson(a, "cube", 2, x: 0, y: 3, material: "{\"color\":\"#FF0000\"}", geometry: "{\"hollow\":0.3}")); await Frames(2);
            commands.Clear(); editor.Align.RestOnGround(editor.GroundAt);
            Require(commands.Count == 2 && commands.Select(m => JsonDocument.Parse(m).RootElement.GetProperty("object").GetProperty("positionY").GetSingle()).All(y => Math.Abs(y - .5f) < .05f), "Pousar no chão lowers the selection until its base touches the ground");
            commands.Clear(); editor.Selection.Set([a]); tab.PressCopyForTesting(); editor.Selection.Set([b]); tab.PressPasteForTesting();
            Require(commands.Count == 1 && Obj(commands).GetProperty("materialJson").GetString()!.Contains("#FF0000"), "pasting gives the look that was copied");
            commands.Clear(); editor.Selection.Set([c]); tab.PressPasteForTesting();
            Require(commands.Count == 1 && Obj(commands).GetProperty("materialJson").GetString()!.Contains("#FF0000") && Obj(commands).GetProperty("geometryJson").GetString()!.Contains("hollow") && Obj(commands).GetProperty("primitiveType").GetString() == "sphere", "a sphere gets the look and the adjustments of a box that still fit, and stays a sphere");
            Require(editor.History.UndoCount > 0, "alignments and pastes are in the history");
            GD.Print("event=build_object_tab_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=build_object_tab_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
