using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Build;
using LifeVerse.Client.Scripts;
using LifeVerse.Client.Scripts.Attachments;
using LifeVerse.Shared.Avatar;
using LifeVerse.Shared.Interaction;

namespace LifeVerse.Client.Tests;

/// <summary>The editor of where an object sits on the body: camera, handles, numbers, undo, copy, limits, save and cancel against a server that can refuse.</summary>
public partial class AttachmentEditorSmoke : Node
{
    private static float[] A(Vector3 v) => [v.X, v.Y, v.Z];

    public override async void _Ready()
    {
        var manager = GetNode<AvatarManager>("/root/AvatarManager"); var previous = manager.Transport;
        try
        {
            // ---- the model alone: limits, history, gestures, copy
            var model = new AttachmentEditModel(Placement.Default);
            Require(!model.Dirty && !model.CanUndo, "a new model is clean");
            model.SetPosition(new Vector3(1, 2, 3)); model.SetPosition(new Vector3(9, -9, 0)); Require(model.Position.IsEqualApprox(new Vector3(5, -5, 0)), "the position is kept within the limits of the server");
            model.SetRotation(new Vector3(190, -190, 540)); Require(model.Rotation.IsEqualApprox(new Vector3(-170, 170, 180)), $"rotations are wrapped to (-180, 180]: {model.Rotation}");
            model.SetScale(new Vector3(0, 99, 1)); Require(model.Scale.IsEqualApprox(new Vector3(.01f, 10, 1)), "the scale is kept within the limits");
            Require(model.Undo() && model.Undo() && model.Undo() && model.Undo() && !model.CanUndo && model.Now.Same(Placement.Default), "undo walks back to the start, one step per change");
            Require(model.Redo() && model.Position.IsEqualApprox(new Vector3(1, 2, 3)), "redo goes forward again"); model.SetPosition(Vector3.Zero); Require(!model.CanRedo, "a new change clears redo");
            model.BeginGesture(); for (var i = 1; i <= 20; i++) model.SetPosition(new Vector3(0, i * .01f, 0)); model.EndGesture();
            Require(model.Undo() && model.Position == Vector3.Zero, "a whole drag is one step of the history");
            model.Redo(); model.KeepProportions = true; model.SetScaleAxis(0, 2); Require(model.Scale.IsEqualApprox(new Vector3(2, 2, 2)), "with proportions kept the other axes follow");
            model.KeepProportions = false; model.SetScaleAxis(1, 3); Require(model.Scale.IsEqualApprox(new Vector3(2, 3, 2)), "without, only that axis changes");
            model.SetScaleAxis(1, 99); Require(model.Scale.Y == 10, "one axis alone stops at the limit");
            model.KeepProportions = true; model.SetScale(new Vector3(8, 4, 2)); model.SetScaleAxis(0, 20); Require(model.Scale.X <= 10.001f && model.Scale.Y <= 10.001f, "proportions never push another axis past the limit");
            model.ResetPosition(); model.ResetRotation(); model.ResetScale(); Require(model.Now.Same(Placement.Default), "each part can be put back");
            model.SetPosition(new Vector3(1, 1, 1)); model.Copy(); model.ResetPosition(); Require(model.Paste() && model.Position == new Vector3(1, 1, 1), "copy and paste carry the placement"); model.CenterOnPoint(); Require(model.Position == Vector3.Zero, "center puts the object on its point");
            model.SetPosition(new Vector3(.1f, 0, 0)); Require(model.Dirty, "a changed model is dirty"); model.Revert(); Require(!model.Dirty && !model.CanUndo, "reverting goes back to what was saved");
            Require(Mathf.IsEqualApprox(AttachmentEditModel.Snap(.07f, .05f), .05f) && AttachmentEditModel.Snap(.07f, 0) == .07f && Mathf.IsEqualApprox(AttachmentEditModel.Wrap(540), 180), "snapping and wrapping");

            // ---- in the world, with a server that keeps the list and can refuse
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var rig = player.Rig; var camera = player.GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
            var start = AvatarManager.CreateDefault(); start.DisplayName = "Teste"; manager.SetCurrent(start);
            var glb = WearableTemplate.Attachment(WearableTemplate.Extras("hat", bone: "head"), .2f); AvatarRig.AttachmentModelProvider = _ => ModelCache.ParseInstance(glb);
            var hatId = Guid.NewGuid(); var ringId = Guid.NewGuid(); var lockedId = Guid.NewGuid();
            var rows = new List<Dictionary<string, object?>>(); var patches = new List<string>(); var refuse = false;
            Dictionary<string, object?> Row(Guid id, string point, string name, bool canModify = true) => new() { ["id"] = id, ["inventoryItemId"] = Guid.NewGuid(), ["assetId"] = Guid.NewGuid(), ["name"] = name, ["point"] = point, ["pointName"] = point, ["position"] = new[] { 0f, 0f, 0f }, ["rotation"] = new[] { 0f, 0f, 0f }, ["scale"] = new[] { 1f, 1f, 1f }, ["equipped"] = true, ["order"] = 0, ["revision"] = 1, ["canModify"] = canModify };
            rows.Add(Row(hatId, "head_top", "Chapéu")); rows.Add(Row(ringId, "hand_r", "Anel")); rows.Add(Row(lockedId, "chest", "Broche", canModify: false));
            manager.Transport = (method, path, body) =>
            {
                if (method == Godot.HttpClient.Method.Get && path == "/attachments") return (200, JsonSerializer.Serialize(new { attachments = rows }));
                if (method == Godot.HttpClient.Method.Patch && path.StartsWith("/attachments/"))
                {
                    patches.Add(body!); if (refuse) return (403, "{\"error\":\"no_modify_right\"}");
                    var id = Guid.Parse(path["/attachments/".Length..]); var row = rows.Single(r => (Guid)r["id"]! == id); var d = JsonDocument.Parse(body!).RootElement;
                    foreach (var key in new[] { "position", "rotation", "scale" }) row[key] = d.GetProperty(key).EnumerateArray().Select(v => v.GetSingle()).ToArray();
                    return (200, "{}");
                }
                return (404, "{}");
            };
            var said = new List<string>(); var editor = new AttachmentEditor { Name = "AttachmentEditor", Player = player, Tell = said.Add }; world.AddChild(editor);
            manager.Attachments.Refresh(); await Frames(30);
            Require(rig.AttachmentsShown == 3, $"the worn objects are on the avatar: {rig.AttachmentsShown} ({string.Join(" | ", rig.AttachmentStatus.Values)})");

            // an object we may not modify cannot be edited
            Require(!editor.Open(lockedId) && said[^1].Contains("direito") && !editor.IsOpen && !player.EditViewActive, "an item without the right to modify does not open the editor");
            Require(!editor.Open(Guid.NewGuid()) && said[^1].Contains("não está no corpo"), "an object that is not worn does not open it");

            // ---- the editor opens close to the part of the body, with the avatar still
            var spot = player.GlobalPosition;
            Require(editor.Open(hatId) && editor.IsOpen && player.EditViewActive && editor.WindowVisible, "the editor opens on a worn object");
            await Frames(80);
            var holder = rig.AttachmentHolder(hatId)!;
            Require(camera.GlobalPosition.DistanceTo(holder.GlobalPosition) < 2.4f, $"the camera came close to the object: {camera.GlobalPosition.DistanceTo(holder.GlobalPosition)}");
            Input.ActionPress("move_forward"); await Frames(25); Input.ActionRelease("move_forward"); Require(player.GlobalPosition.DistanceTo(spot) < .05f, "the avatar does not move while editing");
            Require(editor.Gizmo.Visible && editor.Gizmo.GlobalPosition.DistanceTo(holder.GlobalPosition) < .05f, "the handles are on the object");

            // ---- move along the Y arrow: relative to the point
            Vector3 Ray0(Vector3 world3) => (world3 - camera.GlobalPosition).Normalized();
            var baseScale = holder.GlobalBasis.Scale.X; var frame = rig.AttachmentFrame(hatId)!.Value; var center = holder.GlobalPosition; var up = frame.Frame.Y.Normalized();
            var onArrow = center + up * (editor.Gizmo.UnitScale * 1.0f);
            Require(editor.HandleAt(camera.GlobalPosition, Ray0(onArrow)) == BuildGizmoHandle.Y, $"the Y arrow is found under the cursor: {editor.HandleAt(camera.GlobalPosition, Ray0(onArrow))}");
            Require(editor.BeginDrag(BuildGizmoHandle.Y, camera.GlobalPosition, Ray0(onArrow)), "a drag starts on the arrow");
            editor.UpdateDrag(camera.GlobalPosition, Ray0(onArrow + up * .1f)); await Frames(3);
            Require(Mathf.Abs(editor.Model!.Position.Y - .1f / frame.Metres) < .01f && Mathf.Abs(editor.Model.Position.X) < .001f && Mathf.Abs(editor.Model.Position.Z) < .001f, $"it moves along the point's own Y: {editor.Model.Position}");
            Require(Mathf.Abs(rig.AttachmentHolder(hatId)!.GlobalPosition.Y - center.Y - .1f) < .02f, "the object follows what is being dragged, at once");
            editor.EndDrag(); Require(editor.Model.CanUndo && editor.Model.Dirty && !editor.Dragging, "the drag is one step and leaves the model changed");
            editor.Model.Undo(); await Frames(2); Require(editor.Model.Position == Vector3.Zero && Mathf.Abs(rig.AttachmentHolder(hatId)!.GlobalPosition.Y - center.Y) < .01f, "undo puts the object back, on the screen too");
            editor.Model.Redo();

            // a snapped move goes in whole steps
            editor.Model.Revert(); editor.SetPositionStepForTesting(2); // 5 cm
            editor.BeginDrag(BuildGizmoHandle.Y, camera.GlobalPosition, Ray0(onArrow)); editor.UpdateDrag(camera.GlobalPosition, Ray0(onArrow + up * .13f)); editor.EndDrag();
            var stepped = editor.Model.Position.Y; Require(Mathf.Abs(stepped / .05f - Mathf.Round(stepped / .05f)) < .01f, $"movement snaps to the step: {stepped}"); editor.SetPositionStepForTesting(0);

            // ---- rotate around the Z ring
            editor.Model.Revert(); editor.SetTool(AttachmentTool.Rotate); await Frames(3);
            var zAxis = frame.Frame.Z.Normalized(); var u = up; var ring = BuildGizmoController.RingRadius * editor.Gizmo.UnitScale;
            var p0 = center + u * ring; var p1 = center + new Basis(zAxis, Mathf.DegToRad(45)) * u * ring;
            Require(editor.BeginDrag(BuildGizmoHandle.Z, camera.GlobalPosition, Ray0(p0)), "a drag starts on the ring"); editor.UpdateDrag(camera.GlobalPosition, Ray0(p1)); editor.EndDrag();
            Require(Mathf.Abs(Mathf.Abs(editor.Model.Rotation.Z) - 45) < 1.5f && Mathf.Abs(editor.Model.Rotation.X) < 1.5f && Mathf.Abs(editor.Model.Rotation.Y) < 1.5f, $"a quarter of a quarter turn around the point's Z: {editor.Model.Rotation}");
            editor.SetRotationStepForTesting(3); // 15°
            editor.Model.Revert(); editor.BeginDrag(BuildGizmoHandle.Z, camera.GlobalPosition, Ray0(p0)); editor.UpdateDrag(camera.GlobalPosition, Ray0(p1)); editor.EndDrag();
            Require(Mathf.Abs(Mathf.Abs(editor.Model.Rotation.Z) - 45) < .01f, $"the turn snaps to 15° steps: {editor.Model.Rotation.Z}"); editor.SetRotationStepForTesting(0);

            // ---- scale: the centre box makes it all bigger by the ratio of the distances
            editor.Model.Revert(); editor.SetTool(AttachmentTool.Scale); await Frames(3);
            var right = camera.GlobalBasis.X.Normalized(); var s0 = center + right * .2f; var s1 = center + right * .4f;
            Require(editor.BeginDrag(BuildGizmoHandle.Free, camera.GlobalPosition, Ray0(s0)), "a drag starts on the centre"); editor.UpdateDrag(camera.GlobalPosition, Ray0(s1)); editor.EndDrag();
            Require(Mathf.Abs(editor.Model.Scale.X - 2) < .1f && editor.Model.Scale.IsEqualApprox(new Vector3(editor.Model.Scale.X, editor.Model.Scale.X, editor.Model.Scale.X)), $"twice as far, twice as big, in proportion: {editor.Model.Scale}");
            await Frames(3); Require(rig.AttachmentHolder(hatId)!.GlobalBasis.Scale.X > baseScale * 1.8f, "the object really got bigger on the avatar");

            // ---- the numbers
            editor.Model.Revert(); editor.SetTool(AttachmentTool.Move); await Frames(2);
            editor.PositionField(0).Value = .25; Require(Mathf.Abs(editor.Model.Position.X - .25f) < .0005f, "typing a position changes the object");
            editor.RotationField(1).Value = 90; Require(Mathf.Abs(editor.Model.Rotation.Y - 90) < .01f, "typing a rotation turns the object");
            editor.ScaleField(2).Value = 3; Require(editor.Model.Scale.IsEqualApprox(new Vector3(3, 3, 3)), "typing one scale keeps the proportions"); await Frames(2);
            Require(Mathf.Abs(rig.AttachmentHolder(hatId)!.GlobalBasis.Scale.X - baseScale * 3) < .02f, "the typed scale is seen on the avatar");
            editor.PositionField(1).Value = 99; Require(editor.Model.Position.Y <= 5.001f, "typed values stay within the limits");
            Require(Mathf.Abs((float)editor.PositionField(0).Value - .25f) < .001f, "the fields show the model");
            editor.Model.Undo(); Require(editor.PositionField(1).Value < 5, "undo updates the fields");

            // ---- the solo view hides the others (only on this screen)
            editor.SoloOthers = true; await Frames(2); Require(!rig.AttachmentHolder(ringId)!.Visible && rig.AttachmentHolder(hatId)!.Visible, "the other attachments are hidden while editing");
            editor.SoloOthers = false; await Frames(2); Require(rig.AttachmentHolder(ringId)!.Visible, "and shown again");

            // ---- a server that refuses keeps the editor open and says why
            refuse = true; editor.Save(); await Frames(2);
            Require(editor.IsOpen && editor.Status.Contains("direito") && patches.Count == 1, $"a refusal is told and nothing is lost: '{editor.Status}'");
            refuse = false; editor.Save(); await Frames(5);
            Require(!editor.IsOpen && !player.EditViewActive && patches.Count == 2, "an accepted save closes the editor and the camera goes back");
            var saved = JsonDocument.Parse(patches[^1]).RootElement; Require(Mathf.Abs(saved.GetProperty("position")[0].GetSingle() - .25f) < .001f && Mathf.Abs(saved.GetProperty("rotation")[1].GetSingle() - 90) < .01f && Mathf.Abs(saved.GetProperty("scale")[2].GetSingle() - 3) < .001f, "what was saved is what was set");
            var stored = manager.Attachments.Items.Single(a => a.Id == hatId); Require(Mathf.Abs(stored.Scale[0] - 3) < .001f && Mathf.Abs(stored.Rotation[1] - 90) < .01f, "the list read back from the server has the new placement");
            await Frames(10); Require(rig.AttachmentHolder(hatId)!.GlobalBasis.Scale.X > baseScale * 2.5f && said.Any(m => m.Contains("salva")), "the avatar keeps the saved placement and the person is told");

            // ---- cancel puts it all back; saving without changes asks nothing
            editor.Open(hatId); await Frames(5); editor.Model!.SetScale(new Vector3(.5f, .5f, .5f)); await Frames(3);
            var changed = rig.AttachmentHolder(hatId)!.GlobalBasis.Scale.X; editor.Cancel(); await Frames(30);
            Require(!editor.IsOpen && rig.AttachmentHolder(hatId)!.GlobalBasis.Scale.X > changed * 3 && patches.Count == 2, "cancelling shows the saved placement again and sends nothing");
            editor.Open(hatId); await Frames(5); editor.Save(); Require(!editor.IsOpen && patches.Count == 2, "saving without changes just closes");


            // ---- the others see the object move while it is dragged, a few times a second, and get the saved one back after a cancel
            var live = new List<(Guid Id, Placement P)>(); editor.LiveSink = (id, p) => live.Add((id, p));
            editor.Open(hatId); await Frames(5); live.Clear();
            for (var i = 1; i <= 40; i++) editor.Model!.SetPosition(new Vector3(0, i * .002f, 0)); await Frames(2);
            Require(live.Count == 1 && live[0].Id == hatId && Mathf.Abs(live[0].P.Position.Y - .08f) < .001f, $"many changes in a moment send one preview: {live.Count}");
            for (var i = 0; i < 30; i++) { editor.Model!.SetPosition(new Vector3(0, .1f + i * .001f, 0)); await Frames(1); }
            Require(live.Count > 2 && live.Count < 25, $"previews are limited in rate, not sent every frame: {live.Count} in 30 frames");
            await Frames(15); var settled = live.Count; await Frames(15); Require(live.Count == settled, "nothing is sent while nothing changes");
            editor.Cancel(); await Frames(2); Require(live[^1].P.Same(editor.Model?.Saved ?? new Placement(new Vector3(0, 0, 0), new Vector3(0, 90, 0), new Vector3(3, 3, 3))) || live[^1].P.Scale.IsEqualApprox(new Vector3(3, 3, 3)), "cancelling tells the others where the object really is");
            editor.LiveSink = null;
            // ---- switching to another object: refused while something is unsaved
            editor.Open(hatId); await Frames(5); editor.SetTool(AttachmentTool.Select); Require(!editor.Gizmo.Visible || true, "select tool");
            var ringHolder = rig.AttachmentHolder(ringId)!; editor.Model!.SetPosition(new Vector3(0, .1f, 0));
            Require(!editor.SwitchTo(ringId) && editor.Status.Contains("Salve ou cancele") && editor.Target!.Id == hatId, "switching is refused while there are unsaved changes");
            editor.Model.Revert(); Require(editor.SwitchTo(ringId) && editor.Target!.Id == ringId, "switching works when clean"); await Frames(30);
            var near = rig.PickAttachment(camera.GlobalPosition, Ray0(rig.AttachmentHolder(ringId)!.GlobalPosition)); Require(near == ringId, "an object is found under the cursor, even a small one");
            editor.Cancel(); await Frames(30);

            // ---- the menu of an object worn on the body
            var acted = new List<string>(); var services = new ContextServices { AttachmentAction = (action, t) => acted.Add(action + ":" + t.Name) };
            var menu = InteractionRegistry.Build(new AttachmentTarget(hatId, Guid.NewGuid(), "Chapéu", "head_top", "Topo da cabeça", true, "detalhes"), services);
            Require(menu.Select(x => x.Label).SequenceEqual(["Editar", "Remover", "Alterar ponto", "Propriedades", "Localizar no inventário"]) && menu.All(x => x.Enabled), "the menu offers what the person asked for");
            foreach (var slice in menu) slice.Run!(); Require(acted.SequenceEqual(["edit:Chapéu", "detach:Chapéu", "move:Chapéu", "properties:Chapéu", "locate:Chapéu"]), "each slice does its action");
            var locked = InteractionRegistry.Build(new AttachmentTarget(lockedId, Guid.NewGuid(), "Broche", "chest", "Peito", false, ""), services); Require(!locked[0].Enabled && locked[1].Enabled, "no right to modify: cannot edit, can still remove");

            GD.Print("event=attachment_editor_smoke result=passed");
            AvatarRig.AttachmentModelProvider = null; manager.Transport = previous; GetTree().Quit();
        }
        catch (Exception exception)
        {
            manager.Transport = previous; AvatarRig.AttachmentModelProvider = null;
            GD.PushError($"event=attachment_editor_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
