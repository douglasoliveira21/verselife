using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>First person: the avatar stays drawn (its body, what it wears and the effects), and only the head, which the camera sits in, and what hangs on the head and the neck are left out.</summary>
public partial class FirstPersonSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static AttachmentData Data(string point, Guid asset) => new(Guid.NewGuid(), Guid.NewGuid(), asset, "Objeto", point, point, [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true);

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head"), .2f); AvatarRig.AttachmentModelProvider = _ => ModelCache.ParseInstance(glb);

            foreach (var custom in new[] { false, true })
            {
                var modelId = Guid.NewGuid(); var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); rig.OwnAvatar = true; root.AddChild(rig);
                if (custom) rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Corpo", CustomModelId = modelId, CustomModelHash = HashOf(body), CustomModelScale = 1f });
                await Frames(40); var name = custom ? "custom body" : "standard body";
                Require(rig.CustomBodyActive == custom, $"the {name} is the one shown");
                var hat = Data("head_top", Guid.NewGuid()); var ring = Data("hand_r", Guid.NewGuid()); var collar = Data("neck", Guid.NewGuid()); var belt = Data("pelvis", Guid.NewGuid()); var badge = Data("chest", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar, belt, badge]); await Frames(8);
                Require(rig.AttachmentsShown == 5 && new[] { hat, ring, collar, belt, badge }.All(a => rig.AttachmentShownForTesting(a.Id)), $"{name}: everything worn is shown in third person");
                if (custom) Require(rig.EyeHeight is > 1.2f and < 2.3f, $"{name}: the camera has a place at the eyes: {rig.EyeHeight}"); else Require(rig.EyeHeight is null, "the standard body keeps its camera");
                var handBefore = rig.CustomBonePositionForTesting("hand_l"); var elbowBefore = rig.CustomBonePositionForTesting("lower_arm_l"); var kneeBefore = rig.CustomBonePositionForTesting("lower_leg_l"); var thighBefore = rig.CustomBoneSizeForTesting("upper_leg_l");

                rig.SetFirstPersonView(true); await Frames(8);
                Require(rig.Visible, $"{name}: the avatar is still drawn in first person");
                if (custom)
                {
                    foreach (var gone in new[] { "head", "neck", "chest", "spine", "upper_arm_l", "upper_arm_r" }) Require(rig.CustomBoneSizeForTesting(gone) < .01f, $"{name}: the {gone} is not drawn");
                    foreach (var kept in new[] { "hips", "upper_leg_l", "lower_leg_r", "lower_arm_l", "hand_l", "hand_r" }) Require(rig.CustomBoneSizeForTesting(kept) > .3f, $"{name}: the {kept} is drawn");
                    Require(rig.CustomBonePositionForTesting("hand_l")!.Value.DistanceTo(handBefore!.Value) < .03f && rig.CustomBonePositionForTesting("lower_arm_l")!.Value.DistanceTo(elbowBefore!.Value) < .03f, $"{name}: the forearm and the hand stay exactly where they are");
                    Require(rig.CustomBonePositionForTesting("lower_leg_l")!.Value.DistanceTo(kneeBefore!.Value) < .03f, $"{name}: and so do the legs");
                }
                else
                {
                    Require(!rig.StandardHeadShownForTesting && !rig.StandardChestShownForTesting && !rig.StandardPartShownForTesting("Hips/Torso/LeftUpperArm"), $"{name}: the head, the chest and the upper arms are not drawn");
                    Require(rig.StandardPartShownForTesting("Hips/Torso/LeftUpperArm/LeftLowerArm") && rig.StandardPartShownForTesting("Hips/Torso/RightUpperArm/RightLowerArm") && rig.StandardPartShownForTesting("Hips") && rig.StandardPartShownForTesting("Hips/LeftUpperLeg"), $"{name}: the forearms, the hips and the legs are");
                }
                Require(rig.AttachmentShownForTesting(ring.Id) && rig.AttachmentShownForTesting(belt.Id), $"{name}: what is worn on the hand and on the waist is seen");
                Require(!rig.AttachmentShownForTesting(hat.Id) && !rig.AttachmentShownForTesting(collar.Id) && !rig.AttachmentShownForTesting(badge.Id), $"{name}: what is worn on the head, the neck and the chest is out of the way");

                var later = Data("thigh_l", Guid.NewGuid()); var onHead = Data("skull", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar, later, onHead]); await Frames(8);
                Require(rig.AttachmentShownForTesting(later.Id) && !rig.AttachmentShownForTesting(onHead.Id), $"{name}: what is put on while in first person follows the same rule");
                rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = custom ? "Corpo" : "Teste", CustomModelId = custom ? modelId : null, CustomModelHash = custom ? HashOf(body) : null, CustomModelScale = 1f }); await Frames(40);
                if (custom) Require(rig.CustomBoneSizeForTesting("head") < .01f && rig.CustomBoneSizeForTesting("hand_l") > .3f, $"{name}: a look applied again keeps the same rule"); else Require(!rig.StandardHeadShownForTesting && !rig.StandardChestShownForTesting, $"{name}: a look applied again keeps the same rule");

                rig.SetFirstPersonView(false); await Frames(8);
                if (custom) Require(rig.CustomBoneSizeForTesting("head") > .3f && rig.CustomBoneSizeForTesting("spine") > .3f && rig.CustomBoneSizeForTesting("upper_arm_l") > .3f, $"{name}: back to third person the whole body returns");
                else Require(rig.StandardHeadShownForTesting && rig.StandardChestShownForTesting && rig.StandardPartShownForTesting("Hips/Torso/LeftUpperArm"), $"{name}: back to third person the whole body returns");
                Require(rig.AttachmentShownForTesting(hat.Id) && rig.AttachmentShownForTesting(collar.Id), $"{name}: and so do the objects of the head");
                rig.QueueFree(); await Frames(2);
            }

            GD.Print("event=first_person_smoke result=passed");
            AvatarRig.ModelProvider = null; AvatarRig.AttachmentModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null; AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=first_person_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
