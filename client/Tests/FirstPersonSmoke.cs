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
                var hat = Data("head_top", Guid.NewGuid()); var ring = Data("hand_r", Guid.NewGuid()); var collar = Data("neck", Guid.NewGuid()); var belt = Data("pelvis", Guid.NewGuid()); var badge = Data("chest", Guid.NewGuid()); var sleeve = Data("lower_arm_l", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar, belt, badge, sleeve]); await Frames(8);
                Require(rig.AttachmentsShown == 6 && new[] { hat, ring, collar, belt, badge, sleeve }.All(a => rig.AttachmentShownForTesting(a.Id)), $"{name}: everything worn is shown in third person");
                var eyesBefore = rig.EyeWorldPosition(Vector3.Forward); Require(eyesBefore is { } e0 && e0.Y > 1.3f && e0.Y < 2.2f, $"{name}: the eyes have a place at about the height of the head: {eyesBefore}");
                Require(rig.EyeHeight is > 1.3f and < 2.2f, $"{name}: and a height: {rig.EyeHeight}");
                var parts = custom ? new[] { "hips", "spine", "chest", "upper_arm_l", "lower_arm_l", "hand_l", "hand_r", "upper_leg_l", "lower_leg_r", "foot_l", "foot_r" } : [];
                var before = parts.ToDictionary(p => p, p => rig.CustomBonePositionForTesting(p)!.Value);

                rig.SetFirstPersonView(true); await Frames(8);
                Require(rig.Visible, $"{name}: the avatar is still drawn in first person");
                if (custom)
                {
                    foreach (var gone in new[] { "head", "neck" }) Require(rig.CustomBoneSizeForTesting(gone) < .01f, $"{name}: the {gone} is not drawn");
                    foreach (var kept in parts) Require(rig.CustomBoneSizeForTesting(kept) > .3f, $"{name}: the {kept} is drawn (size {rig.CustomBoneSizeForTesting(kept)})");
                    foreach (var kept in parts) Require(rig.CustomBonePositionForTesting(kept)!.Value.DistanceTo(before[kept]) < .04f, $"{name}: the {kept} stays where it is");
                }
                else
                {
                    Require(!rig.StandardPartShownForTesting("Hips/Torso/Neck") && !rig.StandardPartShownForTesting("Hips/Torso/Neck/Head"), $"{name}: the neck and the head are not drawn");
                    foreach (var kept in new[] { "Hips", "Hips/Torso", "Hips/Torso/LeftUpperArm", "Hips/Torso/LeftUpperArm/LeftLowerArm", "Hips/Torso/RightUpperArm/RightLowerArm", "Hips/LeftUpperLeg", "Hips/LeftUpperLeg/LeftLowerLeg", "Hips/RightUpperLeg/RightLowerLeg" }) Require(rig.StandardPartShownForTesting(kept), $"{name}: {kept} is drawn");
                }
                Require(new[] { ring, belt, badge, sleeve }.All(a => rig.AttachmentShownForTesting(a.Id)), $"{name}: what is worn on the hand, the waist, the chest and the arm is seen");
                Require(!rig.AttachmentShownForTesting(hat.Id) && !rig.AttachmentShownForTesting(collar.Id), $"{name}: what is worn on the head and the neck is out of the way");
                Require(rig.EyeWorldPosition(Vector3.Forward)!.Value.DistanceTo(eyesBefore!.Value) < .05f, $"{name}: the eyes are where they were");

                var forwardEyes = rig.EyeWorldPosition(Vector3.Forward)!.Value; var backEyes = rig.EyeWorldPosition(Vector3.Back)!.Value;
                Require(forwardEyes.Z < backEyes.Z - .05f, $"{name}: the camera sits in front of the neck, whichever way the avatar faces");

                var later = Data("thigh_l", Guid.NewGuid()); var onHead = Data("skull", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar, later, onHead]); await Frames(8);
                Require(rig.AttachmentShownForTesting(later.Id) && !rig.AttachmentShownForTesting(onHead.Id), $"{name}: what is put on while in first person follows the same rule");
                rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = custom ? "Corpo" : "Teste", CustomModelId = custom ? modelId : null, CustomModelHash = custom ? HashOf(body) : null, CustomModelScale = 1f }); await Frames(40);
                if (custom) Require(rig.CustomBoneSizeForTesting("head") < .01f && rig.CustomBoneSizeForTesting("hand_l") > .3f, $"{name}: a look applied again keeps the same rule"); else Require(!rig.StandardPartShownForTesting("Hips/Torso/Neck/Head") && rig.StandardPartShownForTesting("Hips/Torso"), $"{name}: a look applied again keeps the same rule");

                rig.SetFirstPersonView(false); await Frames(8);
                if (custom) Require(rig.CustomBoneSizeForTesting("head") > .3f && rig.CustomBoneSizeForTesting("neck") > .3f, $"{name}: back to third person the head returns");
                else Require(rig.StandardPartShownForTesting("Hips/Torso/Neck/Head"), $"{name}: back to third person the head returns");
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
