using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>The man of LifeVerse: the body that comes with the game is what a new character starts with, is drawn with no download, takes the colours chosen for its clothes and hair, and the older bodies are still there.</summary>
public partial class BuiltInBodySmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            AvatarRig.ModelProvider = (_, _) => throw new InvalidOperationException("a body that comes with the game is never downloaded");
            var root = new Node3D(); AddChild(root);

            // ---- a new character starts as the man
            var fresh = AvatarManager.CreateDefault();
            Require(fresh.BaseModel == AvatarRules.DefaultMan, "a new character is the man of LifeVerse");
            Require(fresh.Tint("top", "") == "#3AAFC1" && fresh.Tint("bottom", "") == "#263B56", "with the tee and the trousers the game offers");
            Require(BuiltInBodies.TryFor(AvatarRules.DefaultMan, out var id, out var hash) && id == AvatarRules.DefaultManModelId && hash.Length == 64, "the man has a body file in the game");
            Require(!BuiltInBodies.TryFor("human_a", out _, out _) && !BuiltInBodies.TryFor("human_b", out _, out _), "the older bodies are drawn by the game itself");
            Require(BuiltInBodies.Bytes(Guid.NewGuid()) is null && BuiltInBodies.Bytes(AvatarRules.DefaultManModelId) is { Length: > 1_000_000 }, "and only the man has a file");

            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            fresh.DisplayName = "Novo"; rig.ApplyAppearance(fresh); await Seconds(2.5f);
            Require(rig.CustomBodyActive, $"the man is drawn as a full 3D body: {rig.CustomBodyStatus}");
            Require(rig.CustomBodyTriangles is > 15_000 and < 60_000, $"with the triangles of the model and its clothes: {rig.CustomBodyTriangles}");
            var tall = rig.EyeHeight; Require(tall is > 1.5f and < 1.95f, $"and a man\x27s height: eyes at {tall} m");
            Require(Look(rig, BuiltInBodies.TopMaterial) == new Color("#3AAFC1") && Look(rig, BuiltInBodies.BottomMaterial) == new Color("#263B56") && Look(rig, BuiltInBodies.ShoesMaterial) == new Color("#E7E9EE"), "its clothes have the colours the character wears");
            Require(Look(rig, BuiltInBodies.HairMaterial) == new Color(fresh.HairColor), "and its hair the colour of the character\x27s hair");

            // ---- the colours chosen are the colours drawn
            var red = AvatarManager.CreateDefault(); red.DisplayName = "Novo"; red.HairColor = "#C1440E"; red.EquippedItems[0].TintColor = "#E03030";
            rig.ApplyAppearance(red); await Seconds(2.0f);
            Require(Look(rig, BuiltInBodies.TopMaterial) == new Color("#E03030") && Look(rig, BuiltInBodies.HairMaterial) == new Color("#C1440E"), "a new colour of the tee and of the hair is drawn");

            // ---- the other bodies are as before
            var old = new AvatarAppearanceData { DisplayName = "Antigo", BaseModel = "human_a" }; var standard = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); standard.Position = new Vector3(2, 0, 0); root.AddChild(standard);
            standard.ApplyAppearance(old); await Seconds(1.0f); Require(!standard.CustomBodyActive, "the classic body is still the game\x27s own");

            // ---- far away it is simplified, and the clothes follow the skeleton (the animation moves them with the body)
            rig.SetMotionState(LifeVerse.Shared.Gameplay.AvatarMotionState.Wave, true); await Seconds(1.0f);
            Require(rig.CustomBodyActive && rig.CustomBonePositionForTesting("hand_r") is { } hand && hand.Y > 1.0f, "the man animates like any other body");

            GD.Print("event=built_in_body_smoke result=passed");
            AvatarRig.ModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.ModelProvider = null;
            GD.PushError($"event=built_in_body_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    /// <summary>The colour of the material of that name on the body shown.</summary>
    private static Color? Look(AvatarRig rig, string material)
    {
        foreach (var mesh in Meshes(rig.CustomBodyNode!))
            for (var s = 0; s < (mesh.Mesh?.GetSurfaceCount() ?? 0); s++)
                if (mesh.GetActiveMaterial(s) is BaseMaterial3D m && m.ResourceName == material) return m.AlbedoColor;
        return null;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node) { if (node is MeshInstance3D m) yield return m; foreach (var c in node.GetChildren()) foreach (var x in Meshes(c)) yield return x; }
    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
