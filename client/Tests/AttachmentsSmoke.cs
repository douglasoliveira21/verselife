using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Gameplay;
using LifeVerse.Shared.Interaction;

namespace LifeVerse.Client.Tests;

/// <summary>Objects worn on the body: they follow their bone through animations and keep their placement, the store talks to the server and shows its refusals, and the window of points offers every region.</summary>
public partial class AttachmentsSmoke : Node
{
    private static AttachmentData Data(Guid? id = null, string point = "head_top", float[]? position = null, float[]? rotation = null, float[]? scale = null, bool equipped = true, Guid? asset = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), asset ?? Guid.NewGuid(), "Objeto", point, point, position ?? [0, 0, 0], rotation ?? [0, 0, 0], scale ?? [1, 1, 1], equipped, 0, 1, true);

    public override async void _Ready()
    {
        var avatars = GetNode<AvatarManager>("/root/AvatarManager"); var previous = avatars.Transport;
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(5);
            var k = 1.8f / 1.78f;
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head"), .2f); var missing = Guid.NewGuid();
            AvatarRig.AttachmentModelProvider = asset => asset == missing ? null : ModelCache.ParseInstance(glb);

            // ---- a hat at the top of the head follows the head
            var hat = Data(point: "head_top");
            rig.ApplyAttachments([hat]); await Frames(4);
            Require(rig.AttachmentsShown == 1 && rig.AttachmentStatus[hat.Id] == "ready", $"the hat is on the avatar: {(rig.AttachmentStatus.TryGetValue(hat.Id, out var s) ? s : "none")}");
            var holder = rig.AttachmentHolder(hat.Id)!;
            Require(holder.GlobalPosition.Y > 1.55f * k && holder.GlobalPosition.Y < 2.0f && Mathf.Abs(holder.GlobalPosition.X) < .06f, $"the hat sits at the top of the head: {holder.GlobalPosition}");
            var restScale = holder.GlobalBasis.Scale.X; Require(restScale > .05f && restScale < 1.5f, $"the object has a sensible size: {restScale}");
            rig.SetMotionState(AvatarMotionState.Run, true); await Frames(25);
            Require(holder.GlobalPosition.Y > 1.4f && holder.GlobalPosition.Y < 2.1f, "the hat stays with the head while running");
            Require(Mathf.Abs(holder.GlobalBasis.Scale.X - restScale) < .001f, "an animation does not change the scale");
            rig.SetMotionState(AvatarMotionState.Idle, true); await Frames(10);

            // ---- the placement is relative to the point: position, rotation and scale
            var before = holder.GlobalPosition;
            var moved = Data(hat.Id, "head_top", position: [0, .5f, 0], scale: [2, 2, 2], rotation: [0, 90, 0]);
            rig.ApplyAttachments([moved]); await Frames(4); holder = rig.AttachmentHolder(hat.Id)!;
            Require(Mathf.Abs(holder.GlobalPosition.Y - before.Y - .5f * k) < .05f, $"the position is relative to the point: {holder.GlobalPosition.Y - before.Y}");
            Require(Mathf.Abs(holder.GlobalBasis.Scale.X - restScale * 2) < .01f, "the scale is the one set");
            Require(Mathf.Abs(holder.GlobalBasis.GetEuler().Y) > .3f, "the rotation is the one set");
            rig.Rotation = new Vector3(0, Mathf.Pi / 2, 0); await Frames(4);
            Require(Mathf.Abs(rig.AttachmentHolder(hat.Id)!.GlobalPosition.X - rig.GlobalPosition.X) < .1f, "turning the avatar turns the object with it");
            rig.Rotation = Vector3.Zero; await Frames(2);

            // ---- two objects at the same point, one on the right hand and one off the body
            var a = Data(point: "skull"); var b = Data(point: "skull"); var hand = Data(point: "hand_r"); var off = Data(point: "chest", equipped: false);
            rig.ApplyAttachments([a, b, hand, off]); await Frames(6);
            Require(rig.AttachmentsShown == 3 && rig.AttachmentHolder(off.Id) is null, "what is worn is drawn, what is off the body is not");
            Require(rig.AttachmentHolder(hand.Id)!.GlobalPosition.X > .1f, $"the right hand is on the character's right (+X facing -Z): {rig.AttachmentHolder(hand.Id)!.GlobalPosition}");
            Require(Math.Abs(rig.AttachmentHolder(a.Id)!.GlobalPosition.Y - rig.AttachmentHolder(b.Id)!.GlobalPosition.Y) < .001f, "both objects of a point sit on it, each with its own placement");

            // ---- what cannot be drawn says why
            var hud = Data(point: "hud_center"); var lost = Data(asset: missing); var odd = Data(point: "nowhere");
            rig.ApplyAttachments([hud, lost, odd]); await Frames(4);
            Require(rig.AttachmentsShown == 0 && rig.AttachmentStatus[hud.Id].StartsWith("HUD") && rig.AttachmentStatus[lost.Id].Contains("carregar") && rig.AttachmentStatus[odd.Id].Contains("não existe"), $"each reason is told: {string.Join(" | ", rig.AttachmentStatus.Values)}");

            // ---- the look being applied again keeps what is worn
            rig.ApplyAttachments([a]); await Frames(3); rig.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Teste" }); await Frames(6);
            Require(rig.AttachmentsShown == 1 && rig.AttachmentStatus[a.Id] == "ready", "the attachments stay when the look is applied again");
            rig.ApplyAttachments([]); await Frames(2); Require(rig.AttachmentsShown == 0, "taking everything off removes the objects");


            // ---- other players: their objects come inside their look, and a live preview moves them
            var remote = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); remote.Position = new Vector3(3, 0, 0); root.AddChild(remote); await Frames(4);
            var theirs = Data(point: "skull"); string Body(params AttachmentData[] list) => JsonSerializer.Serialize(list.Select(a => new { id = a.Id, inventoryItemId = a.InventoryItemId, assetId = a.AssetId, name = a.Name, point = a.Point, pointName = a.PointName, position = a.Position, rotation = a.Rotation, scale = a.Scale, equipped = a.Equipped, order = 0, revision = 1, canModify = true }));
            remote.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro", BodyAttachmentsJson = Body(theirs) }); await Frames(8);
            Require(remote.AttachmentsShown == 1 && remote.AttachmentStatus[theirs.Id] == "ready", "the objects another player wears are drawn on their avatar");
            var before2 = remote.AttachmentHolder(theirs.Id)!.GlobalPosition.Y;
            remote.PreviewRemoteAttachment(theirs.Id, new Vector3(0, .3f, 0), Vector3.Zero, Vector3.One); await Frames(2);
            Require(Mathf.Abs(remote.AttachmentHolder(theirs.Id)!.GlobalPosition.Y - before2 - .3f * k) < .03f, "a live preview moves the object on the other avatar");
            remote.PreviewRemoteAttachment(theirs.Id, new Vector3(0, 99, 0), Vector3.Zero, Vector3.One); remote.PreviewRemoteAttachment(theirs.Id, new Vector3(float.NaN, 0, 0), Vector3.Zero, Vector3.One); await Frames(2);
            Require(Mathf.Abs(remote.AttachmentHolder(theirs.Id)!.GlobalPosition.Y - before2 - .3f * k) < .03f, "values the server would never accept are ignored");
            remote.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro" }); await Frames(4);
            Require(remote.AttachmentsShown == 1, "a look that does not say anything about objects leaves the worn ones as they are");
            remote.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro", BodyAttachmentsJson = Body(theirs) }); await Frames(8);
            Require(Mathf.Abs(remote.AttachmentHolder(theirs.Id)!.GlobalPosition.Y - before2) < .03f, "the saved look replaces the preview");
            remote.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro", BodyAttachmentsJson = "[]" }); await Frames(4);
            Require(remote.AttachmentsShown == 0, "taking the object off removes it on their avatar");
            remote.ApplyAppearance(new LifeVerse.Client.Models.AvatarAppearanceData { DisplayName = "Outro", BodyAttachmentsJson = Body(theirs, theirs) }); await Frames(8);
            Require(remote.AttachmentsShown == 1, "the same attachment sent twice is drawn once (no duplicates after a reconnection)");
            Require(AttachmentData.ParseArray("not json").Count == 0 && AttachmentData.ParseArray("{}").Count == 0 && AttachmentData.ParseArray("[{\"x\":1}]").Count == 0, "bad data from the wire is ignored");
            // ---- the store: server answers, list read back, refusals explained
            var worn = new List<object>(); var seen = new List<string>(); var itemId = Guid.NewGuid(); var changed = 0; var asset1 = Guid.NewGuid();
            avatars.Transport = (method, path, body) =>
            {
                seen.Add($"{method} {path}");
                if (method == Godot.HttpClient.Method.Get && path == "/attachments") return (200, JsonSerializer.Serialize(new { attachments = worn }));
                if (method == Godot.HttpClient.Method.Post && path == "/attachments")
                {
                    var d = JsonDocument.Parse(body!).RootElement; if (d.GetProperty("point").GetString() == "mouth") return (409, "{\"error\":\"too_many_at_point\"}");
                    worn.Add(new { id = Guid.NewGuid(), inventoryItemId = d.GetProperty("inventoryItemId").GetGuid(), assetId = asset1, name = "Óculos", point = d.GetProperty("point").GetString(), pointName = "Rosto", position = new[] { 0, 0, 0 }, rotation = new[] { 0, 0, 0 }, scale = new[] { 1, 1, 1 }, equipped = true, order = 0, revision = 1, canModify = true });
                    return (201, "{}");
                }
                if (method == Godot.HttpClient.Method.Delete) { worn.Clear(); return (200, "{}"); }
                return (404, "{\"error\":\"not_found\"}");
            };
            var store = avatars.Attachments; store.Changed += () => changed++;
            string? result = null; store.Attach(itemId, "face", false, (ok, text) => result = ok ? "ok" : text);
            Require(result == "ok" && store.IsWorn(itemId) && store.Items[0].Point == "face" && store.WornAt("face") == 1 && changed >= 1, $"attaching asks the server and reads the list back: {result}");
            Require(seen.SequenceEqual(["Post /attachments", "Get /attachments"]), $"one change, one read: {string.Join(", ", seen)}");
            store.Attach(Guid.NewGuid(), "mouth", false, (ok, text) => result = ok ? "ok" : text); Require(result!.Contains("ponto já está cheio"), $"a refusal is explained in Portuguese: {result}");
            Require(AttachmentStore.Explain(403, "{\"error\":\"no_modify_right\"}").Contains("direito") && AttachmentStore.Explain(0, "").Contains("conexão") && AttachmentStore.Explain(500, "x").Contains("500"), "other refusals are explained too");
            store.Detach(store.Items[0].Id, (ok, _) => result = ok ? "ok" : "no"); Require(result == "ok" && !store.IsWorn(itemId) && store.Items.Count == 0, "taking off asks the server and reads the list back");
            var parsed = AttachmentData.ParseList("{\"attachments\":[{\"id\":\"" + Guid.NewGuid() + "\",\"inventoryItemId\":\"" + Guid.NewGuid() + "\",\"assetId\":\"" + Guid.NewGuid() + "\",\"point\":\"hand_r\",\"position\":[1,2,3],\"rotation\":[10,20,30],\"scale\":[2,2,2],\"equipped\":true,\"canModify\":false},{\"nonsense\":1}]}");
            Require(parsed.Count == 1 && parsed[0].Rotation[2] == 30 && parsed[0].Scale[0] == 2 && !parsed[0].CanModify && AttachmentData.ParseList("not json").Count == 0, "the list is read as the server writes it, and bad data is ignored");

            // ---- the window of points: every region, every point of it, the choice and the replace switch
            var window = new AttachPointWindow { Name = "AttachPointWindow" }; AddChild(window); await Frames(2);
            string? picked = null; var replaced = false;
            window.Open("Chapéu", string.Empty, point => point == "skull" ? 2 : 0, (point, replace) => { picked = point; replaced = replace; });
            Require(window.IsOpen && window.Region == "head" && window.VisiblePoints.Contains("skull") && window.VisiblePoints.Contains("mouth") && window.VisiblePoints.Count == AttachmentPoints.For(null).Count(p => p.Region == "head"), $"the head points are offered: {window.VisiblePoints.Count}");
            foreach (var (region, expected) in new[] { ("torso", "back"), ("arms", "hand_r"), ("legs", "foot_l"), ("special", "avatar_center") }) { window.ShowRegion(region); await Frames(2); Require(window.VisiblePoints.Contains(expected), $"{region} offers {expected}"); }
            window.ShowRegion("special"); await Frames(2); Require(!window.VisiblePoints.Any(p => p.StartsWith("hud_")) && !window.VisiblePoints.Contains("tail"), "HUD points and points the body lacks are not offered");
            window.ShowRegion("legs"); await Frames(2); window.ReplaceChecked = true; Require(window.Pick("foot_l") && picked == "foot_l" && replaced, "choosing a point reports it and the replace switch");
            window.Open("Chapéu", "foot_l", _ => 0, (_, _) => { }); await Frames(2); Require(window.Region == "legs" && !window.Pick("foot_l") is false, "the window opens on the region of the point the object is on");
            window.Close(); Require(!window.IsOpen, "the window closes");

            // ---- the radial menu of an inventory item
            var actions = new List<int>(); var services = new ContextServices { InventoryAction = action => actions.Add(action), IsAttached = id => id == itemId };
            var free = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Chapéu", "models", LifeVerse.Shared.Items.ItemRights.All), services).Single(x => x.Label == "Mais opções").Children!;
            Require(free.Select(x => x.Label).Take(3).SequenceEqual(["Anexar em…", "Adicionar ao corpo", "Substituir anexo"]) && free.Take(3).All(x => x.Enabled), "a model offers to be attached");
            free[0].Run!(); free[1].Run!(); free[2].Run!(); Require(actions.SequenceEqual([30, 31, 32]), $"the slices run the inventory's attach actions: {string.Join(",", actions)}");
            var worn2 = InteractionRegistry.Build(new InventoryItemTarget(itemId, "Chapéu", "models", LifeVerse.Shared.Items.ItemRights.All), services).Single(x => x.Label == "Mais opções").Children!;
            Require(worn2.Take(4).Select(x => x.Label).SequenceEqual(["Editar anexo", "Alterar ponto", "Substituir anexo", "Remover do corpo"]), "a worn object offers to edit, change its point or come off");
            var texture = InteractionRegistry.Build(new InventoryItemTarget(Guid.NewGuid(), "Trovão", "sounds", LifeVerse.Shared.Items.ItemRights.All), services).Single(x => x.Label == "Mais opções").Children!;
            Require(!texture[0].Enabled, "a sound cannot be attached");

            GD.Print("event=attachments_smoke result=passed");
            AvatarRig.AttachmentModelProvider = null; avatars.Transport = previous;
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            avatars.Transport = previous; AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=attachments_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
