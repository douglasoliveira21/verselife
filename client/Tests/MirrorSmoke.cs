using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>The appearance editor inside the world: the camera goes in front of the avatar and it stands still, changes show on the avatar at once and are saved or undone.</summary>
public partial class MirrorSmoke : Node
{
    public override async void _Ready()
    {
        try
        {
            var world = GD.Load<PackedScene>("res://Scenes/World.tscn").Instantiate<Node3D>(); AddChild(world);
            var player = world.GetNode<PlayerController>("Player"); var rig = player.GetNode<AvatarRig>("AvatarRig");
            var camera = player.GetNode<Camera3D>("CameraPivot/SpringArm3D/Camera3D");
            var manager = GetNode<AvatarManager>("/root/AvatarManager");
            var start = AvatarManager.CreateDefault(); start.DisplayName = "Teste"; start.HairStyle = "short"; manager.SetCurrent(start);
            await Frames(40);

            var hairId = Guid.NewGuid(); var lookA = Guid.NewGuid(); var lookB = Guid.NewGuid();
            var requests = new List<string>();
            var window = new AppearanceWindow { Name = "AppearanceWindow", Player = player };
            window.Transport = (method, path, body) =>
            {
                requests.Add($"{method} {path} {body}");
                if (method == Godot.HttpClient.Method.Get && path == "/wearables") return (200, $"[{{\"id\":\"{hairId}\",\"name\":\"Meu cabelo\",\"slot\":\"hair\",\"layer\":\"base\",\"kind\":\"attachment\",\"triangles\":12,\"key\":\"custom:{hairId}\",\"hash\":\"{new string('a', 64)}\",\"covers\":[],\"hidesHair\":true,\"attachBone\":\"head\",\"offset\":[0,0.12,0],\"rotation\":[0,0,0],\"scale\":1,\"tint\":\"hair\"}}]");
                if (method == Godot.HttpClient.Method.Get && path == "/looks") return (200, $"[{{\"id\":\"{lookA}\",\"name\":\"Praia\"}},{{\"id\":\"{lookB}\",\"name\":\"Festa\"}}]");
                if (method == Godot.HttpClient.Method.Put && path == string.Empty) return (200, body!);
                if (path.EndsWith("/apply")) return (200, "{\"lookSkipped\":[\"body\"],\"displayName\":\"Teste\",\"baseModel\":\"human_b\",\"skinColor\":\"#8D5524\",\"hairColor\":\"#2B1B14\",\"hairStyle\":\"long\",\"eyeColor\":\"#4A3326\",\"beardStyle\":\"none\",\"bodySettingsJson\":\"{}\",\"faceSettingsJson\":\"{}\",\"equippedItems\":[{\"slot\":\"top\",\"itemKey\":\"tee_basic\",\"tintColor\":\"#3AAFC1\"},{\"slot\":\"bottom\",\"itemKey\":\"pants_basic\",\"tintColor\":\"#263B56\"},{\"slot\":\"shoes\",\"itemKey\":\"sneakers_basic\",\"tintColor\":\"#E7E9EE\"}]}");
                if (method == Godot.HttpClient.Method.Post && path == "/looks") return (200, "{}");
                return (404, "{}");
            };
            world.AddChild(window); await Frames(2);

            // ---- the mirror: the camera in front of a still avatar
            var beforeDistance = player.CameraDistance; var spot = player.GlobalPosition;
            window.Open(); await Frames(60);
            Require(window.Visible && player.MirrorActive && player.MirrorFace, "opening puts the camera in the mirror view");
            var toCamera = camera.GlobalPosition - player.GlobalPosition; var front = -player.GlobalBasis.Z;
            Require(toCamera.Dot(front) > .6f && toCamera.Length() < 2.2f, $"the camera is close and in front of the avatar: {toCamera} / facing {front}");
            Input.ActionPress("move_forward"); await Frames(30); Input.ActionRelease("move_forward");
            Require(player.GlobalPosition.DistanceTo(spot) < .05f, $"the avatar stands still in front of the mirror: moved {player.GlobalPosition.DistanceTo(spot)}");
            player.SetMirrorView(false); await Frames(60);
            Require(!player.MirrorFace && (camera.GlobalPosition - player.GlobalPosition).Length() > 2f, "the whole-body view is farther");
            player.TurnMirror(Mathf.Pi); await Frames(80);
            Require((camera.GlobalPosition - player.GlobalPosition).Dot(front) < -.5f, "turning the camera shows the back");
            player.SetMirrorView(true); await Frames(30);

            // ---- the wardrobe and the draft
            Require(window.MenuCountForTesting("hair") == 2 && window.MenuCountForTesting("top") >= 3 && !window.SaveEnabledForTesting, "the menus hold the catalog and the own items, and there is nothing to save yet");
            window.ChooseForTesting("hairstyle", "bald"); await Frames(6);
            Require(window.Dirty && window.SaveEnabledForTesting && window.Draft.HairStyle == "bald" && rig.FindChild("Hair", true, false) is null, "a change shows on the avatar at once and can be saved");
            window.SetSliderForTesting("height", 1.1);
            Require(window.Draft.BodySettingsJson.Contains("1.1"), $"sliders go to the draft: {window.Draft.BodySettingsJson}");
            window.ChooseForTesting("top", "dress_basic");
            Require(window.MenuKeyForTesting("bottom") == "covered" && window.Draft.Item("bottom", "") == "covered", "the dress covers the legs, as in the full editor");
            window.ChooseForTesting("top", "tee_basic"); window.ChooseForTesting("bottom", "pants_basic");
            window.ChooseForTesting("hair", AvatarRules.CustomKey(hairId));
            Require(window.Draft.Item("hair", "") == AvatarRules.CustomKey(hairId) && window.Draft.CustomWearables.Any(w => w.Slot == "hair" && w.Tint == "hair" && w.Hash.Length == 64), "an own item goes to the draft with what the preview needs to draw it");
            window.ChooseForTesting("hair", "none");
            window.SetColourForTesting("hair", "#FF0000"); Require(window.Draft.HairColor.Equals("#ff0000", StringComparison.OrdinalIgnoreCase), "colours go to the draft");

            // ---- saving
            requests.Clear(); window.SaveNow();
            Require(requests.Count == 1 && requests[0].StartsWith("Put  "), $"saving sends the draft: {string.Join(" | ", requests)}");
            Require(!window.Dirty && !window.SaveEnabledForTesting && manager.Current.HairStyle == "bald" && window.StatusText.Contains("salvo"), "saved: the manager holds it and nothing is pending");

            // ---- undoing and closing
            window.ChooseForTesting("hairstyle", "long"); await Frames(6);
            Require(window.Dirty && rig.FindChild("Hair", true, false) is not null, "a new change is pending");
            window.Undo(); await Frames(6);
            Require(!window.Dirty && window.Draft.HairStyle == "bald" && rig.FindChild("Hair", true, false) is null, "undo goes back to the saved look");
            window.ChooseForTesting("hairstyle", "ponytail"); await Frames(6);
            window.Close(); await Frames(60);
            Require(!window.Visible && !player.MirrorActive && manager.Current.HairStyle == "bald" && rig.FindChild("Hair", true, false) is null, "closing without saving puts the saved look back");
            Require(Mathf.Abs(player.CameraDistance - beforeDistance) < .01f, $"the camera goes back to where it was: {player.CameraDistance} / {beforeDistance}");
            Input.ActionPress("move_forward"); await Frames(30); Input.ActionRelease("move_forward");
            Require(player.GlobalPosition.DistanceTo(spot) > .3f, "the avatar walks again once the mirror is closed");

            // ---- looks
            window.Open(); await Frames(10);
            Require(window.Looks.Count == 2 && window.Looks[0].Name == "Praia", "the looks are listed");
            requests.Clear(); window.ApplyLook(0); await Frames(6);
            Require(requests.Any(r => r.StartsWith("Post ") && r.Contains($"/looks/{lookA}/apply")) && window.Draft.HairStyle == "long" && manager.Current.HairStyle == "long" && !window.Dirty, "applying a look puts it on the avatar and saves it");
            Require(window.StatusText.Contains("Ficaram de fora") && window.StatusText.Contains("corpo próprio"), $"what the look could not bring back is said: {window.StatusText}");
            window.SetLookNameForTesting("Novo visual"); requests.Clear(); window.SaveAsLook();
            Require(requests.Count == 3 && requests[0].StartsWith("Put ") && requests[1].StartsWith("Post /looks") && requests[1].Contains("Novo visual") && requests[2].StartsWith("Get /looks"), $"saving as a look saves the avatar, then the look, then reloads: {string.Join(" | ", requests.Select(r => r[..Math.Min(20, r.Length)]))}");
            window.SetLookNameForTesting(""); window.SaveAsLook(); Require(window.StatusText.Contains("nome"), "a look needs a name");
            window.Close();

            GD.Print("event=mirror_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=mirror_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
