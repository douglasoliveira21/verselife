using System.Text.Json;
using Godot;
using LifeVerse.Client.Autoloads;
using LifeVerse.Client.Models;
using LifeVerse.Client.Scripts;
using LifeVerse.Shared.Avatar;

namespace LifeVerse.Client.Tests;

/// <summary>Avatar 2.0, first part, on the client: the sliders and wearables drawn on the body, the editor with conflict handling and the looks manager.</summary>
public partial class Avatar2Smoke : Node
{
    private static AvatarAppearanceData Data(string body = "{}", string face = "{}", params (string Slot, string Key)[] items) => new()
    {
        DisplayName = "Teste", BodySettingsJson = body, FaceSettingsJson = face,
        EquippedItems = [.. (items.Length > 0 ? items : [("top", "tee_basic"), ("bottom", "pants_basic"), ("shoes", "sneakers_basic")]).Select(i => new EquippedItemData { Slot = i.Slot, ItemKey = i.Key, TintColor = "#FFFFFF" })]
    };

    private static (float Low, float High) Extent(Node node)
    {
        var low = float.MaxValue; var high = float.MinValue;
        void Visit(Node current)
        {
            if (current is VoiceIndicator or NamePlate) return;
            if (current is MeshInstance3D { Mesh: not null } mesh)
            {
                var box = mesh.Mesh.GetAabb();
                for (var corner = 0; corner < 8; corner++)
                {
                    var y = (mesh.GlobalTransform * (box.Position + new Vector3((corner & 1) * box.Size.X, ((corner >> 1) & 1) * box.Size.Y, ((corner >> 2) & 1) * box.Size.Z))).Y;
                    low = Mathf.Min(low, y); high = Mathf.Max(high, y);
                }
            }
            foreach (var child in current.GetChildren()) Visit(child);
        }
        Visit(node); return (low, high);
    }

