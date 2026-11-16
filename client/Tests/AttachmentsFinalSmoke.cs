using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>The last part of the attachments: the list in "Meu Avatar", HUD objects on the screen, models that move by themselves, the limits that protect the frame rate, the light sway and the points a body has.</summary>
public partial class AttachmentsFinalSmoke : Node
{
    private static AttachmentData Data(string point = "head_top", Guid? id = null, float sway = 0, float[]? position = null, Guid? asset = null, string name = "Objeto") =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), asset ?? Guid.NewGuid(), name, point, point, position ?? [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true, Sway: sway);

    public override async void _Ready()
    {
        var manager = GetNode<AvatarManager>("/root/AvatarManager"); var previous = manager.Transport;
        try
        {
            var root = new Node3D(); AddChild(root);
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head"), .2f);
            var special = new Dictionary<Guid, Func<ModelInstance?>>();
            AvatarRig.AttachmentModelProvider = asset => special.TryGetValue(asset, out var make) ? make() : ModelCache.ParseInstance(glb);

            // ---- models that move by themselves, and lights and particles kept to what an avatar may have
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig); await Frames(5);
            var spinner = Guid.NewGuid(); var lamp = Guid.NewGuid();
            special[spinner] = () =>
            {
                var node = new Node3D(); var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.2f, .2f, .2f) } }; node.AddChild(mesh);
                var animation = new Animation { Length = 1f }; var track = animation.AddTrack(Animation.TrackType.Value); animation.TrackSetPath(track, "Mesh:rotation"); animation.TrackInsertKey(track, 0, Vector3.Zero); animation.TrackInsertKey(track, 1f, new Vector3(0, Mathf.Pi, 0));
                mesh.Name = "Mesh"; var library = new AnimationLibrary(); library.AddAnimation("spin", animation); var player = new AnimationPlayer { Name = "Player" }; player.AddAnimationLibrary("", library); node.AddChild(player);
                player.RootNode = new NodePath(".."); return new ModelInstance(node, Vector3.One, new Vector3(.2f, .2f, .2f));
            };
            special[lamp] = () =>
            {
                var node = new Node3D(); node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.2f, .2f, .2f) } });
                for (var i = 0; i < 4; i++) node.AddChild(new OmniLight3D()); for (var i = 0; i < 3; i++) node.AddChild(new GpuParticles3D());
                return new ModelInstance(node, Vector3.One, new Vector3(.2f, .2f, .2f));
            };
            var spin = Data("wrist_l", asset: spinner); var light = Data("head_top", asset: lamp);
            rig.ApplyAttachments([spin, light]); await Frames(10);
            Require(rig.AttachmentStatus[spin.Id] == "ready" && rig.AttachmentStatus[light.Id] == "ready", $"both are drawn: {string.Join(" | ", rig.AttachmentStatus.Values)}");
            var player1 = rig.AttachmentHolder(spin.Id)!.GetChild(0).GetNode<AnimationPlayer>("Player");
            Require(player1.IsPlaying() && player1.GetAnimation("spin").LoopMode == Animation.LoopModeEnum.Linear, "an animation inside the object plays, looping, together with the movement of the bone");
            var before = rig.AttachmentHolder(spin.Id)!.GetChild(0).GetNode<MeshInstance3D>("Mesh").Rotation.Y; await Frames(15);
            Require(Mathf.Abs(rig.AttachmentHolder(spin.Id)!.GetChild(0).GetNode<MeshInstance3D>("Mesh").Rotation.Y - before) > .01f, "and it really changes over time");
            var models = rig.AttachmentHolder(light.Id)!.GetChild(0);
            Require(models.GetChildren().OfType<Light3D>().Count(n => !n.IsQueuedForDeletion()) == AvatarRig.LightsPerAvatar && models.GetChildren().OfType<GpuParticles3D>().Count(n => !n.IsQueuedForDeletion()) == 2, "an avatar keeps at most two lights and two particle systems from its objects");

            // ---- the triangle budget: more is refused with a reason, and a smaller budget for the others
            var crowd = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(crowd); await Frames(5);
            Require(!crowd.OwnAvatar && AvatarRig.OthersTriangleBudget < AvatarRig.OwnTriangleBudget, "other people's avatars have the smaller budget");
            crowd.TriangleBudgetForTesting = 30; var first = Data("skull"); var second = Data("chest"); var third = Data("neck"); crowd.ApplyAttachments([first, second, third]); await Frames(8);
            Require(crowd.AttachmentsShown == 2 && crowd.AttachmentStatus.Values.Count(s => s.StartsWith("Acima do limite")) == 1 && crowd.AttachmentTriangles <= 30, $"what does not fit the budget is left out and says why: {string.Join(" | ", crowd.AttachmentStatus.Values)}");
            Require(AvatarRig.Triangles(rig.AttachmentHolder(spin.Id)!) == 12, "triangles are counted from the meshes");
            crowd.TriangleBudgetForTesting = null; crowd.ApplyAttachments([first]); await Frames(6);
            var meshOfOthers = crowd.AttachmentHolder(first.Id)!.GetChild(0); GeometryInstance3D? found = null; void Find(Node n) { if (n is GeometryInstance3D g && found is null) found = g; foreach (var c in n.GetChildren()) Find(c); } Find(meshOfOthers);
            Require(found is not null && Mathf.IsEqualApprox(found.VisibilityRangeEnd, AvatarRig.OthersRange), "other people's objects are not drawn from far away");
            rig.ApplyAttachments([]); crowd.ApplyAttachments([]); await Frames(4); Require(rig.AttachmentTriangles == 0 && crowd.AttachmentTriangles == 0, "taking objects off gives the budget back");

            // ---- a light sway while the body moves, none at zero
            var swayer = Data("chest", sway: .9f); var still = Data("back", sway: 0); rig.ApplyAttachments([swayer, still]); await Frames(8);
            for (var i = 0; i < 20; i++) { rig.Position += new Vector3(.12f, 0, 0); await Frames(1); }
            Require(rig.AttachmentSwayAngle(swayer.Id) > .02f && rig.AttachmentSwayAngle(still.Id) < .0001f, $"an object with sway leans against the movement, one without does not: {rig.AttachmentSwayAngle(swayer.Id)} / {rig.AttachmentSwayAngle(still.Id)}");
            await Frames(60); Require(rig.AttachmentSwayAngle(swayer.Id) < .02f, "and settles when the body stops"); rig.SetAttachmentSway(swayer.Id, 0); rig.Position = Vector3.Zero;
            rig.ApplyAttachments([]); await Frames(2);

            // ---- HUD objects: anchored to the screen
            var screen = new Vector2(1280, 720);
            Require(HudAttachmentLayer.Anchor("hud_top_left", screen, Vector2.Zero) == new Vector2(24, 24) && HudAttachmentLayer.Anchor("hud_bottom_right", screen, Vector2.Zero) == new Vector2(1280 - 160 - 24, 720 - 160 - 24) && HudAttachmentLayer.Anchor("hud_center", screen, Vector2.Zero) == new Vector2(560, 280), "each HUD point has its place on the screen");
            Require(HudAttachmentLayer.Anchor("hud_top", screen, new Vector2(.1f, .1f)) == new Vector2(560 + 20, 24 - 20), "the position moves the object from its anchor (up is up)");
            var items = new List<object>();
            object Row(AttachmentData d) => new { id = d.Id, inventoryItemId = d.InventoryItemId, assetId = d.AssetId, name = d.Name, point = d.Point, pointName = d.Point, position = d.Position, rotation = d.Rotation, scale = d.Scale, equipped = true, order = 0, revision = 1, canModify = true };
            var hud = Data("hud_top_left", name: "Medidor"); var body = Data("skull", name: "Chapéu"); items.Add(Row(hud)); items.Add(Row(body));
            var profiles = new List<string> { "humanoid" }; var deleted = new List<string>();
            manager.Transport = (method, path, req) =>
            {
                if (method == Godot.HttpClient.Method.Get && path == "/attachments") return (200, JsonSerializer.Serialize(new { profiles, attachments = items }));
                if (method == Godot.HttpClient.Method.Delete && path.StartsWith("/attachments/")) { deleted.Add(path); var id = Guid.Parse(path["/attachments/".Length..]); items.RemoveAll(o => (Guid)o.GetType().GetProperty("id")!.GetValue(o)! == id); return (200, "{}"); }
                return (404, "{}");
            };
            var layer = new HudAttachmentLayer { Name = "HudAttachments" }; AddChild(layer); manager.Attachments.Refresh(); await Frames(10);
            Require(layer.Shown == 1 && layer.Holder(hud.Id) is { } picture && picture.Position.IsEqualApprox(new Vector2(24, 24)) && picture.MouseFilter == Control.MouseFilterEnum.Ignore, "a HUD object is on the screen where its point is, and the mouse goes through it");
            Require(layer.Holder(body.Id) is null, "an object on the body is not on the screen");

            // ---- the list in "Meu Avatar → Anexos"
            var page = new AttachmentsPage { World = this }; AddChild(page); await Frames(3);
            Require(page.Count == 2 && page.Rows.Count == 4 && page.Rows[0] is null && page.Rows[2] is null, $"the objects are grouped under their regions: {page.Rows.Count} rows");
            page.Select(body.Id); Require(page.Selected?.Id == body.Id && page.Note.Contains("Chapéu"), "choosing one tells what it is and where it sits");
            profiles.Add("tail"); manager.Attachments.Refresh(); Require(manager.Attachments.Profiles.Contains("tail"), "the store knows the profiles of the body");
            var window = new AttachPointWindow { Name = "AttachPointWindow" }; AddChild(window); await Frames(2);
            window.Open("Rabo", string.Empty, _ => 0, (_, _) => { }, false, manager.Attachments.Profiles); window.ShowRegion("special"); await Frames(2);
            Require(window.VisiblePoints.Contains("tail") && !window.VisiblePoints.Contains("wing_l"), "a body with a tail offers the tail point and only that");
            window.ShowRegion("hud"); await Frames(2); Require(window.VisiblePoints.Count == 8 && window.VisiblePoints.All(p => p.StartsWith("hud_")), "the HUD points have their own tab");
            window.Open("Rabo", string.Empty, _ => 0, (_, _) => { }, false, []); window.ShowRegion("special"); await Frames(2); Require(!window.VisiblePoints.Contains("tail"), "a body without it does not");
            AttachmentActions.Run(this, "detach", page.Selected!, _ => { }); await Frames(3);
            Require(deleted.Count == 1 && page.Count == 1 && !manager.Attachments.IsWorn(body.InventoryItemId), "removing from the list asks the server and the list follows");
            var moved = new AttachmentData(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Rabo", "pelvis_back", "Pelve traseira", [0, 0, 0], [0, 0, 0], [1, 1, 1], true, 0, 1, true, "Cauda");
            Require(moved.Describe().Contains("o corpo atual não tem Cauda") && AttachmentData.Parse(JsonDocument.Parse("{\"id\":\"" + Guid.NewGuid() + "\",\"inventoryItemId\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + Guid.NewGuid() + "\",\"point\":\"back\",\"originalPointName\":\"Asa esquerda\",\"sway\":0.4}").RootElement) is { OriginalPointName: "Asa esquerda", Sway: > .39f and < .41f }, "an object moved to another point because of the body says where it was meant to be");

            // ---- the page is a tab of the appearance window
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world); await Frames(20);
            var appearance = new AppearanceWindow { Name = "AppearanceWindow", Player = world.GetNode<PlayerController>("Player") }; world.AddChild(appearance); await Frames(3);
            Require(appearance.AttachmentsPageNode is not null && appearance.AttachmentsPageNode.World == world, "Meu Avatar has the Anexos tab");

            GD.Print("event=attachments_final_smoke result=passed");
            AvatarRig.AttachmentModelProvider = null; manager.Transport = previous; GetTree().Quit();
        }
        catch (Exception exception)
        {
            manager.Transport = previous; AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=attachments_final_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
