using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>What first person shows of the arms in each state of the avatar: nothing is moved for the camera, the hands are where the animation puts them, and they are in view only when that is natural (relaxed at the sides when standing, in front when typing or waving).</summary>
public partial class FirstPersonHandsSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        try
        {
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var root = new Node3D(); AddChild(root);
            var camera = new Camera3D { Fov = 75, Near = .06f, Current = true }; root.AddChild(camera);
            foreach (var custom in new[] { false, true })
            {
                var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); rig.OwnAvatar = true; root.AddChild(rig);
                if (custom) rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "C", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f });
                await Frames(40); rig.SetFirstPersonView(true); await Frames(10);
                var name = custom ? "custom body" : "standard body";
                foreach (var state in new[] { AvatarMotionState.Idle, AvatarMotionState.Walk, AvatarMotionState.Run, AvatarMotionState.Jump, AvatarMotionState.Fall, AvatarMotionState.FlyIdle, AvatarMotionState.FlyMove, AvatarMotionState.Sit, AvatarMotionState.SitFloor, AvatarMotionState.Typing, AvatarMotionState.Wave, AvatarMotionState.Point, AvatarMotionState.Applaud, AvatarMotionState.Greet, AvatarMotionState.Laugh, AvatarMotionState.Dance, AvatarMotionState.Swim })
                {
                    rig.SetMotionState(state, true); await Seconds(.7f);
                    var eyes = rig.EyeWorldPosition(Vector3.Forward)!.Value; var hands = new[] { Hand(rig, custom, "hand_l"), Hand(rig, custom, "hand_r") };
                    bool Seen(float pitch, Vector3 at) { camera.GlobalPosition = eyes; camera.GlobalRotation = new Vector3(pitch, 0, 0); return camera.IsPositionInFrustum(at) && !camera.IsPositionBehind(at); }
                    foreach (var hand in hands) Require(eyes.DistanceTo(hand) > .25f, $"{name} {state}: a hand never ends up inside the camera ({eyes.DistanceTo(hand):0.00} m)");
                    var straight = hands.Any(h => Seen(0f, h)); var down = hands.Any(h => Seen(-1.0f, h)); var feet = hands.Count(h => Seen(-1.35f, h));
                    GD.Print($"{name} {state,-9} ahead:{(straight ? "VIEW" : "out ")} slightly down:{(down ? "VIEW" : "out ")} at the feet:{feet}");
                    if (state is AvatarMotionState.Idle or AvatarMotionState.Walk or AvatarMotionState.Laugh or AvatarMotionState.Sit or AvatarMotionState.SitFloor) Require(!straight, $"{name} {state}: the arms are relaxed at the sides, so the hands are not in front of the eyes");
                    if (state == AvatarMotionState.Idle) Require(feet >= 1, $"{name}: looking at the feet shows the hands at the sides");
                    if (state == AvatarMotionState.Typing)
                    {
                        Require(down, $"{name}: typing, the hands are in view when looking a little down");
                        Require(hands.Any(h => (h - eyes).Dot(Vector3.Forward) > .02f), $"{name}: and they are in front of the person");
                    }
                }
                rig.QueueFree(); await Frames(2);
            }
            GD.Print("event=first_person_hands_smoke result=passed"); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=first_person_hands_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private static Vector3 Hand(AvatarRig rig, bool custom, string bone)
    {
        if (custom) return rig.CustomBonePositionForTesting(bone)!.Value;
        var path = bone == "hand_l" ? "Hips/Torso/LeftUpperArm/LeftLowerArm" : "Hips/Torso/RightUpperArm/RightLowerArm";
        var node = rig.GetNode<Node3D>("Visual/" + path); return node.GlobalPosition + node.GlobalBasis * new Vector3(0, -.22f, 0);
    }

    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
}
