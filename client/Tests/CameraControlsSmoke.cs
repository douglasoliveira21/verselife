using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Camera;
using LifeVerse.Shared.Camera;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>The free camera on the real player: ALT, CTRL+ALT, CTRL+ALT+SHIFT with the left button, the wheel with and without CTRL, ESC, keeping the focus, walls and what ends it.</summary>
public partial class CameraControlsSmoke : Node
{
    private static InputEventMouseButton Click(MouseButton button, bool pressed, bool ctrl = false, bool alt = false, bool shift = false) => new() { ButtonIndex = button, Pressed = pressed, CtrlPressed = ctrl, AltPressed = alt, ShiftPressed = shift, Position = new Vector2(400, 300) };
    private static InputEventMouseMotion Drag(float dx, float dy, bool ctrl = false, bool alt = false, bool shift = false) => new() { Relative = new Vector2(dx, dy), Position = new Vector2(400, 300), CtrlPressed = ctrl, AltPressed = alt, ShiftPressed = shift };
    private static InputEventKey Key(Key key) => new() { Keycode = key, Pressed = true };

    public override async void _Ready()
    {
        try
        {
            CameraSettings.Reset();
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var cam = player.GetNode<CameraController>("CameraController"); var pivot = player.GetNode<Node3D>("CameraPivot");
            player.CameraTargetResolver = null; // the interface of the world answers what is under the cursor; these steps give their own answers
            await Frames(40);
            var spot = player.GlobalPosition;
            Require(!player.FreeViewActive && !cam.Handle(Click(MouseButton.Left, true)) && !cam.Handle(Click(MouseButton.Left, false)), "a plain click is not the camera's");
            Require(!cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }) && !player.FreeViewActive, "the plain wheel of the avatar's camera is left to the player");
            Require(!cam.Handle(Key(Godot.Key.Escape)), "ESC does nothing to the camera when it is not free");

            // ---- ALT + drag: orbit around, vertical drag changes the distance
            var yaw0 = player.CameraYaw; var distance0 = player.CameraDistanceGoal; var focus0 = player.FocusWorld;
            Require(cam.Handle(Click(MouseButton.Left, true, alt: true)) && cam.Gesture == CameraGesture.OrbitDolly && player.FreeViewActive, "ALT + left button starts the free camera");
            Require(cam.Handle(Drag(100, 0, alt: true)) && Mathf.Abs(player.CameraYaw - (yaw0 - .3f)) < .001f, $"dragging sideways turns around: {player.CameraYaw - yaw0}");
            var pitch0 = player.CameraPitch; cam.Handle(Drag(0, 100, alt: true)); Require(player.CameraDistanceGoal < distance0 * .7f && Mathf.Abs(player.CameraPitch - pitch0) < .0001f, $"dragging down brings it closer without tilting: {player.CameraDistanceGoal}");
            cam.Handle(Drag(0, -100, alt: true)); Require(Mathf.Abs(player.CameraDistanceGoal - distance0) < .02f, "and back up takes it away again");
            Require(cam.Handle(Click(MouseButton.Left, false, alt: true)) && cam.Gesture == CameraGesture.None && player.FreeViewActive, "letting go keeps the camera where it is");
            await Frames(30);
            Require(player.GlobalPosition.DistanceTo(spot) < .001f, "the avatar never moved"); Require(player.FocusWorld.DistanceTo(focus0) < .01f, "orbiting kept the focus where it was");
            Require(pivot.GlobalPosition.DistanceTo(player.FocusWorld) < .05f, $"the camera really turns around the focus: {pivot.GlobalPosition.DistanceTo(player.FocusWorld)}");
            Require(Mathf.Abs(player.CameraDistanceNow - player.CameraDistanceGoal) < .1f, "and stands at the distance asked");

            // ---- CTRL + ALT + drag: both ways
            var y1 = player.CameraYaw; var p1 = player.CameraPitch;
            cam.Handle(Click(MouseButton.Left, true, ctrl: true, alt: true)); Require(cam.Gesture == CameraGesture.Orbit, "CTRL + ALT is the full orbit");
            cam.Handle(Drag(-60, 40, ctrl: true, alt: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true, alt: true));
            Require(player.CameraYaw > y1 && player.CameraPitch < p1, $"it turns both ways: {player.CameraYaw - y1} / {player.CameraPitch - p1}");
            for (var i = 0; i < 50; i++) { cam.Handle(Click(MouseButton.Left, true, ctrl: true, alt: true)); cam.Handle(Drag(0, 5000, ctrl: true, alt: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true, alt: true)); }
            Require(player.CameraPitch >= -CameraOptions.DefaultPitchLimit - .001f && player.CameraPitch <= CameraOptions.DefaultPitchLimit + .001f, "the vertical turn cannot go over the top"); await Frames(20);
            Require(player.FocusWorld.DistanceTo(focus0) < .01f, "the focus is still the same after all that");

