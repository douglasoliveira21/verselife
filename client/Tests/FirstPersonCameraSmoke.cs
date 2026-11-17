using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Client.Scripts.Camera;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Camera;

namespace LifeVerse.Client.Tests;

/// <summary>True first person, the camera: it is at the eyes of the avatar's head (also of a custom body, whatever its height), it can look at the feet, its field of view is its own (60 to 100) and the other modes keep theirs.</summary>
public partial class FirstPersonCameraSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        try
        {
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var rig = player.GetNode<AvatarRig>("AvatarRig");
            await Frames(40);

            // ---- field of view
            Require(Mathf.Abs(PlayerController.FirstPersonFovDefault - 75f) < .01f && PlayerController.FirstPersonFovMin == 60f && PlayerController.FirstPersonFovMax == 100f, "first person opens 75 degrees, from 60 to 100");
            var third = player.FieldOfView;
            player.SetCameraMode("FirstPerson"); await Seconds(.8f);
            Require(Mathf.Abs(player.ViewCamera.Fov - 75f) < .01f, $"first person opens 75 degrees by default: {player.ViewCamera.Fov}");
            player.SetFirstPersonFov(120); Require(player.ViewCamera.Fov == 100f, "never wider than 100"); player.SetFirstPersonFov(20); Require(player.ViewCamera.Fov == 60f, "never narrower than 60"); player.SetFirstPersonFov(90); Require(player.ViewCamera.Fov == 90f, "and the setting is applied at once");
            player.SetCameraMode("ThirdPerson"); await Seconds(.8f);
            Require(Mathf.Abs(player.ViewCamera.Fov - third) < .01f, $"third person keeps its own field of view: {player.ViewCamera.Fov} (was {third})");
            player.SetCameraMode("FirstPerson"); await Seconds(.8f); Require(Mathf.Abs(player.ViewCamera.Fov - 90f) < .01f, "and first person remembers its own");
            player.SetFirstPersonFov(75);

            // ---- the camera is at the eyes (standard body)
            await Frames(30);
            var eyes = rig.EyeWorldPosition(-player.GlobalBasis.Z)!.Value;
            Require(player.ViewCamera.GlobalPosition.DistanceTo(eyes) < .08f, $"the camera is at the eyes of the body: {player.ViewCamera.GlobalPosition} / {eyes}");
            Require(player.ViewCamera.Near < .1f, $"and sees what is close ({player.ViewCamera.Near})");

            // ---- and of a body with another height
            rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Alto", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f }); await Frames(60);
            Require(rig.CustomBodyActive, "a custom body is worn"); var eyes1 = rig.EyeWorldPosition(-player.GlobalBasis.Z)!.Value;
            Require(player.ViewCamera.GlobalPosition.DistanceTo(eyes1) < .08f, $"the camera follows the eyes of the custom body: {player.ViewCamera.GlobalPosition} / {eyes1}");
            rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Alto", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f, BodySettingsJson = "{\"height\":1.3}" }); await Frames(60);
            var eyes2 = rig.EyeWorldPosition(-player.GlobalBasis.Z)!.Value; Require(eyes2.Y > eyes1.Y + .2f, $"a taller body has its eyes higher: {eyes1.Y} -> {eyes2.Y}");
            Require(player.ViewCamera.GlobalPosition.DistanceTo(eyes2) < .1f, "and the camera went there");

            // ---- looking at the feet
            for (var i = 0; i < 25; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, 60), Position = new Vector2(500, 400) }); await Frames(1); }
            await Frames(40);
            Require(player.CameraPitch <= -1.45f, $"the person can look almost straight down: {player.CameraPitch}");
            Require((-player.ViewCamera.GlobalBasis.Z).Y < -.95f, $"to their own feet: {(-player.ViewCamera.GlobalBasis.Z)}");
            for (var i = 0; i < 40; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, -60), Position = new Vector2(500, 400) }); await Frames(1); }
            Require(player.CameraPitch >= 1.2f && player.CameraPitch <= PlayerController.FirstPersonPitchUp + .001f, $"and up: {player.CameraPitch}");

            // ---- the head bob and the lean (the settings can turn them off)
            player.SetCameraMode("FirstPerson"); await Seconds(.8f);
            for (var i = 0; i < 25; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, -60), Position = new Vector2(500, 400) }); await Frames(1); } // looking ahead again
            await Frames(10); FirstPersonSettings.Reset();
            player.MotionForTesting = (4.5f, true); var biggest = 0f; for (var i = 0; i < 90; i++) { await Frames(1); biggest = Mathf.Max(biggest, Mathf.Abs(player.HeadBobForTesting.Y)); }
            Require(biggest > .006f && biggest < .03f, $"walking bobs the head a little: {biggest} m");
            FirstPersonSettings.Current = new FirstPersonOptions { HeadBob = false }; await Frames(90); Require(player.HeadBobForTesting == Vector2.Zero, "and the bob can be switched off");
            FirstPersonSettings.Reset(); player.MotionForTesting = (4.5f, false); await Frames(90); Require(player.HeadBobForTesting == Vector2.Zero, "no bob in the air");
            player.MotionForTesting = null;
            var roll = 0f; for (var i = 0; i < 30; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(25, 0), Position = new Vector2(500, 400) }); await Frames(1); roll = Mathf.Max(roll, Mathf.Abs(player.SwayRollForTesting)); }
            Require(roll > .002f && roll <= LifeVerse.Shared.Camera.CameraSway.MaxRoll + .0005f, $"turning leans the view a little: {roll}");
            await Frames(90); Require(Mathf.Abs(player.SwayRollForTesting) < .001f, "and it comes back");
            player.SetCameraMode("ThirdPerson"); await Seconds(.5f); Require(player.HeadBobForTesting == Vector2.Zero && player.SwayRollForTesting == 0f, "third person has neither");

            // ---- the view follows the mouse at once (the default), and softly when asked
            async Task<float> Progress(float smoothing)
            {
                FirstPersonSettings.Current = new FirstPersonOptions { LookSmoothing = smoothing, Sway = false }; player.SetCameraMode("FirstPerson"); await Seconds(.8f);
                var start = player.ViewCamera.GlobalRotation.Y; Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(150, 0), Position = new Vector2(500, 400) });
                await ToSignal(GetTree().CreateTimer(.07), SceneTreeTimer.SignalName.Timeout); var soon = Mathf.AngleDifference(start, player.ViewCamera.GlobalRotation.Y); await Frames(120); var total = Mathf.AngleDifference(start, player.ViewCamera.GlobalRotation.Y);
                return Mathf.Abs(total) < .01f ? 0f : soon / total;
            }
            var quick = await Progress(0f); var normal = await Progress(.35f); var slow = await Progress(1f);
            Require(quick > .95f && normal > .9f, $"by default the view is on the mouse within 70 ms: {quick} / {normal}");
            Require(slow < .75f && slow < normal, $"and a soft turn takes longer: {slow}");
            FirstPersonSettings.Reset();

            // ---- the camera does not go through a wall that the face is against
            player.SetCameraMode("FirstPerson"); await Seconds(.6f);
            for (var i = 0; i < 30; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, -60), Position = new Vector2(500, 400) }); await Frames(1); } await Seconds(.6f);
            var faceDown = -player.GlobalBasis.Z; var head = rig.HeadAnchorWorld!.Value;
            var bodyMask = player.CollisionMask; player.CollisionMask = 0;          // the wall must not push the avatar away: the test is about the camera
            var wall = new StaticBody3D { Name = "Wall", CollisionLayer = 1 }; var shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(6, 6, .4f) } }; wall.AddChild(shape); AddChild(wall);
            wall.GlobalPosition = head + faceDown * (.08f + .2f); wall.GlobalBasis = player.GlobalBasis;                          // its face is 8 cm in front of the head
            await Seconds(.5f);
            var wallFace = head + faceDown * .08f; var depth = (player.ViewCamera.GlobalPosition - wallFace).Dot(faceDown);
            Require(depth < -.001f, $"the camera stays on this side of the wall: {depth}");
            FirstPersonSettings.Current = new FirstPersonOptions { Collision = false }; await Seconds(.5f);
            var without = (player.ViewCamera.GlobalPosition - wallFace).Dot(faceDown); Require(without > depth + .02f, $"without the collision it would go through: {without} vs {depth}");
            FirstPersonSettings.Reset(); wall.QueueFree(); player.CollisionMask = bodyMask;

            // ---- the wheel of the mouse goes in and out of first person, and nothing jumps
            player.SetCameraMode("ThirdPerson"); await Seconds(.8f);
            static InputEventMouseButton Wheel(bool up) => new() { ButtonIndex = up ? MouseButton.WheelUp : MouseButton.WheelDown, Pressed = true, Position = new Vector2(500, 400) };
            for (var i = 0; i < 30; i++) { Input.ParseInputEvent(Wheel(false)); await Frames(1); }
            await Seconds(.8f); Require(player.CameraModeName == "ThirdPerson", "scrolling out stays in third person");
            for (var i = 0; i < 30; i++) { Input.ParseInputEvent(Wheel(true)); await Frames(1); if (player.CameraModeName == "FirstPerson") break; }
            Require(player.CameraModeName == "FirstPerson", "scrolling in to the nearest distance goes into first person");
            var yawBody = player.Rotation.Y; var last = player.ViewCamera.GlobalPosition; var lastFov = player.ViewCamera.Fov; var headAtStart = rig.StandardPartShownForTesting("Hips/Torso/Neck") || rig.CustomBoneSizeForTesting("head") > .3f;
            float stepMax = 0, stepTotal = 0, fovMax = 0, fovTotal = 0; var movingFrames = 0; for (var i = 0; i < 120; i++) { await Frames(1); var now = player.ViewCamera.GlobalPosition; var step = now.DistanceTo(last); stepMax = Mathf.Max(stepMax, step); stepTotal += step; if (step > .02f) movingFrames++; last = now; var df = Mathf.Abs(player.ViewCamera.Fov - lastFov); fovMax = Mathf.Max(fovMax, df); fovTotal += df; lastFov = player.ViewCamera.Fov; }
            Require(stepTotal > .5f && movingFrames >= 3 && stepMax < stepTotal * .9f, $"the camera glides in over several frames, without a jump: {movingFrames} frames, the largest step is {stepMax} m of {stepTotal} m"); Require(fovTotal < .01f || fovMax < fovTotal * .95f, $"and so does the field of view: {fovMax} of {fovTotal} degrees");
            Require(headAtStart, "the head is still there while the camera comes in");
            await Seconds(.8f); Require(player.ViewCamera.Fov == player.FirstPersonFov && player.ViewCamera.GlobalPosition.DistanceTo(rig.EyeWorldPosition(-player.GlobalBasis.Z)!.Value) < .1f, "and ends at the eyes");
            Require(rig.CustomBodyActive ? rig.CustomBoneSizeForTesting("head") < .01f : !rig.StandardPartShownForTesting("Hips/Torso/Neck"), "with the head left out once the camera is there");
            Require(Mathf.Abs(Mathf.AngleDifference(yawBody, player.Rotation.Y)) < .1f, "and the avatar keeps facing where it faced");
            for (var i = 0; i < 5; i++) { Input.ParseInputEvent(Wheel(true)); await Frames(1); } Require(player.CameraModeName == "FirstPerson", "more scrolling in changes nothing");
            Input.ParseInputEvent(Wheel(false)); await Frames(2); Require(player.CameraModeName == "ThirdPerson", "scrolling out of first person goes back to third");
            await Seconds(1f); Require(rig.CustomBodyActive ? rig.CustomBoneSizeForTesting("head") > .3f : rig.StandardPartShownForTesting("Hips/Torso/Neck"), "and the head is back");
            Require(player.ViewCamera.GlobalPosition.DistanceTo(player.GetNode<Node3D>("CameraPivot").GlobalPosition) is > 1.3f and < 1.7f && Mathf.Abs(player.ViewCamera.Fov - third) < .1f, "at the nearest distance of third person, with its own field of view");
            PlayerController.WheelEntersFirstPerson = false; for (var i = 0; i < 10; i++) { Input.ParseInputEvent(Wheel(true)); await Frames(1); } await Seconds(.5f);
            Require(player.CameraModeName == "ThirdPerson", "and the wheel can be kept out of first person"); PlayerController.WheelEntersFirstPerson = true;

            GD.Print("event=first_person_camera_smoke result=passed");
            AvatarRig.ModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null;
            GD.PushError($"event=first_person_camera_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
