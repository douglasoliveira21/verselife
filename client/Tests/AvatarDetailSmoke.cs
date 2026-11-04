using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Networking;

namespace LifeVerse.Client.Tests;

/// <summary>Level of detail and progressive loading: nothing expensive is fetched or built until an avatar is close and within the budget.</summary>
public partial class AvatarDetailSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(4);

            var body = HumanoidTemplate.Build();
            var hat = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head", offset: [0, .15f, 0])); var hatId = Guid.NewGuid();
            var tracks = new (string, int, float)[] { ("upper_leg_l", 0, 1.2f), ("upper_leg_r", 0, -1.2f), ("lower_leg_l", 0, .4f), ("lower_leg_r", 0, .4f), ("upper_arm_l", 0, -.4f), ("upper_arm_r", 0, .4f), ("spine", 1, .1f), ("head", 1, -.1f), ("chest", 1, .1f) };
            var animations = HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Passada", 1f, tracks)] }); var animationId = Guid.NewGuid();
            int bodyDownloads = 0, itemDownloads = 0, animationDownloads = 0;
            AvatarRig.ModelProvider = (_, _) => { bodyDownloads++; return Task.FromResult<byte[]?>(body); };
            AvatarRig.WearableProvider = (_, _) => { itemDownloads++; return Task.FromResult<byte[]?>(hat); };
            AvatarRig.AnimationProvider = (_, _) => { animationDownloads++; return Task.FromResult<byte[]?>(animations); };
            AvatarAppearanceData Look() => new()
            {
                DisplayName = "Longe", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1.8f / 1.78f, CustomModelTriangles = 30_000,
                CustomWearablesJson = JsonSerializer.Serialize(new[] { new WornWearable { Id = hatId, Key = AvatarRules.CustomKey(hatId), Hash = HashOf(hat), Slot = "hat", Kind = "attachment", AttachBone = "head", Triangles = 8_000, Offset = [0, .15f, 0] } }),
                AnimationOverridesJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["Walk"] = $"custom:{animationId}#Passada" }),
                AnimationFilesJson = JsonSerializer.Serialize(new[] { new { id = animationId, hash = HashOf(animations) } }),
                EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }]
            };

            // ---- far away: the simple body, and nothing is downloaded
            rig.SetDetail(AvatarDetail.Basic); rig.ApplyAppearance(Look()); await Frames(8);
            Require(!rig.CustomBodyActive && rig.WearablesShown == 0 && bodyDownloads + itemDownloads + animationDownloads == 0, $"at Basic nothing is fetched: {bodyDownloads}/{itemDownloads}/{animationDownloads}");
            Require(rig.CustomBodyStatus.Contains("Longe"), $"the reason is said: {rig.CustomBodyStatus}");
            Require(rig.ReducedCost == AvatarDetailPlanner.ReducedTriangles(30_000) && rig.BodyCost == 30_000 && rig.FullCost == 38_000, $"the costs are known without loading anything: {rig.ReducedCost}/{rig.FullCost}");

            // ---- closer: the custom body comes, the items still wait
            rig.SetDetail(AvatarDetail.Reduced); await Frames(8);
            Require(rig.CustomBodyActive && rig.WearablesShown == 0 && bodyDownloads == 1 && itemDownloads == 0 && animationDownloads == 0, $"at Reduced only the body is fetched: {bodyDownloads}/{itemDownloads}/{animationDownloads}");
            Require(rig.AnimationStatus["Walk"].Contains("Longe"), $"the custom animation waits: {rig.AnimationStatus["Walk"]}");

            // ---- close: everything
            rig.SetDetail(AvatarDetail.Full); await Frames(10);
            Require(rig.CustomBodyActive && rig.WearablesShown == 1 && itemDownloads == 1 && animationDownloads == 1 && rig.AnimationStatus["Walk"] == "ready", $"at Full the items and animations come: {itemDownloads}/{animationDownloads}/{rig.AnimationStatus["Walk"]}");

            // ---- hidden stops the work and keeps everything; coming back costs nothing
            var before = bodyDownloads + itemDownloads + animationDownloads;
            rig.SetDetail(AvatarDetail.Hidden); await Frames(3);
            Require(!rig.Visible && !rig.IsProcessing(), "hidden avatars are not drawn and not processed");
            rig.SetDetail(AvatarDetail.Full); await Frames(3);
            Require(rig.Visible && rig.IsProcessing() && rig.CustomBodyActive && rig.WearablesShown == 1 && bodyDownloads + itemDownloads + animationDownloads == before, "back from hidden nothing is downloaded or rebuilt again");

            // ---- going away again drops what is expensive
            rig.SetDetail(AvatarDetail.Basic); await Frames(5);
            Require(!rig.CustomBodyActive && rig.WearablesShown == 0, "far again: the simple body is back");
            rig.SetDetail(AvatarDetail.Full); await Frames(10);
            Require(rig.CustomBodyActive && rig.WearablesShown == 1, "and close again everything is there");
            rig.SetDetail(AvatarDetail.Reduced); await Frames(2); // the reduced avatar updates every other frame but still moves
            rig.SetMotionState(AvatarMotionState.Run, true);
            var skeleton = FindSkeleton(rig.CustomBodyNode!)!; var leg = BoneIndex(skeleton, "upper_leg_l"); var restLeg = skeleton.GetBoneRest(leg).Basis.GetRotationQuaternion(); var peak = 0f;
            for (var i = 0; i < 40; i++) { await Frames(1); peak = Mathf.Max(peak, restLeg.AngleTo(skeleton.GetBonePoseRotation(leg))); }
            Require(peak > .3f, $"a reduced avatar still runs: {peak:F2}");
            rig.SetMotionState(AvatarMotionState.Idle, true);

            // ---- the scene decides for every other player
            var network = GetNode<NetworkManager>("/root/NetworkManager");
            AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(null); // the crowd's files are not the point here
            string Crowd(int n) => JsonSerializer.Serialize(new AvatarAppearanceData { DisplayName = $"P{n}", CustomModelId = Guid.NewGuid(), CustomModelHash = new string('a', 64), CustomModelTriangles = 30_000, EquippedItems = [new() { Slot = "top", ItemKey = "tee_basic" }, new() { Slot = "bottom", ItemKey = "pants_basic" }, new() { Slot = "shoes", ItemKey = "sneakers_basic" }] });
            var distances = new[] { 10f, 20f, 40f, 55f, 80f, 110f, 150f };
            for (var i = 0; i < distances.Length; i++) network.OnPlayerSpawned(100 + i, Guid.NewGuid().ToString(), $"P{i}", Crowd(i), new Vector3(0, 0, distances[i]), (int)NetworkMotionState.Idle);
            network.AvatarBudget = new DetailBudget { Triangles = 10_000_000 };
            network.UpdateDetail();
            AvatarDetail Of(int i) => network.RemoteNode(100 + i)!.GetChildren().OfType<AvatarRig>().Single().Detail;
            Require(Of(0) == AvatarDetail.Full && Of(1) == AvatarDetail.Full && Of(2) == AvatarDetail.Reduced && Of(3) == AvatarDetail.Reduced && Of(4) == AvatarDetail.Basic && Of(5) == AvatarDetail.Basic && Of(6) == AvatarDetail.Hidden, $"by distance: {string.Join(",", Enumerable.Range(0, 7).Select(i => Of(i)))}");
            Require(network.DetailCounts[AvatarDetail.Hidden] == 1 && network.DetailCounts[AvatarDetail.Full] == 2, "the counts are reported");

            // a smaller budget: the nearest keep their detail and the farthest lose it
            network.AvatarBudget = new DetailBudget { Triangles = 70_000 };
            network.UpdateDetail();
            Require(Of(0) == AvatarDetail.Full && Of(1) == AvatarDetail.Full, "the nearest are served first");
            Require(Of(2) <= AvatarDetail.Basic && Of(3) <= AvatarDetail.Basic, $"the next ones no longer fit: {Of(2)}/{Of(3)}");

            // somebody walks up to us and then goes back and forth around the edge of a range
            network.AvatarBudget = new DetailBudget { Triangles = 10_000_000 };
            network.PlaceRemoteForTesting(106, new Vector3(0, 0, 18)); network.UpdateDetail(); await Frames(2);
            Require(Of(6) == AvatarDetail.Full, $"somebody who comes close gets full detail: {Of(6)}");
            var changes = 0; var last = Of(6);
            foreach (var z in new[] { 24f, 26f, 27f, 26f, 25f, 24f, 26f, 27f, 25f })
            {
                network.PlaceRemoteForTesting(106, new Vector3(0, 0, z)); network.UpdateDetail(); await Frames(1);
                if (Of(6) != last) { changes++; last = Of(6); }
            }
            Require(changes == 0, $"stepping around the edge of the range does not flicker: {changes} changes");

            for (var i = 0; i < distances.Length; i++) network.OnPlayerDespawned(100 + i, false);
            AvatarRig.ModelProvider = null; AvatarRig.WearableProvider = null; AvatarRig.AnimationProvider = null;
            GD.Print("event=avatar_detail_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_detail_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static Skeleton3D? FindSkeleton(Node node) { if (node is Skeleton3D s) return s; foreach (var c in node.GetChildren()) if (FindSkeleton(c) is { } f) return f; return null; }
    private static int BoneIndex(Skeleton3D skeleton, string standard) { for (var i = 0; i < skeleton.GetBoneCount(); i++) if (BoneMapper.Map(skeleton.GetBoneName(i)) == standard) return i; return -1; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
