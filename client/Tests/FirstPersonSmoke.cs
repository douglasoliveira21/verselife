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
                var hat = Data("head_top", Guid.NewGuid()); var ring = Data("hand_r", Guid.NewGuid()); var collar = Data("neck", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar]); await Frames(8);
                Require(rig.AttachmentsShown == 3 && rig.AttachmentShownForTesting(hat.Id) && rig.AttachmentShownForTesting(ring.Id) && rig.AttachmentShownForTesting(collar.Id), $"{name}: everything worn is shown in third person");

                rig.SetFirstPersonView(true); await Frames(6);
                Require(rig.Visible, $"{name}: the avatar is still drawn in first person");
                Require(custom ? rig.CustomHeadScaleZeroForTesting : !rig.StandardHeadShownForTesting, $"{name}: only the head is left out");
                Require(rig.AttachmentShownForTesting(ring.Id), $"{name}: what is worn on the hand is seen");
                Require(!rig.AttachmentShownForTesting(hat.Id) && !rig.AttachmentShownForTesting(collar.Id), $"{name}: what hangs on the head or the neck is out of the way of the camera");

                var later = Data("chest", Guid.NewGuid()); var onHead = Data("skull", Guid.NewGuid());
                rig.ApplyAttachments([hat, ring, collar, later, onHead]); await Frames(8);
                Require(rig.AttachmentShownForTesting(later.Id) && !rig.AttachmentShownForTesting(onHead.Id), $"{name}: what is put on while in first person follows the same rule");
                rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = custom ? "Corpo" : "Teste", CustomModelId = custom ? modelId : null, CustomModelHash = custom ? HashOf(body) : null, CustomModelScale = 1f }); await Frames(40);
                Require(custom ? rig.CustomHeadScaleZeroForTesting : !rig.StandardHeadShownForTesting, $"{name}: a look applied again keeps the head out");

                rig.SetFirstPersonView(false); await Frames(6);
                Require(custom ? !rig.CustomHeadScaleZeroForTesting : rig.StandardHeadShownForTesting, $"{name}: back to third person the head returns");
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