    public override async void _Ready()
    {
        try
        {
            var root = new Node3D(); AddChild(root);
            var rig = GD.Load<PackedScene>("res://Scenes/AvatarRig.tscn").Instantiate<AvatarRig>(); root.AddChild(rig);
            await Frames(5);

            // ---- the body follows its sliders and still stands on the ground
            rig.ApplyAppearance(Data("{\"height\":1.15,\"legs\":1.15,\"arms\":1.15,\"hips\":1.15,\"head\":1.15}"));
            await Frames(5);
            var tall = Extent(rig);
            Require(tall.Low is > -.03f and < .03f, $"a tall avatar still stands on the ground: {tall.Low:F3}");
            rig.ApplyAppearance(Data("{\"legs\":0.85,\"arms\":0.85,\"head\":0.85,\"torso\":0.9}"));
            await Frames(5);
            Require(Extent(rig).Low is > -.03f and < .03f, "a short-legged avatar stands on the ground too");
            var legsLong = LegLength(rig, Data("{\"legs\":1.15}")); var legsShort = LegLength(rig, Data("{\"legs\":0.85}"));
            Require(legsLong > legsShort * 1.2f, $"the legs slider changes the legs: {legsShort:F2} / {legsLong:F2}");
            var headSmall = HeadWidth(rig, Data("{\"head\":0.85}")); var headBig = HeadWidth(rig, Data("{\"head\":1.15}"));
            Require(headBig > headSmall * 1.2f, "the head slider changes the head");

            // ---- the face
            rig.ApplyAppearance(Data(face: "{\"eyeSpacing\":1.15,\"noseSize\":1.2}")); await Frames(3);
            var head = rig.FindChild("Head", true, false) as Node3D;
            Require(head!.GetNodeOrNull<Node3D>("Nose") is not null && head.GetNodeOrNull<Node3D>("Mouth") is not null && head.GetNodeOrNull<Node3D>("BrowLeft") is not null, "the face has nose, mouth and brows");
            var wide = Mathf.Abs(head.GetNode<Node3D>("EyeLeft").Position.X);
            rig.ApplyAppearance(Data(face: "{\"eyeSpacing\":0.85}")); await Frames(3);
            head = rig.FindChild("Head", true, false) as Node3D;
            Require(wide > Mathf.Abs(head!.GetNode<Node3D>("EyeLeft").Position.X) * 1.2f, "the eye spacing slider moves the eyes");

            // ---- wearables
            rig.ApplyAppearance(Data(items: [("top", "dress_basic"), ("jacket", "coat_long"), ("bottom", "covered"), ("shoes", "boots_basic"), ("hat", "beanie_basic"), ("glasses", "glasses_square"), ("neck", "scarf_basic"), ("ears", "earrings_hoop"), ("gloves", "gloves_basic"), ("back", "backpack_basic")]));
            await Frames(5);
            foreach (var name in new[] { "Dress", "Coat", "Beanie", "GlassesLeft", "Scarf", "EarringLeft", "Glove", "Backpack" })
                Require(rig.FindChild(name, true, false) is not null, $"{name} is drawn");
            Require(rig.FindChild("Hair", true, false) is null, "a beanie hides the hair");
            Require(Extent(rig).Low is > -.03f and < .03f, "a fully dressed avatar stands on the ground");
            rig.ApplyAppearance(Data(items: [("top", "tee_basic"), ("bottom", "skirt_basic"), ("shoes", "sandals_basic"), ("hat", "cap_basic")])); await Frames(3);
            Require(rig.FindChild("Skirt", true, false) is not null && rig.FindChild("Cap", true, false) is not null && rig.FindChild("Hair", true, false) is not null && rig.FindChild("Coat", true, false) is null, "a skirt and a cap are drawn, the hair stays and the old coat is gone");

            // ---- the editor
            var manager = GetNode<AvatarManager>("/root/AvatarManager");
            var calls = new List<string>(); var saved = new List<(Guid Id, string Name, string? Code)>(); var lastBody = string.Empty;
            manager.Transport = (method, path, body) =>
            {
                calls.Add($"{method} {path} {body}");
                string List() => "[" + string.Join(",", saved.Select(l => $"{{\"id\":\"{l.Id}\",\"name\":\"{l.Name}\",\"shareCode\":{(l.Code is null ? "null" : $"\"{l.Code}\"")},\"skinColor\":\"#C98F65\",\"hairStyle\":\"short\"}}")) + "]";
                if (method == Godot.HttpClient.Method.Get && path == "/looks") return (200, List());
                if (method == Godot.HttpClient.Method.Put && path == string.Empty) { lastBody = body ?? string.Empty; return (200, lastBody); }
                if (method == Godot.HttpClient.Method.Post && path == "/looks")
                {
                    var name = JsonDocument.Parse(body!).RootElement.GetProperty("name").GetString()!;
                    if (saved.Any(l => l.Name == name)) return (409, "{\"error\":\"name_taken\"}");
                    saved.Add((Guid.NewGuid(), name, null)); return (200, "{}");
                }
                if (path.EndsWith("/apply")) return (200, "{\"displayName\":\"Teste\",\"baseModel\":\"human_b\",\"skinColor\":\"#8D5524\",\"hairColor\":\"#2B1B14\",\"hairStyle\":\"long\",\"eyeColor\":\"#4A3326\",\"beardStyle\":\"none\",\"bodySettingsJson\":\"{\\\"height\\\":1.1}\",\"faceSettingsJson\":\"{\\\"noseSize\\\":1.2}\",\"outfitsJson\":\"[]\",\"attachmentsJson\":\"[]\",\"cameraSensitivity\":0.003,\"invertCameraY\":false,\"equippedItems\":[{\"slot\":\"top\",\"itemKey\":\"dress_basic\",\"tintColor\":\"#8E3B6B\"},{\"slot\":\"bottom\",\"itemKey\":\"covered\",\"tintColor\":\"#FFFFFF\"},{\"slot\":\"shoes\",\"itemKey\":\"boots_basic\",\"tintColor\":\"#E7E9EE\"},{\"slot\":\"hat\",\"itemKey\":\"cap_basic\",\"tintColor\":\"#B23A48\"}]}");
                if (path.EndsWith("/duplicate")) { saved.Add((Guid.NewGuid(), saved[0].Name + " (cópia)", null)); return (200, "{}"); }
                if (path.EndsWith("/share") && method == Godot.HttpClient.Method.Post) { saved[0] = (saved[0].Id, saved[0].Name, "ABCD2345"); return (200, "{\"shareCode\":\"ABCD2345\"}"); }
                if (path.EndsWith("/share")) { saved[0] = (saved[0].Id, saved[0].Name, null); return (200, "{\"shareCode\":null}"); }
                if (path == "/looks/import") { var code = JsonDocument.Parse(body!).RootElement.GetProperty("code").GetString(); if (code != "ABCD2345") return (404, "{\"error\":\"not_found\"}"); saved.Add((Guid.NewGuid(), "Importado", null)); return (200, "{}"); }
                if (method == Godot.HttpClient.Method.Delete && path.StartsWith("/looks/")) { saved.RemoveAt(0); return (200, "{}"); }
                return (404, "{}");
            };
            var creator = GD.Load<PackedScene>("res://Scenes/CharacterCreator.tscn").Instantiate<CharacterCreator>(); AddChild(creator);
            await Frames(5);

