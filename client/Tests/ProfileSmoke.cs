using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LifeVerse.Client.Scripts;

namespace LifeVerse.Client.Tests;

/// <summary>The profile window: my profile is editable and saved with what I typed, another person profile is read-only, the type of account and the marriage are shown, groups can be hidden, and a proposal can be answered.</summary>
public partial class ProfileSmoke : Node3D
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111"), Other = Guid.Parse("22222222-2222-2222-2222-222222222222"), GroupId = Guid.Parse("33333333-3333-3333-3333-333333333333"), Proposal = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static string Profile(Guid id, bool self, string account = "standard", string? spouse = null, bool privateProfile = false, string bio = "Construtora", string birthday = "07/09") => JsonSerializer.Serialize(new
    {
        id, username = self ? "ana" : "bia", displayName = self ? "Ana Souza" : "Bia Lima", isSelf = self, privateProfile, joinedAt = "2026-01-15T10:00:00Z", characterAgeYears = 1, accountType = account, donorSince = account == "donor" ? "2026-06-01T00:00:00Z" : null, presence = "online",
        bio = privateProfile ? "" : bio, firstLifeBio = privateProfile ? "" : "Recife", interests = privateProfile ? "" : "música", hasPhoto = false, photoUrl = (string?)null, birthday = privateProfile ? null : birthday,
        married = spouse is not null, marriage = spouse is null ? null : new { id = Guid.NewGuid(), spouseId = Other, spouseName = spouse, since = "2026-08-20T12:00:00Z" },
        groups = privateProfile ? Array.Empty<object>() : [new { id = GroupId, name = "Os Exploradores", tag = "EXP", role = "member", color = "#12F2E0", shown = true }],
        settings = self ? new { hideGroups = false, hideMarriage = false, privateProfile } : null
    });

    public override async void _Ready()
    {
        try
        {
            var window = new ProfileWindow { Name = "ProfileWindow", MyIdOverride = () => Me }; AddChild(window);
            var calls = new List<(string Method, string Path, string? Body)>(); var profiles = new Dictionary<string, string>(); var mine = "{\"current\":null,\"incoming\":[],\"outgoing\":[]}";
            window.UseTransportForTesting((method, path, body) =>
            {
                calls.Add((method, path, body));
                if (method == "GET" && path == $"/api/v1/users/{Me}/profile/full") return Task.FromResult<(long, string)>((200, profiles.GetValueOrDefault("me", Profile(Me, true))));
                if (method == "GET" && path == $"/api/v1/users/{Other}/profile/full") return Task.FromResult<(long, string)>((200, profiles.GetValueOrDefault("other", Profile(Other, false))));
                if (method == "GET" && path.StartsWith("/api/v1/users/profile?name=bia")) return Task.FromResult<(long, string)>((200, Profile(Other, false)));
                if (method == "GET" && path.StartsWith("/api/v1/users/profile?name=")) return Task.FromResult<(long, string)>((404, "{}"));
                if (method == "PUT" && path == "/api/v1/users/me/profile/details")
                {
                    var sent = JsonNode.Parse(body!)!.AsObject();
                    if (sent["bio"]?.GetValue<string>() == "ruim") return Task.FromResult<(long, string)>((400, "{\"error\":\"too_long\",\"message\":\"O texto passou do limite.\"}"));
                    return Task.FromResult<(long, string)>((200, Profile(Me, true, bio: sent["bio"]!.GetValue<string>(), birthday: "12/03")));
                }
                if (method == "PUT" && path.Contains("/groups/")) return Task.FromResult<(long, string)>((204, ""));
                if (method == "GET" && path == "/api/v1/marriage") return Task.FromResult<(long, string)>((200, mine));
                if (method == "POST" && path == "/api/v1/marriage/propose") return Task.FromResult<(long, string)>((200, "{\"id\":\"" + Guid.NewGuid() + "\",\"status\":\"proposed\"}"));
                if (method == "POST" && path == $"/api/v1/marriage/{Proposal}/accept") { mine = "{\"current\":{\"id\":\"" + Guid.NewGuid() + "\",\"spouseId\":\"" + Other + "\",\"spouseName\":\"Bia Lima\",\"since\":\"2026-11-10T12:00:00Z\"},\"incoming\":[],\"outgoing\":[]}"; profiles["me"] = Profile(Me, true, spouse: "Bia Lima"); return Task.FromResult<(long, string)>((204, "")); }
                if (method == "POST" && path == "/api/v1/marriage/divorce") { mine = "{\"current\":null,\"incoming\":[],\"outgoing\":[]}"; profiles.Remove("me"); return Task.FromResult<(long, string)>((204, "")); }
                return Task.FromResult<(long, string)>((404, "{}"));
            });
            await Frames(3);
            Require(window.TabsForTesting.SequenceEqual(["SOBRE", "VIDA REAL", "GRUPOS", "CASAMENTO"]), "the tabs of the profile");

            // --- mine ------------------------------------------------------------------------------------------------------
            await window.OpenSelfAsync(); await Frames(2);
            Require(window.IsOpen && window.NameForTesting == "Ana Souza", "the profile opens with the name");
            Require(window.AccountForTesting.Contains("Conta padrão"), "a standard account is shown as such");
            Require(window.MarriageForTesting == "Solteiro(a)", "not married is shown");
            Require(window.EditableForTesting && window.SaveVisibleForTesting && !window.ProposeVisibleForTesting, "mine can be edited and saved, and there is no proposal to myself");
            Require(window.BioForTesting == "Construtora" && window.FirstLifeForTesting == "Recife" && window.InterestsForTesting == "música", "the texts come from the server");
            Require(window.GroupRowsForTesting == 1, "my groups are listed");

            window.BioForTesting = "Arquiteta de ilhas"; window.SetHideMarriageForTesting(true);
            Require(await window.SaveAsync(), "saving works");
            var saved = JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject();
            Require(saved["bio"]!.GetValue<string>() == "Arquiteta de ilhas", "the text is sent");
            Require(saved["hideMarriage"]!.GetValue<bool>() && !saved["hideGroups"]!.GetValue<bool>(), "what I hide is sent");
            Require(window.StatusForTesting == "Perfil salvo." && window.BioForTesting == "Arquiteta de ilhas", "the saved profile comes back");

            window.BioForTesting = "ruim"; Require(!await window.SaveAsync() && window.StatusForTesting.Contains("passou do limite"), "a refusal shows the server message");
            var puts = calls.Count(c => c.Method == "PUT" && c.Path.EndsWith("details"));
            window.BioForTesting = "x"; window.PickPhotoForTesting(Guid.NewGuid()); await window.SaveAsync();
            var withPhoto = JsonNode.Parse(calls.Last(c => c.Method == "PUT" && c.Path.EndsWith("details")).Body!)!.AsObject();
            Require(calls.Count(c => c.Method == "PUT" && c.Path.EndsWith("details")) == puts + 1 && withPhoto["photoAssetId"] is not null , "a chosen photo is sent");

            await window.ToggleGroupForTesting(0, false);
            Require(calls.Any(c => c.Method == "PUT" && c.Path == $"/api/v1/users/me/groups/{GroupId}/profile" && c.Body!.Contains("false")), "hiding a group is sent");

            // --- the photo ---------------------------------------------------------------------------------------------------
            var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8); image.Fill(new Color("#12F2E0"));
            window.ShowPhoto(image.SavePngToBuffer()); Require(window.PhotoShownForTesting, "a PNG sent by the server becomes the photo");
            window.ShowPhoto([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]);

            // --- another person ----------------------------------------------------------------------------------------------
            profiles["other"] = Profile(Other, false, account: "donor", spouse: "Caio Rocha", bio: "Viajante");
            await window.OpenByIdAsync(Other); await Frames(2);
            Require(window.NameForTesting == "Bia Lima" && window.BioForTesting == "Viajante", "another profile shows their texts");
            Require(!window.EditableForTesting && !window.SaveVisibleForTesting, "and it cannot be edited");
            Require(window.AccountForTesting.Contains("Doador") && window.AccountForTesting.Contains("06/2026"), "a donor is shown with since when");
            Require(window.MarriageForTesting.Contains("Casado(a) com Caio Rocha") && window.MarriageForTesting.Contains("20/08/2026"), "the marriage is shown with the date");
            Require(window.BirthdayForTesting.StartsWith("07/09"), "the birthday is the creation day the server sent"); Require(window.BirthdayForTesting.Contains("ano(s) de LifeVerse"), "with how long the character exists");
            Require(!window.ProposeVisibleForTesting, "no proposal to somebody who is already married");

            profiles["other"] = Profile(Other, false); await window.OpenByIdAsync(Other);
            Require(window.ProposeVisibleForTesting, "a free person can be proposed to");
            window.SetProposeMessageForTesting("Aceita?"); await window.ProposeAsync();
            Require(calls.Last(c => c.Method == "POST").Body!.Contains(Other.ToString()) && calls.Last(c => c.Method == "POST").Body!.Contains("Aceita?") && window.StatusForTesting.Contains("enviado"), "the proposal goes with its message");

            profiles["other"] = Profile(Other, false, privateProfile: true); await window.OpenByIdAsync(Other);
            Require(window.BioForTesting == "" && window.StatusForTesting.Contains("privado"), "a private profile shows only the name");

            await window.OpenByNameAsync("bia"); Require(window.NameForTesting == "Bia Lima" && window.Viewing == Other, "a profile opens by name");
            await window.OpenByNameAsync("ninguem"); Require(window.StatusForTesting.Contains("Não encontrei"), "an unknown name says so");

            // --- marriage: accept a proposal, then divorce ---------------------------------------------------------------------
            mine = "{\"current\":null,\"incoming\":[{\"id\":\"" + Proposal + "\",\"fromId\":\"" + Other + "\",\"fromName\":\"Bia Lima\",\"message\":\"Oi\",\"createdAt\":\"2026-11-09T10:00:00Z\"}],\"outgoing\":[]}";
            await window.OpenSelfAsync(); await Frames(2);
            Require(window.MarriageRowsForTesting >= 2, "an incoming proposal is listed with its buttons");
            await window.AnswerForTesting(0, "accept"); await Frames(2);
            Require(window.MarriageForTesting.Contains("Casado(a) com Bia Lima"), "after accepting I am married to her");
            await window.DivorceForTesting(); await Frames(2);
            Require(window.MarriageForTesting == "Solteiro(a)", "after a divorce I am single again");

            GD.Print("event=profile_smoke result=passed");
            GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError($"event=profile_smoke result=failed error={exception.Message}"); GetTree().Quit(1); }
    }

    private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
