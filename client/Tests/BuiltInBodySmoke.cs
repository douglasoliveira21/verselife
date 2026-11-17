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
            Require(rig.CustomBodyFileTriangles == 17_940, $"it is the whole model, not a reduced one: {rig.CustomBodyFileTriangles} triangles");
            var tall = rig.EyeHeight; Require(tall is > 1.5f and < 1.95f, $"and a man's height: eyes at {tall} m");

            // ---- exactly the model that was given: no clothes and no hair made up for it
            var names = Meshes(rig.CustomBodyNode!).Select(m => m.Name.ToString()).ToList();
            Require(names.Count == 5 && names.All(n => n.StartsWith("CC_") || n.StartsWith("Male_Brow")), $"only the parts of the model: {string.Join(", ", names)}");
            Require(!Meshes(rig.CustomBodyNode!).Any(m => (m.Mesh?.GetSurfaceCount() ?? 0) > 0 && Enumerable.Range(0, m.Mesh!.GetSurfaceCount()).Any(s => m.GetActiveMaterial(s)?.ResourceName.StartsWith("LV_") == true)), "no material made for clothes or hair");

            // ---- the skin keeps the full quality of the maps and is a solid, natural surface
            var skin = Material(rig, "Ga_Skin_Body")!; Require(skin.AlbedoTexture is { } albedo && albedo.GetWidth() == 2048 && albedo.GetHeight() == 2048, $"the skin keeps its 2048 pixel colour map: {skin.AlbedoTexture?.GetWidth()}");
            Require(skin.NormalEnabled && skin.NormalTexture is { } normal && normal.GetWidth() == 2048, "and its 2048 pixel normal map");
            Require(skin.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && skin.Roughness is > .4f and < .8f && skin.Metallic < .05f, $"it is a solid surface with a natural roughness, not wet plastic: {skin.Transparency} / {skin.Roughness}");
            Require(Material(rig, "Ga_Skin_Head")!.AlbedoTexture!.GetWidth() == 2048 && Material(rig, "Ga_Skin_Leg")!.AlbedoTexture!.GetWidth() == 2048 && Material(rig, "Ga_Skin_Arm")!.AlbedoTexture!.GetWidth() == 2048, "head, arms and legs too");
            Require(Material(rig, "Ga_Eyelash")!.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor && Material(rig, "Male_Brow_Transparency")!.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor, "the lashes and the brows are cut-outs (they sort properly)");

            // ---- every avatar has its own copy of the man, and they share the pictures
            var other = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); other.Position = new Vector3(2, 0, 0); root.AddChild(other);
            other.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Outro", BaseModel = AvatarRules.DefaultMan }); await Seconds(2.0f);
            Require(other.CustomBodyActive && !ReferenceEquals(other.CustomBodyNode, rig.CustomBodyNode), $"a second person is the man too, with a body of their own: {other.CustomBodyStatus}");
            Require(ReferenceEquals(Material(other, "Ga_Skin_Body")!.AlbedoTexture, skin.AlbedoTexture), "but the pictures are in memory once");
            var handIdle = other.CustomBonePositionForTesting("hand_r")!.Value; other.SetMotionState(LifeVerse.Shared.Gameplay.AvatarMotionState.Wave, true); await Seconds(.8f);
            Require(other.CustomBonePositionForTesting("hand_r")!.Value.DistanceTo(handIdle) > .1f, "the copy animates on its own");
            var before = rig.CustomBodyTriangles; var far = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); far.Position = new Vector3(-2, 0, 0); root.AddChild(far);
            far.SetDetail(AvatarDetail.Reduced); far.ApplyAppearance(new AvatarAppearanceData { DisplayName = "Longe", BaseModel = AvatarRules.DefaultMan }); await Seconds(3.0f);
            Require(far.CustomBodyActive && far.CustomBodyTriangles < before, $"far away the man is simplified ({far.CustomBodyTriangles} of {before} triangles)");
            Require(rig.CustomBodyTriangles == before && Material(rig, "Ga_Skin_Body")!.AlbedoTexture!.GetWidth() == 2048, "and that does not touch the others, which keep every triangle");
            other.QueueFree(); far.QueueFree(); await Seconds(.3f);

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

    /// <summary>The material of that name on the body shown.</summary>
    private static BaseMaterial3D? Material(AvatarRig rig, string name)
    {
        foreach (var mesh in Meshes(rig.CustomBodyNode!))
            for (var s = 0; s < (mesh.Mesh?.GetSurfaceCount() ?? 0); s++)
                if (mesh.GetActiveMaterial(s) is BaseMaterial3D m && m.ResourceName == name) return m;
        return null;
    }

    private static IEnumerable<MeshInstance3D> Meshes(Node node) { if (node is MeshInstance3D m) yield return m; foreach (var c in node.GetChildren()) foreach (var x in Meshes(c)) yield return x; }
    private async Task Seconds(float seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
