using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>Every material model and option builds a material; with a real renderer (not headless) it also draws them so a shader that does not compile shows in the log and the picture.</summary>
public partial class ShaderSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var cases = new (string Name, string Json)[]
            {
                ("plywood-blinn", "{\"shading\":\"blinnphong\",\"builtin\":\"plywood\",\"color\":\"#FFFFFF\",\"glossiness\":0.8,\"environment\":0.4}"),
                ("bricks-pbr", "{\"shading\":\"pbr\",\"builtin\":\"bricks\",\"roughness\":0.4,\"metallic\":0.3}"),
                ("blinn-plain", "{\"shading\":\"blinnphong\",\"color\":\"#C03030\",\"specularColor\":\"#FFFFFF\",\"glossiness\":1}"),
                ("planks-planar-mask", "{\"builtin\":\"planks\",\"mapping\":\"planar\",\"alphaMode\":\"mask\",\"alphaCutoff\":0.2,\"doubleSided\":true}"),
                ("checker-unlit-blend", "{\"shading\":\"blinnphong\",\"builtin\":\"checker\",\"fullBright\":true,\"alphaMode\":\"blend\",\"transparency\":0.6}"),
                ("tiles-rotated", "{\"builtin\":\"tiles\",\"uvRepeatX\":3,\"uvRepeatY\":3,\"uvRotation\":30,\"mapUv\":{\"normalMap\":{\"repeatX\":6}}}"),
                ("emissive", "{\"shading\":\"pbr\",\"color\":\"#336699\",\"emission\":\"#33FFCC\",\"emissionStrength\":2}")
            };
            var camera = new Camera3D { Position = new Vector3(0, 1.2f, 7.5f), Current = true }; AddChild(camera);
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-45, -30, 0) });
            var environment = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("#202830"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .4f } }; AddChild(environment);
            for (var i = 0; i < cases.Length; i++)
            {
                var material = MaterialBuilder.Build(JsonNode.Parse(cases[i].Json)!.AsObject(), null, null);
                Require(material is ShaderMaterial or StandardMaterial3D, $"{cases[i].Name} builds a material");
                AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, 1.4f, 1.4f) }, MaterialOverride = material, Position = new Vector3(-4.5f + i * 1.5f, 0, 0), RotationDegrees = new Vector3(20, 30, 0) });
            }
            Require(MaterialBuilder.Build(JsonNode.Parse("{\"shading\":\"blinnphong\"}")!.AsObject(), null, null) is ShaderMaterial, "Blinn-Phong is always a shader (it lights the surface itself)");
            Require(MaterialBuilder.Build(JsonNode.Parse("{\"color\":\"#FF0000\"}")!.AsObject(), null, null) is StandardMaterial3D, "an old plain object still builds the plain material");
            Require(MaterialBuilder.Build(JsonNode.Parse("{\"builtin\":\"bricks\",\"noTexture\":true,\"color\":\"#FFFFFF\"}")!.AsObject(), null, null) is StandardMaterial3D, "a cleared object has no texture");
            // a face cleared on its own is blank although the object has a texture, and a face with its own texture is not blank although the object was cleared
            var layer = JsonNode.Parse("{\"builtin\":\"bricks\"}")!.AsObject(); var blankFace = JsonNode.Parse("{\"index\":2,\"noTexture\":true,\"color\":\"#FFFFFF\"}")!.AsObject();
            Require(MaterialBuilder.Build(layer, blankFace, null) is StandardMaterial3D && MaterialBuilder.Build(layer, null, null) is ShaderMaterial, "a face can be blank while the object keeps its texture");
            var cleared = JsonNode.Parse("{\"noTexture\":true,\"color\":\"#FFFFFF\"}")!.AsObject(); var texturedFace = JsonNode.Parse("{\"index\":3,\"builtin\":\"planks\"}")!.AsObject();
            Require(MaterialBuilder.Build(cleared, texturedFace, null) is ShaderMaterial, "a face with its own texture shows it on a cleared object");
            var (repeat, offset, rotation) = MaterialBuilder.MapTransform(JsonNode.Parse("{\"uvRepeatX\":2,\"uvRotation\":15,\"mapUv\":{\"normalMap\":{\"repeatX\":5,\"offsetY\":0.25}}}")!.AsObject(), null, "normalMap");
            Require(repeat.X == 5 && repeat.Y == 1 && offset.Y == .25f && rotation == 15, "a map overrides only what it sets, the rest is the layer's");
            Require(MaterialBuilder.MapTransform(JsonNode.Parse("{\"uvRepeatX\":2}")!.AsObject(), null, "texture").Repeat.X == 2, "maps without their own transform follow the layer");
            await Frames(6);
            var path = OS.GetEnvironment("LIFEVERSE_CAPTURE_PATH");
            if (!string.IsNullOrWhiteSpace(path) && DisplayServer.GetName() != "headless") GetViewport().GetTexture().GetImage().SavePng($"{path}-materials.png");
            GD.Print("event=shader_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=shader_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
