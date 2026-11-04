using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>Custom clothes and accessories on the client: rigid pieces follow a bone, rigged garments are rebound to the skeleton of a custom body.</summary>
public partial class AvatarWearablesSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static WornWearable Piece(Guid id, byte[] glb, string slot, string kind, string bone = "", float[]? offset = null) => new() { Id = id, Key = AvatarRules.CustomKey(id), Hash = HashOf(glb), Slot = slot, Kind = kind, AttachBone = bone, Offset = offset ?? [0, 0, 0] };

    private static AvatarAppearanceData Look(byte[]? body, float scale, params WornWearable[] worn) => new()
    {
        DisplayName = "Teste", CustomModelId = body is null ? null : Guid.NewGuid(), CustomModelHash = body is null ? null : HashOf(body), CustomModelScale = scale,
        CustomWearablesJson = JsonSerializer.Serialize(worn),
        EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }]
    };

    private static Skeleton3D? Skeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (Skeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(5);
            var k = 1.8f / 1.78f;
            var provided = new Dictionary<Guid, byte[]>();
            AvatarRig.WearableProvider = (id, _) => Task.FromResult(provided.TryGetValue(id, out var bytes) ? bytes : null);
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(HumanoidTemplate.Build());

            // ---- a hat on the procedural body
            var hatId = Guid.NewGuid(); var hat = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .15f, 0]), .2f); provided[hatId] = hat;
            rig.ApplyAppearance(Look(null, 1, Piece(hatId, hat, "hat", "attachment", "head", [0, .15f, 0]))); await Frames(6);
            Require(rig.WearablesShown == 1 && rig.WearableStatus[hatId] == "ready", $"the hat is on the avatar: {rig.WearableStatus[hatId]}");
            var hatNode = (Node3D)rig.PlacedWearables[0];
            Require(Mathf.Abs(hatNode.GlobalPosition.Y - 1.70f * k) < .1f && Mathf.Abs(hatNode.GlobalPosition.X) < .05f, $"the hat sits on top of the head: {hatNode.GlobalPosition}");
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(20);
            Require(Mathf.Abs(hatNode.GlobalPosition.Y - 1.70f * k) < .15f, "the hat stays with the head while running");
            rig.SetMotionState(AvatarMotionState.Idle, true);

            // the offset turns with the avatar: a piece 0.3 to the left of the head ("+x" is the character's left) is on the character's left whichever way it faces
            var sideId = Guid.NewGuid(); var side = WearableTemplate.Attachment(WearableTemplate.Extras("ears", bone: "head", offset: [.3f, 0, 0]), .1f); provided[sideId] = side;
            rig.ApplyAppearance(Look(null, 1, Piece(sideId, side, "ears", "attachment", "head", [.3f, 0, 0]))); await Frames(6);
            var piece = (Node3D)rig.PlacedWearables[0]; var fromRig = piece.GlobalPosition - rig.GlobalPosition;
            Require(fromRig.X < -.2f && Mathf.Abs(fromRig.Z) < .1f, $"facing -Z the character's left is world -X: {fromRig}");
            rig.Rotation = new Vector3(0, Mathf.Pi / 2, 0); await Frames(4);
            fromRig = piece.GlobalPosition - rig.GlobalPosition;
            Require(fromRig.Z > .2f && Mathf.Abs(fromRig.X) < .12f, $"after turning the piece turned with the avatar: {fromRig}");
            rig.Rotation = Vector3.Zero; await Frames(2);

            // ---- clothing needs a custom body
            var shirtId = Guid.NewGuid(); var shirt = WearableTemplate.Clothing("top", WearableTemplate.ShirtBones); provided[shirtId] = shirt;
            rig.ApplyAppearance(Look(null, 1, Piece(shirtId, shirt, "top", "clothing"))); await Frames(6);
            Require(rig.WearablesShown == 0 && rig.WearableStatus[shirtId].Contains("corpo personalizado"), $"a garment is not drawn on the standard body: {rig.WearableStatus[shirtId]}");

            // ---- on a custom body, whatever the naming of the body and of the garment
            foreach (var (bodyNaming, clothNaming) in new[] { (BoneNaming.Blender, BoneNaming.Blender), (BoneNaming.Blender, BoneNaming.Mixamo), (BoneNaming.Mixamo, BoneNaming.Standard) })
            {
                var body = HumanoidTemplate.Build(new TemplateOptions { Naming = bodyNaming }); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
                var cloth = WearableTemplate.Clothing("top", WearableTemplate.ShirtBones, options: new TemplateOptions { Naming = clothNaming }); provided[shirtId] = cloth;
                var hand = WearableTemplate.Attachment(WearableTemplate.Extras("neck", bone: "hand_l", offset: [0, 0, 0]), .06f); provided[sideId] = hand;
                rig.ApplyAppearance(Look(body, 1.8f / 1.78f, Piece(shirtId, cloth, "top", "clothing"), Piece(sideId, hand, "neck", "attachment", "hand_l"))); await Frames(10);
                var label = $"{bodyNaming}/{clothNaming}";
                Require(rig.CustomBodyActive, $"{label}: the custom body is on");
                Require(rig.WearableStatus[shirtId] == "ready" && rig.WearableStatus[sideId] == "ready" && rig.WearablesShown == 2, $"{label}: both items are placed: {rig.WearableStatus[shirtId]} / {rig.WearableStatus[sideId]}");
                var skeleton = Skeleton(rig.CustomBodyNode!)!;
                var cloth3d = rig.PlacedWearables.OfType<MeshInstance3D>().Single();
                Require(cloth3d.GetParent() == skeleton && cloth3d.Skin!.GetBindCount() == 17, $"{label}: the garment is a child of the skeleton and has a bind per bone: {cloth3d.Skin?.GetBindCount()}");
                var names = Enumerable.Range(0, skeleton.GetBoneCount()).Select(skeleton.GetBoneName).ToHashSet();
                Require(Enumerable.Range(0, cloth3d.Skin!.GetBindCount()).All(b => names.Contains(cloth3d.Skin.GetBindName(b))), $"{label}: every bind is a bone of the avatar (the names were translated)");
                Require(cloth3d.Skeleton.ToString() == "..", $"{label}: the garment points to the skeleton");

                // the rigid piece on the left hand follows the hand when the arms swing
                var handPiece = rig.PlacedWearables.OfType<Node3D>().First(n => n is not MeshInstance3D); // the holder of the rigid piece (names get a suffix while an old node is still waiting to be freed)
                var handBone = BoneIndex(skeleton, "hand_l");
                var start = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(handBone)).Origin;
                Require(handPiece.GlobalPosition.DistanceTo(start) < .08f, $"{label}: the piece starts on the hand: {handPiece.GlobalPosition.DistanceTo(start):F3}");
                rig.SetMotionState(AvatarMotionState.Run, true);
                var moved = 0f; var worst = 0f;
                for (var frame = 0; frame < 40; frame++)
                {
                    await Frames(1);
                    var now = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(handBone)).Origin;
                    moved = Mathf.Max(moved, now.DistanceTo(start)); worst = Mathf.Max(worst, handPiece.GlobalPosition.DistanceTo(now));
                }
                Require(moved > .08f && worst < .1f, $"{label}: the hand moves ({moved:F2} m) and the piece stays on it (off by {worst:F3} m)");
                rig.SetMotionState(AvatarMotionState.Idle, true);
            }

            // ---- the garment deforms with the body: a vertex weighted to the upper arm moves when the arm swings
            {
                var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
                var cloth = WearableTemplate.Clothing("top", ["upper_arm_l"]); provided[shirtId] = cloth;
                rig.ApplyAppearance(Look(body, 1.8f / 1.78f, Piece(shirtId, cloth, "top", "clothing"))); await Frames(10);
                var skeleton = Skeleton(rig.CustomBodyNode!)!; var meshInstance = rig.PlacedWearables.OfType<MeshInstance3D>().Single();
                var armIndex = BoneIndex(skeleton, "upper_arm_l"); var rest = skeleton.GetBoneGlobalRest(armIndex);
                rig.SetMotionState(AvatarMotionState.Run, true); await Frames(15);
                var swing = rest.Basis.GetRotationQuaternion().AngleTo(skeleton.GetBoneGlobalPose(armIndex).Basis.GetRotationQuaternion());
                // the skin matrix of that bone at rest is the identity (bind = inverse rest), and moves with the arm
                var skinMatrix = skeleton.GetBoneGlobalPose(armIndex) * meshInstance.Skin!.GetBindPose(Enumerable.Range(0, meshInstance.Skin.GetBindCount()).First(b => meshInstance.Skin.GetBindName(b) == skeleton.GetBoneName(armIndex)));
                var shift = skinMatrix.Origin.Length() + (skinMatrix.Basis.GetRotationQuaternion().AngleTo(Quaternion.Identity));
                Require(swing > .1f && shift > .05f, $"the garment follows the arm: bone swing {swing:F2}, skin matrix change {shift:F2}");
                rig.SetMotionState(AvatarMotionState.Idle, true);
            }

            // ---- things that go wrong
            var good = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(good);
            var wrongId = Guid.NewGuid(); provided[wrongId] = hat; var wrong = Piece(wrongId, hat, "hat", "attachment", "head"); wrong.Hash = new string('0', 64);
            rig.ApplyAppearance(Look(null, 1, wrong)); await Frames(6);
            Require(rig.WearablesShown == 0 && rig.WearableStatus[wrongId].Contains("confere"), $"a file that is not the approved one is not used: {rig.WearableStatus[wrongId]}");
            var lostId = Guid.NewGuid(); var lost = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .77f, 0])); // never provided, never cached
            rig.ApplyAppearance(Look(null, 1, Piece(lostId, lost, "hat", "attachment", "head"))); await Frames(6);
            Require(rig.WearablesShown == 0 && rig.WearableStatus[lostId].Contains("baixar"), $"a failed download is said: {rig.WearableStatus[lostId]}");
            var badBone = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head")); var badId = Guid.NewGuid(); provided[badId] = badBone;
            var broken = Piece(badId, badBone, "hat", "attachment", "tail"); rig.ApplyAppearance(Look(null, 1, broken)); await Frames(6);
            Require(rig.WearablesShown == 0 && rig.WearableStatus[badId].Contains("tail"), $"an unknown bone is explained: {rig.WearableStatus[badId]}");

            // a download that arrives after the avatar changed is ignored
            AvatarRig.WearableProvider = async (id, _) => { await Task.Delay(150); return provided.GetValueOrDefault(id); };
            rig.ApplyAppearance(Look(null, 1, Piece(hatId, hat, "hat", "attachment", "head", [0, .15f, 0])));
            rig.ApplyAppearance(Look(null, 1));
            await Task.Delay(400); await Frames(4);
            Require(rig.WearablesShown == 0, "an old download that finishes late is ignored");

            // ---- own hair and eyes replace the catalog ones and take the colours of the avatar; far away the catalog ones come back
            AvatarRig.WearableProvider = (id, _) => Task.FromResult(provided.GetValueOrDefault(id));
            var hairId = Guid.NewGuid(); var hairGlb = WearableTemplate.Attachment(WearableTemplate.Extras("hair", bone: "head", offset: [0, .12f, 0], tint: "hair"), .3f); provided[hairId] = hairGlb;
            var eyesId = Guid.NewGuid(); var eyesGlb = WearableTemplate.Attachment(WearableTemplate.Extras("eyes", bone: "head", offset: [0, .06f, .09f], tint: "eyes"), .05f); provided[eyesId] = eyesGlb;
            var hairPiece = Piece(hairId, hairGlb, "hair", "attachment", "head", [0, .12f, 0]); hairPiece.Tint = "hair"; hairPiece.HidesHair = true;
            var eyesPiece = Piece(eyesId, eyesGlb, "eyes", "attachment", "head", [0, .06f, .09f]); eyesPiece.Tint = "eyes";
            var own = Look(null, 1, hairPiece, eyesPiece); own.HairColor = "#FF0000"; own.EyeColor = "#00FF00";
            own.EquippedItems.Add(new() { Slot = "hair", ItemKey = hairPiece.Key }); own.EquippedItems.Add(new() { Slot = "eyes", ItemKey = eyesPiece.Key });
            rig.ApplyAppearance(own); await Frames(6);
            Require(rig.WearablesShown == 2 && rig.WearableStatus[hairId] == "ready" && rig.WearableStatus[eyesId] == "ready", $"own hair and eyes are on the avatar: {rig.WearableStatus[hairId]} / {rig.WearableStatus[eyesId]}");
            Require(rig.FindChild("Hair", true, false) is null && rig.FindChild("EyeLeft", true, false) is null && rig.FindChild("EyeRight", true, false) is null, "the catalog hair and eyes are replaced");
            static Color ColourOf(Node node) { if (node is MeshInstance3D { Mesh: not null } m && m.GetSurfaceOverrideMaterial(0) is BaseMaterial3D b) return b.AlbedoColor; foreach (var c in node.GetChildren()) { var found = ColourOf(c); if (found != default) return found; } return default; }
            var hairNode = rig.PlacedWearables[0]; var eyesNode = rig.PlacedWearables[1]; // placed in the order they are worn
            Require(ColourOf(hairNode).R > .95f && ColourOf(hairNode).G < .05f, $"the hair piece takes the hair colour: {ColourOf(hairNode)}");
            Require(ColourOf(eyesNode).G > .95f && ColourOf(eyesNode).R < .05f, $"the eyes piece takes the eye colour: {ColourOf(eyesNode)}");
            Require(Mathf.Abs(((Node3D)hairNode).GlobalPosition.Y - (1.55f + .12f) * k) < .12f, $"the hair sits on the head: {((Node3D)hairNode).GlobalPosition}");
            rig.SetDetail(AvatarDetail.Reduced); await Frames(6);
            Require(rig.FindChild("Hair", true, false) is not null && rig.FindChild("EyeLeft", true, false) is not null && rig.WearablesShown == 0, "far away, where own pieces are not drawn, the catalog hair and eyes stay");
            rig.SetDetail(AvatarDetail.Full); await Frames(6);
            Require(rig.WearablesShown == 2 && rig.FindChild("Hair", true, false) is null, "close again, the own pieces are back");

            // taking everything off leaves a clean avatar
            AvatarRig.WearableProvider = (id, _) => Task.FromResult(provided.GetValueOrDefault(id));
            rig.ApplyAppearance(Look(null, 1, Piece(hatId, hat, "hat", "attachment", "head", [0, .15f, 0]))); await Frames(6);
            Require(rig.WearablesShown == 1, "the hat is back");
            rig.ApplyAppearance(Look(null, 1)); await Frames(4);
            Require(rig.WearablesShown == 0, "taking the item off removes it");

            AvatarRig.WearableProvider = null; AvatarRig.ModelProvider = null;
            GD.Print("event=avatar_wearables_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_wearables_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
