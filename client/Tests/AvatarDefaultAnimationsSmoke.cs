using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>The animations the game carries for custom bodies (the Mixamo clips of idle, walk, run, jump, fall and flying): they load from the resource, move a custom body where the person chose nothing, give way to anything the person chose, and leave the other states and the procedural body alone.</summary>
public partial class AvatarDefaultAnimationsSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AvatarAppearanceData Custom(byte[] glb, Dictionary<string, string>? overrides = null) => new()
    {
        DisplayName = "Custom", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(glb), CustomModelScale = 1.8f / 1.78f, AnimationOverridesJson = System.Text.Json.JsonSerializer.Serialize(overrides ?? [])
    };

    private static Skeleton3D? Skeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (Skeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }

    public override async void _Ready()
    {
        try
        {
            DefaultAvatarAnimations.BytesForTesting = null; DefaultAvatarAnimations.Reset();
            Require(DefaultAvatarAnimations.Count == 12, $"the file carries the twelve clips the game uses (got {DefaultAvatarAnimations.Count})");
            foreach (var state in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "FlyIdle", "FlyMove", "WalkBack", "FlatImpact", "FallDown", "Swim", "SwimIdle" }) Require(DefaultAvatarAnimations.For(state) is { Duration: > .2f }, $"there is a clip for {state}");
            foreach (var state in new[] { "Sit", "Dance", "Wave", "Landing", "StandUp" }) Require(DefaultAvatarAnimations.For(state) is null, $"{state} keeps the game's own animation");

            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig); await Frames(5);
            Require(!rig.DefaultClipActiveForTesting, "the procedural body does not play the clips");
            rig.SetMotionState(AvatarMotionState.FlatImpact, true); await Frames(3); Require(rig.CurrentAnimationNode == "Landing" && !rig.CanFlatImpact, "the procedural doll has no flat landing: its own landing plays"); rig.SetMotionState(AvatarMotionState.SwimIdle, true); await Frames(30); Require(rig.CurrentAnimationNode == "Swim", "the procedural doll treads water with its swim"); rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(30); rig.SetMotionState(AvatarMotionState.WalkBack, true); await Frames(30); Require(rig.CurrentAnimationNode == "Walk" && !rig.DefaultClipActiveForTesting, $"the procedural doll walks back with its walk ({rig.CurrentAnimationNode})"); rig.SetMotionState(AvatarMotionState.Idle, true);

            // the sound of walking: it plays on the ground while walking, running and walking backwards, faster when running, and fades out when the avatar stops
            Require(AvatarRig.FootstepStreamForTesting is null || AvatarRig.FootstepStreamForTesting.GetLength() > 1, "the footstep recording is there");
            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(5); Require(!rig.FootstepsPlayingForTesting, "standing still makes no steps");
            rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(5); Require(rig.FootstepsPlayingForTesting && Math.Abs(rig.FootstepsPitchForTesting - 1f) < .01f, "walking plays the steps at their own pace");
            Require(AvatarRig.FootstepStreamForTesting is { } steps && steps.GetLength() > 2, $"the recording loaded ({AvatarRig.FootstepStreamForTesting?.GetLength():F1} s)");
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(3); Require(rig.FootstepsPlayingForTesting && rig.FootstepsPitchForTesting > 1.3f, "running plays them faster");
            rig.SetMotionState(AvatarMotionState.WalkBack, true); await Frames(3); Require(rig.FootstepsPlayingForTesting && rig.FootstepsPitchForTesting < 1f, "walking backwards, a little slower");
            foreach (var quiet in new[] { AvatarMotionState.Jump, AvatarMotionState.Fall, AvatarMotionState.FlyMove, AvatarMotionState.Swim, AvatarMotionState.Sit })
            { rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); rig.SetMotionState(quiet, true); await Frames(2); Require(!rig.FootstepsPlayingForTesting, $"{quiet} is not walking: no steps"); }
            rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); rig.SetMotionState(AvatarMotionState.Idle, true); await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout); await Frames(3);
            Require(!rig.FootstepsPlayingForTesting && rig.GetNode<AudioStreamPlayer3D>("Footsteps") is { Playing: false }, "after stopping the sound fades out and ends");
            AvatarRig.FootstepsEnabled = false; rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); Require(!rig.FootstepsPlayingForTesting, "it can be turned off"); AvatarRig.FootstepsEnabled = true; rig.SetMotionState(AvatarMotionState.Idle, true);
            var glb = HumanoidTemplate.Build(new TemplateOptions { Naming = BoneNaming.Mixamo });
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(glb);
            rig.ApplyAppearance(Custom(glb)); await Frames(8);
            Require(rig.CustomBodyActive && rig.CustomBodyStatus == "ready", $"the custom body is on: {rig.CustomBodyStatus}");
            var skeleton = Skeleton(rig.CustomBodyNode!)!; var legLeft = BoneIndex(skeleton, "upper_leg_l"); var legRight = BoneIndex(skeleton, "upper_leg_r");
            var restLeft = skeleton.GetBoneRest(legLeft).Basis.GetRotationQuaternion(); var restRight = skeleton.GetBoneRest(legRight).Basis.GetRotationQuaternion();

            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(4); Require(rig.DefaultClipActiveForTesting, "an idle custom body plays the game's idle clip");
            rig.SetMotionState(AvatarMotionState.Run, true);
            var moved = 0f; var opposite = false;
            for (var frame = 0; frame < 45; frame++)
            {
                await Frames(1); Require(rig.DefaultClipActiveForTesting, "running plays the game's run clip");
                var left = skeleton.GetBonePoseRotation(legLeft); var right = skeleton.GetBonePoseRotation(legRight);
                moved = Mathf.Max(moved, restLeft.AngleTo(left));
                if (restLeft.AngleTo(left) > .3f && restRight.AngleTo(right) > .3f && left.AngleTo(right) > .3f) opposite = true;
            }
            Require(moved > .4f && opposite, $"the run clip swings the legs against each other: {moved:F2}");
            foreach (var state in new[] { AvatarMotionState.Walk, AvatarMotionState.WalkBack, AvatarMotionState.Jump, AvatarMotionState.FlyIdle, AvatarMotionState.FlyMove }) { rig.SetMotionState(state, true); await Frames(3); Require(rig.DefaultClipActiveForTesting, $"{state} plays its clip"); }
            foreach (var state in new[] { AvatarMotionState.Sit, AvatarMotionState.Dance, AvatarMotionState.Wave }) { rig.SetMotionState(state, true); await Frames(3); Require(!rig.DefaultClipActiveForTesting, $"{state} has no default clip: the game's own animation plays"); }

            // the long falling clip is for falling after flight ends in the air; a fall after a jump or off a ledge keeps the game's own animation
            rig.SetMotionState(AvatarMotionState.Jump, true); await Frames(2); rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(3);
            Require(!rig.FallFromFlightForTesting && rig.DefaultClipNameForTesting == "JumpDown", $"coming down after a jump plays the jumping-down clip, not the flight fall ({rig.DefaultClipNameForTesting})");
            rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(2); rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(3);
            Require(!rig.FallFromFlightForTesting && rig.DefaultClipNameForTesting == "JumpDown", "walking off a ledge comes down the same way");
            rig.SetMotionState(AvatarMotionState.FlyMove, true); await Frames(2); rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(3);
            Require(rig.FallFromFlightForTesting && rig.DefaultClipNameForTesting == "Fall", $"falling after the flight ended in the air plays the long falling clip ({rig.DefaultClipNameForTesting})");
            rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(2); Require(rig.DefaultClipNameForTesting == "Fall", "and it goes on while the fall goes on");
            rig.SetMotionState(AvatarMotionState.Landing, true); await Frames(2); rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(2);
            Require(!rig.FallFromFlightForTesting && rig.DefaultClipNameForTesting == "JumpDown", "a later fall that did not start from flight is an ordinary fall again");
            rig.SetMotionState(AvatarMotionState.FlyIdle, true); await Frames(2); rig.SetMotionState(AvatarMotionState.Fall, true); await Frames(2); Require(rig.DefaultClipNameForTesting == "Fall", "hovering and then letting go is a flight fall too");

            // hitting the ground after a fall from the sky: the flat landing, then a slow getting up
            Require(rig.CanFlatImpact && rig.FlatImpactSeconds is > 1.4f and < 1.8f, $"the custom body can show the flat landing ({rig.FlatImpactSeconds:F2} s)");
            rig.SetMotionState(AvatarMotionState.FlatImpact, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "FallFlat", $"the flat landing plays its clip ({rig.DefaultClipNameForTesting})");
            await ToSignal(GetTree().CreateTimer(1.9), SceneTreeTimer.SignalName.Timeout); await Frames(2);
            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(1); Require(rig.BlendingForTesting, "getting up blends");
            await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout); await Frames(2); Require(rig.BlendingForTesting && rig.BlendProgressForTesting < 1, "getting up takes longer than an ordinary change of state (still on its way after 0.4 s)");
            await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout); await Frames(2); Require(!rig.BlendingForTesting, "and ends");

            // a change of state is a short crossfade, not a cut: right after the change the legs are still where they were
            rig.SetMotionState(AvatarMotionState.Idle, true); await ToSignal(GetTree().CreateTimer(.6), SceneTreeTimer.SignalName.Timeout); await Frames(2);
            var before = skeleton.GetBonePoseRotation(legLeft); rig.SetMotionState(AvatarMotionState.Run, true); await Frames(1);
            var after = skeleton.GetBonePoseRotation(legLeft);
            Require(rig.BlendingForTesting && before.AngleTo(after) < .25f, $"just after the change the pose is still near the old one ({before.AngleTo(after):F2} rad) and a crossfade runs");
            await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout); await Frames(2);
            Require(!rig.BlendingForTesting, "the crossfade ends");
            var arrived = 0f; for (var i = 0; i < 40; i++) { await Frames(1); arrived = Mathf.Max(arrived, restLeft.AngleTo(skeleton.GetBonePoseRotation(legLeft))); }
            Require(arrived > .4f, $"and the new animation takes over fully ({arrived:F2})");
            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(1); Require(rig.BlendingForTesting, "back to idle blends too (clip to clip)");
            rig.SetMotionState(AvatarMotionState.Sit, true); await Frames(1); Require(rig.BlendingForTesting, "from a clip to the game's own animation blends as well");

            // in the water: swimming, and treading water when it goes nowhere; flying and standing still in the air has its own clip too
            rig.SetMotionState(AvatarMotionState.Swim, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "Swim", $"swimming plays the swimming clip ({rig.DefaultClipNameForTesting})");
            rig.SetMotionState(AvatarMotionState.SwimIdle, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "SwimIdle", $"treading water plays its clip ({rig.DefaultClipNameForTesting})");
            rig.SetMotionState(AvatarMotionState.FlyIdle, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "FlyIdle", $"hovering plays the floating clip ({rig.DefaultClipNameForTesting})");
            rig.SetMotionState(AvatarMotionState.FlyMove, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "FlyMove", "flying somewhere keeps the flying clip");
            rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); Require(rig.DefaultClipNameForTesting == "Walk", "and walking its own");

            // what the person chose wins: a library alternative for Walk, so the default does not play there
            rig.ApplyAppearance(Custom(glb, new() { ["Walk"] = "builtin:happy" })); await Frames(8);
            rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); Require(!rig.DefaultClipActiveForTesting, "an alternative the person chose replaces the default clip");
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(3); Require(rig.DefaultClipActiveForTesting, "the other states still play the default clips");
            rig.ApplyAppearance(Custom(glb)); await Frames(8); rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(3); Require(rig.DefaultClipActiveForTesting, "back to all default: the walk clip plays again");

            // the clips do not load: the game's own animations carry on
            DefaultAvatarAnimations.BytesForTesting = new byte[] { 0 }; DefaultAvatarAnimations.Reset();
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(3); Require(!rig.DefaultClipActiveForTesting, "without the file nothing breaks: the game's own run plays");
            DefaultAvatarAnimations.BytesForTesting = null; DefaultAvatarAnimations.Reset(); AvatarRig.ModelProvider = null;
            GD.Print("event=avatar_default_animations_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_default_animations_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
