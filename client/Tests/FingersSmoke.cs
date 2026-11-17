using System.Security.Cryptography;
using Godot;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;

namespace LifeVerse.Client.Tests;

/// <summary>The fingers of a body that has them are not boards: relaxed they are curled toward the palm, they open to wave, close to a fist except the one that points, grip what the hand holds, and a body without fingers is untouched.</summary>
public partial class FingersSmoke : Node
{
    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var man = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(man);
            man.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Homem", BaseModel = AvatarRules.DefaultMan }); await Seconds(3.0f);
            Require(man.CustomBodyActive && man.HasFingersForTesting, "the man has finger bones the game moves");

            // ---- relaxed: curled, not straight
            await Seconds(.6f);
            foreach (var side in new[] { 'l', 'r' })
                for (var finger = 1; finger <= 4; finger++)
                    Require(man.FingerBendForTesting(side, finger) is > 18f and < 150f, $"relaxed, finger {finger} of the {side} hand is bent a natural amount: {man.FingerBendForTesting(side, finger):0} degrees");
            var relaxed = man.FingerBendForTesting('r', 2);

            // the fingers curl toward the palm: with the arm hanging, the palm looks at the thigh, so the tips come toward the body
            var rootX = man.GlobalPosition.X; var tip = man.FingerBonePositionForTesting('r', 2, 3)!.Value; var knuckle = man.FingerBonePositionForTesting('r', 2, 1)!.Value;
            Require(Mathf.Abs(tip.X - rootX) < Mathf.Abs(knuckle.X - rootX) + .02f, $"and toward the palm (the tip is not farther from the body than the knuckle): {tip.X} / {knuckle.X}");
            Require(Mathf.Abs(man.FingerBendForTesting('l', 2) - relaxed) < 20f, "both hands look alike");

            // ---- the shapes of the states
            man.SetMotionState(AvatarMotionState.Wave, true); await Seconds(1.2f);
            Require(man.FingerBendForTesting('r', 2) < relaxed - 8f, $"waving, the hand is open: {man.FingerBendForTesting('r', 2):0} against {relaxed:0}");
            man.SetMotionState(AvatarMotionState.Point, true); await Seconds(1.2f);
            Require(man.FingerBendForTesting('r', 1) < 18f && man.FingerBendForTesting('r', 2) > relaxed + 25f, $"pointing, the index is out and the others are closed: {man.FingerBendForTesting('r', 1):0} / {man.FingerBendForTesting('r', 2):0}");
            man.SetMotionState(AvatarMotionState.Typing, true); await Seconds(1.2f);
            Require(man.FingerBendForTesting('r', 2) is > 30f, $"typing, the fingers are curled over the keys: {man.FingerBendForTesting('r', 2):0}");
            man.SetMotionState(AvatarMotionState.Idle, true); await Seconds(1.2f);
            Require(Mathf.Abs(man.FingerBendForTesting('r', 2) - relaxed) < 12f, "and they relax again");
            Require(man.FingerCurlForTesting('r', 1) is > .15f and < .3f, "the curl is a blend, not a switch");

            // ---- holding something closes the hand
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head"), .1f); AvatarRig.AttachmentModelProvider = _ => ModelCache.ParseInstance(glb);
            var held = new LifeVerse.Client.Scripts.Attachments.AttachmentData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Objeto", "hand_r", "hand_r", [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true);
            man.ApplyAttachments([held]); await Seconds(1.5f);
            Require(man.FingerBendForTesting('r', 2) > relaxed + 20f && Mathf.Abs(man.FingerBendForTesting('l', 2) - relaxed) < 12f, "the hand that holds an object closes on it; the other one does not");
            man.ApplyAttachments([]); await Seconds(1.2f); AvatarRig.AttachmentModelProvider = null;

            // ---- a body without fingers is left as it is
            var body = HumanoidTemplate.Build(); AvatarRig.ModelProvider = (_, _) => Task.FromResult<byte[]?>(body);
            var plain = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); plain.Position = new Vector3(2, 0, 0); root.AddChild(plain);
            plain.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Simples", CustomModelId = Guid.NewGuid(), CustomModelHash = HashOf(body), CustomModelScale = 1f }); await Seconds(1.5f);
            Require(plain.CustomBodyActive && !plain.HasFingersForTesting, "a body without finger bones has none moved (and nothing breaks)");

            GD.Print("event=fingers_smoke result=passed");
            AvatarRig.ModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null; AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=fingers_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
