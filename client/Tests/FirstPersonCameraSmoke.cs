using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Settings;
using LifeVerse.Shared.Avatar;

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
            player.SetCameraMode("FirstPerson"); await Frames(30);
            Require(Mathf.Abs(player.ViewCamera.Fov - 75f) < .01f, $"first person opens 75 degrees by default: {player.ViewCamera.Fov}");
            player.SetFirstPersonFov(120); Require(player.ViewCamera.Fov == 100f, "never wider than 100"); player.SetFirstPersonFov(20); Require(player.ViewCamera.Fov == 60f, "never narrower than 60"); player.SetFirstPersonFov(90); Require(player.ViewCamera.Fov == 90f, "and the setting is applied at once");
            player.SetCameraMode("ThirdPerson"); await Frames(10);
            Require(Mathf.Abs(player.ViewCamera.Fov - third) < .01f, $"third person keeps its own field of view: {player.ViewCamera.Fov} (was {third})");
            player.SetCameraMode("FirstPerson"); await Frames(10); Require(Mathf.Abs(player.ViewCamera.Fov - 90f) < .01f, "and first person remembers its own");
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

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
