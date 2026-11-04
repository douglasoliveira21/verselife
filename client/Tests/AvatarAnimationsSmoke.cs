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

/// <summary>Animation Override on the client: the library, the animations of a person's own files on a custom body, and appearance changes reaching other players (also after a reconnection).</summary>
public partial class AvatarAnimationsSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AvatarAppearanceData Look(Dictionary<string, string> overrides, byte[]? body = null, params (Guid Id, byte[] Bytes)[] files) => new()
    {
        DisplayName = "Teste", CustomModelId = body is null ? null : Guid.NewGuid(), CustomModelHash = body is null ? null : HashOf(body), CustomModelScale = 1.8f / 1.78f,
        AnimationOverridesJson = JsonSerializer.Serialize(overrides),
        AnimationFilesJson = JsonSerializer.Serialize(files.Select(f => new { id = f.Id, hash = HashOf(f.Bytes) })),
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

            // ---- the library
            var player = rig.GetNode<AnimationPlayer>("AnimationPlayer");
            foreach (var (state, names) in AnimationStates.Builtin) foreach (var name in names) Require(player.HasAnimation($"{state}@{name}"), $"the library has {state}@{name}");
            rig.ApplyAppearance(Look([])); rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(6);
            Require(rig.CurrentAnimationNode == "Walk", $"the default walk plays: {rig.CurrentAnimationNode}");
            async Task<float> PeakArm()
            {
                var node = (Node3D)rig.FindChild("LeftUpperArm", true, false)!; var peak = 0f;
                for (var i = 0; i < 70; i++) { await Frames(1); peak = Mathf.Max(peak, Mathf.Abs(node.Rotation.X)); }
                return peak;
            }
            var normal = await PeakArm();
            rig.ApplyAppearance(Look(new() { ["Walk"] = "builtin:happy" })); await Frames(4);
            Require(rig.CurrentAnimationNode == "Walk@happy" && rig.AnimationStatus["Walk"] == "ready", $"the happy walk replaces the default: {rig.CurrentAnimationNode}");
            var happy = await PeakArm();
            Require(happy > normal * 1.3f, $"it is a different animation (arms swing {normal:F2} to {happy:F2})");
            rig.ApplyAppearance(Look(new() { ["Run"] = "builtin:sprint" })); await Frames(4);
            Require(rig.CurrentAnimationNode == "Walk", "an override of another state does not change this one");
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(4);
            Require(rig.CurrentAnimationNode == "Run@sprint", $"the sprint plays when running: {rig.CurrentAnimationNode}");
            rig.ApplyAppearance(Look(new() { ["Run"] = "builtin:nao_existe" })); await Frames(4);
            Require(rig.CurrentAnimationNode == "Run" && rig.AnimationStatus["Run"].Contains("não existe"), $"an unknown alternative falls back to the default: {rig.CurrentAnimationNode}");
            rig.ApplyAppearance(Look([])); await Frames(4);
            Require(rig.CurrentAnimationNode == "Run" && rig.Overrides.Count == 0, "without overrides everything is default");
            rig.Preview(AvatarMotionState.Dance); rig.ApplyAppearance(Look(new() { ["Dance"] = "builtin:spin" })); await Frames(4);
            Require(rig.CurrentAnimationNode == "Dance@spin", "a change of look while dancing switches the dance");
            rig.SetMotionState(AvatarMotionState.Idle, true);

            // ---- the animation of a person's own file on a custom body
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var tracks = new (string, int, float)[] { ("upper_leg_l", 0, 1.2f), ("upper_leg_r", 0, -1.2f), ("lower_leg_l", 0, .4f), ("lower_leg_r", 0, .4f), ("upper_arm_l", 0, -.4f), ("upper_arm_r", 0, .4f), ("spine", 1, .1f), ("head", 1, -.1f), ("chest", 1, .1f) };
            var file = HumanoidTemplate.Build(new TemplateOptions { Naming = BoneNaming.Mixamo, Animations = [new AnimationSpec("Passada", 1f, tracks, HipsBob: .06f)] }); var fileId = Guid.NewGuid();
            AvatarRig.AnimationProvider = (_, _) => Task.FromResult<byte[]?>(file);
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{fileId}#Passada" }, body, (fileId, file))); await Frames(10);
            Require(rig.CustomBodyActive && rig.AnimationStatus["Walk"] == "ready", $"the body and the clip are ready: {rig.AnimationStatus["Walk"]}");
            var skeleton = Skeleton(rig.CustomBodyNode!)!; var legL = BoneIndex(skeleton, "upper_leg_l"); var legR = BoneIndex(skeleton, "upper_leg_r"); var hips = BoneIndex(skeleton, "hips"); var foot = BoneIndex(skeleton, "foot_l");
            var restLeg = skeleton.GetBoneRest(legL).Basis.GetRotationQuaternion(); var restHips = skeleton.GetBoneRest(hips).Origin;
            await Frames(3);
            var idleAngle = restLeg.AngleTo(skeleton.GetBonePoseRotation(legL));
            Require(idleAngle < .15f, $"idle is the normal pose: {idleAngle:F2}");
            rig.SetMotionState(AvatarMotionState.Walk, true);
            float peak = 0, lift = 0, reach = 0; var startFoot = (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(foot)).Origin; var opposite = false;
            for (var frame = 0; frame < 70; frame++)
            {
                await Frames(1);
                var l = skeleton.GetBonePoseRotation(legL); var r = skeleton.GetBonePoseRotation(legR);
                peak = Mathf.Max(peak, restLeg.AngleTo(l)); lift = Mathf.Max(lift, skeleton.GetBonePosePosition(hips).Y - restHips.Y);
                reach = Mathf.Max(reach, (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(foot)).Origin.DistanceTo(startFoot));
                if (restLeg.AngleTo(l) > .8f && restLeg.AngleTo(r) > .8f && l.AngleTo(r) > .8f) opposite = true;
            }
            Require(peak > 1.0f && peak < 1.4f, $"the leg swings as the file says (1.2 rad), not as the default walk (0.48): {peak:F2}");
            Require(opposite, "the legs swing against each other");
            Require(lift > .03f, $"the hips bob as the file says: {lift:F3}");
            Require(reach > .5f, $"the foot really travels: {reach:F2} m");
            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(60); // the crossfade of the hidden procedural body takes a moment
            Require(restLeg.AngleTo(skeleton.GetBonePoseRotation(legL)) < .15f && Mathf.Abs(skeleton.GetBonePosePosition(hips).Y - restHips.Y) < .005f, "back to idle the pose and the hips return to rest");

            // ---- things that go wrong
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{fileId}#Passada" }, body)); await Frames(6);
            Require(rig.AnimationStatus["Walk"].Contains("não foi informado"), $"no file information: {rig.AnimationStatus["Walk"]}");
            var other = Guid.NewGuid(); var wrongFile = HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Zzz", 1f, tracks)] });
            AvatarRig.AnimationProvider = (_, _) => Task.FromResult<byte[]?>(HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Outra", 1f, tracks)] }));
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{other}#Zzz" }, body, (other, wrongFile))); await Frames(8);
            Require(rig.AnimationStatus["Walk"].Contains("confere"), $"a file that is not the approved one is not used: {rig.AnimationStatus["Walk"]}");
            var lost = Guid.NewGuid(); var lostBytes = HumanoidTemplate.Build(new TemplateOptions { Animations = [new AnimationSpec("Perdida", 1f, tracks)] });
            AvatarRig.AnimationProvider = (_, _) => Task.FromResult<byte[]?>(null);
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{lost}#Perdida" }, body, (lost, lostBytes))); await Frames(8);
            Require(rig.AnimationStatus["Walk"].Contains("baixar"), $"a failed download is said: {rig.AnimationStatus["Walk"]}");
            AvatarRig.AnimationProvider = (_, _) => Task.FromResult<byte[]?>(file);
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{fileId}#NaoTem" }, body, (fileId, file))); await Frames(8);
            Require(rig.AnimationStatus["Walk"].Contains("não está"), $"an animation that is not in the file: {rig.AnimationStatus["Walk"]}");
            // on the standard body a file of one's own cannot play: the default plays and the status says why
            rig.ApplyAppearance(Look(new() { ["Walk"] = $"custom:{fileId}#Passada" }, null, (fileId, file))); rig.SetMotionState(AvatarMotionState.Walk, true); await Frames(8);
            Require(rig.CurrentAnimationNode == "Walk" && rig.AnimationStatus["Walk"].Contains("corpo personalizado"), $"standard body: {rig.AnimationStatus["Walk"]}");

            // ---- other players see the change at once, and a player who comes back gets the current look
            var network = GetNode<NetworkManager>("/root/NetworkManager");
            var change = new List<string>(); network.AppearanceRefreshed += (peer, name) => change.Add($"{peer}:{name}");
            network.OnPlayerSpawned(7, Guid.NewGuid().ToString(), "Rafa", JsonSerializer.Serialize(Look([])), Vector3.Zero, (int)NetworkMotionState.Walk);
            var remote = network.RemoteNode(7)!; var remoteRig = remote.GetChildren().OfType<AvatarRig>().Single(); await Frames(4);
            Require(remoteRig.Overrides.Count == 0 && remoteRig.CurrentAnimationNode == "Walk", "the other player starts with the default walk");
            var updated = Look(new() { ["Walk"] = "builtin:happy" }); updated.HairStyle = "long"; updated.DisplayName = "Rafa Novo";
            network.OnAppearanceChanged(7, "Rafa Novo", JsonSerializer.Serialize(updated)); await Frames(4);
            Require(remoteRig.Overrides["Walk"] == "builtin:happy" && remoteRig.CurrentAnimationNode == "Walk@happy" && change.Contains("7:Rafa Novo"), $"the other player now walks the happy walk: {remoteRig.CurrentAnimationNode}");
            network.OnAppearanceChanged(7, "Rafa", "not json"); network.OnAppearanceChanged(99, "Ninguem", JsonSerializer.Serialize(updated)); // damaged and unknown messages are ignored
            Require(remoteRig.Overrides["Walk"] == "builtin:happy", "a damaged message does not change anything");
            network.OnPlayerDespawned(7, false); await Frames(2); // the player drops, the server keeps what it learnt, and shows it when they return
            network.OnPlayerSpawned(7, Guid.NewGuid().ToString(), "Rafa Novo", JsonSerializer.Serialize(updated), Vector3.Zero, (int)NetworkMotionState.Walk); await Frames(4);
            var back = network.RemoteNode(7)!.GetChildren().OfType<AvatarRig>().Single();
            Require(back.Overrides["Walk"] == "builtin:happy" && back.CurrentAnimationNode == "Walk@happy", "after a reconnection the new look is there from the first moment");
            network.OnPlayerDespawned(7, false);

            AvatarRig.AnimationProvider = null; AvatarRig.ModelProvider = null;
            GD.Print("event=avatar_animations_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar_animations_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
