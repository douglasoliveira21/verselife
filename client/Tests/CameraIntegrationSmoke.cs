using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Client.Scripts.Camera;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Camera;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>The camera together with the rest: the build editor and the editor of attachments (the camera's gestures are never also a click on an object), the editing camera, the interface, and what takes the avatar somewhere else.</summary>
public partial class CameraIntegrationSmoke : Node
{
    private static string StateJson(Guid id, string type, float x, float y, float z, int revision, int cost, string name = "Objeto") =>
        JsonSerializer.Serialize(new { operation = revision <= 1 ? "create" : "update", objectState = new { id, islandId = Guid.NewGuid(), creatorUserId = Guid.Empty, ownerUserId = Guid.Empty, name, description = "", primitiveType = type, positionX = x, positionY = y, positionZ = z, rotationX = 0, rotationY = 0, rotationZ = 0, scaleX = 1, scaleY = 1, scaleZ = 1, materialJson = "{}", geometryJson = "{}", physicsJson = "{\"collision\":true}", permissionsJson = "{}", contentJson = "[]", revision, cost } });

    private static InputEventMouseButton Press(Vector2 at, bool pressed = true, bool ctrl = false, bool alt = false, bool shift = false, MouseButton button = MouseButton.Left) => new() { ButtonIndex = button, Pressed = pressed, Position = at, GlobalPosition = at, CtrlPressed = ctrl, AltPressed = alt, ShiftPressed = shift };
    private static InputEventMouseMotion Move(float dx, float dy, bool ctrl = false, bool alt = false, bool shift = false) => new() { Relative = new Vector2(dx, dy), Position = new Vector2(400, 300), CtrlPressed = ctrl, AltPressed = alt, ShiftPressed = shift };

    public override async void _Ready()
    {
        var manager = GetNode<AvatarManager>("/root/AvatarManager"); var previous = manager.Transport;
        try
        {
            CameraSettings.Reset();
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var cam = player.GetNode<CameraController>("CameraController"); cam.PollKeyboard = false; player.CameraTargetResolver = null;
            await Frames(40); var spot = player.GlobalPosition;

            // ================= the build editor =================
            var editor = BuildHost.Ensure(world); var commands = new List<string>(); editor.Synchronizer.OperationSink = json => { if (!json.Contains("\"operation\":\"lock\"") && !json.Contains("\"operation\":\"unlock\"")) commands.Add(json); return true; };
            editor.SetActive(true); await Frames(3);
            var sphere = Guid.NewGuid(); var target = player.GlobalPosition + new Vector3(4, .5f, 0); /* to the side, so the avatar is not between the camera and it */ editor.Synchronizer.OnOperation(StateJson(sphere, "sphere", target.X, target.Y, target.Z, 1, 12, "Esfera"));
            editor.EditCamera.Enter(player.ViewCamera, target, 8); await Frames(30); editor.Selection.Clear();
            Require(editor.OwnsTheMouse && cam.Handle(Press(new Vector2(0, 0), alt: true)) && cam.Gesture == CameraGesture.OrbitDolly && !player.FreeViewActive, "while building the camera gestures belong to the editing camera and nothing starts on the avatar's");
            cam.Handle(Press(new Vector2(0, 0), false, alt: true));
            var onSphere = editor.EditCamera.UnprojectPosition(target);
            editor.EditCamera.Dolly(0); editor._UnhandledInput(Press(onSphere)); Require(editor.Selection.Ids.SequenceEqual([sphere]), "a plain click selects the object as always"); editor._UnhandledInput(Press(onSphere, false));
            editor.Selection.Clear(); editor._UnhandledInput(Press(onSphere, alt: true)); editor._UnhandledInput(Press(onSphere, false, alt: true)); Require(editor.Selection.Count == 0 && !editor.Dragging, "ALT + click does not select: it is a camera gesture");
            editor._UnhandledInput(Press(onSphere, ctrl: true)); Require(editor.Selection.Ids.SequenceEqual([sphere]), "CTRL + click still adds to the selection while the simple CTRL mode is off"); editor._UnhandledInput(Press(onSphere, false, ctrl: true)); editor.Selection.Clear();
            CameraSettings.Current = CameraSettings.Current with { CtrlSimplified = true }; editor._UnhandledInput(Press(onSphere, ctrl: true)); editor._UnhandledInput(Press(onSphere, false, ctrl: true));
            Require(editor.Selection.Count == 0, "with the simple CTRL mode the same click is the camera's and selects nothing"); CameraSettings.Reset();
            editor.Selection.Set([sphere]); commands.Clear();
            var yaw0 = editor.EditCamera.Yaw; var camYaw0 = player.CameraYaw; var distance0 = editor.EditCamera.Distance;
            cam.Handle(Press(onSphere, ctrl: true, alt: true)); cam.Handle(Move(120, 40, true, true)); cam.Handle(Press(onSphere, false, ctrl: true, alt: true)); await Frames(5);
            Require(Mathf.Abs(editor.EditCamera.Yaw - yaw0) > .1f && Mathf.Abs(player.CameraYaw - camYaw0) < .0001f && commands.Count == 0, "CTRL + ALT turns the editing camera around the selection: no command, no change to the object, no turn of the avatar's camera");
            var focus0 = editor.EditCamera.Target; cam.Handle(Press(onSphere, ctrl: true, alt: true, shift: true)); cam.Handle(Move(80, 0, true, true, true)); cam.Handle(Press(onSphere, false, ctrl: true, alt: true, shift: true));
            Require(editor.EditCamera.Target.DistanceTo(focus0) > .05f && commands.Count == 0 && editor.Synchronizer.Views[sphere].Position.DistanceTo(target) < .001f, "CTRL + ALT + SHIFT slides the editing camera without moving the object");
            cam.Handle(Press(onSphere, alt: true)); cam.Handle(Move(0, 80, alt: true)); cam.Handle(Press(onSphere, false, alt: true)); Require(editor.EditCamera.Distance < distance0 * .8f, "ALT and a vertical drag brings the editing camera closer");
            var near = editor.EditCamera.Distance; var wheel = (bool ctrl) => new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true, CtrlPressed = ctrl };
            var normalFar = editor.EditCamera.Distance; editor.EditCamera._Input(wheel(false)); var normalStep = editor.EditCamera.Distance - normalFar; var preciseFrom = editor.EditCamera.Distance; editor.EditCamera._Input(wheel(true)); var preciseStep = editor.EditCamera.Distance - preciseFrom;
            Require(normalStep > 0 && preciseStep > 0 && preciseStep < normalStep * .5f, $"CTRL + wheel is a small step for the editing camera too: {preciseStep} against {normalStep}");
            player.CameraTargetResolver = _ => new ObjectTarget(editor.Synchronizer.Views[sphere]); var before = editor.EditCamera.Target; editor.EditCamera.Focus(before + new Vector3(5, 0, 0), 1);
            cam.Handle(Press(onSphere, alt: true)); cam.Handle(Press(onSphere, false, alt: true)); await Frames(40); player.CameraTargetResolver = null;
            Require(editor.EditCamera.Target.DistanceTo(editor.Synchronizer.Views[sphere].GlobalPosition) < .6f && !player.FreeViewActive, "ALT + click on an object puts the editing camera on it");
            editor.SetActive(false); await Frames(5); Require(!editor.OwnsTheMouse, "leaving the editor gives the camera back");

            // ================= the editor of attachments =================
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("watch", bone: "hand_l"), .04f); AvatarRig.AttachmentModelProvider = _ => ModelCache.ParseInstance(glb);
            var watchId = Guid.NewGuid(); var rows = new List<Dictionary<string, object?>>();
            rows.Add(new() { ["id"] = watchId, ["inventoryItemId"] = Guid.NewGuid(), ["assetId"] = Guid.NewGuid(), ["name"] = "Relógio", ["point"] = "wrist_l", ["pointName"] = "Pulso esquerdo", ["position"] = new[] { 0f, 0f, 0f }, ["rotation"] = new[] { 0f, 0f, 0f }, ["scale"] = new[] { 1f, 1f, 1f }, ["equipped"] = true, ["order"] = 0, ["revision"] = 1, ["canModify"] = true });
            manager.Transport = (method, path, body) => method == Godot.HttpClient.Method.Get && path == "/attachments" ? (200, JsonSerializer.Serialize(new { attachments = rows })) : (404, "{}");
            var said = new List<string>(); var attach = new AttachmentEditor { Name = "AttachmentEditor", Player = player, Tell = said.Add }; world.AddChild(attach); manager.Attachments.Refresh(); await Frames(30);
            player.Rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(10);
            Require(attach.Open(watchId) && player.EditViewActive, "the editor of attachments opens"); await Frames(60);
            var holder = player.Rig.AttachmentHolder(watchId)!; var pivot = player.GetNode<Node3D>("CameraPivot");
            Require(player.FocusedNode == holder && pivot.GlobalPosition.DistanceTo(holder.GlobalPosition) < .1f, "the camera goes to the watch and stays on it");
            Require(player.CameraDistanceGoal < 1f && player.ViewCamera.GlobalPosition.DistanceTo(holder.GlobalPosition) < 1.2f, $"close enough to work on a small object: {player.CameraDistanceGoal}");
            for (var i = 0; i < 20; i++) { player.Rig.SetMotionState(i < 10 ? AvatarMotionState.Run : AvatarMotionState.Walk, true); await Frames(2); }
            await Frames(30); Require(pivot.GlobalPosition.DistanceTo(player.Rig.AttachmentHolder(watchId)!.GlobalPosition) < .1f, "the camera follows the watch while the arm moves (the view is kept while the animation plays)");
            player.Rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(20);
            // orbit and zoom in the edit view, without touching the placement
            var dirty0 = attach.Model!.Dirty; var yawA = player.CameraYaw; cam.Handle(Press(new Vector2(400, 300), ctrl: true, alt: true)); cam.Handle(Move(150, 30, true, true)); cam.Handle(Press(new Vector2(400, 300), false, ctrl: true, alt: true));
            Require(Mathf.Abs(player.CameraYaw - yawA) > .1f && attach.Model.Dirty == dirty0, "the camera turns around the watch and the placement is not touched");
            var zoom0 = player.CameraDistanceGoal; cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, CtrlPressed = true }); Require(player.CameraDistanceGoal < zoom0 && player.CameraDistanceGoal > zoom0 * .9f, "precision zoom works in the edit view");
            // a camera gesture over a handle is not a drag of the handle
            attach.SetTool(AttachmentTool.Move); await Frames(5); var center = attach.Gizmo.GlobalPosition; var camNode = player.ViewCamera; var at = camNode.UnprojectPosition(center);
            attach._Input(Press(at, alt: true)); Require(!attach.Dragging, "ALT + click on the handles does not grab them"); attach._Input(Press(at, false, alt: true));
            attach._Input(Press(at, ctrl: true, alt: true, shift: true)); Require(!attach.Dragging, "nor does CTRL + ALT + SHIFT"); attach._Input(Press(at, false, ctrl: true, alt: true, shift: true));
            attach._Input(Press(at)); Require(attach.Dragging, "a plain click on the middle handle does grab it"); attach._Input(Press(at, false)); Require(!attach.Dragging, "and letting go ends it");
            CameraSettings.Current = CameraSettings.Current with { RestoreAfterEdit = false }; attach.Cancel(); await Frames(10);
            Require(!player.EditViewActive && !player.MirrorActive && player.FreeViewActive && player.CameraDistanceGoal < 1f, "with the option off the camera stays where the editing left it");
            player.ExitFreeView(); await Frames(40); CameraSettings.Reset();
            await Frames(30); var framing2 = player.CameraDistanceGoal; attach.Open(watchId); await Frames(30); attach.Cancel(); await Frames(60);
            Require(!player.EditViewActive && !player.MirrorActive && !player.FreeViewActive && Mathf.Abs(player.CameraDistanceGoal - framing2) < .01f, "by default the camera goes back to the framing of before the edit");
            AvatarRig.AttachmentModelProvider = null; player.Rig.ApplyAttachments([]); manager.Transport = previous;

            // ================= the interface keeps its clicks =================
            // a viewport of its own with a window over the world: the interface takes the click first, so the camera never sees it
            var screen = new SubViewport { Size = new Vector2I(800, 600), GuiDisableInput = false, HandleInputLocally = true, Disable3D = true }; AddChild(screen);
            var cover = new Button { Text = "janela", MouseFilter = Control.MouseFilterEnum.Stop, Position = Vector2.Zero, Size = new Vector2(800, 600) }; screen.AddChild(cover);
            var guarded = new CameraController { Name = "GuardedCamera", Player = player, PollKeyboard = false }; screen.AddChild(guarded); await Frames(3);
            screen.PushInput(Press(new Vector2(300, 200), alt: true), true); await Frames(2);
            Require(!guarded.Gesturing && !player.FreeViewActive, "a click on a window or a menu never starts a camera gesture");
            screen.PushInput(Press(new Vector2(300, 200), false, alt: true), true); cover.QueueFree(); await Frames(3);
            screen.PushInput(Press(new Vector2(300, 200), alt: true), true); await Frames(2); Require(guarded.Gesturing && player.FreeViewActive, "the same click on the world does");
            screen.PushInput(Press(new Vector2(300, 200), false, alt: true), true); await Frames(2); player.ExitFreeView(); screen.QueueFree();
            player.ControlsLocked = true; Require(!cam.Handle(Press(new Vector2(300, 200), alt: true)), "while the loading screen covers the world the camera does not answer"); player.ControlsLocked = false;

            // ================= other resolutions and sensitivities =================
            var window = GetWindow(); var originalSize = window.Size; var yaws = new List<float>();
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(1920, 1080), new Vector2I(3840, 2160), new Vector2I(800, 600) })
            {
                window.Size = size; await Frames(3); player.ExitFreeView(); var y = player.CameraYaw; cam.Handle(Press(new Vector2(size.X / 2f, size.Y / 2f), alt: true)); cam.Handle(Move(100, 0, alt: true)); cam.Handle(Press(new Vector2(size.X / 2f, size.Y / 2f), false, alt: true)); yaws.Add(player.CameraYaw - y);
            }
            window.Size = originalSize; Require(yaws.All(v => Mathf.Abs(v - yaws[0]) < .0001f), $"the same movement of the mouse turns the camera the same at any resolution: {string.Join(", ", yaws)}");
            foreach (var factor in new[] { .25f, 1f, 3f }) { CameraSettings.Current = CameraSettings.Current with { SensitivityH = .003f * factor }; player.ExitFreeView(); var y = player.CameraYaw; cam.Handle(Press(new Vector2(400, 300), alt: true)); cam.Handle(Move(100, 0, alt: true)); cam.Handle(Press(new Vector2(400, 300), false, alt: true)); Require(Mathf.Abs(Mathf.Abs(player.CameraYaw - y) - .3f * factor) < .001f, $"sensitivity {factor}×"); }
            CameraSettings.Reset(); player.ExitFreeView();

            // ================= leaving the free camera and going on =================
            await Frames(40); spot = player.GlobalPosition;
            cam.Handle(Press(new Vector2(400, 300), alt: true)); cam.Handle(Press(new Vector2(400, 300), false, alt: true)); CameraSettings.Current = CameraSettings.Current with { AllowDetached = true }; cam.Handle(new InputEventKey { Keycode = Key.V, Pressed = true, AltPressed = true });
            Require(player.DetachedActive && !player.FreeViewActive == false || player.DetachedActive || player.FreeViewActive, "free camera on");
            var teleport = GetNode<NetworkManager>("/root/NetworkManager"); teleport.OnTeleportResult(true, "", 0, 0, 90); await Frames(3);
            Require(!player.FreeViewActive && !player.DetachedActive, "a teleport brings the camera back to the avatar");
            cam.Handle(Press(new Vector2(400, 300), alt: true)); cam.Handle(Press(new Vector2(400, 300), false, alt: true)); Require(player.FreeViewActive, "free again");
            var seat = new InteractableSeat { Position = player.GlobalPosition + new Vector3(2, 0, 0) }; seat.AddChild(new Marker3D { Name = "SeatAnchor" }); seat.AddChild(new Marker3D { Name = "ExitAnchor", Position = new Vector3(0, 0, 1) }); world.AddChild(seat); await Frames(2);
            player.Sit(seat); await Frames(5); Require(!player.FreeViewActive && !player.DetachedActive && seat.IsOccupied, "sitting brings the camera back too");
            Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = true }); await Frames(3); Input.ParseInputEvent(new InputEventAction { Action = "interact", Pressed = false }); await Frames(80); spot = player.GlobalPosition; Require(!seat.IsOccupied, "the avatar stands up");
            Input.ActionPress("move_forward"); await Frames(40); Input.ActionRelease("move_forward"); Require(player.GlobalPosition.DistanceTo(spot) > .1f, $"the avatar walks after the free camera: {player.GlobalPosition.DistanceTo(spot)}");
            CameraSettings.Reset();

            GD.Print("event=camera_integration_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            manager.Transport = previous; AvatarRig.AttachmentModelProvider = null; CameraSettings.Reset();
            GD.PushError($"event=camera_integration_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
