using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Tests;

/// <summary>Looking up and down in first person reaches the others as an angle (never the camera): the head of the avatar leans that far, smoothly, on any body and in any animation, and the player sends the angle only a few times a second.</summary>
public partial class HeadLookSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        try
        {
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);

            // ---- the avatars of the others lean their heads
            var root = new Node3D(); AddChild(root);
            var standard = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); standard.Position = new Vector3(-1, 0, 0); root.AddChild(standard);
            var custom = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); custom.Position = new Vector3(1, 0, 0); root.AddChild(custom);
            custom.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "C", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f });
            await Seconds(1.0f); Require(custom.CustomBodyActive, "a custom body is worn");

            Require(Mathf.Abs(standard.StandardNeckPitchForTesting) < .001f, "an avatar looks straight ahead by default");
            standard.SetLookPitch(.6f); await Seconds(.8f);
            Require(Mathf.Abs(standard.StandardNeckPitchForTesting - .6f) < .02f, $"the standard body leans its head up as far as it was told: {standard.StandardNeckPitchForTesting}");
            standard.SetLookPitch(-.5f); await Seconds(.8f); Require(Mathf.Abs(standard.StandardNeckPitchForTesting + .5f) < .02f, "and down");
            standard.SetLookPitch(2.5f); await Seconds(.8f); Require(Mathf.Abs(standard.StandardNeckPitchForTesting - HeadLook.MaxPitch) < .02f, $"but never farther than a head can: {standard.StandardNeckPitchForTesting}");
            standard.SetLookPitch(-9f); await Seconds(.8f); Require(standard.LookPitch == -HeadLook.MaxPitch, "also downward");
            standard.SetLookPitch(.4f); await Frames(2); Require(Mathf.Abs(standard.StandardNeckPitchForTesting - standard.LookPitch) > .1f, "the head turns smoothly, not at once");
            standard.SetMotionState(AvatarMotionState.Typing, true); await Seconds(.8f); Require(Mathf.Abs(standard.StandardNeckPitchForTesting - .4f) < .02f, "also while it is typing (the animation drives the rest of the body)");
            standard.SetLookPitch(0); await Seconds(.8f); Require(Mathf.Abs(standard.StandardNeckPitchForTesting) < .005f, "and it straightens again");
            standard.SetMotionState(AvatarMotionState.Idle, true);
            standard.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro" }); standard.SetLookPitch(.5f); await Seconds(.8f);
            standard.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro", SkinColor = "#CC9966" }); await Seconds(.4f);
            Require(Mathf.Abs(standard.StandardNeckPitchForTesting - .5f) < .03f, "a look applied again keeps the head where it is");

            var before = custom.CustomHeadWorldBasisForTesting!.Value;
            custom.SetLookPitch(.6f); await Seconds(.9f);
            var up = custom.CustomHeadWorldBasisForTesting!.Value * before.Inverse() * Vector3.Forward;
            Require(up.Y > .4f && Mathf.Abs(up.X) < .08f, $"the head of a custom body looks up: {up}");
            custom.SetLookPitch(-.6f); await Seconds(.9f);
            var down = custom.CustomHeadWorldBasisForTesting!.Value * before.Inverse() * Vector3.Forward;
            Require(down.Y < -.4f && Mathf.Abs(down.X) < .08f, $"and down: {down}");
            custom.SetMotionState(AvatarMotionState.Walk, true); await Seconds(.8f);
            var walking = custom.CustomHeadWorldBasisForTesting!.Value * before.Inverse() * Vector3.Forward; Require(walking.Y < -.2f, $"also in an animation: {walking}");
            custom.SetLookPitch(0); await Seconds(.9f); custom.SetMotionState(AvatarMotionState.Idle, true); await Seconds(.6f);
            var straight = custom.CustomHeadWorldBasisForTesting!.Value * before.Inverse() * Vector3.Forward; Require(Mathf.Abs(straight.Y) < .15f, $"and it comes back: {straight}");

            // ---- the player tells the island how far it looks, a few times a second, in first person only
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var sent = new List<(float Pitch, ulong At)>(); player.HeadLookSinkForTesting = pitch => sent.Add((pitch, Time.GetTicksMsec()));
            await Seconds(.8f);
            for (var i = 0; i < 10; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, 60), Position = new Vector2(500, 400) }); await Frames(1); }
            await Seconds(.5f); Require(sent.Count == 0, "third person sends nothing: the camera orbiting is not the head looking");
            player.SetCameraMode("FirstPerson"); await Seconds(.8f);
            for (var i = 0; i < 12; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, 40), Position = new Vector2(500, 400) }); await Frames(1); }
            await Seconds(.6f); sent.Clear();
            for (var i = 0; i < 30; i++) { Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(0, -12), Position = new Vector2(500, 400) }); await Seconds(.03f); }
            await Seconds(.4f);
            Require(sent.Count >= 3, $"looking around is sent: {sent.Count} times");
            var perSecond = sent.Count / Mathf.Max(.2f, (sent[^1].At - sent[0].At) / 1000f); Require(perSecond <= 14f, $"but only a few times a second: {perSecond}");
            Require(sent.All(s => LifeVerse.Shared.Networking.HeadLook.Check(s.Pitch) is null), "always an angle the island accepts");
            var looking = sent[^1].Pitch; sent.Clear(); await Seconds(2.4f); Require(sent.Count is >= 1 and <= 4, $"keeping still, it is only repeated now and then: {sent.Count}");
            sent.Clear(); player.SetCameraMode("ThirdPerson"); await Seconds(1.0f);
            Require(sent.Count >= 1 && sent[^1].Pitch == 0f, "stepping out of first person straightens the head for everybody"); sent.Clear(); await Seconds(1.2f); Require(sent.Count == 0, "and nothing more is sent");

            GD.Print("event=head_look_smoke result=passed");
            AvatarRig.ModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null;
            GD.PushError($"event=head_look_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