            // ---- CTRL + ALT + SHIFT + drag: slides the camera sideways and up
            var before = player.FocusWorld; var distance1 = player.CameraDistanceGoal; var viewRight = player.ViewCamera.GlobalBasis.X;
            cam.Handle(Click(MouseButton.Left, true, true, true, true)); Require(cam.Gesture == CameraGesture.Pan, "CTRL + ALT + SHIFT is the pan");
            cam.Handle(Drag(80, 0, true, true, true)); cam.Handle(Drag(0, 60, true, true, true)); cam.Handle(Click(MouseButton.Left, false, true, true, true));
            var moved = player.FocusWorld - before; Require(moved.Length() > .1f && Mathf.Abs(player.CameraDistanceGoal - distance1) < .001f && player.GlobalPosition.DistanceTo(spot) < .001f, $"the focus slid and nothing else changed: {moved}");
            Require(moved.Dot(viewRight) < -.05f, "dragging right slides the view to the right (the focus goes left of the screen)"); await Frames(30);
            Require(pivot.GlobalPosition.DistanceTo(player.FocusWorld) < .05f, "the camera follows the new focus");

            // ---- the wheel: proportional in the free camera, precision with CTRL
            var d = player.CameraDistanceGoal; cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }); var normal = d - player.CameraDistanceGoal;
            Require(normal > 0 && Mathf.Abs(player.CameraDistanceGoal - d * .88f) < .01f, $"a notch of the wheel brings it 12% closer: {player.CameraDistanceGoal}");
            var d2 = player.CameraDistanceGoal; cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, CtrlPressed = true }); var precise = d2 - player.CameraDistanceGoal;
            Require(precise > 0 && precise < normal * .5f, $"CTRL + wheel is a small step: {precise} against {normal}");
            for (var i = 0; i < 300; i++) cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true }); Require(Mathf.Abs(player.CameraDistanceGoal - CameraSettings.Current.MinDistance) < .001f, "it can come as close as the limit and no more");
            for (var i = 0; i < 300; i++) cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true }); Require(Mathf.Abs(player.CameraDistanceGoal - CameraSettings.Current.MaxDistance) < .001f, "and as far as the other limit");
            player.ZoomCamera(1, false); player.ZoomCamera(1, false); await Frames(60);
            Require(Mathf.Abs(player.CameraDistanceNow - player.CameraDistanceGoal) < .5f, "the distance glides to what was asked");

            // ---- ESC gives the camera back
            Require(cam.Handle(Key(Godot.Key.Escape)) && !player.FreeViewActive, "ESC leaves the free camera"); await Frames(60);
            Require(pivot.Position.DistanceTo(new Vector3(0, 1.55f, 0)) < .2f && player.CameraDistanceGoal >= 1.5f && player.GlobalPosition.DistanceTo(spot) < .001f, $"the camera is behind the avatar again: {pivot.Position}");

            // ---- walking ends it (and the option can turn that off)
            cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true)); Require(player.FreeViewActive, "free again");
            Input.ActionPress("move_forward"); await Frames(12); Input.ActionRelease("move_forward"); Require(!player.FreeViewActive && player.GlobalPosition.DistanceTo(spot) > .05f, "walking brings the camera back behind the avatar");
            CameraSettings.Current = CameraSettings.Current with { RestoreAutomatically = false }; cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true));
            Input.ActionPress("move_forward"); await Frames(12); Input.ActionRelease("move_forward"); Require(player.FreeViewActive, "with the option off the camera stays where it was while walking"); player.ExitFreeView(); CameraSettings.Reset();

            // ---- walls: the camera stands in front of them, or goes through when told to
            await Frames(60); spot = player.GlobalPosition;  cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true));  player.ZoomCamera(-12, false); await Frames(2);  await Frames(58);
            
            var back = (player.ViewCamera.GlobalPosition - pivot.GlobalPosition).Normalized(); var wall = new StaticBody3D { CollisionLayer = 1, CollisionMask = 1 }; wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(40, 40, .4f) } }); world.AddChild(wall);
            wall.GlobalTransform = new Transform3D(Basis.LookingAt(back, Vector3.Up), pivot.GlobalPosition + back * 2f); await Frames(30);
            var free = player.CameraDistanceGoal; Require(player.CameraDistanceNow < 2f && player.CameraDistanceNow > .3f && free > 4f, $"a wall in the way brings the camera in front of it: {player.CameraDistanceNow} of {free}");
            player.ApplyCameraSettings(player.FieldOfView, player.CameraDistanceGoal, player.PivotOffset, player.ShoulderOffset, player.CameraFollowRate, false); await Frames(30);
            Require(player.CameraDistanceNow > 3.5f, $"with the collision off it goes through: {player.CameraDistanceNow}"); player.ApplyCameraSettings(player.FieldOfView, player.CameraDistanceGoal, player.PivotOffset, player.ShoulderOffset, player.CameraFollowRate, true); wall.QueueFree(); player.ExitFreeView();

            // ---- looking at a thing that moves, and staying on it while orbiting
            var marker = new Node3D { Position = player.GlobalPosition + new Vector3(3, 1, 0) }; world.AddChild(marker);
            player.FocusOn(marker, new Vector3(0, .1f, 0), .6f); await Frames(40);
            Require(player.FreeViewActive && player.FocusedNode == marker && player.FocusWorld.DistanceTo(marker.GlobalPosition + new Vector3(0, .1f, 0)) < .02f && pivot.GlobalPosition.DistanceTo(player.FocusWorld) < .05f, "the camera looks at the thing");
            Require(player.CameraDistanceNow < 1f, $"and comes close: {player.CameraDistanceNow}");
            cam.Handle(Click(MouseButton.Left, true, true, true)); cam.Handle(Drag(200, 80, true, true)); cam.Handle(Click(MouseButton.Left, false, true, true)); marker.Position += new Vector3(2, 0, 1); await Frames(40);
            Require(pivot.GlobalPosition.DistanceTo(marker.GlobalPosition + new Vector3(0, .1f, 0)) < .05f, "it keeps looking at the same spot of the thing while it moves and the camera orbits");
            Require(player.GlobalPosition.DistanceTo(spot) < .001f, "the avatar still has not moved"); marker.QueueFree(); await Frames(5); player.ExitFreeView();

            // ---- the options decide which gestures exist
            CameraSettings.Current = CameraSettings.Current with { Classic = false }; Require(!cam.Handle(Click(MouseButton.Left, true, alt: true)), "with the classic controls off ALT does nothing");
            CameraSettings.Current = CameraSettings.Current with { CtrlSimplified = true }; var y2 = player.CameraYaw; Require(cam.Handle(Click(MouseButton.Left, true, ctrl: true)) && cam.Gesture == CameraGesture.CtrlOrbit, "with the simple control CTRL alone orbits"); cam.Handle(Drag(50, 0, ctrl: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true));
            Require(Mathf.Abs(player.CameraYaw - y2) > .1f, "and it turns"); player.ExitFreeView();
            CameraSettings.Current = CameraSettings.Current with { InvertH = true }; var y3 = player.CameraYaw; cam.Handle(Click(MouseButton.Left, true, ctrl: true)); cam.Handle(Drag(50, 0, ctrl: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true)); Require(player.CameraYaw > y3, "inverting turns it the other way"); player.ExitFreeView();
            CameraSettings.Reset(); player.ControlsLocked = true; Require(!cam.Handle(Click(MouseButton.Left, true, alt: true)), "while the controls are locked (loading screen) the camera is left alone"); player.ControlsLocked = false;

            // ================= stage 2 =================
            cam.PollKeyboard = false; await Frames(30); spot = player.GlobalPosition;
            // ---- CTRL held: orbit with the left button, and what happens to the camera when CTRL is let go
            CameraSettings.Current = CameraSettings.Current with { CtrlSimplified = true, KeepAfterCtrl = false };
            Require(!cam.Handle(Key(Godot.Key.Ctrl)) , "the CTRL key itself is nobody's to take");
            var ctrlDown = new InputEventKey { Keycode = Godot.Key.Ctrl, Pressed = true }; cam.Handle(ctrlDown); Require(cam.CtrlHeld && !player.FreeViewActive, "holding CTRL alone changes nothing yet");
            var yawA = player.CameraYaw; cam.Handle(Click(MouseButton.Left, true, ctrl: true)); cam.Handle(Drag(80, 0, ctrl: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true));
            Require(player.FreeViewActive && cam.CtrlUsed && Mathf.Abs(player.CameraYaw - (yawA - .24f)) < .001f, "CTRL and a drag orbits the camera");
            cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, CtrlPressed = true }); await Frames(20);
            Require(player.GlobalPosition.DistanceTo(spot) < .001f, "the avatar is not touched");
            cam.Handle(new InputEventKey { Keycode = Godot.Key.Ctrl, Pressed = false }); Require(!player.FreeViewActive && !cam.CtrlHeld, "letting go of CTRL brings the camera back (the option says so)");
            CameraSettings.Current = CameraSettings.Current with { KeepAfterCtrl = true };
            cam.Handle(ctrlDown); cam.Handle(Click(MouseButton.Left, true, ctrl: true)); cam.Handle(Drag(40, 10, ctrl: true)); cam.Handle(Click(MouseButton.Left, false, ctrl: true)); var kept = player.CameraYaw;
            cam.Handle(new InputEventKey { Keycode = Godot.Key.Ctrl, Pressed = false }); await Frames(10); Require(player.FreeViewActive && Mathf.Abs(player.CameraYaw - kept) < .001f, "or keeps it where it was"); Require(cam.Handle(Key(Godot.Key.Escape)) && !player.FreeViewActive, "ESC still gives it back");
            CameraSettings.Current = CameraSettings.Current with { CtrlSimplified = false, KeepAfterCtrl = false };
            cam.Handle(ctrlDown); cam.Handle(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, CtrlPressed = true }); cam.Handle(new InputEventKey { Keycode = Godot.Key.Ctrl, Pressed = false });
            Require(player.FreeViewActive, "with the simple control off CTRL + wheel is only the precision zoom and the camera stays"); player.ExitFreeView(); CameraSettings.Reset();

            // ---- looking at things: ALT + click, double click, small things on the body and people who move
            var glb = LifeVerse.Shared.Avatar.WearableTemplate.Attachment(LifeVerse.Shared.Avatar.WearableTemplate.Extras("watch", bone: "hand_l"), .04f); AvatarRig.AttachmentModelProvider = _ => ModelCache.ParseInstance(glb);
            var watch = new LifeVerse.Client.Scripts.Attachments.AttachmentData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Relógio", "wrist_l", "Pulso esquerdo", [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true);
            player.Rig.ApplyAttachments([watch]); await Frames(15); var holder = player.Rig.AttachmentHolder(watch.Id)!;
            ContextTarget? answer = new AttachmentTarget(watch.Id, watch.InventoryItemId, "Relógio", "wrist_l", "Pulso esquerdo", true, ""); player.CameraTargetResolver = _ => answer;
            cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Drag(30, 0, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true)); await Frames(20);
            Require(player.FocusedNode is null, "an ALT drag is a turn, not a look at something");
            cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true)); await Frames(60);
            Require(player.FocusedNode == holder && player.FreeViewActive, "ALT and a click puts the camera on the thing under the cursor");
            Require(player.CameraDistanceGoal < .4f, $"a watch is looked at from very close: {player.CameraDistanceGoal}");
            Require(player.ViewCamera.GlobalPosition.DistanceTo(holder.GlobalPosition) < .6f, $"the camera is really next to the watch: {player.ViewCamera.GlobalPosition.DistanceTo(holder.GlobalPosition)}");
            cam.Handle(Click(MouseButton.Left, true, true, true)); cam.Handle(Drag(300, 60, true, true)); cam.Handle(Click(MouseButton.Left, false, true, true));
            for (var i = 0; i < 30; i++) { player.Rig.SetMotionState(i < 15 ? AvatarMotionState.Run : AvatarMotionState.Idle, true); await Frames(2); }
            await Frames(30); Require(pivot.GlobalPosition.DistanceTo(holder.GlobalPosition) < .08f, $"it keeps the watch in the middle while the arm swings and the camera turns around it: {pivot.GlobalPosition.DistanceTo(holder.GlobalPosition)}");
            Require(player.GlobalPosition.DistanceTo(spot) < .001f, "and the avatar stays where it was"); player.ExitFreeView(); player.Rig.ApplyAttachments([]); AvatarRig.AttachmentModelProvider = null;
            // ground and yourself
            answer = new GroundTarget(spot + new Vector3(6, 0, 0)); cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true)); await Frames(40);
            Require(player.FocusedNode is null && player.FocusWorld.DistanceTo(spot + new Vector3(6, 0, 0)) < .05f, "the ground is looked at as a point"); player.ExitFreeView();
            answer = new SelfTarget("Eu"); cam.Handle(Click(MouseButton.Left, true, alt: true)); cam.Handle(Click(MouseButton.Left, false, alt: true)); await Frames(40);
            Require(player.FocusedNode == player && player.CameraDistanceGoal > 1.5f && player.CameraDistanceGoal < 4, $"you are looked at as a whole person: {player.CameraDistanceGoal}"); player.ExitFreeView();
            answer = null; Require(!cam.FocusAtScreen(new Vector2(1, 1)), "nothing there, nothing to look at");
            // double click on a person who walks
            var walker = new Node3D { Position = spot + new Vector3(4, 0, 2) }; world.AddChild(walker); CameraFocus.RemoteNode = peer => peer == 7 ? walker : null; answer = new AvatarTarget(7, "Ana", walker.Position);
            var doubleClick = Click(MouseButton.Left, true); doubleClick.DoubleClick = true;
            Require(!cam.Handle(doubleClick), "a double click does what it always did (walks) unless the option is on");
            CameraSettings.Current = CameraSettings.Current with { FocusOnDoubleClick = true };
            Require(cam.Handle(doubleClick) && player.FocusedNode == walker, "with the option on, a double click puts the camera on the person"); walker.Position += new Vector3(5, 0, 0); await Frames(60);
            Require(player.FocusWorld.DistanceTo(walker.Position + new Vector3(0, .95f, 0)) < .1f, "and follows them when they walk away"); player.ExitFreeView();
            answer = new GroundTarget(spot + new Vector3(3, 0, 0)); Require(!cam.Handle(doubleClick), "a double click on the ground still walks"); CameraSettings.Reset(); CameraFocus.RemoteNode = null; walker.QueueFree();

            // ---- the detached camera: its own place, the avatar still, and limits
            Require(!cam.Handle(new InputEventKey { Keycode = Godot.Key.V, Pressed = true, AltPressed = true }) && !player.DetachedActive, "ALT + V does nothing unless the option allows the detached camera");
            CameraSettings.Current = CameraSettings.Current with { AllowDetached = true, DetachedRange = 6, DetachedSpeed = 10 };
            Require(cam.Handle(new InputEventKey { Keycode = Godot.Key.V, Pressed = true, AltPressed = true }) && player.DetachedActive && !player.FreeViewActive, "ALT + V lets the camera go");
            var start = player.DetachedPosition; Input.ActionPress("move_forward"); await Frames(30); Input.ActionRelease("move_forward"); await Frames(5);
            Require(player.DetachedPosition.DistanceTo(start) > 1f && player.GlobalPosition.DistanceTo(spot) < .001f, $"the keys fly the camera and the avatar stands still: {player.DetachedPosition.DistanceTo(start)}");
            Require(player.ViewCamera.GlobalPosition.DistanceTo(player.DetachedPosition) < .1f, "the camera is where the detached place is");
            Input.ActionPress("move_forward"); await Frames(120); Input.ActionRelease("move_forward"); await Frames(5);
            var flat = new Vector2(player.DetachedPosition.X - player.GlobalPosition.X, player.DetachedPosition.Z - player.GlobalPosition.Z).Length(); Require(flat <= 6.01f, $"it cannot go farther than the range from the avatar: {flat}");
            var yawD = player.CameraYaw; cam.Handle(Click(MouseButton.Left, true, ctrl: true, alt: true)); cam.Handle(Drag(60, 0, true, true)); cam.Handle(Click(MouseButton.Left, false, true, true)); Require(!player.FreeViewActive && Mathf.Abs(player.CameraYaw - yawD) > .1f, "turning while detached turns the view, nothing else starts");
            Require(cam.Handle(Key(Godot.Key.Escape)) && !player.DetachedActive, "ESC brings it back"); await Frames(60);
            Require(player.ViewCamera.GlobalPosition.DistanceTo(player.GlobalPosition) < 7, "the camera is by the avatar again");
            Input.ActionPress("move_forward"); await Frames(15); Input.ActionRelease("move_forward"); Require(player.GlobalPosition.DistanceTo(spot) > .05f, "and the avatar walks again");
            await Frames(40); cam.Handle(new InputEventKey { Keycode = Godot.Key.V, Pressed = true, AltPressed = true }); Require(player.DetachedActive, "again");
            CameraSettings.Reset(); await Frames(3); Require(!player.DetachedActive, "switching the option off takes the camera back by itself");

            GD.Print("event=camera_controls_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            CameraSettings.Reset();
            GD.PushError($"event=camera_controls_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
