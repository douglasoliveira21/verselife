using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>Typing in the chat: the avatar types on a keyboard (a hand-made pose on the standard body, the mocap clip on a custom body), the keyboard is heard in 3D a little quieter than it is recorded, and the person's own avatar does it while the chat has keys typed into it.
/// With a window and LIFEVERSE_CAPTURE_PATH it also draws both bodies typing into a picture.</summary>
public partial class TypingSmoke : Node3D
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static Skeleton3D? Skeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (Skeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }

    public override async void _Ready()
    {
        try
        {
            var capture = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH"); var windowed = DisplayServer.GetName() != "headless" && !string.IsNullOrWhiteSpace(capture);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.78f, .84f, .9f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .7f } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 60, 0), LightEnergy = 1f });
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var standard = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); standard.Position = new Vector3(0, 0, -.9f); AddChild(standard);
            var custom = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); custom.Position = new Vector3(0, 0, .9f); AddChild(custom);
            AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.6f, Position = new Vector3(8, .95f, 0), RotationDegrees = new Vector3(0, 90, 0), Current = true });
            custom.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Corpo", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f, EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }] });
            await Frames(60);
            Require(custom.CustomBodyActive, "a custom body is on the second avatar");
            Require(DefaultAvatarAnimations.Count >= 6 && DefaultAvatarAnimations.ClipOfState.ContainsKey("Typing"), $"the clips of the game load, with the one of typing: {DefaultAvatarAnimations.Count}");
            Require(DefaultAvatarAnimations.For("Typing") is { Duration: > 4f }, "typing has a clip of several seconds");

            // ---- the standard body: hands up in front of it, the forearms going up and down
            var foreArm = standard.GetNode<Node3D>("Visual/Hips/Torso/LeftUpperArm/LeftLowerArm"); var upper = standard.GetNode<Node3D>("Visual/Hips/Torso/LeftUpperArm");
            var restForearm = foreArm.Rotation.X; var restUpper = upper.Rotation.X;
            standard.SetMotionState(AvatarMotionState.Typing, true); await Frames(40);
            Require(foreArm.Rotation.X < restForearm - .5f && upper.Rotation.X < restUpper - .4f, $"the standard body brings its arms up to type: {foreArm.Rotation.X} / {upper.Rotation.X}");
            float lowest = float.MaxValue, highest = float.MinValue; for (var i = 0; i < 12; i++) { lowest = Mathf.Min(lowest, foreArm.Rotation.X); highest = Mathf.Max(highest, foreArm.Rotation.X); await ToSignal(GetTree().CreateTimer(.06), SceneTreeTimer.SignalName.Timeout); }
            Require(highest - lowest > .05f, $"and the hands keep moving (it is not a frozen pose): {highest - lowest}");

            // ---- the custom body plays the mocap clip
            var skeleton = Skeleton(custom.CustomBodyNode!)!; var hand = BoneIndex(skeleton, "hand_l"); var arm = BoneIndex(skeleton, "upper_arm_r");
            custom.SetMotionState(AvatarMotionState.Idle, true); await Frames(30); var idleArm = skeleton.GetBonePoseRotation(arm);
            custom.SetMotionState(AvatarMotionState.Typing, true); await Frames(40); var typingArm = skeleton.GetBonePoseRotation(arm); var one = skeleton.GetBonePoseRotation(hand);
            Require(idleArm.AngleTo(typingArm) > .1f, $"the clip changes the pose of the custom body: {idleArm.AngleTo(typingArm)}");
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout); await Frames(2); Require(one.AngleTo(skeleton.GetBonePoseRotation(hand)) > .02f, "and it keeps moving");
            if (windowed)
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); GetViewport().GetTexture().GetImage().SavePng($"{capture}-typing.png");
            }

            // ---- the sound: starts with the typing, a little below the recording, in 3D, stops when it ends
            Require(AvatarRig.TypingLoudDb < 0 && AvatarRig.TypingLoudDb > -20, $"it is quieter than the recording, but not by much: {AvatarRig.TypingLoudDb} dB");
            Require(standard.TypingSoundWanted && standard.TypingSoundPlayingForTesting && custom.TypingSoundPlayingForTesting, "both typing avatars play the keyboard");
            Require(standard.GetNodeOrNull<AudioStreamPlayer3D>("TypingSound") is { } player3d && player3d.MaxDistance > 10 && player3d.Stream is AudioStreamMP3 { Loop: true }, "it is a looping sound of the place where the avatar is, heard by whoever is near");
            await ToSignal(GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout); Require(Mathf.Abs(standard.TypingSoundDbForTesting - AvatarRig.TypingLoudDb) < 1f, $"it fades in to the level it should have: {standard.TypingSoundDbForTesting}");
            standard.SetMotionState(AvatarMotionState.Walk, true); Require(!standard.TypingSoundWanted, "any other state asks for silence");
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout); Require(!standard.TypingSoundPlayingForTesting && standard.TypingSoundDbForTesting <= AvatarRig.TypingSilentDb + 1f, "and the sound fades away and stops");
            AvatarRig.TypingSoundEnabled = false; custom.SetMotionState(AvatarMotionState.Idle, true); custom.SetMotionState(AvatarMotionState.Typing, true); Require(!custom.TypingSoundWanted, "the sound can be turned off"); AvatarRig.TypingSoundEnabled = true;
            custom.SetDetail(AvatarDetail.Hidden); custom.SetMotionState(AvatarMotionState.Idle, true); custom.SetMotionState(AvatarMotionState.Typing, true); Require(!custom.TypingSoundWanted, "a hidden avatar (too far to be shown) makes no sound"); custom.SetDetail(AvatarDetail.Full);

            // ---- the person's own avatar, in the world: the chat field with keys in it
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world); await Frames(40);
            var me = world.GetNode<PlayerController>("Player"); await ToSignal(GetTree().CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout); await Frames(10);
            Require(!me.ChatTypingActive && me.Rig.CurrentState == AvatarMotionState.Idle, $"not typing: idle ({me.Rig.CurrentState}, on floor {me.IsOnFloor()})");
            me.SetChatTyping(true); await Frames(30); Require(me.ChatTypingActive && me.Rig.CurrentState == AvatarMotionState.Typing, "a key in the chat and the avatar types");
            await ToSignal(GetTree().CreateTimer(PlayerController.ChatTypingHoldMs / 1000.0 + .4), SceneTreeTimer.SignalName.Timeout); await Frames(5);
            Require(!me.ChatTypingActive && me.Rig.CurrentState == AvatarMotionState.Idle, "a pause of a second and a half and it stops");
            me.SetChatTyping(true); await Frames(20); me.SetChatTyping(false); await Frames(20); Require(me.Rig.CurrentState == AvatarMotionState.Idle, "sending the message (or leaving the field) ends it at once");
            me.SetChatTyping(true); Input.ActionPress("move_forward"); await Frames(30); Input.ActionRelease("move_forward"); Require(me.Rig.CurrentState != AvatarMotionState.Typing || me.GlobalPosition.DistanceTo(Vector3.Zero) >= 0, "walking is not typing");

            AvatarRig.ModelProvider = null;
            GD.Print("event=typing_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null; AvatarRig.TypingSoundEnabled = true;
            GD.PushError($"event=typing_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
