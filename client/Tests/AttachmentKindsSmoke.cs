using Godot;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Building;
using LifeVerse.Shared.Items;
using LifeVerse.Shared.Interaction;

namespace LifeVerse.Client.Tests;

/// <summary>Not only 3D models can be worn: a picture becomes a flat sign and a saved construction brings its shapes (the file itself tells which it is).</summary>
public partial class AttachmentKindsSmoke : Node
{
    private static AttachmentData Data(Guid asset, string point) => new(Guid.NewGuid(), Guid.NewGuid(), asset, "Objeto", point, point, [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true);

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig); await Frames(5);

            var picture = Image.CreateEmpty(64, 32, false, Image.Format.Rgba8); picture.Fill(new Color("#12F2E0")); var png = picture.SavePngToBuffer();
            var sources = new[]
            {
                new SourceObject("cube", "Corpo", "", 0, .5f, 0, 0, 0, 0, .2f, 1f, .2f, "{\"color\":\"#F4A340\"}", "{}", "{}", "[]", null),
                new SourceObject("sphere", "Luz", "", 0, 1.2f, 0, 0, 0, 0, .4f, .4f, .4f, "{\"color\":\"#FFFFFF\",\"emission\":\"#FFE0A8\",\"emissionStrength\":2}", "{}", "{}", "[]", null)
            };
            var construction = System.Text.Encoding.UTF8.GetBytes(BuildPackages.Serialize(BuildPackages.Create("Poste", sources)));
            var picAsset = Guid.NewGuid(); var buildAsset = Guid.NewGuid(); var badAsset = Guid.NewGuid();
            AvatarRig.AttachmentModelProvider = asset => asset == picAsset ? ModelCache.ParseInstance(png) : asset == buildAsset ? ModelCache.ParseInstance(construction) : ModelCache.ParseInstance([1, 2, 3, 4]);

            // ---- a picture is a flat sign with that picture, a few tens of centimetres wide
            var sign = Data(picAsset, "chest");
            rig.ApplyAttachments([sign]); await Frames(5);
            Require(rig.AttachmentStatus.GetValueOrDefault(sign.Id) == "ready", $"a picture can be worn: {rig.AttachmentStatus.GetValueOrDefault(sign.Id)}");
            var holder = rig.AttachmentHolder(sign.Id)!; var quad = holder.GetChild(0).GetNodeOrNull<MeshInstance3D>("Picture");
            Require(quad is { Mesh: QuadMesh { Size.X: > .9f } }, "it is a flat sign with the proportion of the picture (twice as wide as tall)");
            var width = rig.AttachmentSize(sign.Id); Require(width > .15f && width < .8f, $"and a sensible size: {width} m");

            // ---- a construction brings its shapes
            var post = Data(buildAsset, "pelvis");
            rig.ApplyAttachments([post]); await Frames(6);
            Require(rig.AttachmentStatus.GetValueOrDefault(post.Id) == "ready", $"a saved construction can be worn: {rig.AttachmentStatus.GetValueOrDefault(post.Id)}");
            var body = FindBody(rig.AttachmentHolder(post.Id)!);
            Require(body is { Shapes: 2 }, "with all of its shapes");
            Require(body!.GetChildren().OfType<BuildObjectView>().All(v => v.GetChildCount() > 0), "each one built");
            Require(body.GetNodeOrNull<TextureCache>("TextureCache") is not null, "and a place to fetch the pictures they use");
            var height = rig.AttachmentSize(post.Id); Require(height > .5f && height < 3f, $"it keeps its real size (about as tall as the poste): {height} m");

            // ---- what is not a model, a picture or a construction still says it could not be drawn
            var junk = Data(badAsset, "chest"); rig.ApplyAttachments([junk]); await Frames(4);
            Require(rig.AttachmentStatus.GetValueOrDefault(junk.Id)!.Contains("carregar"), "something else cannot be drawn and says so");

            // ---- the inventory offers it for pictures and constructions
            var services = new ContextServices { InventoryAction = _ => { } };
            foreach (var category in new[] { "models", "packages", "textures" })
            {
                var menu = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Item", category, ItemRights.All), services).Single(x => x.Label == "Mais opções").Children!;
                Require(menu[0].Enabled, $"{category} can be attached");
            }

            GD.Print("event=attachment_kinds_smoke result=passed");
            AvatarRig.AttachmentModelProvider = null; GetTree().Quit();
        }
        catch (Exception exception)
        {
            AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=attachment_kinds_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static ModelCache.ConstructionBody? FindBody(Node node)
    {
        if (node is ModelCache.ConstructionBody found) return found;
        foreach (var child in node.GetChildren()) if (FindBody(child) is { } inner) return inner;
        return null;
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
