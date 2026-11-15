using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>The body sliders (Altura, Ombros, Tronco, Braços, Pernas, Cabeça) change a custom body too, not only the procedural one.</summary>
public partial class AvatarCustomShapeSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AvatarAppearanceData Custom(byte[] glb, Dictionary<string, float> body) => new()
    {
        DisplayName = "Custom", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(glb), CustomModelScale = 1.8f / 1.78f,
        BodySettingsJson = AvatarRules.WriteSettings(body, AvatarRules.BodyRanges)
    };

    private static Skeleton3D? Skeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (Skeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }
    private static Vector3 Where(Skeleton3D skeleton, string standard) => skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(BoneIndex(skeleton, standard)).Origin;

    private async Task<Skeleton3D> Put(AvatarRig rig, byte[] glb, Dictionary<string, float> body)
    {
        AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(glb);
        rig.ApplyAppearance(Custom(glb, body)); await Frames(10);
        Require(rig.CustomBodyActive && rig.CustomBodyStatus == "ready", $"the model is on the avatar: {rig.CustomBodyStatus}");
        return Skeleton(rig.CustomBodyNode!) ?? throw new InvalidOperationException("no skeleton");
    }

    public override async void _Ready()
    {
        try
        {
            DefaultAvatarAnimations.BytesForTesting = new byte[] { 0 }; DefaultAvatarAnimations.Reset();
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(5);
            foreach (var naming in new[] { BoneNaming.Blender, BoneNaming.Mixamo, BoneNaming.Standard })
            {
                var glb = HumanoidTemplate.Build(new TemplateOptions { Naming = naming });
                var flat = await Put(rig, glb, new() { ["legs"] = 1f });
                float Leg(Skeleton3D s) => (Where(s, "hips") - Where(s, "foot_l")).Length();
                float Arm(Skeleton3D s) => (Where(s, "upper_arm_l") - Where(s, "hand_l")).Length();
                float baseLeg = Leg(flat), baseArm = Arm(flat), baseHead = Where(flat, "head").Y, baseFoot = Where(flat, "foot_l").Y;
                var baseSpan = (Where(flat, "upper_arm_l") - Where(flat, "upper_arm_r")).Length();

                var longLegs = await Put(rig, glb, new() { ["legs"] = 1.15f });
                Require(Leg(longLegs) > baseLeg * 1.1f, $"{naming}: longer legs make the legs longer ({Leg(longLegs):0.000} vs {baseLeg:0.000})");
                Require(Mathf.Abs(Where(longLegs, "foot_l").Y - baseFoot) < .04f, $"{naming}: and the feet stay on the ground ({Where(longLegs, "foot_l").Y:0.000} vs {baseFoot:0.000})");
                Require(Where(longLegs, "head").Y > baseHead + .08f, $"{naming}: so the avatar is taller");
                var shortLegs = await Put(rig, glb, new() { ["legs"] = .85f });
                Require(Leg(shortLegs) < baseLeg * .92f && Mathf.Abs(Where(shortLegs, "foot_l").Y - baseFoot) < .04f, $"{naming}: shorter legs are shorter, feet on the ground");

                Require(Arm(await Put(rig, glb, new() { ["arms"] = 1.15f })) > baseArm * 1.1f, $"{naming}: longer arms");
                var tall = await Put(rig, glb, new() { ["height"] = 1.15f });
                Require(Where(tall, "head").Y > baseHead * 1.1f && Mathf.Abs(Where(tall, "foot_l").Y - baseFoot) < .04f, $"{naming}: taller, feet on the ground ({Where(tall, "head").Y:0.00} vs {baseHead:0.00})");
                var wide = await Put(rig, glb, new() { ["shoulder"] = 1.1f });
                Require((Where(wide, "upper_arm_l") - Where(wide, "upper_arm_r")).Length() > baseSpan * 1.07f, $"{naming}: wider shoulders");
                var bigHead = await Put(rig, glb, new() { ["head"] = 1.15f });
                Require(bigHead.GetBonePoseScale(BoneIndex(bigHead, "head")).X > 1.14f, $"{naming}: a bigger head");
                var thick = await Put(rig, glb, new() { ["torso"] = 1.1f });
                var spineScale = thick.GetBonePoseScale(BoneIndex(thick, "spine")); Require(Mathf.Max(spineScale.X, Mathf.Max(spineScale.Y, spineScale.Z)) > 1.09f, $"{naming}: a wider torso");
                var reset = await Put(rig, glb, new() { ["legs"] = 1f });
                Require(Mathf.Abs(Leg(reset) - baseLeg) < .01f, $"{naming}: back to normal when the sliders go back");
            }
            AvatarRig.ModelProvider = null; DefaultAvatarAnimations.BytesForTesting = null; DefaultAvatarAnimations.Reset();
            GD.Print("event=avatar_custom_shape_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=avatar_custom_shape_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