            creator.ChooseForTesting("top", "dress_basic");
            var worn = creator.FormForTesting().EquippedItems.ToDictionary(x => x.Slot, x => x.ItemKey);
            Require(worn["top"] == "dress_basic" && worn["bottom"] == "covered", $"choosing a dress covers the legs: {worn["bottom"]}");
            Require(AvatarRules.CheckEquipment([.. worn.Select(x => (x.Key, x.Value))]).Count == 0, "the form always produces a valid outfit");
            creator.ChooseForTesting("bottom", "pants_basic");
            worn = creator.FormForTesting().EquippedItems.ToDictionary(x => x.Slot, x => x.ItemKey);
            Require(worn["bottom"] == "pants_basic" && worn["top"] == "tee_basic", $"choosing trousers takes the dress off: {worn["top"]}");
            creator.ChooseForTesting("jacket", "coat_long"); creator.ChooseForTesting("hat", "beanie_basic");
            worn = creator.FormForTesting().EquippedItems.ToDictionary(x => x.Slot, x => x.ItemKey);
            Require(worn["jacket"] == "coat_long" && worn["hat"] == "beanie_basic" && !worn.ContainsKey("glasses"), "outer layers and hats are kept, empty slots are not sent");
            creator.SetBodyForTesting("height", 1.1); creator.SetBodyForTesting("legs", 0.9); creator.SetFaceForTesting("noseSize", 1.2);
            Require(creator.BodyJsonForTesting == "{\"height\":1.1,\"legs\":0.9}" && creator.FaceJsonForTesting == "{\"noseSize\":1.2}", $"sliders become clean settings: {creator.BodyJsonForTesting} {creator.FaceJsonForTesting}");
            Require(AvatarRules.CheckSettings(creator.BodyJsonForTesting, AvatarRules.BodyRanges) is null && AvatarRules.CheckSettings(creator.FaceJsonForTesting, AvatarRules.FaceRanges) is null, "what the editor sends passes the server rules");

            // ---- the looks manager
            creator.SetLookNameForTesting("Praia"); calls.Clear();
            creator.SaveLookForTesting();
            Require(calls[0].StartsWith("Put  ") && calls[1].StartsWith("Post /looks") && calls[1].Contains("Praia") && calls[2].StartsWith("Get /looks"), $"saving a look saves the avatar first, then the look, then reloads the list: {string.Join(" | ", calls.Select(c => c[..Math.Min(18, c.Length)]))}");
            Require(creator.Looks.Count == 1 && creator.Looks[0].Name == "Praia", "the list shows the new look");
            creator.SaveLookForTesting();
            Require(creator.LookMessageForTesting.Contains("já existe", StringComparison.OrdinalIgnoreCase) || creator.LookMessageForTesting.Contains("Já existe"), $"a repeated name is explained: {creator.LookMessageForTesting}");
            creator.SelectLookForTesting(0);
            creator.ShareLookForTesting(true);
            Require(creator.LookCodeForTesting == "ABCD2345" && creator.LookMessageForTesting.Contains("ABCD2345"), "sharing shows the code");
            creator.ShareLookForTesting(false);
            Require(creator.LookCodeForTesting.Length == 0, "stopping the share clears the code");
            creator.DuplicateLookForTesting();
            Require(creator.Looks.Count == 2 && creator.Looks[1].Name.Contains("cópia"), "duplicating adds a copy");
            creator.SelectLookForTesting(0); creator.ApplyLookForTesting();
            var applied = creator.FormForTesting();
            Require(applied.HairStyle == "long" && applied.BaseModel == "human_b" && applied.BodySettingsJson == "{\"height\":1.1}" && applied.Item("top", "") == "dress_basic" && applied.Item("bottom", "") == "covered" && applied.Item("hat", "") == "cap_basic", "applying a look puts every part of it on the form");
            Require(manager.Current.HairStyle == "long", "the current avatar is the applied look");
            creator.SetImportCodeForTesting("NOPE"); creator.ImportLookForTesting();
            Require(creator.LookMessageForTesting.Contains("8"), "a short code is refused before asking");
            creator.SetImportCodeForTesting("ZZZZ9999"); creator.ImportLookForTesting();
            Require(creator.LookMessageForTesting.Contains("não encontrado"), "an unknown code is explained");
            creator.SetImportCodeForTesting("ABCD2345"); creator.ImportLookForTesting();
            Require(creator.Looks.Any(l => l.Name == "Importado"), "a valid code imports the look");
            creator.SelectLookForTesting(0); creator.DeleteLookForTesting();
            Require(creator.Looks.Count == 2, "deleting removes the look");

            manager.Transport = null;
            GD.Print("event=avatar2_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"event=avatar2_smoke result=failed error={exception.Message}");
            GetTree().Quit(1);
        }
    }

    private static float LegLength(AvatarRig rig, AvatarAppearanceData data) { rig.ApplyAppearance(data); var leg = rig.FindChild("LeftUpperLeg", true, false) as Node3D; return leg!.Scale.Y; }
    private static float HeadWidth(AvatarRig rig, AvatarAppearanceData data) { rig.ApplyAppearance(data); var head = rig.FindChild("Head", true, false) as Node3D; return head!.Scale.X; }
    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
