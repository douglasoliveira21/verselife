using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>A custom GLB body on the client: it is imported, scaled to 1.8 m, stands on the ground, faces the right way and follows the game's animations.</summary>
public partial class AvatarCustomSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AvatarAppearanceData Custom(byte[] glb, float scale) => new()
    {
        DisplayName = "Custom", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(glb), CustomModelScale = scale,
        EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }]
    };

    private static (float Low, float High) Extent(Node3D node)
    {
        var low = float.MaxValue; var high = float.MinValue;
        void Visit(Node current)
        {
            if (current is MeshInstance3D { Mesh: not null } mesh)
            {
                var box = mesh.Mesh.GetAabb();
                for (var corner = 0; corner < 8; corner++)
                {
                    var y = (mesh.GlobalTransform * (box.Position + new Vector3((corner & 1) * box.Size.X, ((corner >> 1) & 1) * box.Size.Y, ((corner >> 2) & 1) * box.Size.Z))).Y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
            }
            foreach (var child in current.GetChildren()) Visit(child);
        }
        Visit(node); return (low, high);
    }

    private static Skeleton3D? Skeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (Skeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(5);

            foreach (var naming in new[] { BoneNaming.Blender, BoneNaming.Mixamo, BoneNaming.Standard })
            {
                var glb = HumanoidTemplate.Build(new TemplateOptions { Naming = naming });
                AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(glb);
                rig.ApplyAppearance(Custom(glb, 1.8f / 1.78f));
                await Frames(8);
                Require(rig.CustomBodyActive && rig.CustomBodyStatus == "ready", $"{naming}: the model is on the avatar: {rig.CustomBodyStatus}");
                var bones = rig.RetargetedBones;
                foreach (var bone in new[] { "hips", "spine", "upper_arm_l", "lower_arm_r", "upper_leg_l", "lower_leg_r" }) Require(bones.Contains(bone), $"{naming}: {bone} follows the animations");
                var custom = rig.CustomBodyNode!;
                var (low, high) = Extent(custom);
                Require(low is > -.03f and < .03f && high is > 1.75f and < 1.85f, $"{naming}: the model is 1.8 m tall and stands on the ground: {low:F3}..{high:F3}");
                var skeleton = Skeleton(custom)!;
                var leftHand = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(BoneIndex(skeleton, "hand_l")); var rightHand = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(BoneIndex(skeleton, "hand_r"));
                var shoulderLeft = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(BoneIndex(skeleton, "upper_arm_l"));
                Require(leftHand.Origin.X < -.1f && rightHand.Origin.X > .1f && shoulderLeft.Origin.X < -.1f, $"{naming}: the model faces -Z like the game: left hand at x={leftHand.Origin.X:F2}");
                Require(leftHand.Origin.Y < shoulderLeft.Origin.Y - .5f, $"{naming}: the arms hang down instead of staying in a T-pose: hand {leftHand.Origin.Y:F2}, shoulder {shoulderLeft.Origin.Y:F2}");
                var head = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(BoneIndex(skeleton, "head"));
                Require(head.Origin.Y > 1.4f, "the head is up");

                // the animations reach the bones
                var legLeft = BoneIndex(skeleton, "upper_leg_l"); var legRight = BoneIndex(skeleton, "upper_leg_r");
                var restLeft = skeleton.GetBoneRest(legLeft).Basis.GetRotationQuaternion(); var restRight = skeleton.GetBoneRest(legRight).Basis.GetRotationQuaternion();
                await Frames(3);
                var idleAngle = restLeft.AngleTo(skeleton.GetBonePoseRotation(legLeft));
                rig.SetMotionState(AvatarMotionState.Run, true);
                var moved = 0f; var opposite = false;
                for (var frame = 0; frame < 40; frame++)
                {
                    await Frames(1);
                    var left = skeleton.GetBonePoseRotation(legLeft); var right = skeleton.GetBonePoseRotation(legRight);
                    moved = Mathf.Max(moved, restLeft.AngleTo(left));
                    if (restLeft.AngleTo(left) > .3f && restRight.AngleTo(right) > .3f && left.AngleTo(right) > .3f) opposite = true;
                }
                Require(idleAngle < .15f && moved > .4f, $"{naming}: running swings the upper leg: idle {idleAngle:F2}, moved {moved:F2}");
                Require(opposite, $"{naming}: the two legs swing against each other");
                rig.SetMotionState(AvatarMotionState.Idle, true);
            }

            // ---- a file that does not match the approved hash, and a download that fails
            var good = HumanoidTemplate.Build();
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(HumanoidTemplate.Build(new TemplateOptions { Scale = 2 }));
            rig.ApplyAppearance(Custom(good, 1f)); await Frames(6);
            Require(!rig.CustomBodyActive && rig.CustomBodyStatus.Contains("confere"), $"a file that is not the approved one is not used: {rig.CustomBodyStatus}");
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(null);
            rig.ApplyAppearance(Custom(good, 1f)); await Frames(6);
            Require(!rig.CustomBodyActive && rig.CustomBodyStatus.Contains("baixar"), $"a failed download is said: {rig.CustomBodyStatus}");
            var junk = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(junk);
            rig.ApplyAppearance(Custom(junk, 1f)); await Frames(6);
            Require(!rig.CustomBodyActive && rig.CustomBodyStatus.Length > 0 && rig.CustomBodyStatus != "ready", $"a file the engine cannot read is not used: {rig.CustomBodyStatus}");
            Require(rig.FindChild("Torso", true, false) is Node3D torso && Extent(torso).High > 1.0f, "the procedural body is still there");

            // ---- going back to the procedural body, and a change while the file is still coming
            AvatarRig.ModelProvider = async (_, _) => { await Task.Delay(150); return good; };
            rig.ApplyAppearance(Custom(good, 1.8f / 1.78f));
            rig.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Padrão", EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }] });
            await Task.Delay(400); await Frames(5);
            Require(!rig.CustomBodyActive && rig.CustomBodyStatus.Length == 0, "an old download that finishes late is ignored");
            AvatarRig.ModelProvider = null;
            GD.Print("event=avatar_custom_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_custom_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
